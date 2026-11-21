# Model choice by role — `Local | Anthropic | OpenRouter`, with the picker where the model is used

**Status:** Phase A and Phase B1 **done**; Phase B2 **in progress** — `ModelPickerRow` is live on two
of four pages (mutation, structure editor) and `IdeaToPromptPage` is next, blocked on OpenRouter text
model plumbing (§6, B2.2b). Gate: **1,623 passed / 0 failed / 0 skipped**, app builds at 0 warnings and
0 `MAUIG2045`.
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
8. **Anthropic's model list is a request away, not a constant.** `GET /v1/models` returns each model
   with `display_name`, `created_at`, `line`, `lifecycle` (`active` / `deprecated` / `retired`) and
   a `capabilities` object — including `thinking.types.adaptive.supported`, `effort.supported`,
   `structured_outputs.supported` and `image_input.supported`. So the Anthropic picker can list live
   models, default to the newest active one, and *read* compatibility instead of assuming it. There
   is no Anthropic catalog in this repo today — and there was never an Anthropic vision route either:
   `git log --all -S AnthropicVisionModel` finds the name nowhere in history, so plan text that
   described one was describing something never committed. Corrected here rather than planned
   against.
9. **Anthropic models take images**, so `observe` is the one role where a third provider is not
   speculative. The filter is `capabilities.image_input.supported` — the same shape as the existing
   OpenRouter vision filter — and the Messages-with-image branch does not yet exist
   (`SendOpenRouterVisionAsync` is `private static` inside the vision service). Cost, not capability,
   is why it stays out of the defaults.
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

### Found while implementing Phase B2 (the control)

19. **Converter scope was the real prerequisite, not the control.** `NonEmptyStringToBool` was
    declared seven times and `InverseBool` five, once per page. A control has no page resources to
    resolve from, so it needs application scope — and a page that re-declares a key *shadows* the
    application instance for that page alone, which is exactly how a shared control ends up binding
    on one page and silently not on the next. Both now live once in `App.xaml`, and
    `SharedConverters_AreDeclaredOnceInApplicationResources` fails if a page copy comes back.
20. **`App.xaml` already carried a dead converter.** `StringToEnumConverter` was declared at
    application scope and then re-declared by every page that uses it, so application scope had
    exactly one live consumer in the whole app: `IntGreaterThanZero`, which no page declares. Left
    alone — deleting it is its own sweep — but it is why application scope was the established home
    here rather than a new convention invented for the control.
21. **Content was lost twice and I do not know by what.** An edit to `App.xaml` came back with the
    additions missing, the comment duplicated and an mtime *older* than the write; a whole set of edits to
    this document was likewise gone afterwards, and that absence is what got committed. I blamed an editor
    buffer holding the file open — the obvious story for an mtime that predates the write — and the owner
    says the file was never open. So the mechanism behind these two losses is unknown, and with it goes the
    "two-writer surface" conclusion and the instruction to close files that came from it.
    What *is* established, from earlier in this repo's own history
    (`documents/review-c525269-97fc36e.md` §6), is that an automated IDE inspection has rewritten source in
    this tree unasked at least twice — "use primary constructor", and "remove unnecessary assignment"
    eating test `act` statements. So an external writer is a fact here; but its recorded signature is a
    quick-fix applied to C#, not a markdown file restored to an older version, so it does not explain the
    two events above and must not be filed as its explanation.
    What survives the not-knowing is the check, and it is cheap: after a scripted write, re-read the file
    and grep for a string that exists only if the edit landed. Both losses cost minutes because of that
    grep. Found later they would have been believed, because committed text is exactly what a lost update
    looks like when nobody looks.

### Found while running the first adopted row (2026-11-21)

22. **The row shipped inert and every test stayed green.** A `ContentView`'s content inherits the
    binding context of whatever *hosts* it, and every host here is a page whose view model has
    neither `Models` nor `RefreshCommand`. The picker listed nothing and Refresh was a button with a
    null command, while the app built at 0 warnings, `MAUIG2045` found every path valid (they were:
    the check is against `x:DataType`, a name check and not a source check), and all 1,607 tests
    passed. Confirmed in the generated `InitializeComponent`: 0 occurrences of `BindingContext` in
    the entire file, and every internal binding compiled to `source => source.RefreshCommand` — the
    inherited context. Fixed by naming the context on the row's **content** (regenerated as
    `RelativeBindingSource(FindAncestor, typeof(ModelPickerRow), 1)`) and *not* on the row element,
    because what a page binds on the row — `IsVisible` first — must keep resolving against the page.
23. **The evidence that it worked was consistent with it being broken.** "A bad server URL shows a
    status line" was read as the row reporting. It was the page's own `MutationStatusLabel` at the
    foot of the page, saying the same thing off the same exception. A check that would also pass with
    the feature deleted checks nothing; the row's status line is only distinguishable by its
    `AutomationId`, which is why the manual matrix in §8 names handles and not sentences.
24. **No MAUI control can be constructed in this test project, so there is no runtime binding test.**
    `new Label()` dies in `Microsoft.Maui.Controls.Element`'s static initializer with
    `RPC_E_WRONGTHREAD` on an xunit worker thread, and an STA thread does not help: WinUI's
    `DispatcherQueueController.CreateOnCurrentThread` fails with *ClassFactory cannot supply requested
    class*, because WindowsAppSDK is never bootstrapped in a bare test host. So `ModelPickerRowTests`
    asserts the boundary statically — content names its own context, every internal binding path is
    a property the row itself declares, every host-facing member is a real `BindableProperty`,
    `SelectedModel` defaults to two-way — and the pixels stay on the manual list.
25. **Contract tests read *built* markup, so a restore is not a rebuild.** Twice now a red
    `UiMarkupContractTests` run meant the opposite of what it said: `TestAssets` holds copies made by
    `CopyToOutputDirectory="PreserveNewest"`, so after putting a file back, `dotnet test --no-build`
    re-runs against the copy of the *broken* version. A XAML cycle ends with a build of the test
    project, not a test run.
26. **A working copy holding CRLF while its neighbours hold LF turns an ordinary edit into a
    whole-file diff — and the plan's "preserve CRLF" rule was backwards for this tree.** For a while
    CRLF looked harmless: `922d4e3` and `bd743c8` diffed honestly with CRLF working copies. Then
    swapping three rows on `IdeaToPromptPage.xaml` — 33 lines in, 92 out — reported 472 deletions and
    413 insertions. `git ls-files --eol` settled it: the index is LF everywhere and the working tree is
    **280 files LF / 116 CRLF / 3 mixed**, so this tree is LF-majority, and on a file git declines to
    eol-normalize every line becomes a change. Diagnosis is cheap and belongs *before* any suspicion of
    an external writer: `git diff --ignore-cr-at-eol --numstat` beside `git diff --numstat` separates
    "I edited 33 lines" from "the endings moved", and `git ls-files --eol <path>` says whether git will
    normalize at all.
    **Why the earlier reasoning failed:** it read `git show HEAD:<path>` as the file's stored ending.
    autocrlf stores every text blob as LF, so that command reports LF for everything in the repository
    and cannot tell a CRLF working copy from an LF one. **Rule: write the endings the file's own
    neighbours already have.** The files this session authored were converted to LF once the rule was
    known; the 116 pre-existing CRLF files stay as they are, because converting them would be a
    whole-file round-trip in commits that have nothing to do with line endings.
27. **Replacing a span by index arithmetic ate its neighbour, and only the XAML compiler noticed.** The
    writer row's replacement resolved its start anchor with `index` over the whole file instead of from
    the current position, so the span swallowed the Ideogram V4 JSON checkbox's two labels and the
    `</Grid>` closing their grid. The file still read plausibly and so did the diff. Two checks now run
    before a span-editing script writes: the set of `AutomationId` values must be identical before and
    after (nothing gained, nothing lost), and the result must parse as XML. The first names the casualty
    — `BuildJsonCheckBox` survived while its labels vanished — the second fails on the tag mismatch.
    **A move must state what it forbids changing, and check exactly that.**
28. **Deriving a row's three automation ids from one prefix broke on the first page predating the
    convention.** Idea to Prompt's writer row answers to `IdeaPromptTierOllamaStatusLabel` — named in
    the era when the writer chose a *tier* — while its picker is `IdeaPromptOllamaModelPicker`. The
    widened contract table derived the status id from the picker prefix and failed on it at the first
    honest run. The handle belongs to UI tests outside this change set, so the table carries the wart as
    an explicit column instead of renaming it. **The derivation was the assumption; the failure was
    correct, and what changed was the test's reach, not the app's name.**
29. **The visibility gate is now part of the row contract, because an ungated row is how the old
    confusion comes back.** The first version of the Idea-to-Prompt writer row dropped
    `IsVisible="{Binding IsLocalTier}"` along with the wrapper that had carried it: under a Claude tier
    the user would have seen a Local model picker for a provider they had not selected, which is the
    same class of defect as four pickers writing one value. Every row in the shared table declares the
    flag that shows it (`IsLocalTier`, `ShowEnrichLocalModel`, `IsLocalVisionProvider`,
    `IsOpenRouterVisionProvider`) and the test asserts it, so an always-visible row fails.
30. **`GET /v1/models` pages at twenty by default, so the obvious implementation is silently wrong.**
    "limit … Defaults to 20" means one request returns the twenty newest models with a 200 and no sign
    that anything is missing: a picker built on it looks healthy and hides most of the catalogue.
    `has_more` and `last_id` are both required to finish the job, and both are load-bearing — a server
    that ignores `after_id` will answer `has_more` forever. Lifecycle is the second half of the trap:
    **omitting `lifecycle` returns active and deprecated models but never retired ones**, which is
    already the picker's rule — a retired model fails inference, so it must not be offered, while a
    deprecated one is still callable by the keys that have it and is shown with the word on its label
    rather than hidden. **A list endpoint's defaults are a claim about the whole list only if you
    ask.**
31. **A complete, compiling, never-called surface is not a feature.** `LoadLlmProvider` /
    `PersistLlmProvider` / `LoadLlmModel(role, provider)` / `LoadLlmTarget(role)` — the per-role
    persistence pair the rest of this plan is built on — had **zero callers in app and test alike**
    from the day the Phase-B groundwork landed them, and the OpenRouter vision list, live since Phase
    A, had no tests at all. Both were written inside a slice judged by "the suite passes", which has no
    way to answer "is this type reachable".
32. **A restored backup brings its own timestamp back, and MSBuild reads clocks.** The falsification
    harness mutated a catalog, ran the tests, then moved the backup into place; `shutil.move` carries
    the backup's older mtime, so the next build judged the *mutated* assembly up to date and re-ran the
    suite against it. "Restored, gate green" was the mutation still in the binary — visible only
    because one test failed for the wrong reason first. **A restore must leave the file newer than the
    build it is meant to invalidate.**
33. **A `Picker` bound to an enum shows that enum's `ToString()`, and that is the whole "Local vs
    LocalOllama" report.** `ModelTierOptions` is an `IReadOnlyList<ModelTier>`; the vision picker's source
    is a `VisionObservationProvider[]`. Neither page chose those words, the runtime did. And the app
    already carried a *second* provider-spelling function doing the opposite job: `LlmTarget.ProviderLabel`
    answers "Ollama" / "Claude", which is right inside a sentence and wrong in a list of choices. Two
    spellings were always needed; what was missing was one owner for both. **A bound enum is a label rule
    you don't control.**
34. **A per-item marker cannot live inside the item's value, because the value gets persisted.** The
    Anthropic list wants "Claude Sonnet 4.6 (claude-sonnet-4-6) — deprecated" on the row while the role
    stores the bare id; a string list carrying that suffix would store the suffix as a model id. So the
    list holds ids, the refresh line carries the deprecated count, and the per-item label waits for the
    row to grow an item template — which is why `AnthropicModelInfo.Display` ships unused.
35. **A test helper that defaults a null dependency can only ever test the wired path.**
    `Build(openRouterCatalog: null)` against a signature reading `openRouterCatalog ?? _openRouter.Object`
    handed the test its fake, and the "no catalog" case then failed on an unconfigured-mock null
    reference: a red run that proved nothing about the case it named. The fix is two named builders, not
    an optional argument — **a parameter cannot distinguish "not supplied" from "deliberately absent",
    and only the second one is a test case.**
36. **`Equal(a, b, "because…")` — the third string is an item, not a reason. Third time in this change
    set.** FluentAssertions' `params string[]` overload on a collection folds the reason into the expected
    sequence. First the catalog tests, then the coordinator tests, then `ModelChoiceStateTests` — and only
    the third attempt failed to *compile*, because with non-string elements the compiler can see a string
    where a provider enum belongs. **Anything collection-equal that carries a reason passes the items as
    one array.** A trap you have documented twice is a convention, not a mistake.
37. **An `async` signature is a promise, and an adapter broke it.** The first `ModelSource` took the
    coordinator's `ICommand` and `RefreshAsync()` was `command.Execute(null); return Task.CompletedTask;`
    — correct-looking, compiles, and it makes every caller that awaits a refresh and then reads the list
    read the *previous* answer. The adapter takes `Func<Task>` now, and
    `RefreshCompletesWhenTheProviderHasAnswered` holds the line with a `TaskCompletionSource`.
38. **A member initializer cannot hand over `this`, and that decided where the sources get built.** Each
    model source watches this coordinator for its status property, so it needs `this`; an initializer has
    no `this` to give. So the two dictionaries are built on first read — and the field cache is not a
    micro-optimisation: building twice means two source objects subscribed to the same lists, and a row
    that re-read the dictionary would find itself listening to two copies. `ReadingTheSourcesTwiceGivesThe
    SameObjects` is the test, written *after* noticing that the comment alone was not the constraint.
39. **Twice in one slice I wrote a placeholder into a product file because I was unsure of a language
    rule** — a `new INotifyPropertyChangedProxy()` that has never existed, then two
    `=> throw new NotSupportedException()` members invented to make an initializer type-check. Both were
    one `dotnet build` from being caught, and both came from the same move: writing code that assumes a
    rule instead of checking the rule first. When a construct fights the language, the construct is the
    problem.
40. **A surviving mutation is a specification of what the tests cannot see.** Deleting `Detach(previous)`
    from the provider switch failed nothing. It failed because the handler reads `_source.Status` — the
    *current* source — so a stale subscription writes a correct value a second time: the guard looked
    unnecessary precisely because an unrelated indirection was covering for it. What actually leaks is
    delegates, on a source owned by the coordinator and therefore older than any page. The test that sees
    it counts listeners on the fake (`LeavingAProviderStopsListeningToIt`) — the only instrument that can.
    **Where a bug is made harmless by something other than the line you added, no existing test can tell
    you the line is load-bearing; delete it and watch what stays green.**
41. **A picker writes a position, and a position cannot lie.** MAUI's `Picker` has no `ItemTemplate` in
    this version, so the row's provider picker binds `ItemsSource` to labels spelled in C# and writes back
    through `SelectedIndex`. Position was not only what compiled: two providers ever spelled alike can
    select each other when the selection is matched by text, and cannot when it is matched by slot.
    The same change made `ModelProviderLabelConverter` — a-2's XAML half — unnecessary, because a list
    built in C# needs no converter to spell its own entries. It went back out with its two tests.
    **A rule with one owner is worth more than a rule with one owner and a second, unused mechanism.**
42. **A test that moves the picker mid-run has to move it while a call is still open.**
    `ARunKeepsTheModelItStartedWith` first started on a hosted provider with three variants. Fan-out
    issues every call in the same tick, so the flip ten milliseconds later arrived after all three had
    their target, and the mutation that re-read the picker per variant passed clean. Started on the serial
    provider instead, where the later calls genuinely begin after the flip, it catches the same mutation.
    "Keeps the model it started with" is a claim about ordering, and it needs a run that is still running.
43. **The old shared key had a second reader nobody mentioned.** After the run was made to resolve one
    `LlmTarget`, unloading still freed `_settings.MutationModel`: two lines of code holding two ideas of
    what this page had just called, one of them the key three pages used to fight over. Now the freed
    model is the called model, and the two tests that pinned the endpoint pin the pick.
    **Deleting a shared value means finding every reader of it, including the reader that only fires when
    the run was local.**
44. **A test builder can make an impossible state look ordinary.** `CreateSutWithAi(generator: null)` ran
    the AI path with no settings to read — a page the app cannot construct. It was green for as long as the
    VM fell back to a hard-coded model, and turned into a wall of red the moment the page had to have a
    choice. The builders now hand over what the app always injects; the nothing-to-read state is tested on
    purpose (`WithoutAChoiceToRead_ItRefusesInsteadOfCallingAGuessedModel`) rather than by accident.
45. **An automation id is a contract, not a comment.** Deleting the tier picker deleted
    `MutationModelTierPicker`, and no id replaces it faithfully, so it goes in the release note. The model
    picker keeps the name `MutationOllamaModelPicker` although it now lists Anthropic and OpenRouter models
    too, because tests outside this repository click it. Renaming it is free in this repo and expensive in
    whichever repo has the UI tests; that trade is the owner's, not the refactor's.
46. **The observer was the last service still reading application settings to find out what the user had
    picked.** `VisionObservationService` resolved the provider, the model and the Ollama address from
    `IUiStateStore` inside `ObserveAsync`, which meant the row on screen could not be the truth: the page
    could show one model and call another, and both would still be correctly remembered. The request now
    carries a caller-resolved `LlmTarget` and the service resolves *credentials* only — the contract every
    other LLM service in this change set was already moved to. A service that goes looking for the model is
    a service its caller cannot be specific to, and a caller that cannot be specific cannot be tested.
47. **A path with no tests can hold a feature that has never worked.** `IVisionObservationService` was
    referenced by exactly zero test files before this slice, and no view-model test touched the observation
    path. Under that cover it carried two live defects: the observer's provider lived in a VM property that
    was never persisted, so every navigation answered in Ollama however often OpenRouter had been used;
    and the model called came from the settings store rather than from the picker. Coverage is not only a
    regression tax — on an untested path it is also the only thing that can notice a feature is dead.
48. **A role that is not in the list should say so, not take the default.** Every key switch in
    `UiStateStore` sent an unlisted role to the enrichment keys, because that is what a `switch` does when
    its last arm is `=> EnrichModelKey`. `ModelRole.Observe` now has its own arm and everything unlisted
    throws: a new role quietly writing a different page's memory is the exact defect this plan exists to
    remove, and it arrives as "my settings keep changing themselves". Related, same slice: the free-only
    switch is *not* part of the shared row and keeps its `IsOpenRouterVisionProvider` gate, now with a
    contract test — a pricing filter that is on screen over the list it cannot filter gets clicked, which
    is worse than the filter being missing.
49. **Reusing the old keys is what made this migration free.** The observer reads and writes
    `imggen.ollama_vision_model` and `imggen.openrouter_vision_model`, the two keys the pickers it replaces
    already used; only the provider is new storage (`imggen.observe_provider`). A fresh pair of model keys
    would have needed seeding or a migration and, until whichever of them ran, every existing install would
    have opened with no vision model chosen — a silent reset of a choice somebody had already made, shipped
    as a refactor. Both round-trips are pinned in `UiStateStoreTests`.
50. **Two names for one decision is how it drifted.** `ModelProvider.Local` and
    `VisionObservationProvider.LocalOllama` were the same answer spelled twice, which is why the Idea page
    could offer "Local (Ollama)" in one row and "Local" in another while meaning one thing. The enums are
    folded; the provider subset a role gets is decided by which entries its source dictionary has, so the
    observer offering exactly Local and OpenRouter is now a test rather than a dictionary literal someone
    might add a line to.
51. **Switching a role to a provider that has nothing to list should ask that provider; opening a page must
    not.** An untouched hosted list and a provider that answered "no models" look identical from where the
    user is sitting, and both look like a dead button. `ModelChoiceState` now refreshes on a provider switch
    when the list is empty, and reports an automatic failure on the row (`Couldn't ask for models: …`) rather
    than dropping it on a fire-and-forget task. It still asks nothing on construction: opening a page is not
    a question to a provider, and four pages built at launch is four unasked-for network calls. This landed
    beside a-4 while chasing "the Anthropic list never fills"; the request now goes out and returns 200, and
    the one guard that could still swallow the press without a trace logs which dependency was missing — a
    message that says "unavailable in this build" has no business being quiet about which part of the build
    it means.
52. **A mock that answers null is not an empty store.** `ModelChoiceState` opens on what the store hands
    back, and `UiStateStore.LoadLlmProvider(role, fallback)` hands back the caller's fallback the first
    time a role is read. A bare `Mock<IUiStateStore>` answers *null*, and then the row falls through to
    `_providers[0]` — Local, for its own enum-order reasons. So every "opens on X" test written against
    the bare mock was grading the row's last-ditch fallback, and passed whatever the page had asked for:
    moving the writer's first-run provider to Anthropic failed nothing until the fake was made to honour
    the contract (`EmptyStore()` in the idea tests; `ModelChoiceStateTests`' fake already did). A fake
    that answers more quietly than the real thing turns the tests above it into decoration.
53. **An edit batch is atomic, and the sibling that failed takes the ones that would have worked with
    it.** Two test bodies I believed rewritten were still the old code, and one whole view-model test
    disappeared from the slice because a replacement text dropped the block above it. Neither shows up
    as a build error — deleted tests do not fail. `git diff | grep "^-.*public void"` before believing a
    green suite, and check a new test name reached the assembly before using it as a falsification
    probe: `No test matches the given testcase filter` is a harness error, not a pass.
54. **A tier named two things in one word, so migrating only the provider is half a migration.** The
    writer's old `SelectedModelTier` said who *and* what: `Sonnet` meant Anthropic + `claude-sonnet-4-6`.
    Inheriting the provider alone puts that install on Anthropic's *role default*, which for
    `PromptWrite` is Opus — an update quietly moving someone onto the pricier model on a launch where
    nobody chose anything. `UiStateStore` now adopts the model the tier named, once, and only where no
    model has been written under that role yet.
55. **When a default stops being dangerous, the refusal it justified has to change shape.** "Pick a
    prompt writer first" existed because a tier named a paid Claude model the instant it was read;
    opening the row on Local removes the thing the placeholder was guarding. Build now refuses with
    `LlmTarget.MissingModelError` — the sentence every other page uses — and lights up the moment the row
    has a model. A refusal that has outlived its reason is a tax on every visit.
56. **A helper line that moves onto a shared control needs a handle, or it dies.** The page's "which
    model am I about to pay" label was `IdeaPromptModelSummaryLabel`; the row's helper label had no
    automation id at all. `ModelPickerRow` gained `HelperAutomationId` for exactly that, and the row's
    own tests now require every id a host can set to be sitting on an element — a settable id that
    nothing binds passes the markup test and hands a UI test outside this repository nothing to click.

**Status.** B1 done, gate **1,605**. B2.0 (`8bffa9e`), B2.1 (`c2deff9` + `7ebe6e3`), B2.2a (`bd743c8`,
gate **1,624**), B2.2b (`c8d736c`, gate **1,627**). B2.3 is split a-1 … a-6 below: a-1 (`d31b160`, gate
**1,645**), a-2 (`1c80b24`, gate **1,666**), a-3 (`acba23f`, gate **1,693**), a-4 (`4cfe657`, gate
**1,709**) and **a-5** (both of the idea page's decisions in shared rows, observer and writer alike,
gate **1,744**). B2 is not
done until the tier is deleted and the transitional `GeneratorSettingsReader.*Target(tier)` bridge with it.
Only Settings still hand-rolls pickers, and those go in Phase C.


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

### 5.3 `ModelPickerRow` — one control, four pages · **first host landed 2026-11-21**

As built, `Presentation/Views/Controls/ModelPickerRow.xaml`: `TitleText`, `PickerTitle`, `Models`,
`SelectedModel`, `RefreshCommand`, `Helper`, `Status`, `IsBusy`, and three `*AutomationId` string properties.
`SelectedModel` is two-way *by default* — a row that only read back would let the user pick a model
that is then never used. It renders what the pages hand-rolled: field label, `Picker` + Refresh in a
`*,Auto` grid, helper line, and a status line that collapses when empty. The automation ids are
properties rather than fixed values because they are the owning page's UI-test handles, and two
roles on one page cannot share one. `IsBusy` greys the picker and Refresh together — the structure
editor passes it (mid-enrichment model changes are silently ignored) and the mutation page does not
pass it, exactly as those two pages behaved by hand before the row existed; it is asserted per host,
not in the shared theory, because a default-on rule would have changed mutation behaviour. A fourth
host decides for itself.

Deliberately **not** in it yet: `Providers` /
`SelectedProvider` — each page still shows its own `ModelTier` picker above the row, and provider
selection cannot move into the control in the same commit that deletes `ModelTier` (§6, B2.3).

Two measured facts decided the ordering, and both contradict the obvious way to write this:

- A `ContentView` that no page instantiates compiles fine and evaluates **zero** bindings. A control
  cannot be landed "first, alone" and verified afterwards, so the control and its first host are one
  commit.
- Binding paths *inside* the control are checked against its `x:DataType`: `{Binding TitleTxt}`
  produced `ModelPickerRow.xaml(14,17): warning MAUIG2045: Binding: Property "TitleTxt" not found
  on "…ModelPickerRow"`. Against this project's 0-warning rule that is a gate, not a hint — but it
  is a gate on **names**. It says nothing about whether the object those names are looked up on at
  run time is the control, and that gap is what shipped broken (§4.22).

### 5.4 List and credential sources per provider

| provider | model list | refresh | credential |
|---|---|---|---|
| Local (Ollama) | `Settings.OllamaModels` (all) / `OllamaVisionModels` for `observe` — both already exist | `RefreshOllamaModelsCommand` | none |
| Anthropic | **built (B2.3a-1)**: `IAnthropicModelCatalog` over `GET /v1/models` — paged past the 20-row default, retired dropped, deprecated labelled, capabilities read rather than assumed | catalog call, key required (a missing key answers with where to put it, not a 401) | Anthropic row, Provider API keys |
| OpenRouter | **built (B2.3a-1)**: `ListVisionModelsAsync` (image in, text out) and `ListChatModelsAsync` (text out) — one fetch, two honest filters, not one filter with a `visionOnly` flag | same command, per provider | OpenRouter row / token store |

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

`LlmTarget { Provider, ModelId, BaseUrl }` + `ModelRole { PromptWrite, Mutate, Enrich }` are
now the contract every LLM service takes (the image observer is deliberately **not** a member — it
sends pixels and lives on `VisionObservationService`; folding it in is B2.3a-3); `ICaptionMutationLlmService`, `IEnrichRegionsLlmService` and
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

### Phase B2.0 — shared converter scope · **DONE (2026-11-21)**
The two converters the control needs moved to `App.xaml`; twelve page-local declarations went with
them, three pages also lost an `xmlns:converters` with nothing left to qualify, two lost a resource
block left empty. Gate **1,606**. Falsification: re-declared `InverseBool` on `MainPage` → the guard
failed, naming the page and the key.

### Phase B2.1 — the control and its first host · **DONE (2026-11-21)**
`ModelPickerRow` plus its adoption on `MutationEnginePage.xaml`, one commit (§5.3): 25 hand-rolled
lines became one element keeping all three of the page's automation ids. `ModelPickerRow.xaml`
joined `TestAssets` and the well-formedness and converter-resolution loops as `ControlAssets`.
Falsified twice before shipping — dropped `Mode=TwoWay`, typo'd binding path — and it *still* shipped
not working, because neither check looked at where the binding source comes from (§4.22). What
catches it now is `ModelPickerRowTests` (§4.24) plus the manual matrix in §8. Gate after the fix:
**1,620**.

### Phase B2.2a — structure editor · **DONE (2026-11-21)**
26 hand-rolled lines → one `<ctrl:ModelPickerRow>`, three ids preserved. The shared markup test is now
a theory over (page, role model, id prefix) so the next host is one `[InlineData]` line, and it is
that shape which let the falsification matter: pointing the editor row at `MutationModel` — the original
shared-state defect, one paste away — failed the editor case while mutation stayed green. `IsBusy`

is asserted by its own page test for the same reason (§5.3).

**Also in this slice,** because adopting the row put the cursor on the section around it. The enrichment
section started collapsed behind a grey word with no chevron, while the tier defaults to a paid Anthropic
model and the Enrich button sits further down the same pane — so the page could spend money on a model
nobody had seen. It now opens by default and carries the same `BoolToChevron` disclosure as the page's
three other accordion sections; `EnrichmentSettings_DoNotHideTheModelThatCostsMoney` pins both halves
(falsified twice: re-collapsing, and putting the word "Configure" back). The tier default itself is still
`ModelTier.Sonnet` and still not persisted — an owner decision, §9.

Gate **1,624** after this slice.

### Phase B2.2b — Idea to Prompt · **DONE (2026-11-21)**
Three rows, 85 hand-rolled lines → 33: the local observer (`IdeaPromptOllamaVision*`), the OpenRouter
observer (`IdeaPromptOpenRouterVision*`, the first non-Ollama host, so `Models` / `RefreshCommand` /
`Status` are now columns of the contract table rather than Ollama constants), and the local writer
(`IdeaPromptOllama*`). The OpenRouter *free-only* row stays on the page: it is a second control beside
the row, not part of it.

The four plumbing items this page was thought to need turned out not to be prerequisites: all three rows
already bound `Settings.*`, which is what the row needs. Persisting `SelectedVisionProvider` and adding
OpenRouter as a *writer* are features, not moves, and moving them here would have made a page swap
impossible to review. Both are parked in §9.

The contract test became a table over (page, row id, role model, **gate**, model source, refresh
command, status source, status id) and locates each row by its own picker id, because this page hosts
three. Falsified one row at a time: local observer writing `OllamaModel` (the original defect), OpenRouter
observer gated on `IsLocalVisionProvider` (the copy-paste next door), writer bound to `MutationModel` —
each failed only its own row.

### Phase B2.3 — provider in the row, and the tier deleted · **split; a-1 DONE (2026-11-21)**

Owner decisions that set the shape (2026-11-21): provider selection moves **into** the row; one
display-label rule for every provider picker; **no cost estimates** while any model id is selectable
— `SonnetPerCallUsd` / `OpusPerCallUsd` come out rather than being re-worded; the Anthropic list comes
from a small catalog over `GET /v1/models`, **not** from the SDK, so B1.5 stops being a prerequisite;
and enrichment **starts on Local** and then persists whatever is picked.

A row cannot own the provider without also owning the list, refresh command, status and helper of
*whichever provider is selected*. Handing each host three sets of those bindings would relocate the
duplication instead of deleting it, so a host binds one object per role:

| member | meaning |
|---|---|
| `Providers` | the provider subset that role can use (the observer has no Anthropic today) |
| `SelectedProvider` | persisted per role — the wiring these pages never had |
| `Models` / `RefreshCommand` / `Status` / `Helper` | whichever provider is currently selected |
| `SelectedModel` | persisted per role **and** per provider, so returning to Ollama finds the old tag |

Automation ids stay host-side. They are the UI tests' handles, and B2.2b found the hard way that a page
predating the naming convention does not match a derived name (finding 28).

- **a-1 · the two missing lists · DONE (`d31b160`).** `AnthropicModelCatalog` and `ListChatModelsAsync`;
  18 tests, six falsified mutations (findings 30–32), gate **1,645**. No page touched, `ModelTier` untouched.
- **a-2 · one label rule, then the coordinator surfaces · DONE.** `ModelProviderLabels` (`ForList` /
  `ForProse`)
  becomes the only place either spelling exists, `LlmTarget.ProviderLabel` delegates into it instead of
  keeping its own switch, and `ModelProviderLabelConverter` is registered in the root `App.xaml` beside
  the B2.0 converters. `AppSettingsCoordinator` gains `OpenRouterTextModels` and `AnthropicTextModels`
  with their own refresh commands and status lines. Three rules worth writing down, because each is a
  place a later reader will be tempted to "improve": the OpenRouter text list is **not**
  free-only-filtered (the switch lives on the observer's card, and filtering by a control that is not on
  the page hides models silently); the Anthropic list is ids only (finding 34); and a missing key says
  `LlmCredentials.MissingKeyMessage` — the sentence the services already say — instead of the second,
  near-identical one I had already written today. First **direct** tests of `AppSettingsCoordinator`,
  471 lines every existing test reached only through `GeneratorViewModel`. 21 tests, gate **1,666**.
- **a-3 · `ModelChoiceState` · DONE.** The non-MAUI per-role state object and its unit tests. No XAML.
  It is what `ModelPickerRow` will bind to: `Providers` (the subset the role was given sources for, in
  enum order), `SelectedProvider`, `SelectedModel`, `Models`, `Status`, `Helper`, `RefreshCommand`. The
  rules that used to be markup strings are now ordinary tests — building a row persists nothing; a switch
  of provider re-reads that provider's model memory instead of carrying an Ollama tag to a Claude
  endpoint, and does not write what it merely loaded; a provider that listed nothing does not cost the
  user their pick; a dropped pick stays visible rather than being silently replaced; status follows the
  selected provider because the old provider is *detached*; and the two provider dictionaries on the
  coordinator are checked entry by entry, because five near-identical entries are an invitation to wire
  OpenRouter's row to OpenRouter's *vision* list.
  27 tests (20 state rules, 7 source wiring), gate **1,693**. Nine mutations were tried; eight were caught and one survived and is finding
  40. The gate also caught `CsWinRT1032` on a collection expression assigned to `IReadOnlyList<T>` — the
  0-warning rule is what surfaced it, and the array form is the fix.
- **a-4 · the row and its first host. DONE (`4cfe657`, 2026-11-22).** `ModelPickerRow` grew a `Choice` property taking
  the whole `ModelChoiceState`: provider picker, model picker, refresh, helper and status all come from it,
  and the older per-property shape still works, so the two unmigrated pages are unaffected. One set of
  elements serves both shapes through read-only members — no mirrored lists to drift.
  `MutationEngineViewModel` now owns a `ModelChoiceState` for `ModelRole.Mutate` and resolves one
  `LlmTarget` per run, so the picker may move mid-run without re-deciding what is in flight (finding 42).
  Gone from the page: `ModelTierOptions`, `SelectedModelTier`, `IsLocalTier`, the `Sonnet/Opus` per-call
  rates and the `≈ $0.07` estimate, replaced by a hint that says who is billed how many times (finding 45
  covers the one id that could not survive). The row is fed by one binding, so the contract test gained a
  second theory for choice-fed pages whose job is to fail when a page binds the old shape *as well*.
  Gate **1,705**. Eight falsifications, all caught — one only on its second attempt, which is finding 42.
  It landed together with finding 51's behaviour change, the one part of that commit which is not a
  refactor; worth knowing when reading the diff.
- **a-5 · Idea to Prompt. DONE (2026-11-22) — both rows on the page.**
  The observer is a role now — `ModelRole.Observe`, offered Local and OpenRouter only — and one
  `ModelPickerRow` fed by `Choice="{Binding Vision}"` replaces two provider-gated rows plus a separate
  provider picker. `VisionObservationService` receives the target and resolves credentials only (finding
  46); a provider that cannot take an image is refused in words rather than at the wire; a build resolves
  its vision target once, so moving the row mid-run cannot change who is billed or which model gets freed
  afterwards; and the provider choice is persisted at last, which is the fix for the observer answering in
  Ollama on every navigation (finding 47). Automation ids went 38 → 35, losing exactly
  `IdeaPromptOpenRouterVisionModelPicker`, `…RefreshButton` and `…StatusLabel`, whose control no longer
  exists; the surviving row keeps the `IdeaPromptOllamaVision*` handles for every provider, the same trade
  as finding 45. Release note material, with `MutationModelTierPicker`.
  18 tests (8 view model, 5 store keys, 5 service) and 12 falsifications, all caught, gate **1,727**.
  The writer is this page's other row, and it is what made the naming fix worth doing: the observer
  answered `Local (Ollama)` / `OpenRouter` while the writer two cards below still answered `Local` /
  `Sonnet` / `Opus`. `IdeaToPromptViewModel` owns a `ModelChoiceState` for `ModelRole.PromptWrite` fed by
  `Choice="{Binding Writer}"`, so the writer gets all three text providers and the same
  refresh/status/persistence rules as every other role. Four things in it are decisions, not moves:
  **first run opens on Local** — the provider that cannot bill anyone (finding 55); **no pricing prose**
  — the copy says who is billed, over how many calls, and where the key lives, because a picker that can
  list whatever a key reaches cannot be priced out of constants (finding 56); **a left-behind tier
  carries its model, not only its provider** (finding 54); and **a run resolves its writer once**, so a
  pointer that lands on Claude mid-build does not send the second pass to Claude.
  Handles: `IdeaPromptModelTierPicker` died with the tier picker and `IdeaPromptWriterProviderPicker` is
  new; `IdeaPromptOllamaModelPicker`, `IdeaPromptOllamaRefreshButton`, `IdeaPromptTierOllamaStatusLabel`
  and `IdeaPromptModelSummaryLabel` all survive on the shared row, three of them under names that predate
  what the control now does (finding 45 again, finding 56 for the summary label). The page holds 35 ids
  either side of this half, and the 38 → 35 belongs to the whole of a-5.
  22 test methods in, 7 tier-shaped ones out, theories carrying the extra rows: net **+17**, gate
  **1,744**, app still 0 warnings. 13 falsifications, all caught; the first round reported a survivor
  that was really a test never written (finding 53) and a fake store that would have caught nothing had
  it been real (finding 52).
  `GeneratorSettingsReader` is down to one tier-shaped bridge, `EnrichTarget`, which goes with the
  structure editor in a-6.
- **a-6 · structure editor, then delete the tier.** `ModelTier`, `ModelTierOptions`,
  `GeneratorSettingsReader.*Target(tier)` and the tier keys go in the commit that removes their last
  reader, and after a-5 that reader is one line: the structure editor's `EnrichTarget`. Enrichment opens
  on Local and persists the choice, like the other three roles. Nothing else in the app offers `Sonnet`
  or `Opus` as a choice any more, so a-6 is where the enum, its keys and its round-trip tests get deleted
  rather than deprecated.

Split this way so no commit sits unwired longer than it must: a slice judged by "the suite passes" cannot
say whether its types are reachable (finding 31), so every slice from here either gains a caller in the
same commit or deletes something that had one.

`ModelChoiceState` is deliberately not a MAUI type, which is the point: every rule pinned so far as a
markup string — which model does this role call, is the pick kept across a provider switch, is it
persisted — becomes an ordinary unit test. Finding 24 accepted that a row can only be inspected
statically because no MAUI control can be constructed in this suite; the state object is where that
behaviour belongs instead.

### Phase B1.5 (new) — the official `Anthropic` package, before the Anthropic half of B2

**The picker no longer waits on this (2026-11-21).** a-1 reads `GET /v1/models` over the existing named
client without the package, so the catalog dependency that made B1.5 a prerequisite is gone. What is
left here is the original question about *messages* bodies — version constant, error parsing, typed
output config — and nothing in B2.3 depends on it.
The hand-rolled transport pins `anthropic-version: 2023-06-01`, builds request bodies from anonymous
objects and parses errors by string. The official SDK models all of that, exposes `HttpClient` /
`Handlers` so the existing named client and the `FakeHttpMessageHandler` wire tests survive, and
returns `GET /v1/models` typed with the lifecycle and capability fields §4.8 needs. A separate slice
because it changes what three call sites send, and before B2's Anthropic picker because the picker
needs that list. Worth settling before the AOT objection is raised: this app publishes
`PublishSingleFile` + self-contained with **no** `PublishAot` and **no** `PublishTrimmed`
(`publish.ps1`), so the SDK's open AOT issue costs this project nothing.

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
- **Line endings are per file, not per tree.** `core.autocrlf=true` with no `.gitattributes`, so the
  index holds LF for every file while the working tree is mixed (280 LF / 116 CRLF / 3 mixed). A script
  that writes the other ending trips no build and shows nothing in `git diff --numstat`. Write what the
  file's own neighbours have (`git ls-files --eol`), and read a whole-file hunk with
  `git diff --ignore-cr-at-eol --numstat` beside it before concluding the edit changed anything.

---

## 8. Verification

```
dotnet build ImageGenerator.MAUI/ImageGenerator.MAUI.csproj -c Debug     # 0 warnings expected
grep -c MAUIG2045 <build log>                                            # repath checklist, must stay 0
dotnet test ImageGenerator.MAUI.Tests/ImageGenerator.MAUI.Tests.csproj -c Debug
```

Gate after the first adopted row and its fix: **1,620 passed / 0 failed / 0 skipped**, app builds at
0 warnings and 0 `MAUIG2045`. Current gate after B2.3a-5 (both rows on the idea page): **1,744 passed /
0 failed / 0 skipped**, same build hygiene. Manual matrix per role, naming the handle and not the sentence
(§4.23): pick → restart → still picked; change another role → this one picked; change another role
→ this one unchanged; kill the server → the row explains it under its own `…StatusLabel`; **the
row's own picker lists models after Refresh** — that last one is what silently died first time, and
nothing saw it. For the observer add: pick a model → build → the model named on screen is the one the
status line names, and after a local observation the same model is the one freed.

Pre-existing and not from this work: `ComfyUiImageGenerationServiceTests.Ws_CanceledDuringReceive_
StillNotifiesServerCancel` fails every time it is run alone and passes inside the full run — an
isolation bug in that test, untouched here and worth its own fix.

---

## 9. Open questions for the owner

`generate` is the image model, and its **local (Ollama) image model** can currently only be chosen in
Settings (`MainPage.xaml` has no Ollama picker at all — verified). Under §1.2 that box should move to
the page where generation happens; under §1.1 `generate` is out of scope. Until decided, the Settings
picker stays and is relabelled as the generation model only. Recommend moving it onto the generate
card in Phase C.

**Release notes owed by B2.** Five automation ids are gone and two are new, all of them the deliberate
consequence of one row per decision: gone — `MutationModelTierPicker`, `IdeaPromptModelTierPicker`,
`IdeaPromptOpenRouterVisionModelPicker`, `IdeaPromptOpenRouterVisionRefreshButton`,
`IdeaPromptOpenRouterVisionStatusLabel`; new — `MutationModelProviderPicker`,
`IdeaPromptWriterProviderPicker`. Every other id a page had before still resolves, because
the shared rows kept the legacy handles (`MutationOllamaModelPicker` now offers three providers under a
name that names one — finding 45). Two behaviour notes belong with them: switching a role to a provider
with nothing listed now asks that provider (finding 51), and the OpenRouter *free-only* switch filters
the observer's vision list only — text roles are listed in full, deliberately, because the switch is not
on their pages.

Added while reading the Anthropic API (§4.8); each is a decision and not a discovery:

- **`anthropic-version`.** The transport pins `2023-06-01`; the documented current version is
  `2026-10-21`. Nothing the app sends is rejected by the newer version (no `temperature`, `top_p` or
  `top_k` go to Anthropic), so the bump looks safe — but "safe" here is a decision with a migration
  page behind it, not a free change. Adopting the SDK (B1.5) hands the constant to somebody else.
- **Cost copy. DECIDED (2026-11-21): stop estimating.** Once an arbitrary model id is selectable,
  `SelectedModelTier == Opus ? 0.028 : 0.017` is not an estimate, it is a guess with two inputs — and
  `/v1/models` carries no pricing to replace it with. The constants and the dollar sentence come out in
  a-3; what is said instead is what is actually known: free or paid, and how many calls.
- **Enrichment's starting value. DECIDED (2026-11-21): Local, then persisted.** The role never stored a
  tier and reopened on a paid Anthropic model on every navigation. It starts on the provider that
  cannot bill anyone and remembers what is picked after that.
- **Anthropic as a vision provider.** Capability says yes (`image_input.supported`), cost says not
  by default. Nothing in the target design blocks it: one entry in the observer's source dictionary
  (`AppSettingsCoordinator.VisionModelSources`) plus the Messages-with-image branch (§4.9). Adding it is
  an offer in the UI, so it is a decision and not a capability flag — see finding 48.

## 10. Non-goals

No multi-model compare queue, no per-call model override, no Anthropic/OpenRouter catalogs beyond
what a role needs, no change to how images are generated or to which providers can *render*.
