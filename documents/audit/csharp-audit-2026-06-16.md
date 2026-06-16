# ImageGenerator.MAUI — Audit Report (mutation engine + LLM era)

**Audit date:** 2026-06-16 · **Commit:** `bf215c8` (master) · **Version:** 1.9.1 · **Target:** `net10.0-windows10.0.22621.0` MAUI (Windows-only) · **Tests:** ~1162 passing

**Method:** three parallel `csharp-auditor` agents (Opus), one per subsystem, focused on **workflow, stability, correctness** — the surface that landed since the last full audit (`AUDIT_REPORT.md`, 2026-05-20, v1.3.0). 78 commits since then, concentrated in the deterministic mutation engine (phases 0-6), the LLM features (prompt builder v1/v2, AI caption mutation, Anthropic/Ollama transports), and the GPU/VRAM lifecycle. Findings consolidated and the one High re-verified by hand.

---

## 1. Executive summary

**Overall health: very good.** The new code is, on the whole, the cleanest in the project. The three highest-risk areas all held up under scrutiny:

- **Mutation engine determinism is correct.** Single master `Random(Seed)` → one `subSeed` per slot → per-slot `Random(subSeed)` owning operator pick and every Apply. No `Random.Shared`, `Guid.NewGuid`, `DateTime.*`, `GetHashCode`, or unordered enumeration on the generation path. Every operator clones from the original `source`, self-validates via `V4JsonPromptValidator`, and returns `null` (never a partial caption) on an illegal result. **No determinism breaks, no cross-variant mutation leaks.**
- **LLM/HTTP transports are cancellation- and lifecycle-correct.** Named `IHttpClientFactory` clients only (no `new HttpClient()`), `using` on every request/response, buffered `StringContent` that replays safely under Polly retry, bounded validator-retry loop, `OperationCanceledException` rethrown ahead of the generic catch. No `async void`, no sync-over-async. Tokens read from secure storage, gated on null/empty, never logged.
- **Workflow/handoff/VRAM/CTS lifecycle is clean.** Every `CancellationTokenSource` is `using`-scoped or disposed-in-finally with an `ObjectDisposedException` guard on the late-cancel race. Handoff slots (`PendingMutationBase`/`PendingBreedSet`) are consumed-then-cleared. Batch cancel drains the queue but lets the paid in-flight prediction finish (intended). VRAM `MaybeFree` is correctly guarded (idle + ComfyUI + toggle-on + service-wired) and its fire-and-forget calls can't throw into the void. DI lifetimes are right; no captive dependency, no cycle.

The one finding that warrants action is **F1** — the AI prompt-builder UI path drops the `CancellationToken` and offers no cancel, so a slow paid Opus call is uninterruptible. Everything else is Medium-or-below: bounded edge behaviors, a behavioral-contract mismatch worth a decision, and singleton-state carry-over nits.

### Top findings by severity

| # | Severity | Finding | Files |
|---|---|---|---|
| F1 | **High** | `IdeaToPromptViewModel.BuildAsync` calls `BuildProseAsync`/`BuildJsonAsync` with no `CancellationToken` and exposes no cancel command. The service *does* plumb the token to `SendAsync` — the VM just never supplies one. VM is a Singleton, so navigating away orphans the task, which runs to completion burning paid Opus tokens; the user is stuck on a greyed-out button for up to the full Polly budget (120s/attempt × retries). | `Presentation/ViewModels/IdeaToPromptViewModel.cs:92,109` |
| F2 | **Medium** | `MutationEngineViewModel.Initialize` resets `IsBreedMode` every visit but never resets `IsAiMode` to `false`. After a Breed flow (forces AI on), re-entering "Mutate current prompt" on an ordinary prompt re-appears with the **paid LLM path still selected** — a silent cost path the user didn't choose. | `Presentation/ViewModels/MutationEngineViewModel.cs:280-285` |
| F3 | **Medium** | `DescBudget.Fit` uses `continue` (not `break`) when a phrase is over budget, so a smaller low-priority phrase is admitted after a higher-priority one was skipped — contradicting the documented "highest tiers are the ones left out" contract. Deterministic (not a repro bug), but a behavioral mismatch needing a decision: `break` to honor strict tiering, or update the doc to "best-fit packing within tier priority." | `Core/Domain/Ideogram/Mutation/DescBudget.cs:49-56` |
| F4 | **Medium** | LLM transports throw bare `InvalidOperationException` for HTTP non-2xx and missing content. Works (status detail is in the message), but a caller can't distinguish a retryable transport fault from a malformed response without string-matching. Prefer `HttpRequestException` (carries `StatusCode` in .NET 8+) or a small typed transport exception. | `Infrastructure/External/Ollama/OllamaChatTransport.cs:72`; `Infrastructure/External/Anthropic/AnthropicMessagesTransport.cs:87,92` |
| F5 | **Minor** | Nested retry budgets: Polly (3 attempts on the `"anthropic"` client) × in-method validator loop (2). Self-limiting and correct in isolation (Polly retries only transport faults; the loop only semantic ones), but the compound worst case is several paid attempts per click with no per-click ceiling surfaced to the user. Worth a code comment documenting "3×2 is intentional and bounded," optionally drop client retries to 2. | `Infrastructure/External/Anthropic/AnthropicPromptBuilderService.cs:187`; `MauiProgram.cs:101` |
| F6 | **Minor** | `MutationEngineViewModel` is a Singleton, so `Seed` (re-rolled only when `== 0`) stays pinned across genuinely different bases — base B renders at base A's seed, defeating the per-base "fresh comparison grid" expectation. Re-roll in the new-base branch instead of gating on zero. | `Presentation/ViewModels/MutationEngineViewModel.cs:372` |
| F7 | **Minor** | `MutationLibrary` stores caller-supplied `IReadOnlyList`/`IReadOnlyDictionary` by reference; a caller mutating the original collection after construction mutates library state. Defensive-copy on construction. | `Core/Domain/Ideogram/Mutation/Library/MutationLibrary.cs:21-23,34-43` |
| F8 | **Minor** | No `ConfigureAwait(false)` in the non-UI transport/library code (`AnthropicMessagesTransport`, `OllamaChatTransport`, `MutationLibraryService`). No deadlock today (nothing sync-blocks on them), so consistency-only. ViewModels/Pages correctly omit it — this applies to infrastructure files only. | `Infrastructure/External/**`, `Infrastructure/Services/MutationLibraryService.cs` |
| F9 | **Minor** | `MutatePalette.SwapAccent` is a no-op on a fully-desaturated (gray) palette (180° hue flip of `s=0` is identity); the `SequenceEqual` guard correctly returns `null`, so it's a wasted-but-bounded attempt, never wrong output. Optional early-return when `bestSat <= 0`. | `Core/Domain/Ideogram/Mutation/Operators/MutatePaletteOperator.cs:46,78-101` |
| F10 | **Nit** | `EnsureOverrideReadme` does synchronous `Directory.CreateDirectory`/`File.WriteAllText` on the async build path (try/catch-swallowed, tiny once-per-build write). | `Infrastructure/External/Anthropic/AnthropicPromptBuilderService.cs:262-276` |
| F11 | **Nit** | Magic literals (`MaxOutputTokens=16000`, `effort="high"`); unreachable defensive `Fail(...)` returns after bounded loops; `Batch.PropertyChanged` lambda never unsubscribed (harmless — publisher/subscriber are both process-lifetime singletons); silent `Count` clamp `[0,100]` with no `DropLog` signal. | various |

### Known / acknowledged (carried from v1.3.0 baseline — not re-flagged)

- `ImageSharp 3.1.x` pinned for license reasons. Correct, not flagged.
- `CommunityToolkit.Mvvm` referenced from `Core/Domain/Entities` — the documented single-`.csproj` trade-off.
- `GeneratorViewModel` size (~1650 lines after the mutation/LLM additions). Host responsibilities remain coherent; further carve-up is past diminishing returns. Accepted.
- **F2 from the 2026-05-20 report (`GenerationJob.ShowInFolder` raw `/select` arg) is now RESOLVED/accepted** — it carries a justifying comment (explorer.exe parses its command line non-standardly; `"` is illegal in a Windows path so injection-safe). Do not carry it forward as open.

---

## 2. Subsystem notes

### 2.1 LLM / HTTP transport layer — *health: good*
Clean end-to-end cancellation in the transports/services (the F1 gap is the VM not supplying a token, not the transport). Named-client + Polly + `using` + buffered `StringContent` + bounded validator retry. Secure-token handling correct (loaded with try/catch→null, gated, never logged; `Forget()` cancels the pending debounced write). Open-core override seam reads the private file at call time and never reads the IP to construct the public default; `EnsureOverrideReadme` only ever *adds* a README. Findings: F1, F4, F5, F8, F10.

### 2.2 Caption mutation engine (pure domain) — *health: excellent*
Determinism contract verified clean (see summary). Operator purity verified: every operator clones from `source`, keys slot tags by source-`Element` reference and walks the parallel clone by index (the round-trip clone preserves order; `Element` is reference-equality `sealed`), and self-validates before returning. Bounds/termination correct (`MaxAttemptsPerVariant=20`, `Count` clamp, `MinElements=2`, degenerate/duplicate rejection). Empty library degrades to "every slot drops," never NRE. Math correct (stateless Box–Muller, HSL↔RGB round-trip, uppercase hex matching the validator regex, Python-`.split()`-equivalent word count). Findings: F3, F7, F9, plus a `-180` hue tie-break nit and the silent `Count` clamp nit.

### 2.3 Workflow / handoff / batch / VRAM — *health: very good*
All five hunt axes clean: CTS lifecycle (`using`-scoped or finally-disposed with ODE guard), read-once-clear handoff (`PendingMutationBase`/`PendingBreedSet` nulled before any early return; `_breedSet` reassigned every `Initialize`), batch semantics (sequential over a frozen snapshot; cancel drains queued tail, lets paid in-flight finish; re-entrancy guarded), VRAM `MaybeFree` guards, and threading (background continuations marshalled via `DispatchToUi`). DI lifetimes correct, no captive dependency/cycle. Secondary re-baseline sweep over Descriptors/Converters/gallery/civitai surfaced **no new Medium+ issues** — new services follow the established per-call-create + `using` + structured-logging patterns. Findings: F2, F6, plus the unsubscribed-`PropertyChanged` nit.

---

## 3. Recommended follow-up

**Act now:** F1 (add a CTS + cancel command to `IdeaToPromptViewModel`). **Decide:** F3 (`break` vs doc-update) and F2 (`IsAiMode = IsBreedMode` reset). **Easy polish bundle:** F6 (seed re-roll), F7 (defensive copy), F4 (exception type). **No action needed:** F5, F8, F9, F10, F11 — documented here so they aren't re-discovered as "new."

No code was changed in this pass — audit only. Fixes are a separate, per-finding follow-up.
