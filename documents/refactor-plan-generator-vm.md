# GeneratorViewModel refactor — **Phase 2**: `AppSettingsCoordinator`, exposed as `Generator.Settings`

**Status**: planned, no Phase 2 code written. Tree green — **1,427 passed / 0 failed / 0 skipped**, app
builds 0 warnings / 0 errors.

**This file is Phase 2 only.** The complete Phase 1 record — the extraction itself, its measured blast
radius, the seven corrections to that plan, and Prep A/B/C as written up — is archived verbatim at
`documents/archive/refactor-plan-generator-vm-phase1.md` (byte-identical to that file at `b6b795a`;
the archive was committed as `7aec33f`, before any pruning).

Every number below was **measured from the working tree**, not inherited from the archived text. Where
the two disagree, §11 says what moved and why. The rule this project paid for twice: **a figure with no
stated measurement command is not a measurement** — and a confident table is worse than no table.

---

## 1. Ground rules (locked by owner, 2026-11-17)

| # | Rule | What it forbids in the code |
|---|---|---|
| 1 | Sibling pages bind **`{Binding Generator.Settings.<Prop>}`** | adding a `Settings` property to `IdeaToPromptViewModel`, `MutationEngineViewModel`, `IdeogramStructureEditorViewModel`. One piece of state, one object in the path |
| 2 | `OpenOutputFolderAsync` leaves with region **F** | leaving it on `GeneratorViewModel` "because MainPage binds it" — it repaths with its data |
| 3 | Any new `AppSettingsCoordinatorTests` class carries `[Collection("OutputPathsState")]` on itself | a second fixture outside that collection racing `OutputPaths.SetRootOverride` (§7) |
| 4 | `Parameters` and the token providers stay on the VM | moving `Parameters` without moving the token re-seed in `OnParametersChanged` (§9) |
| 5 | Re-measure every gate count; never copy one forward | gating a XAML step on `--no-build` (§8) |

---

## 2. What moves — three regions, cut by anchor text, never by line number

| Region | Anchor range | Content |
|---|---|---|
| **D** | `193 → 484` (292 lines) | The whole settings block, from `// Non-secret server setting (first of its kind)` down to just above the `[NotifyPropertyChangedFor(nameof(SupportsResolution))]` / `_capabilities` block. `ComfyUiBaseUrl`, `UseSageAttention`, `FreeVramAfterRendering`, `AppThemeIndex` + `ApplyAppTheme`, `OllamaBaseUrl`, `OllamaModel`, `OllamaVisionModel`, `_suppressOllamaModelPersist`, `OpenRouterVisionModel`, `_suppressOpenRouterVisionModelPersist`, `OpenRouterVisionFreeOnly`, `OpenRouterVisionModels` + `RefreshOpenRouterVisionModelsAsync`, `OllamaModels` / `OllamaVisionModels`, `RefreshOllamaModelsAsync`, `_ollamaLoadAttempted`, `EnsureOllamaModelsLoadedAsync`, `SeedPicker`, `LoadOllamaModelsAsync`, `OutputFolder`, `BrowseOutputFolderAsync` |
| **E** | `1625 → 1700` (76 lines), inside `LoadSavedUiState` | The settings half of the UI-state hydration — starts at `// First write equals the loaded value…` (`LoadComfyUiBaseUrl`), ends after the `LoadOutputFolder` echo. **Verified: calls no VM helper at all**, so it drops in verbatim. `LoadSavedUiState()` keeps its signature and calls `Settings.LoadSavedSettings()` **in region E's own position** (§6 step 2) |
| **F** | `1408–1430` (23 lines, attribute line included) | `OpenOutputFolderAsync` `[RelayCommand]` — it exists only to `Process.Start` the folder `OutputFolder` names, so it moves with it (§5 decision 3) |

Region E is **not started** — re-verified, not assumed: `LoadSavedUiState()` is still `public void` at
`:1608` (the method runs to `:1747`, **140** lines) holding **18** `_uiStateStore.Load*` reads — **12 settings** (`AppTheme`, `ComfyUiBaseUrl`,
`FreeVramAfterRendering`, `OllamaBaseUrl`, `OllamaModel`, `OllamaModels`, `OllamaVisionModel`,
`OllamaVisionModels`, `OpenRouterVisionModel`, `OpenRouterVisionModels`, `OpenRouterVisionFreeOnly`,
`OutputFolder`) + **6 parameters** (`Model`, `Prompt`, `Resolution`, `AspectRatio`, `UseJsonPrompt`,
`CivitaiModelRef`). That 12/6 split *is* the cut line. Two commits once attempted it (`64c256b`
extract, `1d76336` revert); they cancelled out and were dropped in a later reword, so there is nothing
to undo and no `LoadSavedSettings` anywhere in the tree.

The shape constraint that killed that attempt is still true and should not be re-litigated mid-step:
settings state here is `[ObservableProperty]` fields with `partial void OnXChanged` hooks, which cannot
be `async`, while today's hydration reads are synchronous (`var savedComfyUrl =
_uiStateStore.LoadComfyUiBaseUrl();`, `:1627`) on the UI thread from `MainPage.OnAppearing`. So the
moved shape is **fields + `Task` loaders called in place**, with **no** persistence policy left on the
VM.

Net **391 lines out** (292 + 76 + 23, measured) → `GeneratorViewModel` lands near **1,515**
once the few wiring lines are added back (the file is **1,905** lines; **292** region D + **76** region E + **23** region F). Of its **32** `[ObservableProperty]` fields **11**
move, and of its **12** `[RelayCommand]` commands **4** move (3 in D, 1 in F).

---

## 3. Why this seam — and why it is the next one

Out of region D+E, the settings state touches **only**: `IUiStateStore` — exactly **14 `Persist*` call
sites over 12 distinct methods** in D — plus **12 `Load*` call sites over 12 distinct methods** in E,
`SetStatus(…)` ×6, `_logger` ×2, `IFolderPicker` ×1, `ApplyAppTheme` (private static — moves with it),
`OutputPaths` statics, `IOllamaModelCatalog`, `IOpenRouterModelCatalog`.

The 14 persist sites, by the line the call is actually on (not the field declaration):
nine `[ObservableProperty]` `OnChanged` hooks (`OnComfyUiBaseUrlChanged` 199, `OnUseSageAttentionChanged`
208, `OnFreeVramAfterRenderingChanged` 216, `OnAppThemeIndexChanged` 228, `OnOllamaBaseUrlChanged`
246, `OnOllamaModelChanged` 262, `OnOllamaVisionModelChanged` 271,
`OnOpenRouterVisionModelChanged` 283, `OnOpenRouterVisionFreeOnlyChanged` 295), one in
`RefreshOpenRouterVisionModelsAsync` (328), three in `LoadOllamaModelsAsync` (419–421 — the
pick *and* both lists), one in `OnOutputFolderChanged` (462).

Those store methods are **completely disjoint** from the ones the remaining VM uses — the
parameters/model cluster's 6 are all in
`OnParametersPropertyChanged` (997–1093), at 1015 / 1028 / 1032 / 1071 /
1077 / 1086:
`Persist{Model,Prompt,Resolution,AspectRatio,UseJsonPrompt,CivitaiModelRef}` and their `Load*` twins.
`IUiStateStore` is a *shared* dependency with **no shared method**, which is the strongest available
signal that this seam is real (and that splitting `IUiStateStore` itself later would be mechanical).

It touches **nothing** else in the VM: not `Parameters`, not `Capabilities`, not `InputImages`, not
`Jobs`. (The one `ComfyUi.` hit in region D is `ModelConstants.ComfyUi.DefaultBaseUrl` — a constant,
not the Phase 1 coordinator.) Inbound, only two VM members read it: `MaybeFreeComfyUiVramAsync` (1329) →
`FreeVramAfterRendering` (1331) and region F.

→ Constructor takes the 2 catalogs + `IFolderPicker` + `ILogger` + `IUiStateStore`, and one delegate
`Action<string, StatusKind> setStatus`. `StatusKind` is declared in
`ImageGenerator.MAUI.Presentation.ViewModels` (`StatusKind.cs:3`) — the coordinator's own namespace, so
no new `using` and no cycle.

---

## 4. Blast radius (all counts re-measured on the current tree)

Definition of the **33**, so the headline number can be re-derived instead of trusted: walk every `{Binding …}` in the page, take
the **bound member** — the last path segment, after stripping `Path=` — and count it when that member is one
of the 17 that move: the 13 properties plus `RefreshOllamaModelsCommand`, `RefreshOpenRouterVisionModelsCommand`,
`BrowseOutputFolderCommand` and `OpenOutputFolderCommand`. Result: MainPage 1 / SettingsPage 16 / IdeaToPrompt 10 /
MutationEngine 3 / IdeogramStructureEditor 3 = **33**. `GalleryPage` contributes one more under the same
rule and is excluded because its owner is a different view model (row 2). The 16 sibling-page ones are
exactly the `Generator.` hop that step 0 machine-checks.

| Surface | Count | Detail |
|---|---|---|
| XAML binding occurrences | **33** | `SettingsPage` 16 (bare — its DataContext *is* `GeneratorViewModel`), `IdeaToPromptPage` 10 + `IdeogramStructureEditorPage` 3 + `MutationEnginePage` 3 (all via `Generator.`, since those pages bind their own VM, which exposes `public GeneratorViewModel? Generator`), `MainPage` 1 (`OpenOutputFolderCommand`). A further **7** `TokenProviders` / `SelectedTokenProvider` occurrences in `SettingsPage` stay put (§5 decision 4) |
| ~~GalleryPage~~ | **0** | `GalleryPage.xaml:48` binds **`GalleryViewModel`'s own** `OpenOutputFolderCommand` (its own method at 377) — **must not be touched** |
| Sibling-VM C# reads | **0** — already done | `3e740db` routed them through `Presentation/ViewModels/GeneratorSettingsReader.cs`: **5 reads, 1 file, 3 sibling VMs** (`OllamaBaseUrl`, `OllamaModel`, `OllamaVisionModel`, `OpenRouterVisionModel`, `SharesGpuWithComfyUi` → `ComfyUiBaseUrl`). `grep -E "_generator[?]?\.(OllamaModel\|OllamaBaseUrl\|ComfyUiBaseUrl\|OllamaVisionModel\|OpenRouterVisionModel)" Presentation/ViewModels/` returns nothing. What Phase 2 owes is **that reader's `GeneratorViewModel?` constructor parameter** and nothing else |
| Page code-behind | **4** | `EnsureOllamaModelsLoadedAsync` at `SettingsPage:26`, `IdeaToPromptPage:38`, `MutationEnginePage:27`, `IdeogramStructureEditorPage:57` |
| Code-behind **not** in scope | 3 | `MainPage:211 LoadAllTokensAsync`, `MainPage:213 LoadSavedUiState` (both stay on the VM), `SettingsPage.xaml.cs:50 and :55 SelectedTokenProvider` (2B) |
| Test member references | **65 occurrences / 27 methods / 5 files** | `GeneratorViewModelTests.Settings.cs` **50** (all 17 of its methods), `GeneratorViewModelTests.cs` **6** (3), `IdeaToPromptViewModelTests.cs` **4** (3), `MutationEngineViewModelTests.cs` **4** (3), `UiMarkupContractTests.cs` **1** (1), `IdeogramStructureEditorViewModelTests` **0**. Prep A put 17 of them in one file, so this is "edit one 296-line fixture + 4 refs elsewhere", not "reach across 3,221 lines" |
| Tests **avoided** | **36** | word-boundary occurrences over all test sources: `LoadSavedUiState` ×22 (keeps its VM signature), `LoadAllTokensAsync` ×4, `TokenProviders` ×10 (all in `GeneratorViewModelTests.cs`, 738–848) — the last two are Phase 2B |
| Markup-contract literals | **1** | `UiMarkupContractTests.cs:83` asserts `IsToggled="{Binding UseSageAttention, Mode=TwoWay}"` verbatim → part of the repath set, and it fails **first** (as Phase 1's two markup assertions did) |

### How to count test references (and three traps that made the old number unreproducible)

Count case-sensitively, **standalone tokens only**, of exactly the members that move: `ComfyUiBaseUrl`,
`UseSageAttention`, `FreeVramAfterRendering`, `AppThemeIndex`, `OllamaBaseUrl`, `OllamaModel`,
`OllamaModels`, `OllamaVisionModel`, `OllamaVisionModels`, `OpenRouterVisionModel`,
`OpenRouterVisionModels`, `OpenRouterVisionFreeOnly`, `OutputFolder`, `RefreshOllamaModelsCommand`,
`RefreshOpenRouterVisionModelsCommand`, `BrowseOutputFolderCommand`, `OpenOutputFolderCommand`,
`EnsureOllamaModelsLoadedAsync`. Without a stated member list the number cannot be re-derived — the
figure this replaced ("53") could not be.

- **`rg -c` counts matching *lines*, and matches *substrings***: over the same member list it
  reports **167 matching lines in 15 files** where the token count is **65 occurrences in 5 files**.
  All **10** extra files are innocent, in three ways. They name the *store*, which Phase 2 does not
  rename — `UiStateStoreTests` alone accounts for **33** of the 167, then `ComfyUiImageGenerationServiceTests`,
  `ComfyUiLoraServiceTests`, `ComfyUiVramServiceTests`, and the four LLM fixtures whose 2 matching lines
  each are `uiStore.Setup(s => s.LoadOllamaModel()` and its URL twin: `CaptionMutationLlmServiceTests`,
  `EnrichRegionsLlmServiceTests`, `PromptBuilderServiceTests`, `VisionObservationServiceTests`; they
  match a **type name** (`OllamaModelCatalogTests`, 7 lines of `OllamaModelCatalog`); or they match
  a **test method name** (`GalleryServiceTests`: `EnumerateAsync_WithNoExplicitRoot_FollowsLiveOutputFolderOverride`).
- **Tests reach commands as `…Command`, never `…Async`** (6 + 2 + 1 = 9 refs; `RefreshOllamaModelsAsync`
  has zero). Counting only the `…Async` spelling the source declares under `[RelayCommand]` reports 0
  and the repath looks free. `OpenOutputFolder*` has zero test refs either way.
- **Test method names embed member names** (9 of them: `RefreshOllamaModels_*` ×6,
  `BrowseOutputFolder_*` ×2, `RefreshOpenRouterVisionModels_*` ×1) and are **not** member refs — outside
  the 65, but a substring rename rewrites them anyway. Honest edit-site count: "65 + whatever you
  decide to rename".

---

## 5. Decisions — all locked

1. **Name: `Settings`** → `{Binding Settings.OllamaModel}`. Collision-free: the only `Settings*`
   identifiers in the app are `SettingsPage` and `SettingsCategory`, and `Generator.Settings.OllamaModel`
   is unambiguous even in pages reaching it via `Generator.`.
   **Locked with it:** the 16 sibling-page bindings become `Generator.Settings.<Prop>` and the three
   sibling VMs get **no** `Settings` property of their own (ground rule 1). An earlier draft of this
   plan recommended a `public AppSettingsCoordinator? Settings => _generator?.Settings;` shim on each;
   that is rejected.
2. **Repath the 4 code-behind calls** rather than leave read-through properties on the VM — same
   "no shims" rule as Phase 1.
3. **`OpenOutputFolderCommand` moves too** (region F) → 1 XAML repath in `MainPage.xaml`. Keeping it on
   the VM reading `Settings.OutputFolder` saves one repath and leaves `OutputFolder` reached into from
   two owners.
4. **Token providers do not move** (`TokenProviders`, `SelectedTokenProvider`, `LoadAllTokensAsync`,
   `ForgetSelectedToken`, 6 stores). ~90 VM lines, the `SettingsPage.xaml.cs` `OnTokenTextChanged`
   split-binding dance, and six `syncToParameters` closures writing into `Parameters` — worst
   value/risk ratio of the remaining clusters. `TokenProviderViewModel` already owns that complexity.
5. **Ctor stays at 21 parameters**; the VM forwards `_ollamaModelCatalog`, `_openRouterModelCatalog`,
   `_folderPicker` to the coordinator. Dropping them (they are already `AddSingleton` at
   `MauiProgram.cs:200/218/221`) would reach 18 but touches `MauiProgram.cs` and 2 of the 5
   `new GeneratorViewModel(…)` test sites, and the coordinator would have to re-invent their *optional*
   semantics (they arrive nullable today and tests pass null). Forward in 2A; do one dedicated
   "ctor diet" commit after the last cluster lands, when the final parameter list is known.

---

## 6. Steps

0. **Extend the binding guard test — DONE (`451b50e`), green before any code moved.**
   `MainPage_PageLevelBindings_ResolveAgainstTheViewModelGraph` became
   `PageLevelBindings_ResolveAgainstTheViewModelGraph(string asset)`, a `[Theory]` over
   `PagesWithADeclaredViewModel()` = **all 7 pages**, each rooted at the `x:DataType` its own page
   declares. Worth far more than the 20 lines it cost: the MainPage-only guard worked by coincidence —
   it caught a moved member only when the name was *absent from the page root and present on a
   sub-model*, which held for Phase 1's 10 members and stops holding the moment `Settings` exists,
   because `Generator.Settings.X` resolves for the new shape and fails for the old one with a **wrong
   root**, the two-way error a prefix table cannot see. It now walks each path segment by segment
   against the real type graph (a collection resolves to its item type), treats a property whose type
   ends in `Coordinator` as a sub-model hop needing no table to edit, fails a member that lives on a
   sub-model **with the path to bind instead**, fails a path ending on a sub-model object, and fails any
   page contributing almost no bindings — so a scope filter that swallowed a page cannot pass silently.
   **Out of scope, and skipped:** `DataTemplate` scopes declaring their own `x:DataType` (there are **8**
   in the 5 Phase 2 pages and **all 8** declare one), and bindings naming their own `Source=` / `RelativeSource=` /
   `x:DataType`. Surface under contract: **382 page-level bindings, 38 through a coordinator**
   (MainPage 114, SettingsPage 27, IdeaToPrompt 68, MutationEngine 47, IdeogramStructureEditor 86,
   Gallery 22, GalleryItemDetail 18) — of which the 16 sibling `Generator.` settings hop and the 33
   repaths are machine-checked. Mutation-tested 8 ways (typo; bare coordinator member; path ending on
   `ComfyUi`; two broken `Generator.OllamaModels` hops; a `GalleryPage` binding pointed at a `Settings`
   hop `GalleryViewModel` does not have; a typo inside a `DataTemplate`, which must stay green) —
   **8/8 as predicted**, with messages like `MainPage.xaml line 267 (Picker.SelectedItem): {Binding
   SelectedKrea2Lora} — SelectedKrea2Lora is not a property of GeneratorViewModel; bind
   ComfyUi.SelectedKrea2Lora instead`.
1. Add `AppSettingsCoordinator.cs` (flat in `Presentation/ViewModels/`, same folder and shape as
   `ComfyUiWorkflowCoordinator`), cut **D + E + F verbatim** using the Phase 1 extraction pattern
   (`git show cdb46c5` — the script itself was never tracked, the commit is the record), then add: the
   ctor (§3), `LoadSavedSettings()`, and the public `EnsureOllamaModelsLoadedAsync()` that 4 pages call.
2. `GeneratorViewModel`: add `public AppSettingsCoordinator Settings { get; }`, constructed in the ctor
   (before `LoadSavedUiState` can possibly run); replace region E **in place** with a single
   `Settings.LoadSavedSettings();` — exactly where E was, after the `CivitaiModelRef` and `Prompt`
   restores (1613 / 1619), before `LoadModel` (1702) and `RestoreSelectedModel` (1705); everything
   below stays untouched.
   Leave the 2 inbound reads (`MaybeFreeComfyUiVramAsync` → `Settings.FreeVramAfterRendering`; F is gone
   entirely). Nothing *forces* the settings block to sit after the Prompt/ModelRef restores (region E
   never reads `Parameters`), but calling it in place makes the sequence bit-identical and keeps "did I
   change an order?" out of the review. Do it in place.
3. Build app → 0 warnings. Then the **33** XAML repaths **by script, with per-name count assertions**
   (the Phase 1 script pattern), then re-run the guard test — it names the page, the line and the path
   to bind for anything left behind.
4. Repath **4** code-behind calls + **65** test occurrences over 27 methods (§4), scripted, with
   per-name assertions. Remember the `…Command` vs `…Async` trap and the 9 test-method names.
5. **Gates**: build 0 errors / 0 warnings; `dotnet test` **1,427 passed, 0 failed, 0 skipped** —
   measured, never copied. The chain to recognise while stepping: **1,417** at Phase 1 → **1,419**
   after Prep A/B (+2 `MutateWithAi_LocalTier_*`) → **1,425** after Step 0 (+6: one MainPage case became
   a 7-case theory) → **1,427** after Prep C (+2 token cases). Phase 2 adds cases only when
   `AppSettingsCoordinatorTests` appears, so a step ending on a **lower** number deleted coverage.
   Never gate a XAML step on `--no-build` (§8).
6. Manual WinUI smoke: Settings page — theme picker actually re-themes, ComfyUI URL + Ollama URL
   round-trip across restart, Ollama "Refresh models" against a live server, output-folder browse +
   "Open output folder", and the Krea-2 path from Phase 1 still generates.

---

## 7. Hazards (each verified in the current source)

- **The split point of `LoadSavedUiState` is load-bearing.** Region E runs *before*
  `ProviderFilter.RestoreSelectedModel(savedModel)` (1705), and the three model-dependent restores
  deliberately run *after* it, each with a comment saying why: resolution ("Restore the resolution after
  the model: `RestoreSelectedModel` ran `RefreshCapabilities`, which both populated `ResolutionOptions`
  … and slammed the value to the first option"), aspect ratio, and the JSON-prompt toggle ("Restore the
  structured-JSON toggle LAST … restoring it blindly would re-check the box on a model whose Build has
  no json_prompt field"). So `Settings.LoadSavedSettings()` must be the **first** thing
  `LoadSavedUiState` does, everything below stays in place, and the single `_uiStateLoaded` guard stays
  on the VM (add a separate `_settingsLoaded` latch only if the coordinator is ever called on its own).
  Caller order is fixed too: `MainPage.OnAppearing` → `LoadAllTokensAsync()` → `LoadCachedCatalogAsync()`
  → `LoadSavedUiState()` (`MainPage.xaml.cs:211-213`), inside a defensive `try` because an `async void`
  here crashes via `SynchronizationContext`.
- **The two restores that currently run before region E** (`LoadCivitaiModelRef` 1613, `LoadPrompt`
  1619) are parameters-side state and stay on the VM — so `LoadSavedUiState` ends up: model-ref/prompt
  restores → `Settings.LoadSavedSettings()` → model + resolution + AR + JSON-prompt restores. Slightly
  odd-looking, deliberately so. Hoisting `Settings.LoadSavedSettings()` above the parameter restores is
  a *separate, explicitly behavioural* commit or nothing.
- **2 picker-suppression flags move private**: `_suppressOllamaModelPersist` (275) and
  `_suppressOpenRouterVisionModelPersist` (287). `LoadSavedSettings` wraps the seeding in `try/finally`
  around them; `LoadOllamaModelsAsync` re-persists the selected models **and** the full lists
  (419–421) inside that window. Keep the `finally` ordering exactly.
- **`AppThemeIndex` has a side effect beyond persistence** (`ApplyAppTheme` →
  `Application.Current.UserAppTheme`). Must stay on the UI thread and keep running *after* the store
  write, exactly as today; `App.xaml.cs` applies the theme independently at startup. `AppThemeIndex`
  also stays public and settable from the view.
- **`OnOutputFolderChanged` calls `OutputPaths.SetRootOverride`** — a process-wide static. The
  composition root already applied it before the VM existed; the `OnChanged` is an echo. Do not
  "improve" the ordering when moving.
- **A new `AppSettingsCoordinatorTests` class needs `[Collection("OutputPathsState")]` on itself** —
  precisely because of the hazard above. That serialisation today rides on one attribute, on the partial
  declaration at `GeneratorViewModelTests.cs:17`; `GeneratorViewModelTests.Settings.cs` carries none
  because it is the *same class* (its header comment says so). A new class is a new xUnit collection and
  would run in parallel with the output-folder tests and race them over the root override.
- **`EnsureOllamaModelsLoadedAsync` is fired-and-forgotten** from 4 pages and latches
  `_ollamaLoadAttempted`; the latch moves with it.
- **`SettingsPage.xaml.cs` holds `_viewModel` typed as `GeneratorViewModel`** — fine in 2A: all
  **27** of that page's bindings live in XAML and its code-behind holds no binding path at all (0 `SetBinding`,
  0 `Path=`) — the same reason `SelectedTokenProvider` stays put.
- **Do not touch `GalleryPage.xaml:48`** — same command *name*, different owner.

---

## 8. Method and invariants inherited from Phase 1 (do not re-derive these)

- **Binding paths are not build-validated in this project.** Measured with a control, full
  `-t:Rebuild` each time: `{Binding DefinitelyNotAProperty12345}` → *build succeeded, 0 warnings, 0
  errors*; `<Label BogusPropertyName="x">` → *build succeeded*; malformed XML → `error MAUIG1001`. So
  neither paths nor member names are validated, only XAML **structure**.
  `MauiEnableXamlCBindingWithSourceCompilation=true` (`csproj:18`) is a misleading leftover — it feeds
  `XamlCTask CompileBindingsWithSource` and the `RuntimeFeature` switch, but validating names is not
  what the pipeline does. **No configuration buys build-time markup safety here; the reflection guard
  test is the only machine check that will ever exist for a binding path.** Do not spend another
  investigation on "just turn it on".
- **A stale build hides XAML edits.** `UiMarkupContractTests` reads its pages from `bin/…/TestAssets`,
  build-time copies (`Link="TestAssets\…Page.xaml" CopyToOutputDirectory="PreserveNewest"`,
  `ImageGenerator.MAUI.Tests.csproj:44-58`). `dotnet test --no-build` after a XAML edit tests the
  **previous** markup. Never verify a XAML change without building.
- **Nested binding paths notify in this app already** (Phase 1's H3). `{Binding Settings.OutputFolder}`
  and the sibling `Generator.Settings.OllamaModel` double hop rely on the binding engine subscribing to
  the *intermediate* segment — proven live by `{Binding ProviderFilter.SelectedModel}`
  (`MainPage.xaml:62`) and `{Binding InputImages.CanAddImage}` (`:180`), both raised from inside their
  coordinators. No `OnPropertyChanged("Settings")` re-raise is needed; do not flatten the design to
  avoid a nesting level that already works.
- **Repath `Path=`, never `AutomationId`** (Phase 1's H8). **175** `AutomationId`s sit on exactly the
  five pages being repathed (MainPage 48, SettingsPage 24, IdeaToPrompt 35, MutationEngine 23,
  IdeogramStructureEditor 45), often on the *same element* as the binding being rewritten — e.g.
  `UpscaleAfterRenderCheckBox`. The ids are the manual- and UI-test hooks; rewriting them silently
  breaks tooling no test will report.
- **Verbatim move beats hand transcription.** Expose what the moved body already calls (Phase 1 gave
  its coordinator a private `Parameters` plus same-named fields) so bodies compile unchanged instead of
  a find/replace across 40 sites. Pattern: `git show cdb46c5`.
- **`UiDispatcher` runs synchronously when `MainThread` throws** (unit tests, no WinRT) — which is why
  tests can drive these view-model and coordinator methods directly, and why `AppSettingsCoordinator`
  needs no dispatcher abstraction of its own.
- **Encoding is part of the diff.** Every touched file stays UTF-8 without BOM, CRLF. Phase 1 produced
  `\r\r\n` in `MainPage.xaml` from an intermediate script — caught and reverted, but it turned a 22-line
  diff into 727. Check the line endings of any file a repath script writes.

---

## 9. Already in the tree (prep commits — no re-doing)

| Commit | What it bought | Gate it landed on |
|---|---|---|
| `c3aedd4` | Settings tests lifted verbatim into `GeneratorViewModelTests.Settings.cs` (**296** lines, 17 tests) as a **`partial` split, not a new fixture** — same collection, same ctor, unchanged test IDs. Phase 2's test work is now one 296-line file, not a 3,221-line god-fixture | 1,417 / 0 / 0 |
| `3e740db` | `GeneratorSettingsReader` (47 lines, **5** reads) replacing the 9 duplicated sibling reads across 3 VMs; `_generator` stays in those VMs for its non-settings uses | 1,419 / 0 / 0 |
| `451b50e` | Step 0: the guard test covers all 7 pages (§6 step 0) | 1,425 / 0 / 0 |
| `ab71fcc` | Replacing the `Parameters` **instance** keeps the stored tokens: both token delegates write `Parameters.ApiToken` / `Parameters.PollinationsApiToken` through the property, and `OnParametersChanged` re-seeds both slots (`TokenProviderViewModel.PushToParameters()`) before revalidating. Pinned by `Parameters_WhenInstanceIsReplaced_SeedsBothTokenSlotsFromTheRows` (failed before: expected `"fresh-token"`, got `""`) and `RemixFromImage_KeepsThePastedApiToken` | 1,427 / 0 / 0 |

Two things were measured on the way and are worth repeating to whoever writes the next phase:
`RemixFromImageAsync` writes into the **live** `ImageGenerationParameters` and never swaps it, and
nothing in app or tests assigns `Parameters` — so the "Remix swaps the instance" comment described a
swap no caller performs. It was a latent bug, not a live one; that is what closing it *before* Phase 2
adds owners is for. **If a later phase moves `Parameters`, the re-seed in `OnParametersChanged` moves
with it** — that call is the only thing keeping a pasted token alive across an instance replacement.

Two clusters were measured and deliberately **not** shrunk: the 33 XAML paths (the sibling picker blocks
differ in size and layout (**25 / 8 / 7** lines — a figure *carried* from the archived plan, not re-derived
here) so a shared control is a bigger, visually riskier change than the repath it would avoid, and the
guard test already machine-checks the number), and the 4 code-behind calls
(collapsing them into one startup call moves an Ollama HTTP fetch to app start, including when Ollama is
down).

---

## 10. What Phase 3 inherits — measured

After D+E+F, `GeneratorViewModel.cs` is ≈**1,515** lines, of which **1,204** are method bodies.
The largest members left, measured as declaration-to-closing-brace spans (this is the Phase 3 short list, and
unlike the paragraph it replaced it is a list of methods that **exist**):

| Member | Line | Lines |
|---|---|---|
| `GeneratorViewModel` (ctor) | 804 | 192 |
| `RunJobAsync` | 1138 | 161 |
| `LoadSavedUiState` | 1608 | 140 (drops to ~64 when E leaves) |
| `RefreshCapabilities` | 559 | 114 |
| `OnParametersPropertyChanged` | 997 | 97 |
| `RemixFromImageAsync` | 1758 | 96 |
| `GenerateImageAsync` | 1096 | 39 |
| `PostJobToCivitaiAsync` | 1342 | 39 |
| `MutateFromImageAsync` | 1504 | 27 |
| `CompleteBatchGpuAsync` | 1300 | 23 |
| `RefreshModelsAsync` | 1564 | 21 |
| `LoadCachedCatalogAsync` | 1855 | 20 |

Read against §5.4 and §5.5, the shape of Phase 3 is: **the job pipeline (`RunJobAsync` + `GenerateImageAsync` +
`CompleteBatchGpuAsync` + `MutateFromImageAsync` + `MaybeFreeComfyUiVramAsync` 12) is the one
remaining extraction of Phase-1 size**, the capabilities cluster (`RefreshCapabilities` 114 +
`OnParametersPropertyChanged` 97) is the riskiest because it is entangled with `Parameters`, `Capabilities`
and the 21-parameter ctor (192 lines) is the last real smell.

**Why this section was rewritten on 2026-11-17.** The paragraph it replaced — inherited from the archived plan's
§11.7 and itself labelled "unmeasured — re-measure before planning" — named **25** methods of which **11 do not
exist anywhere in the app**: `WaitForRenderSlotAsync`, `AcquirePreferredSlotAsync`, `WaitForGpuSlotAsync`,
`ShouldKeepComfyUiVramForNextJobAsync`, `StartNextIfIdle`, `LoadSavedJobIntoParametersAsync`,
`ApplyRemixAsync`, `NavigateToGalleryAfterGenerationAsync`, `ApplyWorkflowDefaultsForSelectedModel`,
`SetModelOptionsProgrammatically`, `ApplyCapabilitiesToParameters`. Every size it gave for a real
method was wrong too (`RunJobAsync` "87" is 161; `RemixFromImageAsync` "63" is 96;
`RefreshCapabilities` "~45" is 114). It also counted `OpenOutputFolderAsync` as staying, which §2
and §5.3 contradict. A section can carry the disclaimer "rough shape" and still be pure invention; that is
what the §4 measurement rule is for, and it now applies to this section as much as to any count.

---

## 11. Corrections applied to this plan

Numbers above were re-measured on the tree at `b6b795a` and replaced figures carried over from the
Phase 1-era draft:

- **Sibling-VM C# reads 9 → 0** (`3e740db` did it; §4).
- **Test references 53 → 65 occurrences / 27 methods / 5 files** — and the *definition* (§4) is the
  deliverable, since "53" could not be re-derived from any stated member list.
- **Persist sites 13 → 14 over 12 methods** (§3); the zero-overlap claim survived re-measurement and is
  still the seam's main evidence.
- **Gate 1,417 → 1,427** with the full chain (§6 step 5). An intermediate run reported 8 skipped with
  no `Skip=` anywhere in the test source: re-run gave **0 skipped**, so that figure is an artifact and
  is carried nowhere.
- **`OpenOutputFolderAsync` moves with region F** (§2, §5.3); §10 no longer counts it as staying.
- **The sibling `Settings` shim the archived plan recommended (its §11.4.1 and §7) is rejected** (§1 rule 1, §5.1).
- **The Phase 3 paragraph named 25 methods, 11 of which do not exist in the app**, and every size it gave
  for a real method was wrong (`RunJobAsync` "87" is **161**). §10 is now a measured table; the
  invented inventory is documented there so nobody re-adopts it.
- **VM shape 1,906 / 19 / 14 → 1,905 lines, 32 `[ObservableProperty]` fields, 12 `[RelayCommand]` commands**
  (the old figures counted nothing reproducible), so the move is **391 lines out → ≈1,515**, and the
  region anchors are now `193–484` / `1625–1700` / `1408–1430` instead of three approximations.
- **Persist sites 14 over 12 methods, not 11** — and the cited lines were *field declarations*, not the
  lines the `Persist*` calls sit on. Same for `MaybeFreeComfyUiVramAsync` (1329, reads at 1331),
  `LoadCivitaiModelRef` (1613), `LoadPrompt` (1619) and `RestoreSelectedModel` (1705). Every line number in this
  document was re-walked after that was found; the method of checking is
  `awk 'NR>=a && NR<=b'` over the declaration, not the field block.
- **Same-rule sweep of the figures the prune inherited:** the 33 now carries its definition and
  reproduces 1 / 16 / 10 / 3 / 3 exactly (the 16 being the same `Generator.` hop step 0 checks);
  DataTemplate scopes **9 → 8**, and all 8 declare `x:DataType`; the DI registrations are
  `MauiProgram.cs:200/218/221`, not four lines; `GeneratorViewModelTests.Settings.cs` is **296**
  lines, not 297; and "tests avoided" is **36** occurrences (22 `LoadSavedUiState` + 4
  `LoadAllTokensAsync` + 10 `TokenProviders`), not 33 — the 7 it used to quote counts the
  *XAML* occurrences, a different surface (SettingsPage really does bind `TokenProviders` 7 times);
  the `rg -c` trap figure is **167 lines / 15 files**, not 85, with all 10 extra files named and
  explained instead of exemplified; `SettingsPage.xaml.cs` reads `SelectedTokenProvider` at 50 **and** 55
  and holds **0** binding paths, so its "23 bindings" became the measured 27;
  the moved line total is **391** (292 + 76 + 23), not 390.
- Two earlier attempts to rewrite this document were *worse* than what they replaced — one invented a
  dozen settings members and a `ModelMetadataService` that exist nowhere in app or tests (the full list
  is in the archive). Kept as the reason §4 states its counting method: **a confident table is not a
  measurement.** §11.1–§11.7 of the archived plan survived only because they had been measured.
