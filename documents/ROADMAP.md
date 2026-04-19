# Road to 1.0 (drop the `-preview` suffix)

> Living document tracking what's between the current `-preview` releases and a clean `v1.0.0`. Updated in-place as items complete. Last session reference: Claude session that shipped `v0.2.0-preview` (`0857642`, 2026-04-19).

## Context

v0.2.0-preview shipped (3-col UI + multi-image input + polish). This document concretizes *what's still missing before the `-preview` suffix can responsibly come off*. Adds a Clean Architecture review the author asked for (they started the layering themselves). Keeps the budget realistic for a dormant-ish hobby project: most items are hours, not weeks.

Four buckets, in execution order. Each item has a paragraph, files touched (when known), and a rough size. Items marked "**blocker**" must ship before 1.0; "**gate**" is quality-raising; "**later**" is safe to defer.

---

## Bucket 0 — Signing path (the $100/yr cert is not the only option)

The $100–300/yr code-signing cert is what *corporate* ISVs buy. GitHub hobbyists use one of three free-to-cheap alternatives:

**Option A — Ship unsigned, document SmartScreen click-through (zero cost, zero effort).** What 90% of GitHub hobby projects do. Windows shows "protected your PC"; README explains "click More info → Run anyway". Reputation builds with downloads over weeks/months and the warning eventually softens. Recommended for this project's current audience (hobbyist devs who trust GitHub releases). Action: add a "Windows SmartScreen" section to README.

**Option B — Apply to SignPath Foundation (zero cost, ~2–6 week wait).** SignPath runs a free code-signing program specifically for OSS projects. Requires a public GitHub repo (check), a real project name (check), and an application. Once approved, integrates into CI. The gold standard for free OSS signing. Action: apply at signpath.org/foundation when the project feels mature enough to commit to the workflow.

**Option C — Azure Trusted Signing ($10/mo, no waiting).** Microsoft's 2024 service for individuals. Pay-per-sign or flat rate. Proper EV-equivalent SmartScreen trust. Only worth it if downloads outgrow the SmartScreen-friction ceiling.

**Recommendation for now:** A (document) + lodge B (apply) in parallel. Don't pay.

**Size:** A = 10 min README edit. B = 30 min application + wait.

---

## Bucket 1 — Pre-1.0 blockers

### 1.1 Bug triage smoke-test matrix — **blocker**

Currently: "happy path works on 5 seeded models." That's not enough to drop `-preview`. Build a deliberate smoke-test matrix as a markdown checklist in `documents/release-smoke-test.md`:

Models × scenarios grid:
- Flux 1.1 Pro, Pro Ultra, Flux 2 Klein 4B, gpt-image-1.5, nano-banana-2
- Prompt only
- Prompt + 1 input image (where supported)
- Prompt + max input images (1/1/1/10/14)
- Custom dimensions (Flux Pro only)
- Seed fixed + randomize on/off
- All four GPT 1.5 advanced knobs
- Token cleared mid-generation (Cancel works?)
- Bad token (401 response — does the error message help?)
- Network disconnected (what does Polly retry look like to the user?)

For each row, one of ✅ / 🐛 (link to issue) / ⏭ (not applicable). Run through manually before cutting 1.0. Any 🐛 that affects the happy path = blocker; edge-case bugs go to a "known issues" README section.

**Size:** 2–3 hours manual testing + 30 min write-up.

### 1.2 Clean Architecture — fix the four CRITICAL violations — **blocker** for clean 1.0

Audit (sub-agent, verified) found these ship-blocking CA breaks:

#### 1.2a `Core/Application/Interfaces/IModelCatalogService.cs:1` imports Presentation
`using ImageGenerator.MAUI.Presentation.ViewModels;` — Application depends on `ModelOption` (a Presentation record). Application cannot compile without UI. Fix: introduce a Core-level record (e.g., `Core/Domain/ValueObjects/ModelOption.cs` — pure `record(Display, Value, Provider)`), have `IModelCatalogService` return that, keep `Presentation.ViewModels.ModelOption` only if a UI-specific wrapper is needed (probably not).

Files: `Core/Application/Interfaces/IModelCatalogService.cs`, new `Core/Domain/ValueObjects/ModelOption.cs` (or similar), `Infrastructure/Services/ModelCatalogService.cs`, `Presentation/ViewModels/GeneratorViewModel.cs`, `Presentation/ViewModels/ModelOption.cs` (delete or keep as thin adapter), all `using ImageGenerator.MAUI.Presentation.ViewModels;` imports that reach for `ModelOption` — grep for them.

**Size:** 1 hour (rename + move + fix imports + test update).

#### 1.2b `Core/Domain/ValueObjects/Factories/ImageModelFactory.cs:2` imports Infrastructure
`using ImageGenerator.MAUI.Infrastructure.External.Replicate;` — the factory reaches into Infrastructure for `ReplicateImageEncoding.BuildDataUri`. Domain ↓ Infrastructure = inversion. Fix: `BuildDataUri` is pure string math on a base64 + content-type — move it to `Core/Domain/Services/ImageDataUriEncoder.cs` (or inline the 3 lines into the factory). Infrastructure can keep a re-export if needed.

Files: `Core/Domain/ValueObjects/Factories/ImageModelFactory.cs`, `Infrastructure/External/Replicate/ReplicateImageEncoding.cs` (move or delete), plus any other callers (grep `BuildDataUri`).

**Size:** 30 min.

#### 1.2c `Core/Domain/Entities/ImageGenerationParameters.cs` uses CommunityToolkit.Mvvm
`using CommunityToolkit.Mvvm.ComponentModel;` + `[ObservableProperty]`. Audit flagged as "framework leak," but on a second look:
- `CommunityToolkit.Mvvm` is UI-framework-agnostic (it only generates `INotifyPropertyChanged` plumbing — cross-platform, works in WPF / Avalonia / console / etc.). Not MAUI-specific.
- The project is a single `.csproj`; there's no compile-time separation of layers to enforce.
- Moving the entity to Presentation (originally "minimal" recommendation) would force Core/Application and Infrastructure services (`IImageGenerationService`, `IImageFileService`, `ImageModelFactory`) to import Presentation — a *worse* CA violation than the current state.

**Decision:** keep the entity in Core, document the deliberate choice with an inline comment. Status: **closed as pragmatic no-op**. Re-open only if the project is ever split into separate Core/Infrastructure/Presentation `.csproj` files — then swap to hand-rolled `INotifyPropertyChanged` so Core carries zero external deps. That's ~1 hour of boilerplate.

**Size:** done (one comment added at `Core/Domain/Entities/ImageGenerationParameters.cs`).

#### 1.2d `GeneratorViewModel` calls platform APIs directly
Lines 351, 397, 444, 453, 462, 480, 485, 538, 577, 590, 603, 616 — direct `SecureStorage`, `FilePicker`, `Process.Start`, `Directory.CreateDirectory`, `File.*`. Audit recommends extracting to `ISecureTokenStorage` / `IFileDialogService` / `IProcessLauncher`.

**Honest call:** this is testability sugar, not a real CA violation in a single-platform Windows app. The ViewModel is allowed to know about MAUI. For 1.0, the recommendation is to **skip** this one unless unit-testing the file/process paths becomes a need. If skipped: add a comment in the VM acknowledging the choice. If done: ~3 hours for three interfaces + implementations + DI wiring + VM refactor.

**Recommendation:** skip for 1.0, reconsider if the VM grows more test cases.

### 1.3 Version hygiene — **blocker**

`ImageGenerator.MAUI.csproj:21-22` — `ApplicationDisplayVersion=1.0`, `ApplicationVersion=1`. Never bumped. For 1.0 release these should match the tag (`1.0.0` / `1`). Also `ApplicationId=com.companyname.imagegenerator.maui` (default template value) — change to `io.github.mr444.imagegenerator.maui` before 1.0 or it's baked into Windows registrations.

**Size:** 5 min edit + one rebuild.

### 1.4 User-facing README section — **blocker**

Current README is developer-oriented. 1.0 users need:
- "Getting a Replicate API token" (link to replicate.com/account/api-tokens, sign-up flow)
- "Costs" (one paragraph: ~$0.003–$0.05 per generation, varies by model — link to Replicate pricing)
- "Where images are saved" (`%USERPROFILE%\Pictures\ImageGenerator.MAUI\`)
- "Reading back the metadata" — recommend [MediaInfo](https://mediaarea.net/en/MediaInfo) (the author's tool of choice; cross-platform GUI + CLI, reads the EXIF `UserComment` field the app writes)
- "Windows SmartScreen warning" (Bucket 0 option A text)

**Size:** 30 min.

---

## Bucket 2 — Quality gates (raise the bar)

### 2.1 Error-path UX audit — **gate**

Run through 401 / 402 (quota) / 429 / 5xx / network drop. Check: is the error message actionable? Is the Generate button usable again? Is the status color right? `GeneratorViewModel.GenerateImageAsync:337–384` is where errors land. May need error-type-aware messages rather than raw `ex.Message`. Most of this is driven by what the smoke test matrix (1.1) surfaces — so do them together.

**Size:** 1–2 hours, overlaps with 1.1.

### 2.2 First-launch speed decision — **gate**

`EnableCompressionInSingleFile=true` cut the exe from 307 MB to 117 MB but adds ~5–10s decompression on first launch (once, then cached). Options: (A) keep compression, add a `documents/first-launch.md` note in the release body. (B) drop compression, accept 300 MB downloads. (C) add a splash screen that hides the decompression. Recommend A: the size win is enormous and the cost is one-time.

**Size:** 10 min if A. If C, splash is ~2 hours of MAUI work.

---

## Bucket 3 — Clean Architecture moderate fixes (later, not blocker)

From the audit's Moderate tier:

- **#6 Flux value objects (`Core/Domain/ValueObjects/Flux/*`) are Replicate API DTOs with `[JsonPropertyName]`, `[Range]`, etc.** They are Replicate contracts, not domain concepts. Move to `Infrastructure/External/Replicate/Models/Flux/`. Domain can keep an `IImageModel` marker interface if needed. **Size: 2–3 hours.**
- **#7, #8 Interface location.** `Infrastructure/Interfaces/IImageFileService.cs` and `IImageEncoderProvider.cs` are ports (things Application depends on), should live in `Core/Application/Interfaces/`. Move them. **Size: 20 min.**
- **#5 `ReplicateModels.cs:4` namespace is `ImageGenerator.MAUI.Models.Replicate` but file lives in `Infrastructure/External/Replicate/`.** Rename the namespace to match folder. **Size: 10 min (auto-refactor).**

Do these post-1.0. A `documents/post-1.0-backlog.md` will be created to track them separately once 1.0 ships.

---

## Bucket 4 — Deferred nice-to-haves

Low priority, one-at-a-time as energy permits:

- **Hex status colors → theme resources.** Colors already named (`StatusSuccessLight`, etc.). MainPage.xaml DataTrigger setters use `AppThemeBinding Light=... Dark=...`. Nothing actually uses hardcoded hex in MainPage's status area — this deferred item may already be resolved; verify and close. **Size: 15 min verify.**
- **ModelCatalogService `LoadCachedAsync` / `SaveCachedAsync` tests.** Needs a filesystem seam; the author explicitly preferred simplicity earlier. Skip unless adding real caching logic. **Size: 1 hour if pursued.**
- **Download-timeout test for `CancelAfter(60s)`.** Via mock `HttpMessageHandler` with a short override. Deprioritized. **Size: 45 min.**
- **Delete `ImageGenerator.MAUI/gpt-image-1.5.json` + `nano-banana-2.json` schema dumps** from project root — uncommitted; confirm they're unused then delete. **Size: 5 min.**

---

## Critical files at a glance

- `Core/Application/Interfaces/IModelCatalogService.cs` — CA #1.2a
- `Core/Domain/ValueObjects/Factories/ImageModelFactory.cs` — CA #1.2b
- `Core/Domain/Entities/ImageGenerationParameters.cs` — CA #1.2c (move to Presentation)
- `Presentation/ViewModels/GeneratorViewModel.cs` — CA #1.2d (skip for 1.0)
- `ImageGenerator.MAUI/ImageGenerator.MAUI.csproj` — version hygiene #1.3
- `README.md` — #1.4 user docs + Bucket 0 SmartScreen note
- New: `documents/release-smoke-test.md` — #1.1 bug triage grid

## Verification to exit 1.0

1. Run `publish.ps1` — builds clean, exe is produced.
2. Run `dotnet test` — all tests pass.
3. Launch the exe, run the smoke-test matrix (Bucket 1.1). Zero 🐛 on happy path. Edge-case 🐛 documented in README "Known issues".
4. Open `Core/Application/Interfaces/IModelCatalogService.cs` — no Presentation imports.
5. Open `Core/Domain/ValueObjects/Factories/ImageModelFactory.cs` — no Infrastructure imports.
6. Open `ImageGenerator.MAUI.csproj` — version = release tag.
7. README has Getting Started, Costs, Where images save, SmartScreen sections.

When all seven green: tag `v1.0.0` (no `-preview`), `gh release create` via the existing `publish.ps1` + notes template from v0.2.0-preview.

## Recommended sequencing across sessions

- **Session A (~2h):** Bucket 1.2a + 1.2b + 1.2c minimal move + 1.3 version + 1.4 README + Bucket 0 SmartScreen note. Ship as v0.3.0-preview to validate the CA refactor didn't break anything.
- **Session B (~3h):** Bucket 1.1 smoke test + 2.1 error-path audit. Fix any happy-path 🐛 found. Ship v0.4.0-preview.
- **Session C (~30m):** Bucket 2.2 first-launch note, final README polish, tag 1.0.0.
- **Later (not time-boxed):** Bucket 3 post-1.0 CA moderate fixes + Bucket 4 deferred items.

Total to 1.0: ~5–6 focused hours spread over three sessions.
