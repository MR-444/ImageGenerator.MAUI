# 🎨 Image Generator MAUI

A Windows desktop image generation app built on .NET MAUI with three image providers — **Replicate** (Flux, Replicate-hosted OpenAI, Google, Ideogram), **Pollinations.ai** (Flux, Zimage, Qwen Image, plus any free image model their `/models` endpoint surfaces), and **ComfyUI** (your own local or LAN server — every API-format workflow export you drop into a folder becomes a selectable model). Switch providers in-app via a tabbed token picker; saved images carry the full prompt and generation parameters as EXIF / PNG-Comment metadata so every output is reproducible.

## 🌟 Features

- Windows 10/11 desktop (MAUI Windows target, `net10.0-windows10.0.22621.0`)
- **Three providers, one app** — switch between Replicate, Pollinations.ai, and ComfyUI via a tabbed token picker. Each provider's token persists independently in OS secure storage; anonymous Pollinations also works (just rate-limited). The active provider is inferred from the selected model, so picking a `pollinations/...` model routes to Pollinations without further clicks. Extensible: adding another provider is one entry in the `TokenProviders` collection — no XAML changes.
  - **Pollinations.ai** — `flux`, `zimage`, `qwen-image` seeded offline; **Refresh Models** fetches `gen.pollinations.ai/models` and surfaces any additional free image models (`output_modalities` includes `image` AND not `paid_only`). Optional Bearer token from [auth.pollinations.ai](https://auth.pollinations.ai) raises the rate limit from 1 req / 15 s anonymous → 1 req / 5 s on the Seed tier. Seeds and pixel parameters are clamped to the documented bounds (e.g. seed ≤ `2147483647`, the positive int32 max — Replicate's wider uint32 range still works unaffected).
  - **ComfyUI (local / LAN)** — point the app at your own ComfyUI server (no token, no cloud). Workflows-as-models: every API-format workflow JSON in `Pictures\ImageGenerator.MAUI\comfy-workflows\` appears in the model picker under provider "ComfyUI". The app patches your prompt (plain text or Ideogram-style structured JSON) into the exported graph, re-rolls every literal seed per run (API submissions don't randomize server-side), applies aspect ratio + megapixels when the workflow has a `ResolutionSelector` node, and expands `%date:...%` filename tokens the server would otherwise choke on. A ready-to-use [sample workflow](comfy-workflows/Ideogram4-Sample.json) (Ideogram 4, stock ComfyUI nodes only) ships in this repo — see [Generating via your own ComfyUI server](#generating-via-your-own-comfyui-server).
- **Concurrent generation queue** — click Generate while a previous job is still running; each click snapshots the current parameters into its own in-flight job. A scrollable queue below the form shows per-job prompt, status, spinner, thumbnail, and independent Cancel / Open / Show-in-folder / Use-as-input actions.
- **Batch from textfile** — point **Import prompts…** at a `.txt` of prompts separated by lines containing only `---`. Lines starting with `#` are ignored, so you can label or disable prompts without deleting them. The batch runs sequentially with the currently-selected model and parameters; failed prompts don't abort the queue. **Cancel batch** drains the remaining queue but lets the in-flight job finish — once a Replicate prediction is created, you're paying for it either way. 100-prompt hard cap.
- **In-app gallery** — browse previously generated images without leaving the app. Tile grid with sortable order (newest/oldest, name A→Z / Z→A, largest/smallest), live updates via `FileSystemWatcher` when new images are saved or removed, and a detail page with a larger preview, copyable metadata, and one-click **Use as input**, **Open in viewer**, and **Show in folder** actions. Thumbnails come from the Windows shell cache so a directory of multi-megabyte PNGs doesn't blow process memory.
- **Persisted UI state** — the last prompt and selected model are restored on next launch (per-user `Preferences`). Pick up where you left off.
- **Dynamic model catalog** — "Refresh Models" queries Replicate's `text-to-image` collection (filtered to `black-forest-labs`, `openai`, and `google` owners) **and** Pollinations' `/models` endpoint (image-only, free-tier) in parallel, then merges with the hardcoded seed entries (the Ideogram V4 family is pinned here — Replicate's collection doesn't list it). Every Replicate-hosted model groups under a single **Replicate** entry in the provider filter. New models surface without recompiling. The catalog is cached to `FileSystem.AppDataDirectory/model-catalog.json` and restored on launch.
- **Flux 2 family** — `flux-2-klein-4b`, `flux-2-flex`, `flux-2-pro`, `flux-2-max` with per-model payload shaping and optional `images` input
- **OpenAI** — `openai/gpt-image-1.5` and `openai/gpt-image-2` (via Replicate). The UI exposes the model-specific knobs: `quality`, `background` (incl. transparent PNGs), `moderation`, `input_fidelity`.
- **Google nano-banana-2** — `google/nano-banana-2` with its 15-value aspect enum, a resolution picker (1K / 2K / 4K), and image-input support. `webp` is auto-coerced to `jpg` (the model doesn't accept webp).
- **Ideogram V4** — `ideogram-v4-balanced`, `ideogram-v4-turbo`, `ideogram-v4-quality` with a dedicated options block: a resolution picker (`Auto` + the 23 native sizes; `Auto` omits the field and lets Ideogram choose), a **Structured JSON prompt** toggle (sends the prompt box as Ideogram's `json_prompt` string instead of `prompt`, validated as real JSON before generate), and an `enable_copyright_detection` checkbox. Output is PNG.
- **Flux classic** — 1.1 Pro / 1.1 Pro Ultra
- API tokens persisted via `SecureStorage` (Windows DPAPI under the hood) — independent slots per provider
- Per-job cancellation with retry/backoff (Polly via `Microsoft.Extensions.Http.Resilience`)
- Images saved to `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames
- Prompt + generation parameters embedded in the file — PNG `Comment` text chunk, or EXIF `UserComment` for JPG/WebP. Includes the actual pixel dimensions and model-specific fields (GPT options, resolution, etc.) so every image carries a complete reproducible recipe.
- **Diagnostics log** — `app.log` next to the saved images captures startup confirmation, every failed generation (Replicate or Pollinations) with model name and reason, and any caught exception. NLog rolls the file at 5 MB and keeps 5 archives. Services log through injected `ILogger<T>` so a provider's silent error can't escape the file, with `Infrastructure.External.*` bumped to `Debug` so HTTP request/response details land without flipping the global level. Easy to find, no spelunking through `%LocalAppData%\Packages\…`.
- MVVM via CommunityToolkit.Mvvm (`[RelayCommand]`, `[ObservableProperty]`)

## 📸 Screenshots

![Initial state — three-column layout: API token + input images, prompt + output settings, run/result column](<documents/Initial_Screenshot 2026-05-04 194431.png>)

![After generation — completed job card with thumbnail, status, and per-job Use as input / Open / Show in folder actions](<documents/Result_Screenshot 2026-05-04 1941259.png>)

![In-app gallery — tile grid with sort modes, live FileSystemWatcher updates, and a detail page with copyable metadata](<documents/Gallery_Screenshot 2026-05-04 193429.png>)

## 🖥️ Using the app (end users)

### Download

Grab `ImageGenerator.MAUI.exe` from the latest [release](https://github.com/MR-444/ImageGenerator.MAUI/releases). It's a self-contained single-file executable — no installer, no .NET runtime prerequisite. First launch unpacks the bundle once (a few seconds of apparent hang); subsequent launches are fast.

### Windows SmartScreen warning

The exe ships unsigned — buying a code-signing certificate is expensive for a hobby project. On first run, Windows SmartScreen will show **"Windows protected your PC"**. That's expected. Click **More info → Run anyway**. Reputation builds with downloads over time, so the warning will soften eventually.

If you'd rather not click through a SmartScreen warning, you can always [build from source](#-building-from-source).

### Picking a provider

The **API Tokens** card at the top of the form has a provider dropdown — switch between **Replicate**, **Pollinations**, and **ComfyUI** to edit each slot independently (the ComfyUI slot holds a server base URL instead of a token). All slots are stored separately, so you can keep them all configured and just flip the selected model to route requests one way or the other.

### Getting a Replicate API token

The Replicate branch covers the Flux family, the OpenAI-hosted `gpt-image-1.5` / `gpt-image-2`, Google's `nano-banana-2`, and the Ideogram V4 family (`balanced` / `turbo` / `quality`).

1. Sign up at [replicate.com](https://replicate.com).
2. Go to **Account → API tokens** (or directly [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens)).
3. Create a token, copy it.
4. With "Replicate" selected in the API Tokens picker, paste it into the **API Token** field. It's stored locally with Windows `SecureStorage` (DPAPI, per-user) — never leaves your machine except to call Replicate.

### Getting a Pollinations token (optional)

Pollinations works anonymously — pick a `pollinations/...` model in the picker, type a prompt, and hit Generate. The anonymous tier is rate-limited to 1 request every 15 seconds. With a free Seed-tier account that goes to 1 request every 5 seconds.

1. Sign up at [auth.pollinations.ai](https://auth.pollinations.ai).
2. Generate a token in your account dashboard.
3. With "Pollinations" selected in the API Tokens picker, paste it into the field. Same DPAPI-backed secure storage as the Replicate slot, just keyed independently.

Note on reproducibility: per Pollinations' own spec, the `seed` parameter is honored only by `flux`, `zimage`, `seedream`, `klein`, `seedance`, `nova-reel`. Other models silently ignore it, so re-running the same prompt+seed pair won't produce the same image on `qwen-image` etc.

### Generating via your own ComfyUI server

The ComfyUI provider talks to a [ComfyUI](https://github.com/comfyanonymous/ComfyUI) instance you run yourself — on the same machine or anywhere on your LAN. No token, no cloud, no cost beyond your own GPU time.

**Server setup**

1. Run a reasonably current ComfyUI build. If the server is on another machine, start it with `--listen` so it binds to the LAN (default is localhost-only), and make sure the port (default `8188`) is open in its firewall.
2. In the app, select **ComfyUI** in the API Tokens picker and enter the server's base URL, e.g. `http://192.168.1.50:8188` (default is `http://127.0.0.1:8188`). There is no auth — the provider assumes a trusted LAN; don't expose a ComfyUI port to the internet.

**Workflows as models**

The app never builds node graphs itself — you bring your own workflow. In ComfyUI, open your workflow and use **Workflow → Export (API)**, saving the JSON into `Pictures\ImageGenerator.MAUI\comfy-workflows\` (the app creates the folder on first run). Every file there becomes its own entry in the model picker under provider "ComfyUI" — the folder is re-scanned on launch and on **Refresh Models**. Normal saves (Ctrl+S / PNG-embedded workflows) are the UI format and can't be queued over the API; the app detects them and tells you to re-export.

At generation time the app patches the exported graph and submits it:

- **Prompt** — plain mode writes your prompt into the lowest-id `CLIPTextEncode` node with a literal text value. With **Structured JSON prompt** checked, the JSON goes into every `Ideogram4PromptBuilderKJ` node (kjnodes pack) and/or replaces any `CLIPTextEncode` literal that is itself a JSON object (a frozen caption from an Ideogram-style workflow).
- **Seeds** — every literal `seed` / `noise_seed` is re-rolled per run. ComfyUI's "randomize after generate" lives in the browser frontend, so API submissions would otherwise reproduce the identical image forever.
- **Aspect ratio + resolution** — if the workflow has a `ResolutionSelector` node, the app's aspect-ratio and megapixels pickers write into it; without one the workflow keeps its own resolution (silently).
- **`%date:...%` filename tokens** — expanded app-side. The ComfyUI server takes `filename_prefix` literally (token expansion is frontend-only), and the `:` in an unexpanded token is path-invalid on Windows servers.

So the minimum a workflow needs: a `CLIPTextEncode` with a literal text prompt and a `SaveImage` node. Everything else is optional.

**Sample workflow**

[`comfy-workflows/Ideogram4-Sample.json`](comfy-workflows/Ideogram4-Sample.json) is a ready-to-use Ideogram 4 text-to-image workflow using **stock ComfyUI nodes only** (no custom node packs). Copy it into `Pictures\ImageGenerator.MAUI\comfy-workflows\`, and it appears in the picker as "Ideogram4-Sample (ComfyUI)". It supports both plain prompts and the structured-JSON mode (including the in-app visual structure editor), aspect-ratio/megapixels selection, and dated output subfolders on the server. Your server needs the Ideogram 4 model files the graph references, in the usual ComfyUI model folders: `ideogram4_fp8_scaled.safetensors` + `ideogram4_unconditional_fp8_scaled.safetensors` (diffusion models), `qwen3vl_8b_fp8_scaled.safetensors` (text encoder), `flux2-vae.safetensors` (VAE) — the same files ComfyUI's built-in Ideogram 4 template uses.

Generations run on your GPU and can take minutes; the job card polls the server and shows progress like any other provider. Canceling in the app stops the polling, but the server finishes its render (interrupt support is on the roadmap).

### Costs

**Replicate**: roughly **$0.003 – $0.05** per generation depending on the model — Flux Pro is on the cheaper end; `gpt-image-1.5` at high quality and `nano-banana-2` at 4K land on the higher end. See [replicate.com/pricing](https://replicate.com/pricing) for current per-model rates. Pay-as-you-go — no monthly commitment.

**Pollinations**: the seeded models (`flux`, `zimage`, `qwen-image`) and every other model the catalog returns are **free** at both anonymous and Seed tiers. Paid-tier models (Pollinations marks them `paid_only` in `/models`) are filtered out and never appear in the picker.

### Where images are saved

Generated images land in `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames (timestamp + truncated prompt + seed). Each completed job card has its own **Show in folder** button that opens Explorer with that specific file highlighted; the Run card also has an **Open output folder** shortcut and a **Gallery** button that opens the in-app browser.

### Browsing past images (gallery)

The **Gallery** button on the main page opens an in-app grid of every image in the output folder, sorted however you like (newest first by default). Click a tile to open the image in your default OS viewer; click **Show metadata** to open the detail page with a larger preview, the parsed metadata as selectable text, and one-click actions: **Copy metadata** to clipboard, **Use as input** (sends the image back to the generator as an input-image attachment), **Open in viewer**, **Show in folder**. The gallery uses the OS shell thumbnail cache, so opening a folder with many large PNGs is instant.

### Running a batch from a textfile

Pick a model and configure the parameters you want to apply to the batch (aspect ratio, output format, seed mode, and so on), then click **Import prompts…** in the Run column. Choose a `.txt` file shaped like this:

```
A young woman holding a bouquet,
standing in a sunlit meadow,
cinematic lighting
---
# disabled — skip this one
Mountain range at golden hour, ultra-wide
---
Portrait of an elderly fisherman, 85mm
```

Rules: a line containing only `---` separates prompts; multi-line prompts are fine; lines starting with `#` are comments and are skipped; empty chunks are ignored. The picker confirms the count (e.g. *"Run 12 prompts using Flux 1.1 Pro?"*) before anything submits. Hard cap is 100 prompts per file.

The batch runs **strictly sequentially** — one job at a time — and reuses the existing queue, so each prompt becomes its own card with status, thumbnail, and per-job actions. Failures don't abort the run; the end-of-batch status reads e.g. *"Batch complete — 11 ok, 1 failed, 0 canceled."*

Click **Cancel batch** at any time to stop the queue from starting any further prompts. The currently-running job is allowed to finish — once a Replicate prediction has been submitted you're paying for it whether you keep the image or not.

### Reading back the embedded metadata

The app embeds the full prompt and generation parameters into the saved file so you can recover the recipe months later. For **PNG** the metadata lives in the standard `Comment` text chunk; for **JPG/WebP** it's written as EXIF `UserComment`. The fastest way to inspect it is the in-app gallery's **Show metadata** button (also lets you copy it to the clipboard); externally, **[MediaInfo](https://mediaarea.net/en/MediaInfo)** (GUI + CLI, cross-platform, free) or `exiftool` work too. Besides the prompt and seed you'll see the actual pixel dimensions produced by the API and the model-specific options (GPT quality/background/moderation/input-fidelity, nano-banana resolution, Flux Ultra raw/image-prompt-strength, Ideogram resolution/JSON-mode/copyright-detection) so two people with the metadata can reproduce the same image.

## 🛠️ Technologies

- .NET MAUI 10 (Windows target)
- CommunityToolkit.Mvvm 8.4.2
- Refit 10 with `System.Text.Json` (custom `NullSkippingDictionaryConverter` so dict-based Replicate payloads never send `"field": null`, which Replicate rejects with 422)
- Microsoft.Extensions.Http.Resilience (retry + timeout policy)
- SixLabors.ImageSharp 3 (image encoding + EXIF metadata)
- Moq + xUnit + FluentAssertions for tests

## 📋 Prerequisites

- .NET 10.0 SDK
- Visual Studio 2022 (17.12+) / JetBrains Rider with the MAUI workload
- Windows 10 1809 (build 17763) or newer
- Replicate API token and/or OpenAI API key

## 🚀 Building from source

1. Clone the repository:
```bash
git clone https://github.com/MR-444/ImageGenerator.MAUI.git
```

2. Open `ImageGenerator.MAUI.sln` in Visual Studio or Rider, restore NuGet packages, build.

3. Run the app. Paste an API token, click **Refresh Models** to populate the picker, pick a model + prompt, and **Generate**.

4. To produce the self-contained single-file release exe, run `pwsh ./publish.ps1` from the repo root.

## 🏗️ Project Structure

```
ImageGenerator.MAUI/
├── Core/
│   ├── Application/          # IImageGenerationService, IModelCatalogService, IGalleryService,
│   │                         # IPromptBatchParser (+ PromptBatchParser implementation)
│   └── Domain/               # Entities (incl. GalleryItem), value objects (Flux/Pollinations/
│                             # ComfyUi request shapes), ModelCapabilities, ComfyUi/ (workflow
│                             # patcher), Descriptors/Pollinations/ + Descriptors/ComfyUi/
│                             # (seed + fallback descriptors mirroring the Replicate pattern)
├── Infrastructure/
│   ├── Diagnostics/          # CrashLogger (NLog backend, app.log + WinUI dispatcher hook)
│   ├── External/
│   │   ├── ComfyUi/          # ComfyUiImageGenerationService (POST /prompt → poll /history →
│   │   │                     # GET /view), ComfyUiWorkflowCatalogService (folder scan)
│   │   ├── OpenAi/           # Refit client + DTOs + service
│   │   ├── Pollinations/     # PollinationsImageGenerationService, PollinationsCatalogService
│   │   └── Replicate/        # Refit client + DTOs + service + image encoding helpers
│   └── Services/             # ImageFileService, ModelCatalogService, GalleryService,
│                             # ImageGenerationDispatcher (routes by model-id prefix),
│                             # FileLauncher, ClipboardService
├── Presentation/
│   ├── ViewModels/           # GeneratorViewModel, GalleryViewModel, GalleryItemDetailViewModel
│   ├── Views/                # MainPage, GalleryPage, GalleryItemDetailPage (.xaml + .cs)
│   ├── Behaviors/            # NumericOnlyBehavior
│   └── Converters/           # ShellThumbnail / ShellPreview / StringToEnum / Inverse / NonEmptyString
├── Shared/Constants/         # ModelConstants, ValidationConstants, OutputPaths
├── Resources/                # Styles, Colors, Fonts, Images
├── Extensions/               # RefitServiceExtensions (serializer + resilience pipeline)
├── AppShell.xaml             # Shell + route registration (gallery, detail)
└── MauiProgram.cs            # DI registration + app bootstrap
```

## 🧪 Testing

```bash
dotnet test
```

653 tests covering: model factory payload shapes, the ComfyUI provider (workflow patcher incl. seed re-roll / `%date%` expansion / structured-JSON targeting, the shipped sample workflow's contract, catalog folder scan, HTTP service flows), Replicate service HTTP flows (via Refit mocks), the `NullSkippingDictionaryConverter`, model catalog filtering and persistence (both Replicate + Pollinations branches), the Ideogram V4 payload (prompt vs `json_prompt` string, resolution omit-on-`Auto`, PNG-locked output, structured-JSON validation), image file naming + EXIF round-trip, GeneratorViewModel commands and state machine (including batch order, partial failure, distinct seeds, Cancel-batch leaves the in-flight job alone, and the tabbed token slots stay independent across providers), prompt batch parser (delimiter, comments, multi-line, BOM, CRLF, hard-cap), GalleryService enumeration + metadata reads + partial-write guard, GalleryViewModel sort modes + watcher debounce, GalleryItemDetailViewModel actions, and CrashLogger smoke + concurrency.

## 📱 Supported Platforms

Windows 10 (build 17763 / 1809) and later, including Windows 11. Compiled against the Windows 11 22H2 SDK.

## 🤝 Feedback & contributions

This is a small side project — bug reports, feature requests, and pull requests are all very welcome. There's no formal process: open an [issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) if something's broken or missing, and I'll take a look. Please be kind and patient, but don't be shy.

If you'd like to contribute code:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Commit (`git commit -m 'Add your feature'`)
4. Push to the branch (`git push origin feature/your-feature`)
5. Open a Pull Request

## ⚠️ Known issues

Limitations that didn't block shipping but are worth knowing:

- **Error-path UX hasn't been exhaustively audited** — for failures like a bad/revoked token (401), a mid-generation network drop, or rate-limit / quota / 5xx responses from Replicate, the status message on the affected job card may be less actionable than it could be. Happy-path generation, explicit Cancel, and concurrent generations all work as expected.
- **GPT 1.5 `input_fidelity=high` with an input image** is not regression-tested in this build. The other GPT options (`quality`, `background`, `moderation`, `input_fidelity=low`) have been verified.
- **Job queue has no eviction policy** — finished job cards accumulate until the app is closed. Cosmetic for short sessions; a "Clear finished" control is a candidate for v1.x.
- **Gallery is read-only in 1.0** — delete / rename / search / multi-select are not yet implemented (you can still delete or rename in Explorer; the gallery picks up the change via `FileSystemWatcher` within ~1 s).
- **Pollinations `kontext` (image-to-image) isn't supported in this build** — the Pollinations route has no input-image plumbing yet, so the `kontext` model surfaces in the catalog but won't accept reference images. Use a Replicate Flux model for image-prompted edits today.

If you hit any of the above, please [open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) with the status text from `Pictures\ImageGenerator.MAUI\app.log` and what you were trying to generate — that's the fastest way these get tightened up.

## 📄 License

MIT — see the LICENSE file for details.

## 👥 Authors

- Silmas — Initial work

## 🙏 Acknowledgments

- .NET MAUI team for the framework
- Replicate and OpenAI for the image generation APIs
