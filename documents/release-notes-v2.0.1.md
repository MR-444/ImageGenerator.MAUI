A small housekeeping release on top of v2.0.0 that brings Emberforge's data layout in line with Windows conventions. No new generation features — but the path changes are worth a deliberate upgrade.

## What changed

- **One configurable data root.** All app data now lives under a single root (default `Pictures\Emberforge`). When you point the output folder elsewhere in **Settings**, your `comfy-workflows\`, `mutation-library\`, and `prompt-builder\` folders now move with it. Previously those stayed pinned to the default location, which stranded your ComfyUI workflows and made the **ComfyUI provider silently disappear** from the model picker when you used another drive. Fixed.
- **`app.log` moved to the standard per-app location:** `%LOCALAPPDATA%\Emberforge\app.log` (no longer in your Pictures library).
- **Windows publisher fixed.** The app shipped with the WinUI template's `"User Name"` placeholder publisher, so per-user data (preferences, API tokens, model/checkpoint caches) landed under `%LOCALAPPDATA%\User Name\…`. It now correctly lands under `%LOCALAPPDATA%\MR-444\…`.

## Upgrading from v2.0.0 (please read — no automatic migration)

Because the default folders were renamed, a fresh v2.0.1 launch will create empty folders and won't see your old data until you move it:

1. **Images / workflows / mutation library / prompt-builder** — move the contents of `Pictures\ImageGenerator.MAUI\` into `Pictures\Emberforge\` (or just set your existing folder as the root via **Settings → output folder**).
2. **Preferences & API tokens** — copy `%LOCALAPPDATA%\User Name\io.github.mr444.imagegenerator.maui` to `%LOCALAPPDATA%\MR-444\io.github.mr444.imagegenerator.maui`. Tokens use user-scoped DPAPI, so they still decrypt once the folder is carried over; otherwise you'll just re-enter them.

If you're a new user, none of this applies — everything is created fresh under `Pictures\Emberforge`.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1219 tests green.*
