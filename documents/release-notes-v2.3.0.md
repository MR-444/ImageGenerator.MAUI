## New

- **Pollinations API v2** — tracked the API rewrite: new model titles, mandatory auth, and quality tiers for the `gptimage` models
- **Moss-Image sample** — added as a new model sample
- **Ollama: model selection is remembered** across app restarts

## Fixes & Improvements

- **CivitAI uploads**: oversized images are now re-encoded/downscaled instead of failing the upload
- **Vision backends**: oversized reference images are downscaled before being sent
- **ComfyUI**: hardened the WebSocket progress stream against premature closes
- **Ollama**: a reference image is now observed only once (fixes a double-attach issue)
- **Prose result editor**: fixed a text-alignment bug (resolved with the licensed ImageSharp 4.x build)
- **Gallery**: "Use as input" on the detail page now rejects invalid use with a clear message instead of failing silently

## Maintenance

- Upgraded **SixLabors.ImageSharp to 4.1.3** (licensed build; key supplied via CI secret)
- Upgraded all NuGet packages, including **Refit 16.4** (breaking changes addressed)
- GitHub Actions: ImageSharp license key wired up via workflow secrets
- Repo hygiene: removed obsolete audit docs, unused imports, and redundant tests
- Local license files (`*.lic`) are now git-ignored
