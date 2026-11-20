# Model choice by role — `Local | Anthropic | OpenRouter`, with the picker where the model is used

**Status:** Phase A **done** (gate moved 1,575 → **1,591 passed / 0 failed**). Phase B and C not started.
**Measured on:** `main` 2026-11-20, the tree that landed the inline server-status labels and
`OllamaEndpointException` (test gate **1,575 passed / 0 failed / 0 skipped**).

---

## 1. Ground rules (locked by owner, 2026-11-20)

1. **`generate` is the image model.** The generation path (ComfyUI / Pollinations / Replicate / a local
   image model) is not an LLM role and is not touched by this plan — see the one open question in §9.
2. **Settings keeps connection facts only**: server base URLs, API keys, the OpenRouter free-only
   switch. **The model pickers in Settings are deleted.**
3. The model picker lives **where the model is used**. A model choice belongs to a *role*, not a page.
4. Existing persisted picks **seed** the new per-role values on first run. Nobody loses a model, and
   first run after the update behaves exactly like the update before it.
5. Every provider choice gets the same three-part affordance: provider, model, inline status. Base
   address and key are referenced by name, never re-entered.
6. Inherited from `refactor-plan-generator-vm.md` and still in force: the `Generator.Settings` seam
   stays; `AutomationId` values are never renamed as collateral; `UiMarkupContractTests` is the
   machine gate for binding paths (it reads the **built** `TestAssets`, so build before testing);
   `MAUIG2045` binding warnings are the repath checklist but never fail a build.

---

## 2. The defect this fixes (measured, not assumed)

One persisted value, four independent two-way bindings:

| Binding | Page | What the user thinks they are choosing |
|---|---|---|
| `Settings.OllamaModel` | `SettingsPage.xaml` ("Local image model") | the image model to generate with |
| `Generator.Settings.OllamaModel` | `IdeaToPromptPage.xaml` | the LLM that writes prompts |
| `Generator.Settings.OllamaModel` | `IdeogramStructureEditorPage.xaml` | the LLM that enriches regions |
| `Generator.Settings.OllamaModel` | `MutationEnginePage.xaml` | the LLM that mutates captions |

All four are `Mode=TwoWay` against `imggen.ollama_model`. Picking a cheap local model to verify a
caption mutation silently changes the model that renders images. `OllamaVisionModel` is shared by
Settings and Idea to Prompt, `OpenRouterVisionModel` likewise — smaller blast radius, same disease.

Three services then make the same decision *again*, inside Infrastructure:

```
Infrastructure/External/Anthropic/PromptBuilderService.cs   ResolveModelId → LoadOllamaModel()
Infrastructure/External/Mutation/CaptionMutationLlmService.cs  ResolveModelId → LoadOllamaModel()
Infrastructure/External/Mutation/EnrichRegionsLlmService.cs    ResolveModelId → LoadOllamaModel()
```

So the transport layer decides application policy, and it decides it from a key that means "the image
model" everywhere else.

---

## 3. The roles

Exactly four, one per service. `generate` (§1.1) is a fifth role with an image model and is out of scope.

| role | page | service | modality | provider persisted? | model persisted? |
|---|---|---|---|---|---|
| `observe` — image → observation | Idea to Prompt, Image source | `VisionObservationService` | **vision** (the only one) | no | yes, `imggen.ollama_vision_model` / `imggen.openrouter_vision_model` |
| `promptWrite` — idea → prose → V4 JSON | Idea to Prompt (both passes) | `Anthropic/PromptBuilderService` | text | **yes**, `imggen.prompt_writer_tier` | no — borrows `imggen.ollama_model` |
| `mutate` — caption → variants | Mutation Engine | `Mutation/CaptionMutationLlmService` | text | no | no — borrows `imggen.ollama_model` |
| `enrich` — V4 → region text | Ideogram structure editor | `Mutation/EnrichRegionsLlmService` | text | no | no — borrows `imggen.ollama_model` |

Modality is a measured fact, not an assumption: image-reference count per service is
`VisionObservationService` 9, every other LLM service **0**.

---

## 4. What the planning pass turned up

1. **The observer can be handed a model that cannot see.**
   `VisionObservationService` resolves `requestedModel → LoadOllamaVisionModel() → LoadOllamaModel()`.
   The last term is the *image generation* model. A comment above it explains that a hardcoded
   fallback was removed because it named a non-vision model and "failed as an opaque 404" — the chain
   still ends at a value that can be exactly that.
2. **Only one role persists its provider.** `imggen.prompt_writer_tier` exists. Mutation Engine
   (`_selectedModelTier = ModelTier.Sonnet`) and the structure editor (`EnrichTier`) re-default on
   every launch, so a paid/expensive tier is one forgotten click away after restart.
3. **Per-role persistence is already the established pattern**, which makes §1.4 cheap: the naming
   convention (`imggen.prompt_writer_tier`) and the load-on-construct / persist-on-change shape both
   exist to copy.
4. **`Sonnet` and `Opus` are constants in three files** (`claude-sonnet-4-6`, `claude-opus-4-8`
   duplicated in all three services), and `PromptBuilderService` additionally exposes
   `public const string ModelId = OpusModelId`. Collapsing the two tiers into one `Anthropic` provider
   needs exactly one home for these, plus a default.
5. **`ModelTier` is load-bearing:** 79 references in the app, **116 in tests**. Deleting it is a
   mechanical but broad sweep — hence phases (§6), not one commit.
6. **Cost copy depends on the tier.** `MutationEngineViewModel` estimates
   `SelectedModelTier == Opus ? OpusPerCallUsd : SonnetPerCallUsd`. With a model *id* instead of a
   tier, price has to come from somewhere: either a small known-id table or dropping the estimate to
   "paid per call on Anthropic". Do not discover this mid-sweep.
7. **`OpenRouterModelInfo` carries no vision flag.** The modality test
   (`architecture.input_modalities` contains `image`) happens inside `ParseVisionModels`, which then
   throws the answer away. An unfiltered list that *marks* image-capable rows needs that flag carried
   out — the computation already exists, so this is a field, not an algorithm.
8. **Anthropic has no catalog and no Settings card.** Its key is a row in the *Provider API keys*
   grid (`TokenProviders`, built in `GeneratorViewModel`). Listing Anthropic models means a new
   catalog + transport call; until then the Anthropic model box is a typed entry with a default, like
   the old `AnthropicVisionModel` was.
9. **Anthropic models take images**, so `observe` is the one role where a third provider is not
   speculative — but it needs a Messages-API-with-image branch that does not exist
   (`SendOpenRouterVisionAsync` is `private static` inside the vision service).
10. **The inline status labels just landed are copied six times** across four pages, plus the
    `Picker + Refresh + status` trio four times. Whatever shape is chosen here absorbs both.

### Found while implementing Phase A

11. **Both unload calls freed the wrong model.** `MutationEngineViewModel` and
    `IdeaToPromptViewModel` freed `_settings.OllamaModel` after a Local run — the image model — while
    the model they had actually loaded was named by a different role. Nowhere near the reported bug,
    found by grepping for every read of the shared value.
12. **The vision fallback existed twice.** `VisionObservationService` ended its resolution chain with
    `LoadOllamaModel()` and `GeneratorSettingsReader.OllamaVisionModel` re-implemented the same borrow
    in C#. Two layers both guessing, in the one place the plan says nobody should guess.
13. **`IdeogramStructureEditorViewModel.EnrichLocalModel` was dead code that lied**: unbound in XAML,
    returning the generation model under a name claiming it was the enricher's. Deleted.
14. **New role pickers inherit two hazards from the pickers they sit beside**, because they share
    `OllamaModels` as their ItemsSource: a refresh clears that list and every bound Picker pushes null
    back (so the three new properties join the existing suppression window rather than getting their
    own), and a model the server stopped listing must be dropped from all four pickers, not just the
    generation one (so the refresh reconciles and persists them too).

---

### Found while implementing Phase B (the seam)

15. **Three services asked Anthropic for a key regardless of provider.** Each carried
    `if (tier != ModelTier.Local) apiKey = await _tokenStore.LoadAsync()` — correct while Anthropic was
    the only hosted provider, silently wrong the moment a second appeared: an OpenRouter pick would have
    demanded an Anthropic key and refused to run. Now one `LlmCredentials.ResolveAsync(provider, ...)`
    picks the store per provider and owns the "No {provider} API key" sentence.
16. **There was no OpenRouter text transport at all** — only `VisionObservationService`'s inline vision
    call. `OpenRouterChatTransport` is new: `/api/v1/chat/completions`, system prompt as a leading
    `system` message, `response_format` json_schema when a schema is supplied, fences stripped. It
    reuses the already-registered `"openrouter"` named client (120 s per attempt, resilience, remote
    logging) — same string as `VisionObservationService.OpenRouterHttpClientName`.
17. **The services' own tests could not see the transport router.** They inject a fake completion, and
    the per-provider switch lives in the *production* completion builder, so "the service re-derived the
    model" and "the OpenRouter call went to Anthropic" both passed every test that existed. Fixed with
    `LlmTransportRoutingTests`, which drives the real completion against a recording `HttpMessageHandler`
    and asserts endpoint, path, `model` field, and which key (if any) was sent.
18. **`GeneratorSettingsReader` now carries a deliberate, named temporary** — `PromptWriterTarget(tier)`,
    `MutationTarget(tier)`, `EnrichTarget(tier)` translate the three-value picker each page still shows
    into an `LlmTarget`. It exists so the seam could land without dragging four XAML surfaces along; the
    `ModelPickerRow` work replaces it and these three methods go with it.

## 5. Target design

### 5.1 Contract

```csharp
public enum ModelProvider { Local, Anthropic, OpenRouter }

/// One resolved model call target. The caller (view model) owns it; services stop
/// reading application settings to find out which model the user asked for.
public sealed record LlmTarget(ModelProvider Provider, string ModelId);
```

The four service contracts take `LlmTarget` in place of `ModelTier` and stop calling
`IUiStateStore.LoadOllamaModel()`. Their transport seams
(`LlmTransport(tier, modelId, apiKey, baseUrl, …)`) already carry all four, so provider routing is a
new branch, not a new seam — that seam was built for exactly this fan-out.

`ModelTier` is deleted, not kept alongside: two enums describing the same choice is how the current
one ended up meaning both "provider" and "one specific model".

### 5.2 Persistence — one pair of values per role

| role | provider key | model key | seeded from |
|---|---|---|---|
| `observe` | `imggen.observe_provider` | `imggen.observe_model` | current `ollama_vision_model` / `openrouter_vision_model` + which list was non-empty |
| `promptWrite` | `imggen.prompt_writer_provider` | `imggen.prompt_writer_model` | `prompt_writer_tier` → provider; `ollama_model` → model |
| `mutate` | `imggen.mutation_provider` | `imggen.mutation_model` | default `Anthropic` + `claude-sonnet-4-6`; `ollama_model` → model |
| `enrich` | `imggen.enrich_provider` | `imggen.enrich_model` | default `Anthropic` + `claude-sonnet-4-6`; `ollama_model` → model |

Seeding rule, so §1.4 holds: *if the role's new key is absent and the old shared value is present,
adopt it once and write it.* Never re-seed, never overwrite.

`imggen.ollama_model` stays as the **generation** model's value (§9). It stops being read by any LLM
service in Phase A, which is what actually breaks the cross-talk.

### 5.3 `ModelPickerRow` — one control, four pages

Bindables: `Providers`, `SelectedProvider`, `Models`, `SelectedModel`, `RefreshCommand`, `IsBusy`,
`Status`, `Helper`. It renders exactly what the pages hand-roll today — provider field, model picker,
Refresh, and the `NonEmptyStringToBool`-gated status label — so the six status labels added this week
stop being six copies. Refresh is per provider: the row asks its host for the provider's list, so a
page never refreshes a list it is not showing.

### 5.4 List and credential sources per provider

| provider | model list | refresh | credential |
|---|---|---|---|
| Local (Ollama) | `Settings.OllamaModels` (all) / `OllamaVisionModels` for `observe` — both already exist | `RefreshOllamaModelsCommand` | none |
| Anthropic | *new*: typed entry + catalog if/when one exists | catalog, or none in Phase A | Anthropic row, Provider API keys |
| OpenRouter | *new*: `ListModelsAsync(freeOnly, visionOnly)` — one call, filtered for `observe`, unfiltered + flagged for text roles | same command, per provider | OpenRouter row / token store |

The list itself stays a connection-side concern in `AppSettingsCoordinator`: sharing *a list of what a
server has* is not the disease of §2; sharing *a choice* is.

---

## 6. Phases

### Phase A — stop the cross-talk · **DONE**

Shipped: three role-scoped model keys (`imggen.prompt_writer_model`, `imggen.mutation_model`,
`imggen.enrich_model`) seeded once by the store from `imggen.ollama_model` and only when the role's
key has never been written (this store reports `""` as null, so "cleared" is distinguished from
"never set" by `ContainsKey`); the three services read their own key; the observer's fallback chain
ends at "nothing picked"; each page's Local picker binds its role's value; the refresh reconciles all
four picks; both unload calls free the model that was actually loaded. Two roles still forget their
*tier* across launches (§4.2) — Phase B owns that, since tier becomes provider there.

Deliberate: the store read stays inside the service. Inverting it to a caller-supplied `LlmTarget` is
§5.1 and belongs to Phase B, where the 116 test references get swept once instead of twice.

Verified by falsification, three ways: pointing the mutator back at `LoadOllamaModel()` (2 failures),
deleting the seeding (1), and making a role persist into the generation key (1). The third escaped
the first time — the persist test covered one role, so it became a `[Theory]` over all three. Worth
keeping as a method note: **check the regression actually reached the file before trusting a green
run**; one of these "passed anyway" readings was a write that had not landed.

<details><summary>original step list</summary>

1. `UiStateStore` + `IUiStateStore`: three model keys (`prompt_writer`, `mutation`, `enrich`), seeded
   from `ollama_model` when absent.
2. The three services read their own key. One-line change each, three tests each.
3. Drop the `→ LoadOllamaModel()` tail of the observer's fallback chain (§4.1); the empty case already
   answers "Pick a local vision model first".
4. Each page's Local-tier picker binds its role's value instead of `Generator.Settings.OllamaModel`.
5. Persist the two roles that forget (§4.2).
6. Gate: full suite, plus a new test per role of the form *"changing the generation model does not
   change the model this role calls"*.

</details>

### Phase B1 — the seam · **DONE (2026-11-21)**

`LlmTarget { Provider, ModelId, BaseUrl }` + `ModelRole { PromptWrite, Mutation, Enrich, Vision }` are
now the contract every LLM service takes; `ICaptionMutationLlmService`, `IEnrichRegionsLlmService` and
`IPromptBuilderService` no longer read the store or know a single model id. `UiStateStore` persists a
provider and a model per role (`LoadLlmTarget(role)`), Local memory mapping back onto the Phase A keys
so a pick made before this change survives. Anthropic ids come from `AnthropicModels.DefaultFor(role)`
instead of being hardcoded in each service; OpenRouter has **no** default slug on purpose — an
unpicked OpenRouter model is a "pick one first" refusal, not a guess that bills somebody.

Deliberately **not** in B1: the pages still show `Sonnet | Opus | Local`. The bridge in
`GeneratorSettingsReader` (§4.18) translates it, and the cost copy in `MutationEngineViewModel` still
speaks in tiers. B2 lands `ModelPickerRow` and deletes both.

Falsification of B1, in order:

- restored the Anthropic-key-for-every-provider bug in `LlmCredentials` → failed exactly the three
  `..._WithoutAnOpenRouterKey_NamesOpenRouter` tests (the only ones holding both keys distinct).
- made the caption service pass `AnthropicModels.Sonnet` instead of `target.ModelId` → **escaped**: no
  test could see it, because the seam tests replace the very code the mutation touched (§4.17). Wrote
  `LlmTransportRoutingTests`, re-ran → failed exactly
  `ProductionRouter_CallsTheModelTheTargetNamed(Anthropic, "claude-haiku-5")`.
- writing those wire tests found the tests' own bug first: the canned Anthropic reply was missing a
  closing brace, so the wire test failed on its own fixture before it proved anything. Check the
  regression reaches the file *and* that the fixture is right.

### Phase B — provider + model, one control
`ModelProvider`/`LlmTarget` replace `ModelTier` (79 + 116 refs); services stop reading settings;
`ModelPickerRow` extracted and adopted on all four pages; OpenRouter `visionOnly` flag + text
transport branch; Anthropic entry with one home for the model ids; cost copy resolved (§4.6).

### Phase C — Settings shrinks to connections
Delete the three model pickers and their helper prose, keep base URLs / keys / free-only, retire the
`observe` model's old keys now that the role owns them, update `UiMarkupContractTests` and the release
notes.

---

## 7. Hazards (each checked in source)

- **116 test references to `ModelTier`.** Sweep in one Phase-B commit with a test helper that builds an
  `LlmTarget`, or the diff becomes unreadable.
- **`Settings.OllamaModel` has 8 app + 21 test references.** Phase C removes the *bindings*, not the
  value — it is still the generation model.
- **`UiMarkupContractTests` reads built `TestAssets`.** A stale build passes for the wrong reason;
  always `dotnet build` before `dotnet test` when XAML changed.
- **Cost estimate breaks silently** (§4.6) — it compiles fine while lying about price.
- **Observer fallback removal changes an error path** with existing tests around it.
- **GPU colocation gate** reads `Settings.OllamaBaseUrl` for `SameHost`; base URLs are not moving, but
  any change here must leave that gate alone.
- **`PromptBuilderService.ModelId` is public** — check its users before Phase B deletes it.
- **Scripts write LF into a CRLF tree.** `core.autocrlf=true` with no `.gitattributes`, so an LF
  working file neither fails a build nor shows in `git diff` — it just silently retrades line endings
  on the next checkout. Every file touched here was re-normalised to CRLF; check the endings, do not
  assume them.

---

## 8. Verification

```
dotnet build ImageGenerator.MAUI/ImageGenerator.MAUI.csproj -c Debug     # 0 warnings expected
grep -c MAUIG2045 <build log>                                            # repath checklist, must stay 0
dotnet test ImageGenerator.MAUI.Tests/ImageGenerator.MAUI.Tests.csproj -c Debug
```

Gate after Phase B1: **1,605 passed / 0 failed / 0 skipped**, app builds at 0 warnings and 0
`MAUIG2045`. Manual matrix per role: pick → restart → still picked; change another role → this one
picked; change another role → this one unchanged; kill the server → the row explains it in place.

Pre-existing and not from this work: `ComfyUiImageGenerationServiceTests.Ws_CanceledDuringReceive_
StillNotifiesServerCancel` fails every time it is run alone and passes inside the full run — an
isolation bug in that test, untouched here and worth its own fix.

---

## 9. One open question for the owner

`generate` is the image model, and its **local (Ollama) image model** can currently only be chosen in
Settings (`MainPage.xaml` has no Ollama picker at all — verified). Under §1.2 that box should move to
the page where generation happens; under §1.1 `generate` is out of scope. Until decided, the Settings
picker stays and is relabelled as the generation model only. Recommend moving it onto the generate
card in Phase C.

## 10. Non-goals

No multi-model compare queue, no per-call model override, no Anthropic/OpenRouter catalogs beyond
what a role needs, no change to how images are generated or to which providers can *render*.
