# GeneratorViewModel Refactor Plan

**Phase 1 — `ComfyUiWorkflowCoordinator`: DONE, committed `c44a6ba`, smoke-tested on WinUI
(Krea-2 with and without LoRA), 1,417 tests green, 0 warnings.** Sections 1–10 below are the
Phase 1 record, kept as the worked template (and the corrections in §10 are general).

**Phase 2 — `AppSettingsCoordinator`: PLANNED, §11 onward. Nothing implemented yet.**

Verified against: `GeneratorViewModel.cs` @ `c44a6ba` (**1,896 lines**).

---

## LOCKED DECISIONS (approved by owner)

1. **Property name: `ComfyUi`** → `{Binding ComfyUi.SupportsKrea2Lora}`. Verified safe: no file imports bare `ImageGenerator.MAUI.Core.Domain`, and the only other `ComfyUi` identifier is `ModelConstants.ComfyUi` (always qualified).
2. **Mechanical test repath (option a)** — no forwarding shims on `GeneratorViewModel`. Assertions/mocks unchanged; only the member *path* changes.
3. **Flat folder**: `ImageGenerator.MAUI/Presentation/ViewModels/ComfyUiWorkflowCoordinator.cs` (matches the other 3 coordinators; implicit globbing, no csproj edit).
4. **Phase 1 only.** Re-plan Phase 2 (§7) afterwards.

---

## IMPLEMENTATION MAP (exact, verified line numbers at HEAD)

`GeneratorViewModel.cs` regions to **cut** (in this order, bottom-up so earlier line numbers stay valid):

| Region | Lines | What |
|---|---|---|
| C | **883–1302** | all 5 probes + 4 `Hide*` helpers + `[RelayCommand] RefreshKrea2LorasAsync` (contiguous; 882 blank, 1303 blank) |
| B | **517–725** | ComfyUI observable state, intent fields, factor helpers, 3 `OnSelected*Changed` handlers (726 starts `_lastValidResolution` comment = STAYS) |

`GeneratorViewModel.cs` regions to **rewrite in place**:

| Lines | Change |
|---|---|
| **871–880** | comment + 5 `_ = Refresh…(modelValue);` → keep a shortened comment + `ComfyUi.RefreshForModel(modelValue);` |
| **1693–1707** | `case UpscaleAfterRender:` whole body → `ComfyUi.OnUpscaleAfterRenderChanged();` + `break;` |
| **31–32** | delete `_checkpointService` / `_comfyUiLoraService` fields (verified used only in the cut regions + ctor) |
| **1467–1468** | ctor assignments → forwarded to the coordinator instead |
| ~123 | add `public ComfyUiWorkflowCoordinator ComfyUi { get; }` beside `ProviderFilter` / `Batch` |
| ctor body | construct `ComfyUi` after `InputImages`, before `ProviderFilter` (`_parameters` + `_capabilities` must already be set) |

`MainPage.xaml`: prefix the 22 binding occurrences in the 239–319 block; leave `Parameters.ComfyUiLoraStrength` (×2) and `Parameters.UpscaleAfterRender` (×1) unprefixed.

`GeneratorViewModelTests.cs`: repath 73 member refs + 3 `GeneratorViewModel.NoKrea2LoraOption` → `ComfyUiWorkflowCoordinator.NoKrea2LoraOption`.

### Verbatim-move technique (avoids hand-transcription bugs)

The coordinator exposes **private `Parameters => _parametersAccessor()`** plus same-named `_uiStateStore` / `_logger` / `_checkpointService` / `_comfyUiLoraService` fields, so the moved bodies compile **unchanged** — no `Parameters` → `_parametersAccessor()` find/replace across 40 sites. Only 3 lines differ (all in `RefreshInputImageSupportAsync`):

```csharp
// old → new
Capabilities = Capabilities with { ImagePrompt = true, MaxImageInputs = 1 };
→  _capsSetter(_capsAccessor() with { ImagePrompt = true, MaxImageInputs = 1 });
InputImages.TruncateToMaxInputs(hasInputImage ? 1 : 0);
→  _truncateInputImages(hasInputImage ? 1 : 0);
```

Namespaces confirmed for the new file's usings: `ModelCapabilities` → `Core.Domain.ValueObjects`; `ComfyUiLoaderKind` → `Core.Domain.ComfyUi`; `IUiStateStore`/`IComfyUiCheckpointService`/`IComfyUiLoraService` → `Infrastructure.Interfaces`; `ModelConstants` → `Shared.Constants`; `ImageGenerationParameters` → `Core.Domain.Entities` (GVM aliases this name).

---

## 0. Corrections to the brief (verified, not assumed)

| Claim in brief | Actual state |
|---|---|
| "~1,280 lines" | **2,527 lines** (`ImageGenerator.MAUI/Presentation/ViewModels/GeneratorViewModel.cs`) |
| "21 constructor dependencies" | **Correct** — 21 params (15 required + 6 optional), lines 1434–1455 |
| "~10 picker-suppression `_suppress*` flags" | **11 flags**, only **7** of which are picker/persist guards. The other 4 are `_suppressUpscalePersist`-family + `_suppressPreferredArUpdate`, which is an AR-stickiness guard, not a picker guard. Full list in §2. |
| "in the files I provide" | No files attached — I inspected the repo directly. Everything below is from the working tree. |

### Baseline (measured, this session)
```
dotnet build  ImageGenerator.MAUI.Tests   →  Build succeeded, 0 warnings, 0 errors
dotnet test   ImageGenerator.MAUI.Tests   →  Passed! Failed: 0, Passed: 1416, Skipped: 0
```
Both commands are the Phase 1 exit gate.

---

## 1. Why Phase 1 is safe: measured blast radius

| Question | Answer |
|---|---|
| Which pages bind the ComfyUI workflow state? | **MainPage.xaml only**, 21 bindings, all inside one contiguous block, **lines 239–319** |
| Are they inside a `DataTemplate` (nested DataContext)? | **No.** The InputImage template ends at 173 and the Jobs template starts at 502. All 21 are on the page DataContext. |
| Are they compiled bindings? | **NO — assumed, and WRONG (measured).** `x:DataType="vm:GeneratorViewModel"` is at `MainPage.xaml:10`, but MAUI only validates binding paths with XamlSourceGen, which is **off** here. Proven: `{Binding DefinitelyNotAProperty12345}` → *Build succeeded, 0 errors, 0 warnings*, while a malformed tag in the same file → `error MAUIG1001`. XAML **is** compiled; binding paths are **not validated at all**. A missed repath is a **silently blank control at runtime**, caught by neither the build nor the 1,416 unit tests. Mitigation: scripted repath + the `ComfyUiBindingPathTests` guard (§6c). |
| Code-behind coupling (`MainPage.xaml.cs`, `SettingsPage.xaml.cs`)? | **None.** Zero references to any of the 18 moved members. |
| Other production callers of the 5 probes? | **Two only:** `RefreshCapabilities` (lines 874–880, five fire-and-forgets) and `RefreshModelsAsync` (line 2204, `await RefreshKrea2LoraOptionsAsync`). |
| Other VMs reading ComfyUI workflow state? | **None** — `IdeaToPromptViewModel` / `MutationEngineViewModel` / `IdeogramStructureEditorViewModel` / `GalleryViewModel` never touch it. |
| Is `RemixFromImageAsync` involved? | **No** — it writes no ComfyUI workflow state (verified by grep over 2380–2478). |
| Do the services read the VM? | **No.** `ComfyUiImageGenerationService` / the patcher re-read `IUiStateStore` / `IComfyUiAuthStore` per request. The VM is write-mostly for these. |

Conclusion: Phase 1 is a **closed, single-page, compile-checked extraction.** Phase 2 is not (see §7).

---

## 2. Exact inventory of what moves

### 2a. Source regions (contiguous — 3 blocks, 483 lines out of 2,527)

| Lines | Contents |
|---|---|
| **517–725** | All ComfyUI observable state, private intent fields, factor helpers, and the three `OnSelected*Changed` persist guards |
| **888–917** | `RefreshInputImageSupportAsync` |
| **919–1301** | `RefreshUpscaleOptionAsync`, `HideUpscaleFactorPicker`, `HideUpscaleOption`, `RefreshBakedModelAsync`, `HideBakedModel`, `RefreshQualityPresetOptionsAsync`, `HideQualityPresetPicker`, `RefreshKrea2LoraOptionsAsync`, `RefreshKrea2LorasAsync` (`[RelayCommand]`), `HideKrea2LoraPicker` |
| + 4 lines | `UpscaleAfterRender` case body in `OnParametersPropertyChanged` (1701-ish), see §4 |

Stays in `GeneratorViewModel`: `SetAspectRatioProgrammatically`, `MirrorImagePromptsToParameters`, `_lastValidResolution`, `UpdateCustomAspectRatio` (all AR/image concerns, lines 726+, 1304+).

### 2b. Members by kind

**16 `[ObservableProperty]`** — `_bakedModelName`, `_supportsCheckpoint`, `_checkpointLabel`, `_qualityPresetOptions`, `_selectedQualityPreset`, `_supportsQualityPreset`, `_krea2LoraOptions`, `_selectedKrea2Lora`, `_supportsKrea2Lora`, `_isLoadingKrea2Loras`, `_krea2LoraStatusText`, `_supportsUpscaleAfter`, `_upscaleWorkflowDisplay`, `_upscaleFactorOptions`, `_selectedUpscaleFactor`, `_supportsUpscaleFactor`

**2 computed** — `IsKrea2LoraSelected`, `IsUpscaleFactorVisible` (+ their `[NotifyPropertyChangedFor]` wiring)

**4 suppression flags (all region-local except one)**
| Flag | Lines | Note |
|---|---|---|
| `_suppressPresetPersist` | 549, 700, 1134, 1147, 1160, 1171 | region-local |
| `_suppressLoraPersist` | 581, 718, 1194, 1201, 1226, 1249, 1265, 1277, 1288, 1301 | region-local |
| `_suppressUpscalePersist` | 594, 958, 966, 1036, 1045, **1701** | **escapes at 1701** → resolved by §4 |
| `_suppressUpscaleFactorPersist` | 623, 651, 979, 1003, 1016, 1030 | region-local |

**5 async version tokens** — `_checkpointRefreshVersion`, `_presetRefreshVersion`, `_loraRefreshVersion`, `_upscaleRefreshVersion`, `_inputImageRefreshVersion` (all region-local; **must stay 5 independent counters**, see §5-H1)

**Private intent state** — `_workflowDefaultPreset`, `_activeLoraWorkflow`, `_workflowDefaultUpscaleFactor`, `_upscaleFactorWorkflow`, `_upscaleFactorOwnsRender`, `_pickedUpscaleFactor`, `CommonUpscaleFactors`, `FormatUpscaleFactor`, `ParseUpscaleFactor`, `NoKrea2LoraOption` (`internal const`), `LegacyNoKrea2LoraOption`

**Services** — `IComfyUiCheckpointService`, `IComfyUiLoraService?` (only these two; `IUiStateStore` and `ILogger` are shared)

---

## 3. New file: `Presentation/ViewModels/ComfyUiWorkflowCoordinator.cs`

Follows the existing `BatchCoordinator` / `InputImagesCoordinator` precedent exactly: `sealed partial class … : ObservableObject`, constructed **inside** `GeneratorViewModel`'s ctor (not DI-registered), `static using …UiDispatcher.DispatchToUi`, collaborators injected as delegates.

```csharp
public sealed partial class ComfyUiWorkflowCoordinator : ObservableObject
{
    internal const string NoKrea2LoraOption = "No LoRA";
    private const string LegacyNoKrea2LoraOption = "None";

    private readonly IComfyUiCheckpointService _checkpointService;
    private readonly IComfyUiLoraService? _loraService;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger _logger;

    // Delegate collaborators (same shape as BatchCoordinator / InputImagesCoordinator).
    private readonly Func<ImageGenerationParameters> _parametersAccessor;
    private readonly Func<ModelCapabilities> _capsAccessor;
    private readonly Action<ModelCapabilities> _capsSetter;
    private readonly Action<int> _truncateInputImages;

    public ComfyUiWorkflowCoordinator(
        IComfyUiCheckpointService checkpointService,
        IUiStateStore uiStateStore,
        ILogger logger,
        Func<ImageGenerationParameters> parametersAccessor,
        Func<ModelCapabilities> capsAccessor,
        Action<ModelCapabilities> capsSetter,
        Action<int> truncateInputImages,
        IComfyUiLoraService? loraService = null) { /* null-checks + assign */ }

    /// <summary>Replaces the five `_ = Refresh…(modelValue);` lines in RefreshCapabilities.</summary>
    internal void RefreshForModel(string? modelValue)
    {
        _ = RefreshBakedModelAsync(modelValue);
        _ = RefreshQualityPresetOptionsAsync(modelValue);
        _ = RefreshKrea2LoraOptionsAsync(modelValue);
        _ = RefreshInputImageSupportAsync(modelValue);
        _ = RefreshUpscaleOptionAsync(modelValue);
    }

    /// <summary>Replaces the `await RefreshKrea2LoraOptionsAsync` in RefreshModelsAsync.</summary>
    internal Task RefreshLorasForCurrentModelAsync(string modelValue)
        => RefreshKrea2LoraOptionsAsync(modelValue);

    /// <summary>Whole body of GVM's `case UpscaleAfterRender:` — see §4.</summary>
    public void OnUpscaleAfterRenderChanged() { … }
}
```

Every moved line is otherwise **copied verbatim**, with `Parameters` → `_parametersAccessor()` (a fresh call each time, never a captured field — this is how `BatchCoordinator` does it and it matters because `Parameters` is an `[ObservableProperty]` that can be reassigned by `RemixFromImageAsync`).

`ModelConstants.ComfyUi.IsId(Parameters.Model)` reads become `ModelConstants.ComfyUi.IsId(_parametersAccessor().Model)` — the constant itself stays put.

---

## 4. The only genuinely non-mechanical edit

`OnParametersPropertyChanged`'s `UpscaleAfterRender` case (lines ~1697–1707) reaches into `_suppressUpscalePersist`, which is ComfyUI-private:

```csharp
// BEFORE
case nameof(ImageGenerationParameters.UpscaleAfterRender):
    ApplyUpscaleFactor();
    OnPropertyChanged(nameof(IsUpscaleFactorVisible));
    if (!_suppressUpscalePersist && ModelConstants.ComfyUi.IsId(Parameters.Model))
        _uiStateStore.PersistComfyUiUpscaleAfter(
            Parameters.UpscaleAfterRender, ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
    break;

// AFTER — whole body moves; the flag never becomes public
case nameof(ImageGenerationParameters.UpscaleAfterRender):
    ComfyUi.OnUpscaleAfterRenderChanged();
    break;
```

This keeps all 4 ComfyUI flags `private` to the coordinator. Do **not** expose `SuppressUpscalePersist` as an internal getter — moving the case body is strictly better and preserves ordering (re-arm factor → raise visibility → guarded persist).

---

## 5. Behaviour-preservation rules (the reason this refactor usually goes wrong)

- **H1 — five independent version tokens.** `_ = Refresh…()` fires all five concurrently, each with its own counter. A combined `RefreshAllAsync` that awaits sequentially, or one shared token, would let a slow `/models/loras` HTTP round-trip discard a fast local preset update. Keep 5 counters, keep 5 discards.
- **H2 — `DispatchToUi` double-check.** Every probe checks `version != _xRefreshVersion` **both** before and **inside** the `DispatchToUi` lambda. Copy both checks. `UiDispatcher` deliberately runs synchronously when `MainThread` throws (unit tests, no WinRT) — that is why the tests can drive these methods directly.
- **H3 — nested-path binding notification.** `{Binding ComfyUi.IsUpscaleFactorVisible}` relies on the binding engine subscribing to the *intermediate* segment. Already proven in this app: `{Binding InputImages.CanAddImage}` and `{Binding ProviderFilter.SelectedModel}` are raised from inside their coordinators today. No `OnPropertyChanged("ComfyUi")` re-raise needed.
- **H4 — `Capabilities` write-back.** `RefreshInputImageSupportAsync` does `Capabilities = Capabilities with { ImagePrompt = true, MaxImageInputs = 1 }`, which triggers `OnCapabilitiesChanged` → `InputImages.OnCapabilitiesChanged()` plus the `[NotifyPropertyChangedFor]` fan-out (`SupportsInputImage` etc.). `Capabilities` **stays on GVM**; the coordinator reaches it through `capsAccessor`/`capsSetter` only, and only inside the existing `DispatchToUi` + version check. Same for the deferred `InputImages.TruncateToMaxInputs(1|0)` that compensates for `RefreshCapabilities` skipping truncation for ComfyUI.
- **H5 — `null`-push guards.** `OnSelectedQualityPresetChanged`, `OnSelectedKrea2LoraChanged`, `OnSelectedUpscaleFactorChanged` all begin `if (value is null) return;` for the WinUI `ComboBox` ItemsSource-swap push. Move verbatim, including the "re-assert through empty" patterns and `_pickedUpscaleFactor` intent retention while the chain is off.
- **H6 — `_activeLoraWorkflow` reset ordering** in `RefreshKrea2LoraOptionsAsync` (clear `Parameters.ComfyUiLora*` + `SelectedKrea2Lora = null` for a *different* workflow, before the `GetWorkflowIsKrea2Async` await). Non-obvious, keep byte-for-byte.
- **H7 — metadata-lie guard.** `ApplyUpscaleFactor()`'s `_upscaleFactorOwnsRender || UpscaleAfterRender` condition is what stops a false `UpscaleBy:` line in image metadata. Unchanged, and `IsUpscaleFactorVisible` must keep using the same expression.
- **H8 — AutomationIds are untouched.** Repathing changes `Path=`, never `AutomationId` (`Krea2LoraPicker`, `QualityPresetPicker`, `UpscaleFactorPicker`, `UpscaleAfterRenderCheckBox`, `BakedModelLabel`, `UpscaleWorkflowLabel`, `RefreshKrea2LorasButton`, `Krea2LoraStrengthSlider`) — so the manual/UI-test hooks survive.

---

## 6. Exact change list (Phase 1)

### 6a. New file
**Create** `ImageGenerator.MAUI/Presentation/ViewModels/ComfyUiWorkflowCoordinator.cs` (~510 lines: 483 moved + ctor + doc comment).

### 6b. `GeneratorViewModel.cs`
1. **Delete** lines 517–725, 888–917, 919–1301 (ComfyUI blocks from §2a).
2. **Add** `public ComfyUiWorkflowCoordinator ComfyUi { get; }` next to `ProviderFilter` / `Batch` (line ~123).
3. **Delete fields** `_checkpointService`, `_comfyUiLoraService` (17 fields → **15**). Ctor *parameters* stay (DI still supplies them and forwards to the coordinator) — see note below.
4. **Construct** in ctor after `InputImages`, before `ProviderFilter`:
   ```csharp
   ComfyUi = new ComfyUiWorkflowCoordinator(
       checkpointService, uiStateStore, logger,
       parametersAccessor: () => Parameters,
       capsAccessor: () => Capabilities,
       capsSetter: caps => Capabilities = caps,
       truncateInputImages: max => InputImages.TruncateToMaxInputs(max),
       loraService: comfyUiLoraService);
   ```
   Must be after `_parameters`/`_capabilities` are set and after `InputImages` exists.
5. **`RefreshCapabilities`**: 5 `_ = Refresh…(modelValue);` → one `ComfyUi.RefreshForModel(modelValue);` (same position, last statement).
6. **`RefreshModelsAsync`**: `await RefreshKrea2LoraOptionsAsync(Parameters.Model);` → `await ComfyUi.RefreshLorasForCurrentModelAsync(Parameters.Model);`
7. **`OnParametersPropertyChanged`**: `UpscaleAfterRender` case → `ComfyUi.OnUpscaleAfterRenderChanged();` (§4).
8. **Keep** `OnPropertyChanged(nameof(IsUpscaleFactorVisible))` calls only inside the coordinator; remove any that lived in the moved blocks' old neighbours.
9. **Trim `using`s** that only served the moved code (`System.Globalization` stays if still used; verify).

### 6c. `MainPage.xaml` — 22 binding occurrences of 18 distinct members, lines 239–319 only

**Measured correction: 22 occurrences / 18 names**, not 21 (the first pass under-counted
`IsLoadingKrea2Loras` ×4 and `Krea2LoraStatusText` ×2). All 22 are on lines 239–319 and both
`DataTemplate`s (158–173 `InputImageItem`, 502–625 `GenerationJob`) are outside that range, so
none of them carries a nested DataContext.

Because **binding paths are not build-validated** (§1), this repath is done **by script with a
per-name count assertion** rather than by eye, and is then verified by a new
`ComfyUiBindingPathTests` guard test that reflects over `ComfyUiWorkflowCoordinator` and scans
the XAML text — the substitute for the compile check this project does not have.
Prefix with `ComfyUi.`:
`SupportsCheckpoint`, `CheckpointLabel`, `BakedModelName`, `SupportsQualityPreset`, `QualityPresetOptions`, `SelectedQualityPreset`, `SupportsKrea2Lora`, `Krea2LoraOptions`, `SelectedKrea2Lora`, `IsLoadingKrea2Loras` (×4), `RefreshKrea2LorasCommand`, `Krea2LoraStatusText` (×2), `IsKrea2LoraSelected`, `SupportsUpscaleAfter`, `UpscaleWorkflowDisplay`, `IsUpscaleFactorVisible`, `UpscaleFactorOptions`, `SelectedUpscaleFactor`.

**Leave unprefixed (they are `Parameters.*`, which stays on GVM):**
`Parameters.ComfyUiLoraStrength` (×2), `Parameters.UpscaleAfterRender` (×1).

`RefreshKrea2LorasCommand` becomes a `[RelayCommand]` on the coordinator → `ComfyUi.RefreshKrea2LorasCommand`.

### 6d. `SettingsPage.xaml` — **no changes** (binds zero ComfyUI workflow state; only `ComfyUiBaseUrl`, which is a Phase 2 item).

### 6e. `MauiProgram.cs` — **no changes.** The coordinator is ctor-constructed like the other three; DI graph is untouched, `_comfyUiLoraService` stays optional/`AddSingleton<IComfyUiLoraService,…>` as-is.

### 6f. Tests — `GeneratorViewModelTests.cs` only
- **73** member references to repath: `_viewModel.SelectedQualityPreset` → `_viewModel.ComfyUi.SelectedQualityPreset`, etc. (`RefreshUpscaleOptionAsync` 9, `RefreshQualityPresetOptionsAsync` 8, `SelectedKrea2Lora` 7, `SelectedUpscaleFactor` 6, `RefreshKrea2LoraOptionsAsync` 5, `RefreshBakedModelAsync` 5, `SupportsCheckpoint`/`CheckpointLabel`/`BakedModelName` 4 each, …).
- **3** references `GeneratorViewModel.NoKrea2LoraOption` → `ComfyUiWorkflowCoordinator.NoKrea2LoraOption` (still `internal`, `InternalsVisibleTo="ImageGenerator.MAUI.Tests"` already exists in the csproj, line 48).
- **Zero ctor-signature changes** in Phase 1 → all 4 `new GeneratorViewModel(...)` sites in this file and the 1 in `MutationEngineViewModelTests.cs:588` are untouched.
- Add `ImageGenerator.MAUI.Tests/ViewModels/ComfyUiWorkflowCoordinatorTests.cs` only if you want direct coverage; the existing 73 references already exercise it through the `ComfyUi` surface.

**No assertions, mock setups or expectations change** — pure repathing. Any test that starts failing is a real behavioural regression, which is the point of the 1,416-test net.

---

## 7. Phase 2 preview — **SUPERSEDED by §11** (kept for history)

> Written before Phase 1 landed, when `GeneratorViewModel` was still 2,527 lines. Its numbers are
> **wrong**: measured XAML surface is **40** occurrences over 5 pages, not "16 bindings"; measured
> test surface is **54 + 4 + 2 + 0**, not "~58 + 10 + 11 + 1". Its claim that GVM reads
> `ComfyUiBaseUrl` "in the render path" is also **false** — `ComfyUiBaseUrl` has exactly 2 uses in
> the VM (declaration + persist hook). Read §11 instead.
>
> Two points from it stay valid and are folded into §11:
> - **Load order is load-bearing** → §11.6, first bullet (verified again against current source).
> - **`syncToParameters` captures the `_parameters` field** (`v => _parameters.ApiToken = v`)
>   instead of re-reading the `Parameters` property, unlike everything else — a latent bug if
>   `Parameters` is ever reassigned. Only matters for the token cluster, which §11.4.4 recommends
>   leaving alone; if that ever changes, fix the capture to `Parameters.ApiToken = v`.

Scope per brief: Appearance/Theme, `OutputFolder`, ComfyUI URL / `UseSageAttention` / `FreeVramAfterRendering`, Ollama & OpenRouter catalogs, CivitAI test, `TokenProviders`. Measured reality:

1. **Wider than "MainPage + SettingsPage".** Three *other* pages bind these settings through a sibling VM's `Generator` property — `IdeaToPromptPage.xaml` (10 `Generator.*` bindings incl. `Generator.OllamaModel`, `Generator.OllamaVisionModels`, `Generator.RefreshOllamaModelsCommand`, `Generator.OpenRouterVisionFreeOnly`), `IdeogramStructureEditorPage.xaml` (3), `MutationEnginePage.xaml` (3). Plus C# readers: `IdeaToPromptViewModel:698-710`, `MutationEngineViewModel:675-682`, `IdeogramStructureEditorViewModel:177`, and **`GpuColocation.SameHost(_generator?.ComfyUiBaseUrl, ResolveOllamaBaseUrl())` in two VMs** (also GVM:1771 in the render path).
   → Recommended: `GeneratorViewModel.Settings` + a thin `public AppSettingsCoordinator? Settings => _generator?.Settings;` on the three sibling VMs, then repath 16 XAML bindings `Generator.X` → `Settings.X`. Keeps `GpuColocation` semantics by reading `Settings.ComfyUiBaseUrl` / `Settings.OllamaBaseUrl`.
2. **`IsValid` needs no token duplication at all.** It reads only `Parameters.ApiToken` / `PollinationsApiToken`, and `TokenProviderViewModel` writes those through its `syncToParameters` delegate. So `TokenProviders` can move as long as the coordinator is handed `() => Parameters` — the token *state* has one owner, the *channel* is `Parameters`. The `SettingsPage.xaml.cs` comment ("a separate VM would just mirror it") is stale; delete it.
   Side-note worth fixing while there: the existing delegates capture the `_parameters` **field** (`v => _parameters.ApiToken = v`) rather than re-reading the property, unlike everything else — reassignment would strand tokens on the old instance.
3. **Load order is load-bearing.** `LoadSavedUiState` restores settings *before* `ProviderFilter.RestoreSelectedModel`, and resolution/AR/JSON-prompt *after* it. Splitting the method must preserve that sequence exactly (`Settings.LoadSavedState()` first, then the model-dependent restores) and keep the single `_uiStateLoaded` / new `_settingsLoaded` re-entry guards — `MainPage.OnAppearing` calls `LoadAllTokensAsync` → `LoadCachedCatalogAsync` → `LoadSavedUiState` in order (MainPage.xaml.cs:206-213).
4. Phase 2 also moves the last 2 picker flags (`_suppressOllamaModelPersist` — deliberately shared by both Ollama pickers, keep it one flag — and `_suppressOpenRouterVisionModelPersist`, which also guards `OnOpenRouterVisionFreeOnlyChanged`'s cascade) and frees `_ollamaModelCatalog`, `_openRouterModelCatalog`, `_openRouterTokenStore`, `_folderPicker`. Only then does the ctor parameter count actually drop (settings services can be injected into `AppSettingsCoordinator` directly).
   → Phase 2 test blast radius: ~58 refs in `GeneratorViewModelTests.cs` + 10 in `IdeaToPromptViewModelTests.cs` + 11 in `MutationEngineViewModelTests.cs` + 1 in `IdeogramStructureEditorViewModelTests.cs`, and 2 of the 5 `new GeneratorViewModel(...)` sites shrink signature.

---

## 8. Sequencing & verification

| Step | Gate |
|---|---|
| 1. Add `ComfyUiWorkflowCoordinator.cs`, delete the 3 blocks, ctor wiring | `dotnet build ImageGenerator.MAUI.Tests` — **the build does NOT validate binding paths** (§1); it only proves the C# side
| 2. Repath the 22 `MainPage.xaml` occurrences **by script** + binding-path guard test | guard test green; build clean, **0 warnings** (baseline is 0 — keep it) |
| 3. Repath 73 + 3 test refs | `dotnet test` → **1416 passed, 0 failed** |
| 4. Manual smoke (WinUI) | pick a ComfyUI workflow → baked-model line, Preset picker, Krea-2 LoRA picker + Refresh, Upscale-after-render checkbox, Upscale-factor picker visibility flipping with the checkbox |
| 5. Commit as a pure move | recommend `-w --detect-moves` style review; keep Phase 1 free of behaviour tweaks so a bisect stays cheap |

Recommended commit split: **(a)** new coordinator file + GVM deletion/wiring + XAML repath, **(b)** test repath. Both must build; only (a) can break the app.

Estimated end state: `GeneratorViewModel` **2,527 → ~2,045 lines**, fields 17 → 15, ComfyUI flags 4 → 0 (on GVM), ComfyUI version tokens 5 → 0 (on GVM). Ctor params unchanged at 21 until Phase 2.

---

## 9. Decisions — RESOLVED (see LOCKED DECISIONS at top)

1. `ComfyUi` · 2. Phase 1 only · 3. mechanical repath (a) · 4. flat `Presentation/ViewModels/`

---

## 10. PROGRESS CHECKLIST — **Phase 1 COMPLETE & GREEN**

- [x] 1. `Presentation/ViewModels/ComfyUiWorkflowCoordinator.cs` created — **758 lines**, blocks
      517–725 and 883–1302 moved **verbatim** (script-assembled from the original text, never
      retyped), plus ctor / `Parameters` accessor / `RefreshForModel` /
      `RefreshLorasForCurrentModelAsync` / `OnUpscaleAfterRenderChanged`
- [x] 2. `GeneratorViewModel.cs` — **2,527 → 1,897 lines**; fields 17 → 15; ComfyUI flags 4 → 0;
      ComfyUI version tokens 5 → 0; ctor signature **unchanged** (21 params)
- [x] 3. `MainPage.xaml` — **22** occurrences prefixed (22 changed lines, nothing else touched)
- [x] 4. GATE: `dotnet build` → **0 errors, 0 warnings** (baseline was 0 warnings)
- [x] 5. `GeneratorViewModelTests.cs` — **77 member refs + 7 `NoKrea2LoraOption` refs** repathed
- [x] 6. GATE: `dotnet test` → **1417 passed, 0 failed, 0 skipped**
- [x] 7. Manual WinUI smoke (§8 step 4) — **done by the owner**: generated with the Krea-2
      workflow with and without LoRA; pushed as `c44a6ba`

### Measured corrections to this plan (all verified, all now reflected above)

> **History of this section (2026-11-17).** An external save at **14:46** — the same moment as the
> Rider reword/rebase in `git reflog` (`reword c44a6ba… → cdb46c5`) — reverted this file to a
> Phase-1-only snapshot, and because it is untracked there was nothing to revert *to*: items 7–10 of
> this list and the whole of §11's measured update were lost. **§11.1–11.7 are now the original text,
> restored from a copy of it**, and §11.8 holds everything measured since. Items 7–8 below are
> re-derived from the tree and the commits, not restored.
>
> Re-measuring this session found §11.1–11.7 **right** on the numbers it could be checked against
> (XAML 1 + 16 + 10 + 3 + 3 = **33** repaths; `UiMarkupContractTests.cs:83`'s `UseSageAttention`
> literal; `GalleryPage`'s same-named command belonging to `GalleryViewModel`) and one count off by
> one (§11.2's persist sites — corrected in §11.8). For the record, the rewritten version this
> replaced was worse than the original: it inventoried `UseDeepSeek`, `UseComfyUiUpscale`,
> `IsNsfwFilterEnabled`, `GenerateImageNsfw`/`SaveImageToGallery` flags, `InferenceSteps`,
> `ModelMaxSteps`, `SelectedAspectRatio`, `SelectedFormat`, `IsUpsamplingPromptEnabled`,
> `OllamaVisionBaseUrl`, `IsAnyModelLoaded`, `IsOllamaUrlValid` and a `ModelMetadataService`, **none of
> which exist anywhere in app or tests**. A confident table is not a measurement.

1. **Binding paths are NOT build-validated** in this project (§1). The plan's central safety
   claim was wrong; mitigation added, see §6c and the new guard test.
2. **`MainPage.xaml` had 22 ComfyUI binding occurrences / 18 names**, not 21.
3. **Test repath was 77 + 7 = 84 refs**, not 73 + 3.
4. **`UiMarkupContractTests.cs` also hard-codes binding strings** — 2 of its tests asserted the
   old paths (`MainPage_Krea2LoraControls_AreBoundAndAutomatable`,
   `MainPage_UpscaleFactorCard_IsVisibleOnlyWhileTheFactorCanApply`) and broke first. So the repo
   *did* have a partial XAML safety net; §6f's "GeneratorViewModelTests.cs only" was wrong.
5. Net moved code was 629 lines (not the estimated 483), hence 1,897 rather than ~2,045.
6. **Correction/strengthening of #1, measured later with a control** (full `-t:Rebuild` of the app
   project each time, then restored and re-verified clean):
   | mutation in `MainPage.xaml` (page-level, under `x:DataType="vm:GeneratorViewModel"`) | result |
   |---|---|
   | `Text="{Binding DefinitelyNotAProperty12345}"` | Build succeeded, 0 warnings, 0 errors |
   | `<Label BogusPropertyName="x" …>` ← **control: an element member, not a path** | Build succeeded, 0 errors |
   | malformed XML tag | `error MAUIG1001` |

   So **neither binding paths nor member names are build-validated**; only XAML *structure* is.
   `MauiEnableXamlCBindingWithSourceCompilation=true` (`csproj:18`) is a misleading leftover: it
   feeds `XamlCTask CompileBindingsWithSource` and the `RuntimeFeature` switch, but validating
   names is not what the pipeline does. (`InitializeComponent()` is absent from code-behind yet
   compiles, so the XAML generator *is* running — it just resolves names at runtime.)
   Consequence for every future refactor: **no configuration can buy build-time markup safety
   here; the reflection guard test is the only machine check that will ever exist for a binding
   path.** Do not spend another investigation on "just turn it on".

7. **`ProviderFilter` does not take settings.** The plan predicted it might; it takes **no** settings
   delegate — what the VM hands it is `isSageAttentionEnabled: () => UseSageAttention` plus the
   `ComfyUi.Supports*` predicates. The settings-reading seam on the sibling side is
   `GeneratorSettingsReader` (§11.8 item 2), and it is not `ProviderFilter`'s problem.
   _Original items 7–10 were lost in the 14:46 revert; 7–8 below are re-derived._
8. **Region E's first attempt is not in the history at all.** `64c256b` (extract) and `1d76336` (its
   revert) cancelled out and were dropped in a later reword/rebase, so there is nothing to undo — and
   no `LoadSavedSettings` anywhere in the tree: **Region E is simply not done** (§11.8 item 3). The
   reason recorded at the time was shape rather than behaviour, and the shape constraint is still
   true: settings state here is `[ObservableProperty]` fields with `partial void OnXChanged` hooks,
   which cannot be `async`, while today's hydration reads are synchronous
   (`var savedComfyUrl = _uiStateStore.LoadComfyUiBaseUrl();`, `:1627`) on the UI thread from
   `MainPage.OnAppearing` (`MainPage.xaml.cs:213`). So the moved shape is fields + `Task` loaders
   called in place, with **no** persistence policy left on the VM. Do not re-litigate it mid-Step-1.

### Notes worth keeping

- **A stale build hides XAML edits.** `UiMarkupContractTests` reads its pages from `bin/…/TestAssets`,
  and those are build-time copies (`Link="TestAssets\…Page.xaml" CopyToOutputDirectory="PreserveNewest"`,
  `ImageGenerator.MAUI.Tests.csproj:44-58`). `dotnet test --no-build` after a XAML edit tests the
  *previous* markup. Never verify a XAML change without building.
- **Region B is in** — `cdb46c5` "Extract ComfyUI workflow probing", `MainPage.xaml`'s 22 `ComfyUi.`
  prefixes, and the guard test. **Region E is not** (item 8). Next coordinator is still
  `BatchCoordinator`; whether Phase 2 goes before it is the open call in §11.
- **This file is untracked**, which is the only reason the 14:46 revert had anything to lose.

### Verification performed (beyond the gates)

- **Nothing lost in the move:** all **187** declared names in the original file still exist in
  `GeneratorViewModel` ∪ coordinator; the only additions are the 10 expected wiring members
  (`ComfyUi`, the ctor, `Parameters`, `RefreshForModel`, `RefreshLorasForCurrentModelAsync`,
  `OnUpscaleAfterRenderChanged`, 4 delegate fields).
- **The guard test is not a no-op** — verified by mutation, twice, with a real build:
  un-prefixing one binding → `line 239: SupportsCheckpoint must be bound as
  ComfyUi.SupportsCheckpoint — it moved to ComfyUi`; typo-ing `ComfyUi.BakedModelNmae` →
  `line 241: ... is not a property of ComfyUiWorkflowCoordinator`. Both `Failed: 1`.
  ⚠ Note: these tests read XAML from `bin/.../TestAssets`, so **a stale build hides XAML edits** —
  never verify a XAML change with `--no-build`.
- **Encoding/line endings preserved:** every touched file is UTF-8 without BOM, CRLF (an
  intermediate script produced `\r\r\n` in `MainPage.xaml`; caught, reverted, re-run — final diff
  is 22 lines, not 727).

### Files touched

```
M  ImageGenerator.MAUI/Presentation/ViewModels/GeneratorViewModel.cs   -630 lines net
A  ImageGenerator.MAUI/Presentation/ViewModels/ComfyUiWorkflowCoordinator.cs   758
M  ImageGenerator.MAUI/Presentation/Views/MainPage.xaml                 22 bindings
M  ImageGenerator.MAUI.Tests/ViewModels/GeneratorViewModelTests.cs      84 refs
M  ImageGenerator.MAUI.Tests/Presentation/UiMarkupContractTests.cs      +new guard test, 2 assertions
```

Untouched on purpose: `SettingsPage.xaml`, `MauiProgram.cs` (DI graph), all other pages/VMs,
`documents/todo.txt`.

---

## 11. PHASE 2 PLAN — `AppSettingsCoordinator`, exposed as `Generator.Settings`

Everything below was **measured**, not estimated; §10 is the lesson about the difference.

### 11.1 What moves (three regions, cut by anchor text, not line numbers)

| Region | Lines | Content |
|---|---|---|
| **D** | **193 → ~482** (≈290) | The whole settings block, from the anchor comment `// Non-secret server setting (first of its kind)` down to just above the `[NotifyPropertyChangedFor(nameof(SupportsResolution))]` / `_capabilities` block. Contains: `ComfyUiBaseUrl`, `UseSageAttention`, `FreeVramAfterRendering`, `AppThemeIndex` + `ApplyAppTheme`, `OllamaBaseUrl`, `OllamaModel`, `OllamaVisionModel`, `_suppressOllamaModelPersist`, `OpenRouterVisionModel`, `_suppressOpenRouterVisionModelPersist`, `OpenRouterVisionFreeOnly`, `OpenRouterVisionModels` + `RefreshOpenRouterVisionModelsAsync`, `OllamaModels` / `OllamaVisionModels`, `RefreshOllamaModelsAsync`, `_ollamaLoadAttempted`, `EnsureOllamaModelsLoadedAsync`, `SeedPicker`, `LoadOllamaModelsAsync`, `OutputFolder`, `BrowseOutputFolderAsync` |
| **E** | **~1617 → ~1690** (inside `LoadSavedUiState`) | The settings half of the UI-state hydration — starts at `// First write equals the loaded value…` (`LoadComfyUiBaseUrl`), ends after the `LoadOutputFolder` echo. **Verified: calls no VM helper at all**, so it drops in verbatim. `LoadSavedUiState()` keeps its signature and calls `Settings.LoadSavedSettings()` **in region E's own position** (§11.5 step 2) |
| **F** | **1399–1422** | `OpenOutputFolderAsync` `[RelayCommand]` — it exists only to `Process.Start` the folder `OutputFolder` names, so it moves with it (decision 11.4.3) |

Net ≈ **400 lines out → `GeneratorViewModel` lands near 1,500.**

### 11.2 The seam is unusually clean (verified, and this is the reason to do this one next)

Out of region D+E, the settings state touches **only**:
`IUiStateStore` — exactly **13 `Persist*` call sites over 11 distinct methods** in D and **12
`Load*` call sites over 12 distinct methods** in E — plus `SetStatus(…)` ×6, `_logger` ×2,
`IFolderPicker` ×1, `ApplyAppTheme` (private static — moves with it), `OutputPaths` statics,
`IOllamaModelCatalog`, `IOpenRouterModelCatalog`.

Those 11+12 store methods are **completely disjoint** from the ones the remaining VM uses (the
parameters/model cluster owns `Persist{Model,Prompt,Resolution,AspectRatio,UseJsonPrompt,
CivitaiModelRef}` and their 6 `Load*` counterparts). `IUiStateStore` is a *shared* dependency, but
no method is shared — which is the strongest possible signal that this seam is the real one. (It
also means a future split of `IUiStateStore` itself would be mechanical.)

It touches **nothing** else in the VM: not `Parameters`, not `Capabilities`, not `InputImages`,
not `Jobs`. (The one `ComfyUi.` hit in region D is `ModelConstants.ComfyUi.DefaultBaseUrl` — a
constant, not the Phase 1 coordinator.) Inbound, only two VM members read it:
`MaybeFreeComfyUiVramAsync` → `FreeVramAfterRendering` (1320) and `OpenOutputFolderAsync` (F).

→ Constructor takes the 2 catalogs + `IFolderPicker` + `ILogger`, **`IUiStateStore`**, and one
delegate `Action<string, StatusKind> setStatus`. `StatusKind` is declared in
`ImageGenerator.MAUI.Presentation.ViewModels` (`StatusKind.cs:3`) — the coordinator's own
namespace, so no new `using` and no cycle.

### 11.3 Blast radius (all counts re-measured)

| Surface | Count | Detail |
|---|---|---|
| XAML binding occurrences | **33** for 2A, over 5 pages | `SettingsPage` 16 (bare — its DataContext *is* `GeneratorViewModel`), `IdeaToPromptPage` 10 + `IdeogramStructureEditorPage` 3 + `MutationEnginePage` 3 (all via `Generator.`, since those pages bind their own VM which exposes `public GeneratorViewModel? Generator`), `MainPage` 1 (`OpenOutputFolderCommand`). A further **7** `TokenProviders`/`SelectedTokenProvider` occurrences sit in `SettingsPage` and stay put (§11.4.4) |
| ~~GalleryPage~~ | 0 | `GalleryPage.xaml:48` binds **`GalleryViewModel`'s own** `OpenOutputFolderCommand` (its own method at 377) — **must not be touched** |
| Sibling-VM C# reads | **9** | `_generator?.OllamaModel` ×3, `.OllamaBaseUrl` ×2, `.ComfyUiBaseUrl` ×2, `.OllamaVisionModel` ×1, `.OpenRouterVisionModel` ×1 |
| Page code-behind | **4** in scope | `EnsureOllamaModelsLoadedAsync` at `SettingsPage:26`, `IdeaToPromptPage:38`, `MutationEnginePage:27`, `IdeogramStructureEditorPage:57` |
| Code-behind **not** in scope | 3 | `MainPage:211 LoadAllTokensAsync`, `MainPage:213 LoadSavedUiState` (both stay on the VM), `SettingsPage:55 SelectedTokenProvider` (Phase 2B) |
| Tests | **53** → mostly isolated already | `GeneratorViewModelTests` 47 + `IdeaToPromptViewModelTests` 4 + `MutationEngineViewModelTests` 2; `IdeogramStructureEditorViewModelTests` 0. **Prep A (§12.1) already moved 17 settings tests into `GeneratorViewModelTests.Settings.cs`**, so this is now "move one 297-line file", not "reach across a 3,221-line fixture" |
| Tests **avoided** | 33 | `LoadSavedUiState` ×22 keeps its VM signature, `LoadAllTokensAsync` ×4 and the 7 `TokenProviders` refs are 2B → all untouched |
| Markup-contract literals | **1** | `UiMarkupContractTests.cs:83` asserts `IsToggled="{Binding UseSageAttention, Mode=TwoWay}"` verbatim → part of the repath set, and it will fail **first** (exactly as Phase 1's 2 markup assertions did). The guard test in step 0 will not excuse it |

### 11.4 Decisions to lock before code (my recommendation each)

1. **Name: `Settings`** → `{Binding Settings.OllamaModel}`. Verified collision-free: the only
   `Settings*` identifiers in the app are `SettingsPage` and `SettingsCategory`, and a
   `Generator.Settings.OllamaModel` path is unambiguous even in pages that reach it via
   `Generator.`. Alternative `AppSettings` if you prefer to avoid any echo of `SettingsPage`.
2. **Repath the 9 sibling-VM reads + 4 code-behind calls** rather than leave read-through
   properties on the VM (same "no shims" rule as Phase 1).
3. **`OpenOutputFolderCommand` moves too** (region F) → 1 XAML repath in `MainPage.xaml`.
   Alternative: keep the command on the VM reading `Settings.OutputFolder` — saves 1 repath but
   leaves `OutputFolder` reached into from two owners. Recommend moving.
4. **Token providers (`TokenProviders`, `SelectedTokenProvider`, `LoadAllTokensAsync`,
   `ForgetSelectedToken`, 6 stores) — recommend NOT extracting.** It is only ~90 VM lines, it
   drags the `SettingsPage.xaml.cs` `OnTokenTextChanged` split-binding dance and six
   `syncToParameters` closures that write into `Parameters`, i.e. the worst value/risk ratio of
   the remaining clusters. `TokenProviderViewModel` already owns its complexity.
5. **Ctor stays at 21 parameters; the VM forwards the settings services to the coordinator**
   (`_ollamaModelCatalog`, `_openRouterModelCatalog`, `_folderPicker`) — the Phase 1 rule. The
   alternative (drop those 3 params, have `MauiProgram` hand them to the coordinator) would shrink
   the ctor to 18 and is *cheap on the DI side* — all four are already `AddSingleton` at
   `MauiProgram.cs:200/215/218/221` — but it touches `MauiProgram.cs`, 2 of the 5
   `new GeneratorViewModel(…)` test sites, and the coordinator has to re-invent the *optional*
   semantics (those 3 arrive as nullable today and tests pass null). Recommend: forward in 2A,
   and do one dedicated "ctor diet" commit after the last cluster lands, when the final parameter
   list is actually known.

### 11.5 Steps (Phase 1's order, with the gates that caught things last time)

0. **Extend the binding guard test to all 5 pages first** — `UiMarkupContractTests` currently
   checks only `MainPage`. Generalize `MainPage_PageLevelBindings_ResolveAgainstTheViewModelGraph`
   to `(page, rootType)` pairs, including the `Generator` sub-model hop on the 3 sibling pages.
   Run it **green before moving anything**; it enumerates the true page-level binding surface (33
   of those paths are the 2A set) and makes the repath machine-checked. ⚠ **never verify a XAML
   change with `--no-build`** — those tests read
   XAML from `bin/…/TestAssets`, which is a build-time copy (§10).
1. Add `AppSettingsCoordinator.cs` (flat, `Presentation/ViewModels/` — same folder and shape as
   `ComfyUiWorkflowCoordinator`), cut D + E + F **verbatim** with the Phase 1 extraction script
   pattern, then add: ctor (`IUiStateStore`, 2 catalogs, `IFolderPicker`, `ILogger`,
   `Action<string, StatusKind> setStatus`), `LoadSavedSettings()`, and the public
   `EnsureOllamaModelsLoadedAsync()` that 4 pages call.
2. `GeneratorViewModel`: add `public AppSettingsCoordinator Settings { get; }`, constructed in the
   ctor (before `LoadSavedUiState` can possibly run); replace region E **in place** with a single
   `Settings.LoadSavedSettings();` — i.e. keep it exactly where E was, after the `CivitaiModelRef`
   and `Prompt` restores (1604/1610) and before `LoadModel`/`RestoreSelectedModel` — then keep
   everything below untouched; leave the 2 inbound reads (`MaybeFreeComfyUiVramAsync` →
   `Settings.FreeVramAfterRendering`; F is gone entirely).
   Ordering note: nothing *forces* the settings block to sit after the Prompt/ModelRef restores
   (region E never reads `Parameters`), but calling it in place makes the sequence bit-identical
   and removes "did I change an order?" from the review. Do it in place.
3. Build app → expect 0 warnings; then the **33** XAML repaths **by script with per-name count
   assertions** (the Phase 1 script pattern), then re-run the guard test.
4. Repath **9 + 4 + 53** C# references (script, per-name assertions).
5. **Gates**: build 0 errors/0 warnings; `dotnet test` **1,417 passed, 0 failed** (same count —
   the guard test is generalized, not added).
6. Manual WinUI smoke: Settings page — theme picker actually re-themes, ComfyUI URL + Ollama URL
   round-trip across restart, Ollama "Refresh models" against a live server, output-folder browse +
   "Open output folder", and the Krea-2 path from Phase 1 still generates.

### 11.6 Hazards (specific, all verified in the current source)

- **The split point of `LoadSavedUiState` is load-bearing.** Region E runs *before*
  `ProviderFilter.RestoreSelectedModel(savedModel)` (1696) and the three model-dependent restores
  deliberately run *after* it, each with a comment saying why: resolution ("Restore the resolution
  after the model: `RestoreSelectedModel` ran `RefreshCapabilities`, which both populated
  `ResolutionOptions` … and slammed the value to the first option"), aspect ratio, and the JSON
  prompt toggle ("Restore the structured-JSON toggle LAST … restoring it blindly would re-check the
  box on a model whose Build has no json_prompt field"). So `Settings.LoadSavedSettings()` must be
  the **first** thing `LoadSavedUiState` does, everything below it stays in place, and the single
  `_uiStateLoaded` guard stays on the VM (add a separate `_settingsLoaded` latch only if the
  coordinator is ever called on its own). Caller order is also fixed:
  `MainPage.OnAppearing` → `LoadAllTokensAsync()` → `LoadCachedCatalogAsync()` →
  `LoadSavedUiState()` (`MainPage.xaml.cs:211-213`), inside a defensive `try` because an
  `async void` here crashes via `SynchronizationContext`.
- Note the two restores that currently run *before* region E (`LoadCivitaiModelRef` 1604,
  `LoadPrompt` 1610) are parameters-side state and stay in the VM — so `LoadSavedUiState` ends up
  as: model-ref/prompt restores → `Settings.LoadSavedSettings()` → model + resolution + AR +
  JSON-prompt restores. Slightly odd-looking, and deliberately so; the tidy-up (hoisting
  `Settings.LoadSavedSettings()` above the parameter restores) belongs in a *separate*, explicitly
  behavioural commit if it happens at all.

- **2 picker-suppression flags move private**: `_suppressOllamaModelPersist` (275) and
  `_suppressOpenRouterVisionModelPersist` (287). `LoadSavedSettings` wraps the seeding in
  `try/finally` around them; `LoadOllamaModelsAsync` re-persists the selected models **and** the
  full lists (419–421) inside that window. Keep the `finally` ordering exactly.
- **`AppThemeIndex` has a side effect beyond persistence** (`ApplyAppTheme` →
  `Application.Current.UserAppTheme`). Must stay on the UI thread and keep running *after* the
  store write, exactly as today; `App.xaml.cs` applies the theme independently at startup.
- **`OnOutputFolderChanged` calls `OutputPaths.SetRootOverride`** — a process-wide static. The
  composition root already applied it before the VM existed; the `OnChanged` is an echo. Do not
  "improve" the ordering when moving.
- `EnsureOllamaModelsLoadedAsync` is fired-and-forgotten from 4 pages and latches
  `_ollamaLoadAttempted`; the latch moves with it.
- `SettingsPage.xaml.cs` holds `_viewModel` typed as `GeneratorViewModel` — fine in 2A (its 23
  bindings are XAML-only), and the reason `SelectedTokenProvider` stays put.
- **Do not touch `GalleryPage.xaml:48`** (§11.3) — same command *name*, different owner.

### 11.7 After Phase 2 — what is left in the VM (rough shape, unmeasured — re-measure before planning)

Ctor ≈197, `OnParametersPropertyChanged` ≈98, job pipeline (`GenerateImageAsync` 46 +
`RunJobAsync` 87 + the GPU/VRAM slot helpers `WaitForRenderSlotAsync` 20 / `AcquirePreferredSlotAsync`
17 / `WaitForGpuSlotAsync` 16 / `ShouldKeepComfyUiVramForNextJobAsync` 22 / `MaybeFreeComfyUiVramAsync`
20 + `StartNextIfIdle` 28) ≈300, remix + navigation (`RemixFromImageAsync` 63,
`LoadSavedJobIntoParametersAsync` 53, `ApplyRemixAsync` 34, `NavigateAsync` 27,
`NavigateToGalleryAfterGenerationAsync` 23, `OpenOutputFolderAsync` 24) ≈250,
catalog + capabilities (`LoadCachedCatalogAsync` 41, `ApplyWorkflowDefaultsForSelectedModel` 43,
`RefreshCapabilities` ~45, `OnSelectedModelChanged` 28, `SetModelOptionsProgrammatically` 29)
≈230, picker/status/token glue (`SetAspectRatioProgrammatically` 29,
`LoadTotalImagesGeneratedAsync` 28, `SetStatus` 22, `ApplyCapabilitiesToParameters` 22,
`LoadAllTokensAsync` 18) ≈220. The job pipeline is the only remaining extraction of Phase-1 size;
the capabilities/picker cluster is the riskiest because it is entangled with `Parameters`,
`Capabilities` and 4 more suppress flags. The 21-param ctor is the last real smell.

### 11.8 Measured update — Step 0 is done, and four numbers above have moved (2026-11-17)

Everything in §11.1–§11.7 above is unchanged original text. This subsection is the delta, measured at
`ab71fcc` on a green tree (**1,427 passed / 0 failed / 8 skipped**, app 0 warnings / 0 errors). Each
item names the command or file:line it came from.

**1. Step 0 (`451b50e`) is done — and it is not the generalisation §11.5 step 0 sketched.**
`MainPage_PageLevelBindings_ResolveAgainstTheViewModelGraph` became
`PageLevelBindings_ResolveAgainstTheViewModelGraph(string asset)`, a `[Theory]` over
`PagesWithADeclaredViewModel()` = **all 7 pages**, each rooted at the `x:DataType` its own page
declares. It was worth more than the 20 lines it cost, because the MainPage-only guard only ever
worked by coincidence: it flagged a moved member when a name was *absent from the page root and
present on a sub-model*, which held for Phase 1's 10 members and stops holding the moment `Settings`
exists — `Generator.Settings.X` resolves for the new shape and fails for the old one with a *wrong
root*, which is exactly the two-way error a prefix table cannot see. It now walks each path one
segment at a time against the real type graph (a collection resolves to its item type), treats a
property whose type ends in `Coordinator` as a sub-model hop needing no table to edit, fails a member
that lives on a sub-model **with the path to bind instead**, fails a path that ends on a sub-model
object, and fails any page that contributes almost no bindings — because a scope filter that swallowed
a page would otherwise be silently green. Out of scope, and skipped: `DataTemplate` scopes declaring
their own `x:DataType`, and bindings naming their own `Source=` / `RelativeSource=` / `x:DataType`
(all 9 `DataTemplate`s in the 5 pages declare one, so nothing was silently dropped).
Measured surface now under contract: **382 page-level bindings, 38 of them through a coordinator**
(MainPage 114, SettingsPage 27, IdeaToPrompt 68, MutationEngine 47, IdeogramStructureEditor 86,
Gallery 22, GalleryItemDetail 18). Mutation-checked 8 ways — typo, bare coordinator member, path
ending on `ComfyUi`, two broken `Generator.OllamaModels` hops, a `GalleryPage` binding pointed at a
`Settings` hop `GalleryViewModel` does not have, and a typo inside a `DataTemplate` which must stay
green — 8/8 as predicted, with messages like
`MainPage.xaml line 267 (Picker.SelectedItem): {Binding SelectedKrea2Lora} — SelectedKrea2Lora is not a property of GeneratorViewModel; bind ComfyUi.SelectedKrea2Lora instead`.

**2. §11.3's "Sibling-VM C# reads — 9" is done.** `3e740db` replaced them with
`Presentation/ViewModels/GeneratorSettingsReader.cs`: one primary-constructor class over
`GeneratorViewModel?`, **5** reads (`OllamaBaseUrl`, `OllamaModel`, `OllamaVisionModel`,
`OpenRouterVisionModel`, `SharesGpuWithComfyUi` → `ComfyUiBaseUrl`), used by all three sibling VMs
(`IdeaToPromptViewModel:33/63`, `MutationEngineViewModel:45`, `IdeogramStructureEditorViewModel:30/53`).
`grep -E "_generator[?]?\.(OllamaModel|OllamaBaseUrl|ComfyUiBaseUrl|OllamaVisionModel|OpenRouterVisionModel)" Presentation/ViewModels/`
now returns nothing, so the C# side of Phase 2 is **re-pointing that one constructor** — its own doc
comment already says so, and §12.2 is the write-up of what Prep B actually moved. What Prep B did *not* do is the markup: the **33 XAML paths are untouched**
and re-measured identical (MainPage 1, SettingsPage 16, IdeaToPrompt 10, IdeogramStructureEditor 3,
MutationEngine 3). Two consequences: **sibling pages say `{Binding Generator.Settings.<Prop>}`**, and
the three sibling VMs get **no `Settings` property of their own** — one piece of state, one object in
the path.

**3. Region E has not been started, and its earlier attempt is gone from history.**
`LoadSavedUiState()` is still `public void` at `:1608` holding **18** `_uiStateStore.Load*` reads:
**12 settings** (`AppTheme`, `ComfyUiBaseUrl`, `FreeVramAfterRendering`, `OllamaBaseUrl`,
`OllamaModel`, `OllamaModels`, `OllamaVisionModel`, `OllamaVisionModels`, `OpenRouterVisionModel`,
`OpenRouterVisionModels`, `OpenRouterVisionFreeOnly`, `OutputFolder`) + **6** parameters
(`Model`, `Prompt`, `Resolution`, `AspectRatio`, `UseJsonPrompt`, `CivitaiModelRef`). That 12/6 split
*is* the cut line §11.1 describes with comment anchors, and it is §11.2's "no shared method" claim
restated in one grep. The two commits that tried it (`64c256b` extract, `1d76336` revert) cancel out
and were dropped in the reword/rebase; nothing needs undoing. VM today: **1,906 lines, 19
`[ObservableProperty]` fields, 14 `[RelayCommand]` methods.**

**4. §11.2's persistence count is 14 call sites over 11 distinct methods, not 13.** Nine sit in the
`[ObservableProperty]` `OnChanged` hooks (`ComfyUiBaseUrl` 196, `UseSageAttention` 205,
`FreeVramAfterRendering` 213, `AppTheme` 223, `OllamaBaseUrl` 243, `OllamaModel` 257,
`OllamaVisionModel` 266, `OpenRouterVisionModel` 278, `OpenRouterVisionFreeOnly` 290), one in
`RefreshOpenRouterVisionModelsAsync` (303), three in `LoadOllamaModelsAsync` (376 — the pick *and*
both lists), one in `OnOutputFolderChanged` (457). The parameters cluster's 6 are all in
`OnParametersPropertyChanged` (997). **Zero overlap still holds** — that part of §11.2 is the seam's
main evidence and it survives re-measurement.

**5. Prep C (`ab71fcc`) — the one thing Phase 3 has to inherit.** Replacing the `Parameters`
*instance* left both token rows showing a pasted token while the new instance held `""`, and
`ValidateParameters` agreed with the refusal. `OnParametersChanged` now re-seeds both slots
(`TokenProviderViewModel.PushToParameters()`) before revalidating, pinned by a test that failed before
the fix. Measured alongside it: `RemixFromImageAsync` writes into the live instance and never swaps
it, and nothing in app or tests assigns `Parameters` — so the "Remix swaps the instance" comment was
describing a swap no caller performs. Constraint: `Parameters` stays on the VM per §11.4/§11.7; if a
later phase moves it, the re-seed moves with it.

**6. Two gates in §11.5 need restating with their new numbers.** Step 0 changed the test count, so
step 5's "1,417, same count" is now **1,425 after step 0, 1,427 after Prep C**. And step 0's own
warning generalises: every page-level binding on all 7 pages is machine-checked from Step 0 onward,
which is what makes step 3's 33 scripted repaths reviewable — leaving one of the 16 sibling paths on
the old owner now fails the case naming the page, the line and the new path.

**7. Add one line to §11.6's hazards: any new fixture needs `[Collection("OutputPathsState")]`.**
`OnOutputFolderChanged` calls `OutputPaths.SetRootOverride`, a process-wide static that §11.6 already
warns not to re-order; `GeneratorViewModelTests.Settings.cs` is inside that collection precisely so
those tests stay serialised. An `AppSettingsCoordinatorTests` fixture written outside it would race
the output-folder tests over the root override.

**8. Provenance note.** §11.1–§11.7 were restored from a copy after an external save at 14:46
reverted this untracked file; see §10 for the incident, and commit the file.

---

## 12. PREP COMMITS — shrink Phase 2's blast radius first

Motivation (owner): "33 XAML + 9 sibling reads + 4 code-behind + 53 test refs sounds scattered —
can we shorten it first?" Measured verdict: **only 2 of the 4 clusters are shrinkable**, and the
XAML one is the one you must not touch — because §10 correction 6 proved no build-time markup
validation exists, pre-refactoring 3 pages is the *riskiest* way to reduce a number that the guard
test already machine-checks.

| Cluster | Shrink? | Action |
|---|---|---|
| 53 test refs | ✅ **Prep A, done** | 17 settings tests moved to `GeneratorViewModelTests.Settings.cs` (§12.1) |
| 9 sibling reads | ✅ **Prep B, done** | one shared settings-resolution helper (§12.2) |
| 33 XAML paths | ❌ don't | the 3 sibling picker blocks are **25 / 8 / 7** lines with different layouts — not duplicated boilerplate; a shared control is a bigger, visually riskier change than the repath it would avoid |
| 4 code-behind calls | ❌ don't | collapsing them into one startup call moves an Ollama HTTP fetch to app start, including when Ollama is down |

### 12.1 Prep A — **DONE** (test-only)

- `GeneratorViewModelTests.cs` **3,221 → 2,943 lines**; new `GeneratorViewModelTests.Settings.cs`
  **297 lines / 17 tests**.
- Mechanism: **`partial` class split, not a new fixture** — deliberate. The tests still need the
  21-param `GeneratorViewModel` (no `AppSettingsCoordinator` yet), and
  `[Collection("OutputPathsState")]` must keep serializing them against the `OutputPaths.
  SetRootOverride` global state. A second class would have escaped that collection and turned the
  output-folder tests into a race. Same class ⇒ same collection, same ctor, and **unchanged test
  IDs** (`GeneratorViewModelTests.UseSageAttention_…`), so CI history does not blink.
- Moved verbatim: 3 contiguous blocks — OpenRouter + `UseSageAttention` (5 tests), output folder
  (banner + 3), the Ollama picker block (banner + 9) — **plus the private helpers `Model(...)` and
  `OllamaVm(...)`**, used only by moved tests (verified: all 7 `OllamaVm(` and all 5 `Model(` call
  sites are inside the moved ranges).
- Deliberately **not** moved: `MaybeFreeComfyUiVram_FreesOnlyWhenEnabled_…`,
  `GenerateImage_ComfyUiModel_SameHostAsOllama_…`, `GenerateImage_NonComfyUiModel_…` — they read a
  setting but *test GPU gating*, so they stay with that cluster. Fixture fields/mocks stay in the
  main part (ctor-initialized).
- **Proof of no drift:** multiset diff of source lines, `original − (main ∪ new)` = exactly
  `{public class GeneratorViewModelTests: 1}`; additions are the 20 header lines + `partial`.
  Zero test content changed.
- Gates: build **0 errors / 0 warnings**; **1,417 passed, 0 failed, 0 skipped** (identical count);
  all 17 moved names re-verified as discovered via `--list-tests` (183 unique method names before
  and after). Usings pruned 13 → 7 with the build still clean.
- Why it pays: Phase 2's test work becomes *moving one 297-line file onto a 5-param fixture*
  instead of reaching across a 3,221-line fixture, and the god-fixture is 17 methods smaller for
  good.

### 12.2 Prep B — **DONE**: one owner for the sibling settings resolution

- New `Presentation/ViewModels/GeneratorSettingsReader.cs` (**47 lines**, `internal sealed class
  GeneratorSettingsReader(GeneratorViewModel? generator)`), held as one ctor-assigned `_settings`
  field in each of the three companion VMs.
- It owns exactly the reads that were duplicated: `OllamaBaseUrl` (blank → 
  `ModelConstants.Ollama.DefaultBaseUrl`), `OllamaModel` (**no** default, deliberately),
  `OllamaVisionModel` (falls back to the text model), `OpenRouterVisionModel` (empty when unset),
  `SharesGpuWithComfyUi` (= `GpuColocation.SameHost(ComfyUiBaseUrl, OllamaBaseUrl)`).
- Deleted: IdeaToPrompt's 3 private resolvers, MutationEngine's `ResolveOllamaBaseUrl` + its XML doc
  (that knowledge now lives on the reader properties), and 3 inline `?? string.Empty` reads.
  `IdeaToPrompt.ResolveVisionModel` **stays** — the provider switch is page policy, not settings
  resolution — but is now 3 lines over the shared properties.
- Fan-out: the 9 sibling settings reads are now **5 reads, all inside the reader**. `_generator` itself
  stays in all three VMs (12 / 28 / 15 refs) for its non-settings uses: render handoff, `IsGpuBusy`,
  `PendingBreedSet`, `Parameters.…`.
- Also removed 2 now-unreferenced `using ImageGenerator.MAUI.Shared.Constants;` (verified none of that
  namespace's other 10 public types are used in those files).
- Diff: 4 files, **69 insertions / 32 deletions**, plus the new 47-line file. App build 0/0, test build
  0/0, **1,419 passed (1,417 + 2 new), 0 failed, 0 skipped**.
- Correction to the §12 table's earlier claim: `IdeogramStructureEditorViewModel:177` re-read
  `OllamaModel`, not `ComfyUiBaseUrl`; `ComfyUiBaseUrl` had exactly 2 sibling reads, both now folded
  into `SharesGpuWithComfyUi`.

**What the mutation checks proved.** Each mutation was applied to the reader, the suite run, then the
file restored and `cmp`-verified byte-identical:

| reader mutation | caught? | by |
|---|---|---|
| `SharesGpuWithComfyUi => false` | ✅ | `MutateWithAi_LocalTier_SameHost_AcquiresAndReleasesGpuGate` |
| drop the `OllamaVisionModel` → text-model fallback | ✅ ≥5 tests | `Build_ImageMode_ObservesImageBeforeVpe…`, `ReObserve_…`, … |
| `OpenRouterVisionModel` falls back to the local model | ✅ | `Build_OpenRouterImageMode_WithBlankModel_FailsBeforeObservation` |
| wrong default constant (`ComfyUi` for `Ollama`) | ✅ **new tests only** | `…_BlankOllamaUrl_UnloadsAtTheDefaultEndpoint` |
| no blank fallback (`?? string.Empty`) | ✅ **new tests only** | same |
| ignores the setting, always the default | ✅ **new tests only** | `…_UnloadsAtTheGeneratorsEndpointAndModel` |

Two findings from that exercise, both worth keeping:

1. **The unload endpoint had zero coverage.** `StubOllamaCatalog` counted `UnloadAsync` calls and
   ignored both arguments; `IdeaToPromptViewModelTests` has no unload fake at all; and the one
   existing unload test builds its VM with `generator: null`, so it only ever exercised the
   *null-generator* branch. "Which endpoint/model reaches `IOllamaModelCatalog.UnloadAsync`" was the
   one moved behavior no test could fail — hence the 2 added tests, which turn the dedupe from
   inspection-checked into machine-checked (and they are the only reason the three endpoint
   mutations above fail). Assert members, not tuples: `Equal` on a `List<(string, string)>` hits a
   FluentAssertions overload trap (`error CS1503: Argument 2: cannot convert from 'string' to
   '(string Url, string Model)'`).
2. **Still unpinned, pre-existing:** the `?? string.Empty` null branch of `OllamaModel`. The settings
   back-fields default to `string.Empty` (`GeneratorViewModel.cs:257/266/278`), so that branch only
   fires when a companion VM is built with `generator: null` — "hosted stand-alone", per
   `EnrichLocalModel`'s own doc. A mutation replacing it with a guessed model name survived all
   1,419 tests. Left as-is; only worth a test if standalone hosting ever becomes real.

**Phase 2 consequence:** the settings surface the coordinator must serve outside XAML is now *one
file* (`GeneratorSettingsReader`) plus 33 XAML paths, 4 code-behind `EnsureOllamaModelsLoadedAsync`
calls and 1 markup-contract literal.
