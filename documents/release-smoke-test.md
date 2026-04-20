# Release smoke test

Run this checklist against a fresh Release build of `ImageGenerator.MAUI.exe` before cutting a non-preview release. Any ❌ on the "happy path" scenarios is a ship-blocker; edge-case failures become entries in the README "Known issues" section (or tracked issues, depending on severity).

Legend: ✅ pass · ❌ fail (link the issue) · ⏭ not applicable for this model · ⬜ not yet tested

**Build under test:** `e5ac761` / v0.3.1-preview / 2026-04-20
**Tester:** MR-444
**API tokens used:** Replicate pay-as-you-go (personal account, nothing unusual)

---

## Happy path (must all pass for a clean 1.0)

Basic prompt-only generation across every seeded model.

| Model | Prompt only | Notes |
|-------|-------------|-------|
| `black-forest-labs/flux-1.1-pro` | ✅ | |
| `black-forest-labs/flux-1.1-pro-ultra` | ✅ | |
| `black-forest-labs/flux-2-klein-4b` | ✅ | |
| `openai/gpt-image-1.5` | ✅ | |
| `google/nano-banana-2` | ✅ | |

Expected per row: generation completes in under a minute, image lands in `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with EXIF `UserComment` containing the prompt and parameters (verify with [MediaInfo](https://mediaarea.net/en/MediaInfo)), Result thumbnail shows in the app.

---

## Input-image flows

| Model | 1 input image | Max input images | Notes |
|-------|---------------|------------------|-------|
| `flux-1.1-pro` | ⏭ (no img2img) | ⏭ | |
| `flux-1.1-pro-ultra` | ✅ (1 max) | ⏭ (same as above) | image_prompt_strength slider relevant |
| `flux-2-klein-4b` | ✅ (1 max) | ⏭ | |
| `gpt-image-1.5` | ✅ | ⬜ (10 max) | |
| `nano-banana-2` | ✅ | ✅ (14 max) | |

Per row: pick 1 or N images via **Add image**, verify the horizontal thumbnail strip, verify the per-image × remove, verify **Clear all** empties the list, verify the × button removes only the intended image. Generate and confirm the output is clearly influenced by the inputs.

Extra: drag-and-drop or paste is **not** supported yet — verify Add image is the only path, without regressing.

---

## Parameter variations

Pick Flux 1.1 Pro (or Pro Ultra where capability-gated) unless noted.

| Scenario | Result | Notes |
|----------|--------|-------|
| Custom dimensions (Flux Pro only, 256×1440 range) | ✅ | UI shows the 256–1440 helper text; numeric entry clamps |
| Seed fixed, run twice → identical image | ✅ | On models that honor seed (all Flux) |
| Seed randomize on → different images | ✅ | |
| GPT 1.5 `quality` = `high` | ✅ | |
| GPT 1.5 `quality` = `low` | ✅ | Noticeably cheaper/faster |
| GPT 1.5 `background` = `transparent` | ✅ | Requires format=png |
| GPT 1.5 `moderation` = `auto` vs `low` | ✅ | |
| GPT 1.5 `input_fidelity` = `high` with input image | ⬜ | |
| nano-banana-2 aspect ratio × 3 values | ✅ | Covers at least portrait / square / landscape |
| nano-banana-2 resolution × 1K / 2K / 4K | ✅ | Size on disk scales as expected |
| Output format: png / jpg / webp round-trip | ✅ | webp on nano-banana-2 should auto-coerce to jpg |

---

## Error paths

These should produce **actionable** status messages and leave the Generate button re-usable (not stuck in "Working…" state).

| Scenario | Status message is actionable? | Generate button re-armed? | Notes |
|----------|------------------------------|---------------------------|-------|
| No token + click Generate | ✅ | ⬜ | Validator should prevent this; if not, message clarity matters |
| Bad/revoked token (401) | ⬜ | ⬜ | Message should mention "token" not just raw ex |
| Network disconnected mid-generate | ⬜ | ⬜ | Polly retries should surface gracefully |
| Replicate 429 (rate limit) | ⬜ | ⬜ | Hard to reproduce; if encountered note what happened |
| Replicate 402 (quota / billing) | ⬜ | ⬜ | Hard to reproduce; may need a drained test account |
| Replicate 5xx | ⬜ | ⬜ | |
| Cancel during generation | ✅ | ⬜ | Click Cancel mid-flight; status should read "canceled" |
| Generate while already generating (double-click) | ✅ | ⬜ | The button should be disabled/debounced |

---

## UI / state

| Scenario | Works | Notes |
|----------|-------|-------|
| First launch after install: SmartScreen click-through | ⬜ | "More info → Run anyway" | I just click the release and it runs...
| Token persists across app restart | ✅ | Close, reopen — token still there |
| Token forget button clears SecureStorage | ✅ | Close, reopen — field empty |
| Refresh Models hydrates picker | ✅ | With a valid token |
| Catalog survives restart without Refresh | ✅ | Close, reopen — picker still populated |
| Switch provider filter | ✅ | Model list updates |
| Switch model → capability-gated UI updates | ✅ | e.g., GPT 1.5 card only visible when selected |
| Result thumbnail clickable → opens in OS viewer |✅ | |
| "Use as input for next generation" works | ✅ | Generated image appears in input strip of next run |
| "Show in folder" opens Explorer at correct file |✅ | |
| About button shows version + license dialog | ✅ | Dialog opens with version + MIT + repo URL. |

---

## Sign-off

When every row above is ✅ or ⏭ with justified notes, and happy-path is 100% ✅, 1.0 can ship. Otherwise, triage to one of:

- **Happy-path blocker:** fix before shipping.
- **Edge-case bug:** document in README "Known issues" section and ship.
- **Nice-to-have gap:** file as a GitHub issue labelled `enhancement` and ship.
