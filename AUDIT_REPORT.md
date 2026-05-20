# ImageGenerator.MAUI — Comprehensive Audit Report

**Audit date:** 2026-05-20  ·  **Version under audit:** 1.3.0+ (post-NLog, post-F1–F10 fix bundle)  ·  **Target:** `net10.0-windows10.0.22621.0` MAUI (Windows-only)  ·  **Tests:** xUnit + FluentAssertions + Moq — **339 passing** (per `documents/continue.txt`)

---

## 1. EXECUTIVE SUMMARY

**Overall health: 9.0 / 10.** The codebase has matured noticeably since the 2026-05-19 audit. All ten previously-flagged findings (F1–F10) shipped — the dispatcher no longer mutates caller state, both token stores share a single tested `DebouncedSecureStorageWriter` with a locked CTS swap, the `MainPage`/`GeneratorViewModel` Singleton promotion plus `addInput` query-property eliminated the navigation state-loss class, `GeneratorViewModel` was carved into three sub-VM coordinators, both raw-HttpClient services now flow through `IHttpClientFactory` with named clients sharing the resilience pipeline, and the missing-tests backlog closed in three batches (ST3, catalog, store wrappers). The XamlC `XC0022` warnings closed alongside. Folder-based Clean Architecture remains clean, DI lifetimes are correct end-to-end, async/cancellation discipline holds, and the test surface is now thorough. What's left is largely cosmetic.

### Top findings by severity

| # | Severity | Finding | Files |
|---|---|---|---|
| 1 | **Moderate** | `ModelCatalogCoordinator.RefreshAsync` uses `replicateTask.Result.Concat(pollinationsTask.Result)` after `Task.WhenAll`. Functionally safe (both tasks have completed), but it reads like sync-over-async to anyone scanning the file and is the only `.Result` access in the codebase. Replace with idiomatic `await` capture. | `Infrastructure/Services/ModelCatalogCoordinator.cs:37-39` |
| 2 | **Minor** | `GenerationJob.ShowInFolder` still passes `Arguments = $"/select,\"{ResultPath}\""` (`GenerationJob.cs:62-68`). `FileLauncher.RevealInFolder` was migrated to `ProcessStartInfo.ArgumentList` (QW5, commit `8551d31`) but this second `explorer /select,…` call site was missed. `BuildFileName` strips invalid filename chars today, so no current exploit; consistency + defense-in-depth says route through `IFileLauncher.RevealInFolder` (already injected at the launcher level) or use `ArgumentList`. | `Presentation/ViewModels/GenerationJob.cs:62-68` |
| 3 | **Minor** | Asymmetric suppression-flag ownership across the F6 carve-up: `_suppressPreferredArUpdate` is host-owned + host-read (`GeneratorViewModel.cs:106,166-169,275-276`), while `SuppressModelPersist` is coordinator-owned + host-read (`ProviderFilterCoordinator.cs:34,73-83,100-108`; `GeneratorViewModel.cs:291`). Same architectural question answered two ways. The AR flag belongs on `InputImagesCoordinator` because the relevant write path (`setAspectRatioProgrammatically` callback) already lives there. | `Presentation/ViewModels/GeneratorViewModel.cs:106,166-169,275-276`; `Presentation/ViewModels/InputImagesCoordinator.cs:100-119` |
| 4 | **Minor** | `DispatchToUi` is duplicated verbatim in `GeneratorViewModel.cs:405-418` and `BatchCoordinator.cs:222-235` (and a slight variant in `GalleryViewModel.cs:304-317`). Both are identical six-line static methods. Extract to a single static helper (`Presentation/Common/UiDispatcher.cs` or similar). | `Presentation/ViewModels/GeneratorViewModel.cs:405-418`; `Presentation/ViewModels/BatchCoordinator.cs:222-235`; `Presentation/ViewModels/GalleryViewModel.cs:304-317` |
| 5 | **Minor** | Named-client string literals (`"replicate-download"`, `"pollinations"`) are declared as `internal const string HttpClientName` on the two services (`ReplicateImageGenerationService.cs:13`, `PollinationsImageGenerationService.cs:13`) but `MauiProgram.cs:60,65` still registers them with bare string literals. `PollinationsCatalogService.cs:31` *does* reference the constant correctly. Wire the same constant into the two `AddHttpClient(…)` calls to make the coupling traceable. | `MauiProgram.cs:60,65`; `Infrastructure/External/Replicate/ReplicateImageGenerationService.cs:13`; `Infrastructure/External/Pollinations/PollinationsImageGenerationService.cs:13` |
| 6 | **Minor** | Five `Infrastructure/Services` and `Infrastructure/External` classes are not `sealed` despite the codebase's sealed-by-default convention (memory + 2026-05-19 audit): `FileLauncher`, `ClipboardService`, `GalleryService`, `ImageEncoderProvider`, `ModelCatalogService`. None has subclasses; none has `protected virtual`. Sealing them matches the convention closed for `ReplicateImageGenerationService`/`ImageFileService` in commit `8551d31`. | `Infrastructure/Services/FileLauncher.cs:6`; `ClipboardService.cs:6`; `GalleryService.cs:15`; `ImageEncoderProvider.cs:18`; `ModelCatalogService.cs:11` |
| 7 | **Minor** | `ModelDescriptorRegistry.Default()` (`ModelDescriptorRegistry.cs:66-87`) hand-lists only the **nine** Replicate descriptors. Production registers **twelve** via `MauiProgram.AddModelDescriptor<T>` (the three `Pollinations*Descriptor`s are absent from `ProductionDescriptors()`). Tests that rely on `Default()` therefore route Pollinations IDs through `FallbackPollinationsDescriptor` instead of the concrete-typed descriptor under test. Specific-descriptor Pollinations behavior is currently tested only indirectly via the service-level fixtures. Add the three Pollinations descriptors to `ProductionDescriptors()` and add a `Default_RegistersAllProductionDescriptors_AndCountMatchesMauiProgram` test pinning the count. | `Core/Domain/Descriptors/ModelDescriptorRegistry.cs:76-87`; `MauiProgram.cs:75-86` |
| 8 | **Nit** | `InputImagesCoordinator.OnSelectedImagesChanged` (`:93-125`) fires `_mirrorImagePromptsToParameters()` on **every** `CollectionChanged`, including each programmatic remove from `TruncateToMaxInputs` (`:82-91`). Cap is small (≤14) and `ImagePrompts` is a tiny `ObservableCollection<string>`, so the cost is negligible — flagging for completeness, not for action. A `BeginBulkUpdate`/`EndBulkUpdate` shape would only matter if MaxImageInputs ever grows past ~50. | `Presentation/ViewModels/InputImagesCoordinator.cs:82-91,93-125` |

### Known / acknowledged (still applies, not new findings)

- `CommunityToolkit.Mvvm` referenced from `Core/Domain/Entities/ImageGenerationParameters.cs` is the documented single-`.csproj` trade-off (file comment at lines 8-12). No new disagreement on read.
- `ImageSharp 3.1.12` is pinned for license reasons — correctly not flagged.
- `GeneratorViewModel` is now ~553 lines after the F6 carve-up. The host responsibilities (parameter mediation, capability refresh, AR sticky orchestration, job runner, command surface, page-lifecycle hooks) are coherent. Not flagged as a new finding.
- `kontext`, `negative_prompt`, and Pollinations image-input remain open features per memory and `continue.txt`. Inert in current code, not in scope.

---

## 2. PER-FILE FINDINGS

### 2.1 Composition root / App / Shell

**`MauiProgram.cs` (131 lines)** — *Severity: minor*
- Named-client registration at lines 60-69 now flows the project's standard resilience pipeline (`ConfigureStandardResilience`) into both `"replicate-download"` and `"pollinations"`. Pipeline shape is right (retry on 5xx/408/429, jittered exponential, total > retry > per-attempt timeout). Cleanly documented at 57-58 why both reuse the same retry budget.
- Lifetime promotion comment at 113-118 explicitly justifies `MainPage` + `GeneratorViewModel` as Singleton against the previous F3 navigation drop. Pages registered transient elsewhere (`GalleryPage`, `GalleryItemDetailPage`) are correct — they're pushable routes.
- **Finding #5**: lines 60 and 65 hard-code `"replicate-download"` and `"pollinations"` instead of referencing the `HttpClientName` constants on the two services.
- Otherwise clean: forwarding-singleton descriptor pattern, Refit client, MEL → NLog bridge.

**`App.xaml.cs`** — *clean.* Centered-window CreateWindow override unchanged.

**`AppShell.xaml(.cs)`** — *clean.* Two registered routes (`gallery`, `detail`); both reachable from production code (`GalleryViewModel.OpenGalleryAsync`, `GalleryPage.OnShowMetadataClicked`).

**`Platforms/Windows/App.xaml(.cs)`** — *clean.* WinUI boilerplate.

**Extensions:**

**`Extensions/DescriptorServiceCollectionExtensions.cs`** — *clean.* Forwarding-singleton wiring of the four narrow interfaces; idiomatic and tightly typed.

**`Extensions/RefitServiceExtensions.cs` (146 lines)** — *clean.*
- `ConfigureStandardResilience` is now a public extension callable from any `IHttpClientBuilder` — that's the right shape post-F4. Internal Refit registration delegates to it (`:73-77`), eliminating duplicate Polly setup.
- Reordered-strategy comment at 109-112 makes the wrap order non-negotiable. `ResiliencePolicyTests` pins the contract.
- `NullSkippingDictionaryConverter` recursion guard is still elegant (line 56-65).

### 2.2 Core / Domain

**`Core/Application/Interfaces/*.cs`** — *clean.* Five narrow interfaces, all used by exactly one concrete.

**`Core/Application/Services/PromptBatchParser.cs`** — *clean.* BOM + CRLF + comment + cap handling all tested.

**`Core/Domain/Entities/GalleryItem.cs`** — *clean.* Record with optional metadata.

**`Core/Domain/Entities/GeneratedImage.cs`** — *clean as documented.* The "ImageData null means error" invariant remains undocumented at the type level; future MT4 (Result-shape refactor) is the right escape, not a §2 finding.

**`Core/Domain/Entities/ImageGenerationParameters.cs` (128 lines)** — *clean.* `OnWidthChanged`/`OnHeightChanged` clamp on the entity; `Clone()` reflection-tested by `ImageGenerationParametersCloneTests`. CTK comment at lines 8-12 acknowledges the trade-off (memory-known).

**`Core/Domain/Enums/ImageOutputFormat.cs`** — *clean.*

**`Core/Domain/Services/ImageDataUriEncoder.cs`** — *clean.* `stackalloc` tested for all five branches.

**`Core/Domain/ValueObjects/Flux/*.cs`** — *clean.* DataAnnotations + JsonPropertyName.

**`Core/Domain/ValueObjects/ModelCapabilities.cs`** — *clean.* Now lives in `Core/Domain/ValueObjects` (moved from Presentation in `8551d31`). The layering inversion is gone.

**`Core/Domain/ValueObjects/ModelOption.cs`**, **`Core/Domain/ValueObjects/Pollinations/PollinationsRequest.cs`** — *clean.* Sealed records.

**`Core/Domain/Descriptors/*.cs`** — *minor (see Finding #7).* The descriptor pattern itself is clean; `ModelDescriptorRegistry.Default()` lists 9 of the 12 production descriptors. Fix is a 3-line edit in `ProductionDescriptors()`.

**`Core/Domain/Descriptors/Pollinations/*.cs`** — *clean.* `PollinationsDescriptorBase` Template Method + `FallbackPollinationsDescriptor` sentinel pattern, consistent with the Replicate side.

### 2.3 Infrastructure — External

**`Infrastructure/External/Replicate/Interfaces/IReplicateApi.cs`** — *clean.* No `Prefer: wait` (documented at 7).

**`Infrastructure/External/Replicate/ReplicateHelper.cs`** — *clean.* Polling loop fully branch-tested.

**`Infrastructure/External/Replicate/ReplicateImageGenerationService.cs` (172 lines)** — *clean.*
- Class is now `sealed` (F10 closed in `8551d31`); `DownloadImageAsync` is private.
- `IHttpClientFactory` injection (F4) wired correctly; `CreateClient(HttpClientName)` per call, `using` scoped (lines 153-170). Polly's pipeline now wraps the CDN download. Inline `CancelAfter` is gone — the per-attempt timeout in the named-client config is the only ceiling.
- `FormatError` redaction (lines 95-128) intact.
- **Finding #5** applies: `HttpClientName = "replicate-download"` const declared at line 13 but `MauiProgram` does not consume it.

**`Infrastructure/External/Replicate/ReplicateModels.cs`** — *clean.* `Input` now `Dictionary<string, object?>?` (QW4 closed, line 24).

**`Infrastructure/External/Replicate/ReplicateOutputConverter.cs`**, **`ReplicateStatus.cs`** — *clean.*

**`Infrastructure/External/Pollinations/PollinationsImageGenerationService.cs` (193 lines)** — *clean.*
- Seed clamp now correct (`request with { Seed = … }` on the immutable `PollinationsRequest` record at line 61). F1 regression closed; `parameters` no longer mutated.
- `IHttpClientFactory` plumbing identical to Replicate side, including `CreateClient(HttpClientName)` + per-call `using`.
- Body-on-error read (lines 81-110) gives the user actionable text. `Redact` on URL + body + UI message line covers every surface where the token could surface.
- **Finding #5** applies: `HttpClientName = "pollinations"` const at line 13 not consumed by `MauiProgram`.

**`Infrastructure/External/Pollinations/PollinationsCatalogService.cs`** — *clean.*
- Correctly references `PollinationsImageGenerationService.HttpClientName` (line 31) — sole correct usage of the constant.
- Filter to `OutputModalities = "image"` + `PaidOnly != true` + non-empty name (lines 36-38) is the right shape.
- Display-name derivation from description prefix vs slug fallback (lines 54-71) is fully tested.

### 2.4 Infrastructure — Diagnostics

**`Infrastructure/Diagnostics/CrashLogger.cs` (160 lines)** — *clean.*
- NLog config is now the central authority (file + debugger targets, rule-graduated `Info+` baseline, `Infrastructure.External.*` lifted to `Debug`).
- `Install(rootDir)` test overload (line 38) preserves the seam `CrashLoggerTests` relies on.
- AppDomain + TaskScheduler + WinUI `Application.UnhandledException` hooks all guarded by `_hooksInstalled`. `e.Handled = true` on the WinUI hook is the documented decision (tracing > failing fast).
- `[Collection(DisableParallelization = true)]` on `CrashLoggerTests` is the right pattern (memory-recorded) — static NLog targets are process-wide.

### 2.5 Infrastructure — Services

**`Infrastructure/Services/ApiTokenStore.cs` (52 lines)** — *clean.*
- `_writer` is the only mutable state; F2 race fully delegated to the shared `DebouncedSecureStorageWriter`.
- `ISecureStorage` ctor seam (line 16, defaulting to `SecureStorage.Default`) matches the project's clock-seam idiom. MS.DI honours the default-null pattern because the interface isn't registered.

**`Infrastructure/Services/PollinationsTokenStore.cs` (51 lines)** — *clean.* Mirror of `ApiTokenStore` with a different key and slightly different log scope; could collapse into a single `SecureStorageTokenStore` parameterised on key + log scope, but the duplication is now contained to ~20 lines of shape so the abstraction earns less than it costs. (Original ST2 plan effectively superseded by extracting `DebouncedSecureStorageWriter`.)

**`Infrastructure/Services/DebouncedSecureStorageWriter.cs` (72 lines)** — *clean.*
- The `_lock` (line 22) is the F2 fix: the CTS swap (lines 40-53) holds the lock so two callers can't race over `_cts?.Cancel(); _cts?.Dispose();`. `var token = _cts.Token` captured *inside* the lock so the `Task.Run` continuation observes a non-disposed source. Three tests pin this contract.
- `OperationCanceledException` separately caught from "any other Exception" (lines 62-69) — the cancel-mid-debounce path stays silent; only real persistence failures log.

**`Infrastructure/Services/UiStateStore.cs` (78 lines)** — *clean.*
- `IPreferences` ctor seam matches the token-store pattern (line 15).
- `SafeGet` ContainsKey-then-Get rationale documented at lines 51-58. Defensive logging at every entry.

**`Infrastructure/Services/ClipboardService.cs`** — *minor.* One-line wrapper around MAUI Clipboard. Sealing it (Finding #6) would match the rest of the codebase.

**`Infrastructure/Services/FileLauncher.cs` (33 lines)** — *minor.*
- `RevealInFolder` uses `ArgumentList.Add($"/select,{path}")` (line 30) — QW5 closed. Note that `ArgumentList[0]` here is still a single concatenated `/select,<path>` argument rather than `["/select", path]`; both shapes work for `explorer.exe` and the former matches the documented invocation. No new finding, just observation.
- Class is not `sealed`: Finding #6.

**`Infrastructure/Services/GalleryService.cs`** — *minor.* `IAsyncEnumerable<GalleryItem>` with `[EnumeratorCancellation]`, in-flight guard, `Image.IdentifyAsync` (no pixel decode), first-colon split for prompts-with-colons. Excellent. Class is not `sealed`: Finding #6.

**`Infrastructure/Services/ImageEncoderProvider.cs`** — *minor.* `or _` future-proofs the switch. Not `sealed`: Finding #6.

**`Infrastructure/Services/ImageFileService.cs` (127 lines)** — *clean.*
- Now `sealed` (F10 closed in `8551d31`).
- `BuildFileName` `Replace("__", "_")` single pass at line 97 — `___` → `__`. Latent edge case but a sanitised prompt + clamped length means a triple-underscore is essentially impossible.
- `GetUniqueSavePath` glob-based collision detection (lines 115-117) is the right call.

**`Infrastructure/Services/ImageGenerationDispatcher.cs` (36 lines)** — *clean.*
- F1 closed: dispatcher is now purely routing; no mutation. Single `IsPollinations` check uses `ModelConstants.Pollinations.IsId(parameters.Model)` (line 30) — QW6 closed.
- 4 direct routing tests in `ImageGenerationDispatcherTests`.

**`Infrastructure/Services/JobRunner.cs` (71 lines)** — *clean.*
- F7 closed: try/catch around `SaveImageWithMetadataAsync` (lines 46-67) with best-effort `File.Delete` cleanup and dedicated logging for the cleanup failure case.
- 6 direct tests in `JobRunnerTests`.

**`Infrastructure/Services/ModelCatalogCoordinator.cs` (60 lines)** — *moderate (Finding #1).*
- `await Task.WhenAll(replicateTask, pollinationsTask)` immediately followed by `replicateTask.Result.Concat(pollinationsTask.Result)` (lines 37-39). Safe in isolation but the project has zero other `.Result`/`.Wait()` accesses, so the pattern stands out. Recommend:
  ```csharp
  await Task.WhenAll(replicateTask, pollinationsTask);
  var fetched = (await replicateTask).Concat(await pollinationsTask).ToList();
  ```
  Or, since the tasks are already awaited:
  ```csharp
  var replicateModels = await replicateTask;
  var pollinationsModels = await pollinationsTask;
  ```
  The latter loses parallelism — keep `WhenAll` + double-`await`.
- `RefreshAsync` doesn't take a `CancellationToken` (interface doesn't define one). Could add for symmetry with `LoadCachedAsync`; deferred until a caller needs cancellation.

**`Infrastructure/Services/ModelCatalogService.cs`** — *minor (sealing only).* Allowlist + atomic-move semantics unchanged. Finding #6 applies.

### 2.6 Presentation — ViewModels

**`Presentation/ViewModels/GeneratorViewModel.cs` (553 lines)** — *moderate as a whole; individual findings minor.*
- Post-F6, the host VM now does: parameter wiring (`_parameters.PropertyChanged` switch at lines 269-298), capability hydration (`RefreshCapabilities` 124-162), single-job orchestration (`GenerateImageAsync` + `RunJobAsync` 324-396), command surface (Refresh / OpenFolder / OpenGallery / ForgetToken), and one-shot OnAppearing hydration helpers (`LoadAllTokensAsync` 484-493, `LoadSavedUiState` 502-518, `LoadCachedCatalogAsync` 520-539). Three of the previously-tangled responsibilities (input images, provider filter, batch) now live on the sub-VMs. Size is healthy.
- **Finding #3**: `_suppressPreferredArUpdate` (lines 106, 166-169, 275-276) is the last remnant of the asymmetric suppression pattern. The write path that needs it is `SetAspectRatioProgrammatically`, which is itself a callback into the coordinator (line 250) — moving the flag into `InputImagesCoordinator` would make ownership uniform with `SuppressModelPersist`.
- **Finding #4**: `DispatchToUi` at lines 405-418 — verbatim duplicate of `BatchCoordinator.cs:222-235`.
- `Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue)` at lines 242, 337 — the third call site (batch) moved to `BatchCoordinator.cs:140`. Two remaining call sites are fine — extracting now would be premature.
- `IsPollinationsSelected` (line 197) uses `ModelConstants.Pollinations.IsId(…)` consistently.

**`Presentation/ViewModels/InputImagesCoordinator.cs` (217 lines)** — *clean.*
- Constructor takes 4 callbacks (caps accessor, AR set, mirror, status). The shape is intentional: it inverts the dependency from "coordinator knows host" to "host hands functions in". Calling out one extraction candidate: `(Action<string, StatusKind> setStatus)` could be a one-method `IStatusSurface` interface that the host implements — would also make stubbing simpler in tests. Not a finding; just an observation.
- `OnSelectedImagesChanged` (93-125) state machine reads cleanly. `_lastImageCount` mutation at line 120 is correct (after the AR write, not before, so the next collection-change sees the right transition).
- 7 fixture tests cover ctor + truncate + auto-AR + image-add.

**`Presentation/ViewModels/ProviderFilterCoordinator.cs` (150 lines)** — *clean.*
- `SuppressModelPersist` public-getter / private-setter (line 34) is the right shape: ApplyCatalog / RestoreSelectedModel own the flip; the host reads it from a single PropertyChanged hook. Three try/finally blocks ensure the flag never sticks.
- `OnSelectedProviderChanged` / `OnSelectedModelChanged` (55-64) drive `RecomputeFilteredModels` and the host's `RefreshCapabilities` respectively. Both partial methods are minimal.
- 8 fixture tests cover the catalog application path, suppression-flag flip-and-clear, saved-state restore, and cross-provider sync.

**`Presentation/ViewModels/BatchCoordinator.cs` (237 lines)** — *minor (Finding #4 only).*
- Re-entrancy guard at line 125 (`if (IsBatchRunning) return;`). Combined with the per-batch `_batchCts` (line 29) the lifetime story is clean: ctor never touches CTS; `RunBatchAsync` allocates and disposes in finally; `CancelBatch` swallows `ObjectDisposedException` from the late-click race.
- Batch-cancel-drains-queue contract documented at 14-17 and 182-185, plus the `CancelBatch_LetsInFlightJobFinish_AndDrainsQueue` test (memory-confirmed: this is the deliberate behaviour for paid Replicate predictions).
- DispatchToUi duplicate at 222-235 — Finding #4.
- 5 fixture tests covering empty-batch / re-entrancy / cancel-drain.

**`Presentation/ViewModels/GalleryViewModel.cs`** — *clean.*
- `IDisposable`, `FileSystemWatcher` lifecycle, debounced refresh with locked CTS swap (`_debounceGate`), AAA-tested. The `_lastSnapshot` field is read on the UI thread (sort change) and written on the UI thread (continuation of `RefreshAsync`) — race-free in practice. A future off-thread sort would need a `lock` or immutable swap.
- `DispatchToUi` at 304-317 — Finding #4.

**`Presentation/ViewModels/GalleryItemDetailViewModel.cs`** — *clean.* `FlashAsync` defends against late overrides.

**`Presentation/ViewModels/GenerationJob.cs`** — *minor (Finding #2).*
- `Cts` owned + disposed by the parent VM (`GeneratorViewModel.RunJobAsync` finally at line 394). Late-click race on `Cancel()` swallows `ObjectDisposedException`.
- `ShowInFolder` (62-68) — `/select,"…"` raw-string concatenation. Two paths:
  1. Inject `IFileLauncher` and call `RevealInFolder(ResultPath)` (the right MVVM shape but `GenerationJob` currently has no DI — it's a parent-VM-constructed item).
  2. Switch the local `Process.Start` to `ProcessStartInfo.ArgumentList.Add($"/select,{ResultPath}")` matching `FileLauncher.RevealInFolder`.
- 11 fixture tests already cover GenerationJob; one more for ShowInFolder argument-list shape would close the loop.

**`Presentation/ViewModels/ModelCapabilities.cs`** — *moved to `Core/Domain/ValueObjects/`* in commit `8551d31`. Now actually doesn't live here; the reference is retained in the prior audit's index because the file path changed. Confirmed via grep: `Core/Domain/ValueObjects/ModelCapabilities.cs` is the current location.

**`Presentation/ViewModels/StatusKind.cs`** — *clean.*

**`Presentation/ViewModels/TokenProviderViewModel.cs`** — *clean.* `_suspendCallbacks` re-entrancy guard tested by `GeneratorViewModelTests.TokenProvider_*`.

### 2.7 Presentation — Views

I walked every `Binding Path=…` in `MainPage.xaml` and `GalleryPage.xaml` against the typed `x:DataType`. Post-F6, the nested binding paths now traverse coordinator sub-properties (`InputImages.*`, `ProviderFilter.*`, `Batch.*`); every one resolves cleanly through the typed context.

**`Presentation/Views/MainPage.xaml(.cs)`** — *clean.*
- Page `x:DataType="vm:GeneratorViewModel"` (line 10) — host VM is bound; coordinator sub-properties (`InputImages`, `ProviderFilter`, `Batch`) are public on the host (`GeneratorViewModel.cs:45-46,80`), so paths like `InputImages.SelectedImages` (line 96), `ProviderFilter.FilteredModels` (line 160), `Batch.IsBatchRunning` (line 311) all resolve.
- Inner `Picker.ItemDisplayBinding`s have explicit `x:DataType="vm:TokenProviderViewModel"` (line 59) and `x:DataType="vo:ModelOption"` (line 164) — the XC0022 close.
- `DataTemplate x:DataType="vm:InputImagesCoordinator+InputImageItem"` (line 101) resolves; `DataTemplate x:DataType="vm:GenerationJob"` (line 425) resolves. Both are reachable through the typed host context.
- `AddInputPath` query property (xaml.cs lines 24-34) is the F3 successor — fire-and-forget into `InputImages.AddAsInputAsync`. Status surface internal to the coordinator means the page never has to log here.
- `OnAppearing` (49-64) is async-void with try/catch, calls the three idempotent hydrate methods (each has a `_…Loaded` guard). F8 closed.

**`Presentation/Views/GalleryPage.xaml(.cs)`** — *clean.*
- Page `x:DataType="vm:GalleryViewModel"`; DataTemplate `x:DataType="entities:GalleryItem"` (line 76). Every `Binding` inside the template resolves to `GalleryItem.FileName` / `FilePath` / etc.
- Lifecycle hooks route through VM's `OnAppearingAsync`/`OnDisappearing`.

**`Presentation/Views/GalleryItemDetailPage.xaml(.cs)`** — *clean.* `NavPath` query-property setter unescapes the path; `OnAppearing` triggers `LoadAsync` with a try/catch.

### 2.8 Presentation — Behaviors / Converters

**`Behaviors/NumericOnlyBehavior.cs`** — *clean.* Subscribe/unsubscribe lifecycle correct.

**`Converters/*.cs`** (six) — *clean.* `ShellThumbnailConverter` and `ShellPreviewConverter` documented for the WIC-decode-crash avoidance.

### 2.9 Shared / Constants

**`Shared/Constants/ModelConstants.cs`** — *clean.* `Pollinations.IsId(string?)` (lines 37-39) is the canonical helper; three former-duplicate call sites all consume it (QW6 closed).

**`Shared/Constants/OutputPaths.cs`**, **`ProviderConstants.cs`**, **`ValidationConstants.cs`** — *clean.* No magic literals in the audited code that don't trace back here.

---

## 3. ASYNC / CANCELLATION HYGIENE — OVERALL

- **`CancellationToken` plumbing:** consistent through every async public method. `IModelCatalogCoordinator.RefreshAsync` is still the lone exception (no `ct` parameter); the called services internally have no token plumbing for that path so adding one is a wider change. Deferred.
- **`async void` cases:** confined to page event handlers (`OnAppearing`, `OnDisappearing`, `OnImportPromptsClicked`, `OnImageDropped`, `OnAboutClicked`, `OnShowMetadataClicked`, `OnTileTapped`). Every one has try/catch with `_logger.LogError`. Correct.
- **Fire-and-forget tasks:** three legitimate cases — `DebouncedSecureStorageWriter.Schedule` (line 55, catches OperationCanceledException + Exception with log), `GalleryViewModel.TriggerDebouncedRefresh` (line 275, same pattern), and `GeneratorViewModel.FlashAsync` invocations from commands (line 345, awaited inside the helper). All observe their exceptions.
- **`.Result` / `.Wait()` / `.GetAwaiter().GetResult()`:** one occurrence, `ModelCatalogCoordinator.cs:39` (Finding #1). Safe in context but stylistically the only crack.
- **`ConfigureAwait(false)`:** intentionally absent per memory + project convention. Not flagged.
- **`OperationCanceledException` discipline:** every async service catches separately from `Exception`, returning a `Canceled` `GeneratedImage` (`ReplicateImageGenerationService.cs:78-85`, `PollinationsImageGenerationService.cs:132-139`). The VM's `RunJobAsync` catches OCE separately (`GeneratorViewModel.cs:374-381`). Per-job + per-batch CTS lifetimes are cleanly scoped and disposed in finally.

---

## 4. LOGGING (post-NLog)

Still one of the cleaner logging surfaces in the codebase's history:
- Structured placeholders consistently used (`"User {UserId} did {Action}"`-style). `PollinationsImageGenerationService.cs:95-100` is the most information-dense example (status code + URL + body, all redacted via `Redact`). 
- Log levels graduated: `LogDebug` for state-store reads/writes (UiStateStore), `LogWarning` for transient/recoverable failures (SecureStorage read failures, model catalog fetch failures), `LogError` for unrecoverable.
- `CrashLogger` static fallback and DI-injected `ILogger<T>` both write through NLog's `LogManager.GetLogger(...)` to one physical `app.log`. The `Infrastructure.External.*` rule at `CrashLogger.cs:103-107` lifts wire-level diagnostics to Debug without flipping the global level.
- `JobRunner.cs:39, 65` adds the architectural-boundary log so a swallow-and-return-Message in a service never disappears.

No findings.

---

## 5. NULLABLE REFERENCE TYPES

`<Nullable>enable</Nullable>` enforced in both csproj. `!` suppression-operator usage remains limited to the JSON-converter context (`ReplicateOutputConverter.cs:20`, `result.GetString()!` after the token-type check). NRT-aware guards (`?? throw new ArgumentNullException`) consistent across services and VMs. No findings.

---

## 6. DEPENDENCY INJECTION — DETAILED CHECK

Rebuilt walk of `MauiProgram.cs:54-127` against every consumer (the `MainPage` + `GeneratorViewModel` lifetimes flipped from Transient → Singleton in commit `c446d35`, invalidating the prior audit's table):

| Service | Lifetime | Consumers | Status |
|---|---|---|---|
| `IReplicateApi` (Refit) | Transient (Refit default) | `ReplicateImageGenerationService` (Singleton), `ModelCatalogService` (Singleton) | OK — Refit's typed-client instances are designed to be safe to capture (stateless wrappers around the configured HttpMessageHandler chain). |
| Named `HttpClient` `"replicate-download"` | Transient (factory) | `ReplicateImageGenerationService.DownloadImageAsync` via `IHttpClientFactory.CreateClient(...)` | OK — created + `using`-disposed per call; `ConfigureStandardResilience(60s, 3m)`. |
| Named `HttpClient` `"pollinations"` | Transient (factory) | `PollinationsImageGenerationService`, `PollinationsCatalogService` (both Singleton) | OK — shared base address; same per-call create + dispose; `ConfigureStandardResilience(60s, 3m)`. |
| `IHttpClientFactory` | Singleton (MS.DI default) | Two services above | OK. |
| All 12 `*Descriptor` types + their four narrow interfaces | Singleton (via `AddModelDescriptor<T>` forwarding) | `IModelDescriptorRegistry`, downstream services | OK — Interface Segregation done right. |
| `IModelDescriptorRegistry` | Singleton | Many Singletons | OK. |
| `IImageEncoderProvider` | Singleton | `ImageFileService` (Singleton) | OK. |
| `IImageFileService` | Singleton | `JobRunner` (Singleton) | OK. |
| `ReplicateImageGenerationService` | Singleton | `ImageGenerationDispatcher` (Singleton) | OK. |
| `PollinationsImageGenerationService` | Singleton | `ImageGenerationDispatcher` (Singleton) | OK. |
| `IImageGenerationService` (= dispatcher) | Singleton | `JobRunner` (Singleton) | OK. |
| `IModelCatalogService` | Singleton | `ModelCatalogCoordinator` (Singleton) | OK. |
| `IPollinationsCatalogService` | Singleton | `ModelCatalogCoordinator` (Singleton) | OK. |
| `IGalleryService` | Singleton (factory `_ => new GalleryService()`) | `GalleryViewModel` (Transient), `GalleryItemDetailViewModel` (Transient) | OK. |
| `IFileLauncher`, `IClipboardService` | Singleton | Multiple VMs | OK. |
| `IApiTokenStore`, `IPollinationsTokenStore`, `IUiStateStore` | Singleton | `GeneratorViewModel` (Singleton) | OK. |
| `IJobRunner`, `IModelCatalogCoordinator`, `IPromptBatchParser` | Singleton | `GeneratorViewModel` (Singleton) | OK. |
| `GeneratorViewModel` | **Singleton** (changed from Transient in `c446d35`) | `MainPage` (Singleton) | OK — explicitly justified in `MauiProgram.cs:113-118` against the F3 state-loss class. |
| `GalleryViewModel`, `GalleryItemDetailViewModel` | Transient | `GalleryPage` / `GalleryItemDetailPage` (Transient) | OK — both are pushable Shell routes, fresh-per-navigation is the right call. `GalleryViewModel.Dispose` stops the watcher on disappearing. |
| `MainPage` | **Singleton** (changed from Transient in `c446d35`) | Shell root | OK — matches the VM lifetime; root ShellContent is not pushable. |
| `GalleryPage`, `GalleryItemDetailPage` | Transient | Shell navigation | OK. |

**No captive dependencies. No Singleton-consuming-Scoped. Lifetimes are correct.**

---

## 7. CLEAN ARCHITECTURE — FOLDER LAYERING

| Layer | Status |
|---|---|
| `Core/Domain` | Pure C# + `CommunityToolkit.Mvvm` (documented exception) + `DataAnnotations`. **No infrastructure types, no MAUI types, no HttpClient.** Includes `ModelCapabilities` (moved 2026-05-19). |
| `Core/Application` | Interfaces + `PromptBatchParser` (pure string logic). |
| `Infrastructure/External` | Replicate + Pollinations HTTP services, Refit, NLog, JSON. |
| `Infrastructure/Services` | Local services (file, clipboard, secure-storage, dispatcher, JobRunner, debounced writer). |
| `Infrastructure/Diagnostics` | `CrashLogger` (NLog config + WinUI hook). |
| `Presentation/ViewModels` | Reference `Core/*`, `Infrastructure/Interfaces`. **Never** reaches into `Infrastructure/External` or `Infrastructure/Services` directly. Three coordinator sub-VMs sit alongside the host VM at the same level. |
| `Presentation/Views` | Code-behind reaches `Microsoft.Maui.*` and Windows-specific (`Windows.ApplicationModel.DataTransfer`, `Microsoft.UI.Xaml`) types in their natural place. No business logic. |
| `Shared/Constants` | Static constants, no behavior. |

The previously-flagged `ModelCapabilities` layering inversion is closed. No new layering observations.

---

## 8. SECURITY / CORRECTNESS — HTTP / PROVIDER CODE

- **API tokens** continue on `Authorization` header for both providers; no URL embedding. Token redaction now layered across URL, body, and surfaced UI text on the Pollinations side (`PollinationsImageGenerationService.cs:99-100,105-107,118,142-146`) and across body + UI on the Replicate side (`ReplicateImageGenerationService.cs:120-127`).
- **No secrets in source.** Confirmed by grep across the tree.
- **SecureStorage** vs Preferences split: tokens (`ApiTokenStore`, `PollinationsTokenStore`) use SecureStorage; non-secret prompt/model state (`UiStateStore`) uses Preferences. Correct split.
- **Resilience pipeline** now consistent: the Refit client, the `"replicate-download"` named client, and the `"pollinations"` named client all share `ConfigureStandardResilience` with their own per-attempt/total budgets. The asymmetry the previous audit flagged is closed.
- **Timeouts** owned by Polly; `HttpClient.Timeout = Timeout.InfiniteTimeSpan` (line 98 of RefitServiceExtensions) is the right opt-out.
- **Error swallowing**: every catch either logs at warning/error AND returns a structured `GeneratedImage` with `Message`, OR logs and rethrows. Confirmed clean across the four service entry points.
- **Replicate prediction cancellation**: explicitly not aborted mid-poll in `BatchCoordinator.RunBatchAsync` per the documented "paid prediction" rule (`BatchCoordinator.cs:14-17,182-185`). Tested.

**One residual concern:** the `Process.Start` argument-string in `GenerationJob.ShowInFolder` (Finding #2). Defense-in-depth, not an active exploit, but the parallel fix already happened on `FileLauncher`.

---

## 9. TEST AUDIT

**Stack:** xUnit + FluentAssertions + Moq. **339 tests passing** per `documents/continue.txt` (counted 305 `[Fact]`/`[Theory]` annotations; `[Theory]` expansions account for the gap). Test layout mirrors source layout.

**Coverage matrix — production file → test fixture mapping:**

| Production file | Fixture | Status |
|---|---|---|
| `MauiProgram.cs` | (none) | **Gap (Finding #7-adjacent)** — no isolation test for named-client wiring. The two `AddHttpClient(name)` calls + `ConfigureStandardResilience(60s, 3m)` policy registrations are uncovered. |
| `ModelDescriptorRegistry.cs` | `ModelDescriptorRegistryTests` (7) | Covers payload/capability resolution + fallback paths. **Missing**: a count-pin asserting `ModelDescriptorRegistry.Default()` lists every production descriptor (Finding #7). |
| Each of 12 descriptor files | Indirectly via `ImageModelFactoryTests` + service-level tests | OK — no need for per-file isolation given the registry+service tests pin the externally-observable behavior. |
| `RefitServiceExtensions` | `ResiliencePolicyTests` (3), `NullSkippingDictionaryConverterTests` (4) | OK. |
| `Core/Application/Services/PromptBatchParser.cs` | `PromptBatchParserTests` (14) | OK. |
| `Core/Domain/Entities/ImageGenerationParameters.cs` | `ImageGenerationParametersCloneTests` | OK (reflection-based completeness test). |
| `Core/Domain/ValueObjects/ModelCapabilities.cs` | `ModelCapabilitiesTests` | OK. |
| `Core/Domain/Services/ImageDataUriEncoder.cs` | `ImageDataUriEncoderTests` (9) | OK. |
| `Infrastructure/External/Replicate/ReplicateHelper.cs` | `ReplicateHelperTests` (9) | OK. |
| `ReplicateImageGenerationService.cs` | `ReplicateImageGenerationServiceTests` (9) | OK. |
| `ReplicateOutputConverter.cs` | `ReplicateOutputConverterTests` (10) | OK. |
| `PollinationsImageGenerationService.cs` | `PollinationsImageGenerationServiceTests` (13) | OK (ST3 fully landed). |
| `PollinationsCatalogService.cs` | `PollinationsCatalogServiceTests` (13) | OK. |
| `CrashLogger.cs` | `CrashLoggerTests` (3) | OK — `[Collection(DisableParallelization = true)]` is the correct pattern. |
| `ApiTokenStore.cs` | `ApiTokenStoreTests` (7) | OK. |
| `PollinationsTokenStore.cs` | `PollinationsTokenStoreTests` (7) | OK. |
| `UiStateStore.cs` | `UiStateStoreTests` (11) | OK. |
| `DebouncedSecureStorageWriter.cs` | `DebouncedSecureStorageWriterTests` (7) | OK — pins debounce + race semantics. |
| `ClipboardService.cs`, `FileLauncher.cs`, `ImageEncoderProvider.cs` | (none) | **Acceptable** — each is a one-line wrapper around platform / encoder API. |
| `GalleryService.cs` | `GalleryServiceTests` (9) | OK. |
| `ImageFileService.cs` | `ImageFileServiceTests` (8) | OK. |
| `ImageGenerationDispatcher.cs` | `ImageGenerationDispatcherTests` (4) | OK — routing + no-mutate-seed contract pinned. |
| `JobRunner.cs` | `JobRunnerTests` (6) | OK. |
| `ModelCatalogCoordinator.cs` | `ModelCatalogCoordinatorTests` (6) | OK. |
| `ModelCatalogService.cs` | `ModelCatalogServiceTests` (8) | OK. |
| `GeneratorViewModel.cs` | `GeneratorViewModelTests` (66) | OK — heavy coverage of the parameter mediation surface. |
| `InputImagesCoordinator.cs` | `InputImagesCoordinatorTests` (7) | OK. |
| `ProviderFilterCoordinator.cs` | `ProviderFilterCoordinatorTests` (8) | OK — assertions are on observable state (AllModels / Providers / FilteredModels / SelectedModel / SuppressModelPersist), not `.Verify()`. |
| `BatchCoordinator.cs` | `BatchCoordinatorTests` (5) | OK — empty-batch / re-entrancy / cancel-drain all asserted on observable state. |
| `GenerationJob.cs` | `GenerationJobTests` (11) | OK — though Finding #2's `ShowInFolder` argument shape is not asserted. |
| `GalleryViewModel.cs` | `GalleryViewModelTests` (13) | OK. |
| `GalleryItemDetailViewModel.cs` | `GalleryItemDetailViewModelTests` (9) | OK. |
| `MainPage.xaml.cs`, `GalleryPage.xaml.cs`, `GalleryItemDetailPage.xaml.cs` | (none) | **Acceptable** — code-behind only orchestrates VM/DispatchAlert calls; the VMs are tested directly. |
| `TokenProviderViewModel.cs` | Indirectly via `GeneratorViewModelTests.TokenProvider_*` | OK. |
| Converters / behaviors | (none) | **Acceptable** — pure value transforms, low risk. |

**Specific test-coverage gaps (severity-tagged):**

1. **MauiProgram named-client wiring** — *Minor.* No test asserts the two `AddHttpClient(name).ConfigureStandardResilience(...)` calls actually register the policy. `ResiliencePolicyTests` exercises the Refit client only. A `MauiProgramHttpClientTests` fixture using `Microsoft.Extensions.DependencyInjection` could build a tiny `ServiceCollection`, call the same registrations, resolve `IHttpClientFactory`, create both named clients, and assert each has a non-default `Timeout` (Polly mode) and a non-zero number of resilience handlers. Optional but cheap.

2. **Registry completeness** — *Minor.* `ModelDescriptorRegistry.Default_RegistersAllProductionDescriptors_AndCountMatchesMauiProgram` is missing. Today the seed-list count is checked, but the registry's actual descriptor count vs `MauiProgram.AddModelDescriptor` call count is not. Couple this to Finding #7 — adding the three Pollinations descriptors to `ProductionDescriptors()` and asserting `Default().Seeds.Count == expected` would pin both shapes.

3. **GenerationJob.ShowInFolder argument shape** — *Nit.* Once Finding #2 is closed, add `ShowInFolder_PathWithSpaces_PassesQuotedSelectArgument` (or the `ArgumentList` shape equivalent).

**Test quality notes:**
- AAA structure consistently applied across the new fixtures (Batch / InputImages / ProviderFilter coordinators).
- Coordinator tests assert observable state (`coord.FilteredModels.Should().Contain(...)`, `coord.SuppressModelPersist.Should().BeFalse()`), not `.Verify()` on mocks. Right shape.
- Two `StubHttpClientFactory` / `StubSecureStorage` / `StubPreferences` families under `TestSupport/` are reused across multiple fixtures. No proliferation.
- Concurrency tests use `TaskCompletionSource` gates — no `Thread.Sleep`-based timing. (Exception: token-store debounce tests wait 700ms to clear the SUT's 500ms debounce window — wall-clock, but bounded and parallel-safe.)
- `[Collection(DisableParallelization = true)]` correctly applied to `CrashLoggerTests`.

**No brittle tests, no order-dependent tests, no `.Verify()` over-asserts.**

---

## 10. POSITIVE OBSERVATIONS (what to keep)

- **Sealed-by-default** is now near-universal: ViewModels and sub-VMs are sealed (`InputImagesCoordinator`, `ProviderFilterCoordinator`, `BatchCoordinator`, `TokenProviderViewModel`), `ImageFileService` and `ReplicateImageGenerationService` got sealed in `8551d31`. Five remaining holdouts are the only outliers (Finding #6).
- **Descriptor pattern with narrow interfaces** continues to be the cleanest part of the architecture.
- **Clock seam** still injected at every time-sensitive service.
- **Coordinator pattern adopted post-F6**: host VM constructs sub-VMs with callback delegates, sub-VMs own bindable state, XAML reaches in via nested binding paths. Mirror of the original `TokenProviderViewModel` precedent.
- **Lifetime promotion documented in code**: `MauiProgram.cs:113-118` spells out exactly why `MainPage` + `GeneratorViewModel` must be Singleton. Future-maintainer-friendly.
- **`DebouncedSecureStorageWriter` extraction**: the F2 fix is now a reusable, tested primitive (72 lines, 7 tests). A third provider drops in for ~10 lines.
- **`ConfigureStandardResilience` as a shared `IHttpClientBuilder` extension**: F4 fix is also a reusable primitive. Refit + two named clients share one policy stack.
- **`ModelConstants.Pollinations.IsId(...)`**: QW6 closed; one canonical helper replaces three duplicate locals.
- **`Random.Shared.NextInt64`**: thread-safe RNG, no static `new Random()` foot-gun.
- **Atomic file replace** in `ModelCatalogService.SaveCachedAsync`: temp+move pattern.
- **NLog config + MEL bridge**: every DI-injected `ILogger<T>` and every static `CrashLogger.Log(...)` writes to one physical `app.log`. `Infrastructure.External.*` graduated to Debug.
- **Structured logging placeholders, never string-interpolation**: NLog migration remains clean.
- **`AllowConcurrentExecutions = true` on the long-running command** with regression test pinning prompt-snapshot isolation.
- **Test-support stubs are tight and reused**: `StubSecureStorage` / `StubPreferences` / `StubHttpClientFactory` are the canonical seams; no per-fixture proliferation.
- **F1–F10 all closed without test regressions**: 268 → 339 green across the fix-bundle window.

Items I checked and decided are **not** concerns:
- `ApiTokenStore` vs `PollinationsTokenStore` duplication — the previously-flagged Finding #5 has effectively been resolved by the `DebouncedSecureStorageWriter` extraction; the remaining 20-line wrapper is below the "merit-a-shared-base-class" threshold.
- Coordinator constructors taking 4–6 `Func`/`Action` delegates — idiomatic for the sub-VM pattern when the host needs to wire in shared callbacks. A one-method `IStatusSurface` interface for `setStatus` is possible but the delegate shape is fine for now.
- `InputImagesCoordinator.OnSelectedImagesChanged` doing N mirrors during a truncate — at MaxImageInputs ≤ 14, the cost is unmeasurable. Listed as a nit, not actioned.
- `GeneratorViewModel` ~553 lines post-F6 — the host responsibilities are coherent; further carve-up would push past the law-of-diminishing-returns line.

---

## 11. PRIORITIZED IMPROVEMENT PLAN

### Quick wins (< 1 hour each)

| # | What | Files | Why | Effort |
|---|---|---|---|---|
| QW1 | Replace `replicateTask.Result.Concat(pollinationsTask.Result)` with `(await replicateTask).Concat(await pollinationsTask)` after the `WhenAll`. | `Infrastructure/Services/ModelCatalogCoordinator.cs:37-39` | Eliminates the only `.Result` access in the project. | S |
| QW2 | Switch `GenerationJob.ShowInFolder` to `ProcessStartInfo.ArgumentList` (matching `FileLauncher.RevealInFolder`). | `Presentation/ViewModels/GenerationJob.cs:62-68` | Closes the parallel argument-shape gap left after QW5. | S |
| QW3 | Seal the five remaining concretes: `FileLauncher`, `ClipboardService`, `GalleryService`, `ImageEncoderProvider`, `ModelCatalogService`. | five files in `Infrastructure/Services` / `External/...` | Codebase-wide convention completion. | S |
| QW4 | Reference `ReplicateImageGenerationService.HttpClientName` and `PollinationsImageGenerationService.HttpClientName` from `MauiProgram.AddHttpClient(...)` calls. | `MauiProgram.cs:60,65` | Makes the named-client coupling traceable from one side. | S |
| QW5 | Extract `DispatchToUi` into `Presentation/Common/UiDispatcher.cs` (single static); update three call sites. | `GeneratorViewModel.cs:405-418`, `BatchCoordinator.cs:222-235`, `GalleryViewModel.cs:304-317` | Removes three verbatim copies. | S |
| QW6 | Add the three `Pollinations*Descriptor`s to `ModelDescriptorRegistry.ProductionDescriptors()`; add a count-pin test asserting `Default().Seeds.Count == 12`. | `Core/Domain/Descriptors/ModelDescriptorRegistry.cs:76-87`; new test in `ModelDescriptorRegistryTests` | Aligns the test-time registry with production; future regression catches a missing descriptor at build-time. | S |

### Short-term (1 day each)

| # | What | Files | Why | Effort |
|---|---|---|---|---|
| ST1 | Move `_suppressPreferredArUpdate` ownership from the host VM onto `InputImagesCoordinator`. The coordinator's `_setAspectRatioProgrammatically` callback is the gate; flipping the flag inside it (and reading it inside `RecordExplicitAspectRatioPick`) would make suppression-flag ownership uniform across the carve-up. Add tests for the new public read. | `GeneratorViewModel.cs:106,164-169,275-276`; `InputImagesCoordinator.cs:62-65,93-125` | Unifies the suppression pattern with `ProviderFilterCoordinator.SuppressModelPersist`. | M |
| ST2 | Add `MauiProgramHttpClientTests` asserting both named clients resolve with the standard pipeline applied (one `[Fact]` per name). | new `ImageGenerator.MAUI.Tests/Extensions/MauiProgramHttpClientTests.cs` | Closes the §9 gap on Finding #5's adjacent surface. | S |

### Medium-term (1 week)

| # | What | Files | Why | Effort |
|---|---|---|---|---|
| MT1 | Migrate `GeneratedImage` from "ImageData null = error" to a discriminated `Result<byte[], GenerationError>`. Updates `IImageGenerationService`, both concretes, `ImageGenerationDispatcher`, `JobRunner`, and the VM-side switch. Removes a ~5-call-site `is null or { Length: 0 }` invariant. | `Core/Domain/Entities/GeneratedImage.cs` + all consumers | Type-level proof replaces an undocumented invariant. | M |
| MT2 | Add the `kontext` + `negative_prompt` features behind their existing descriptor extension points (per project memory). | descriptor files for the supporting models + `ImageGenerationParameters` | Documented open features, project-roadmap items. | M |

### Long-term (ongoing)

| # | What | Why | Effort |
|---|---|---|---|
| LT1 | Once MT1 lands, consider promoting `Core` and `Infrastructure` into separate `.csproj`s. The `CommunityToolkit.Mvvm` reference from `Core/Domain/Entities/ImageGenerationParameters.cs` would need hand-rolled `INPC` (file comment at 8-12 acknowledges). The layering already passes folder-level audit. | Tightens layering enforcement at the compiler level. | L |
| LT2 | Adopt CA + Meziantou (or equivalent) analyzers at warning-as-error. | Static analysis as a force multiplier. | M |
| LT3 | Add a CI test gate (GitHub Actions running `dotnet test` on PR). | Catches regressions before merge. | M |
| LT4 | Add Pollinations image-input (per `continue.txt`) — likely requires hosting the image first (Pollinations is GET-only). Open question per `documents/continue.txt:42-59`. | Documented open feature. | L |

---

## 12. FILE:LINE INDEX OF EVERY FINDING

| Finding | Files:lines |
|---|---|
| F1 (2026-05-20) — `.Result` after `Task.WhenAll` | `Infrastructure/Services/ModelCatalogCoordinator.cs:37-39` |
| F2 (2026-05-20) — `Process.Start /select,` raw concat | `Presentation/ViewModels/GenerationJob.cs:62-68` |
| F3 (2026-05-20) — Asymmetric suppression-flag ownership (host-owned AR flag) | `Presentation/ViewModels/GeneratorViewModel.cs:106,164-169,275-276`; `Presentation/ViewModels/InputImagesCoordinator.cs:93-125` |
| F4 (2026-05-20) — `DispatchToUi` duplicated three times | `GeneratorViewModel.cs:405-418`; `BatchCoordinator.cs:222-235`; `GalleryViewModel.cs:304-317` |
| F5 (2026-05-20) — Named-client string literals not consuming `HttpClientName` const | `MauiProgram.cs:60,65`; `Infrastructure/External/Replicate/ReplicateImageGenerationService.cs:13`; `Infrastructure/External/Pollinations/PollinationsImageGenerationService.cs:13` |
| F6 (2026-05-20) — Five unsealed `public class` concretes | `Infrastructure/Services/FileLauncher.cs:6`; `Infrastructure/Services/ClipboardService.cs:6`; `Infrastructure/Services/GalleryService.cs:15`; `Infrastructure/Services/ImageEncoderProvider.cs:18`; `Infrastructure/Services/ModelCatalogService.cs:11` |
| F7 (2026-05-20) — `ModelDescriptorRegistry.Default()` missing the 3 Pollinations descriptors | `Core/Domain/Descriptors/ModelDescriptorRegistry.cs:76-87`; cross-ref `MauiProgram.cs:75-86` |
| F8 (2026-05-20) — Truncate fires per-item mirror | `Presentation/ViewModels/InputImagesCoordinator.cs:82-91,93-125` |
| Missing test — named-client wiring | (no fixture); proposed `ImageGenerator.MAUI.Tests/Extensions/MauiProgramHttpClientTests.cs` |
| Missing test — registry completeness | `ImageGenerator.MAUI.Tests/Descriptors/ModelDescriptorRegistryTests.cs` (add count-pin) |
| Missing test — `GenerationJob.ShowInFolder` argument shape | `ImageGenerator.MAUI.Tests/ViewModels/GenerationJobTests.cs` (add post-QW2) |
