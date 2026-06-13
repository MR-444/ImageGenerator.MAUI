# 🎨 Image Generator MAUI

A Windows desktop image generation app built on .NET MAUI with three image providers — **Replicate** (Flux, Replicate-hosted OpenAI, Google, Ideogram), **Pollinations.ai** (Flux, Zimage, Qwen Image, plus any free image model their `/models` endpoint surfaces), and **ComfyUI** (your own local or LAN server — every API-format workflow export you drop into a folder becomes a selectable model). Configure each provider once on the Settings page, then route requests just by picking a model; saved images carry the full prompt and generation parameters as EXIF / PNG-Comment metadata so every output is reproducible. Optionally, a checkbox publishes each finished image straight to **CivitAI** — into a model's gallery, generation data included.

## 🌟 Features

- **Windows 10/11 desktop** (MAUI Windows target, `net10.0-windows10.0.22621.0`) — a self-contained single-file exe, no installer.
- **Three providers, one app** — **Replicate**, **Pollinations.ai**, and **ComfyUI** (your own local/LAN server). Each provider's token persists independently in OS secure storage; the active provider is inferred from the selected model, so picking a `pollinations/...` model just routes there. Extensible: adding a provider is one entry in the `TokenProviders` collection — no XAML changes. → [setup](#picking-a-provider)
  - **Models (Replicate)** — Flux 1.1 Pro / 1.1 Pro Ultra · Flux 2 family (`klein-4b`, `flex`, `pro`, `max`) · OpenAI `gpt-image-1.5` / `gpt-image-2` (`quality`, `background` incl. transparent PNG, `moderation`, `input_fidelity`) · Google `nano-banana-2` (15-value aspect enum, 1K/2K/4K, image input; `webp`→`jpg`) · Ideogram V4 `balanced` / `turbo` / `quality` (23 native sizes + `Auto`, structured-JSON prompt, copyright detection). New models surface via **Refresh Models** without recompiling.
  - **Pollinations.ai** — `flux`, `zimage`, `qwen-image` seeded offline, plus any free image model `/models` surfaces; works anonymously (rate-limited). → [details](#getting-a-pollinations-token-optional)
  - **ComfyUI (local / LAN)** — workflows-as-models: drop an API-format workflow JSON in a folder and it becomes a selectable model the app patches and streams live progress for. No token, no cloud. → [details](#generating-via-your-own-comfyui-server)
- **Desktop-grade layout** — settings cards on the left, a results pane on the right (the newest render as a large **Latest result** preview, the job queue below), and an always-visible action bar — nothing scrolls out of reach. **Ctrl+Enter generates** from anywhere on the page, including mid-typing. The window remembers its size and position.
- **Concurrent generation queue** — Generate while a previous job is still running; each click snapshots its parameters into an independent in-flight card with its own Cancel / Open / Show-in-folder / Use-as-input.
- **Batch from a textfile** — **Import prompts…** runs a `.txt` of prompts (separated by `---`, `#` lines ignored) sequentially against the current model; failures don't abort the queue. → [details](#running-a-batch-from-a-textfile)
- **In-app gallery** — a sortable tile grid with live `FileSystemWatcher` updates, multi-select, and a detail page with copyable metadata and one-click **Use as input**. Thumbnails come from the Windows shell cache, so a folder of multi-megabyte PNGs doesn't blow process memory. → [details](#browsing-past-images-gallery)
- **Post to CivitAI** — one checkbox publishes each finished image to [CivitAI](https://civitai.com), optionally into a specific model's gallery with the generation data attached. Fire-and-forget: a slow or failed post never touches the generation job. → [details](#posting-to-civitai-optional)
- **Dynamic model catalog** — **Refresh Models** queries Replicate's `text-to-image` collection (filtered to `black-forest-labs`, `openai`, `google`) and Pollinations' `/models` in parallel, merges with the seed entries (Ideogram V4 is pinned — Replicate's collection doesn't list it), and caches to `AppDataDirectory/model-catalog.json`.
- **Reproducible by default** — every saved file embeds the full prompt, generation parameters, actual pixel dimensions, and model-specific fields, so any output's recipe is recoverable months later. → [reading it back](#reading-back-the-embedded-metadata)
- **Persisted UI state** — last prompt, model, resolution, and window bounds are restored on next launch.
- API tokens via `SecureStorage` (Windows DPAPI), independent slots per provider · per-job cancellation with retry/backoff (Polly) · collision-safe filenames · a **diagnostics log** (`app.log` beside the images: startup, every failed generation with reason, caught exceptions; NLog rolls at 5 MB, keeps 5 archives) · MVVM via CommunityToolkit.Mvvm.

## 📸 Screenshots

![Main page — settings cards on the left, Latest result hero preview and generation queue on the right, always-visible action bar with Generate (Ctrl+Enter) at the bottom](<documents/MainPage_Screenshot 2026-06-12.png>)

![Ideogram structure editor — visual builder for the structured JSON prompt: description and style fields with example pickers, a layout grid for positioning text elements, and one-click Apply to prompt / Save JSON to file](<documents/Ideogram_structure_editor.png>)

![In-app gallery — tile grid with sort modes, live FileSystemWatcher updates, and a detail page with copyable metadata](<documents/Gallery_Screenshot 2026-05-04 193429.png>)

## 🖥️ Using the app (end users)

### Download

Grab `ImageGenerator.MAUI.exe` from the latest [release](https://github.com/MR-444/ImageGenerator.MAUI/releases). It's a self-contained single-file executable — no installer, no .NET runtime prerequisite. First launch unpacks the bundle once (a few seconds of apparent hang); subsequent launches are fast.

### Windows SmartScreen warning

The exe ships unsigned — buying a code-signing certificate is expensive for a hobby project. On first run, Windows SmartScreen will show **"Windows protected your PC"**. That's expected. Click **More info → Run anyway**. Reputation builds with downloads over time, so the warning will soften eventually.

If you'd rather not click through a SmartScreen warning, you can always [build from source](#-building-from-source).

### Picking a provider

The **Settings** button (top right) opens the configuration page. Its **API Tokens** card has a provider dropdown — switch between **Replicate**, **Pollinations**, **ComfyUI**, and **CivitAI** to edit each slot independently (ComfyUI needs a server base URL instead of a token; the CivitAI slot is for publishing finished images, not generating). All slots are stored separately and save as you type, so you can keep them all configured and just flip the selected model on the main page to route requests one way or the other.

### Getting a Replicate API token

The Replicate branch covers the Flux family, the OpenAI-hosted `gpt-image-1.5` / `gpt-image-2`, Google's `nano-banana-2`, and the Ideogram V4 family (`balanced` / `turbo` / `quality`).

1. Sign up at [replicate.com](https://replicate.com).
2. Go to **Account → API tokens** (or directly [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens)).
3. Create a token, copy it.
4. In **Settings**, with "Replicate" selected in the API Tokens picker, paste it into the **API Token** field. It's stored locally with Windows `SecureStorage` (DPAPI, per-user) — never leaves your machine except to call Replicate.

### Getting a Pollinations token (optional)

Pollinations works anonymously — pick a `pollinations/...` model in the picker, type a prompt, and hit Generate. The anonymous tier is rate-limited to 1 request every 15 seconds. With a free Seed-tier account that goes to 1 request every 5 seconds.

1. Sign up at [auth.pollinations.ai](https://auth.pollinations.ai).
2. Generate a token in your account dashboard.
3. In **Settings**, with "Pollinations" selected in the API Tokens picker, paste it into the field. Same DPAPI-backed secure storage as the Replicate slot, just keyed independently.

Note on reproducibility: per Pollinations' own spec, the `seed` parameter is honored only by `flux`, `zimage`, `seedream`, `klein`, `seedance`, `nova-reel`. Other models silently ignore it, so re-running the same prompt+seed pair won't produce the same image on `qwen-image` etc.

### Generating via your own ComfyUI server

The ComfyUI provider talks to a [ComfyUI](https://github.com/comfyanonymous/ComfyUI) instance you run yourself — on the same machine or anywhere on your LAN. No token, no cloud, no cost beyond your own GPU time.

**Server setup**

1. Run a reasonably current ComfyUI build. If the server is on another machine, start it with `--listen` so it binds to the LAN (default is localhost-only), and make sure the port (default `8188`) is open in its firewall.
2. In the app's **Settings** page, enter the server's base URL into the ComfyUI server field, e.g. `http://192.168.1.50:8188` (default is `http://127.0.0.1:8188`). On a plain LAN no auth is needed. If your server sits behind an authenticating reverse proxy, paste the full `Authorization` header value (scheme included, e.g. `Bearer eyJ…` or `Basic dXNlcjpwYXNz`) into the ComfyUI token field — it's sent verbatim on every HTTP request and the WebSocket connect. Either way, don't expose a bare ComfyUI port to the internet.

**Workflows as models**

The app never builds node graphs itself — you bring your own workflow. In ComfyUI, open your workflow and use **Workflow → Export (API)**, saving the JSON into `Pictures\ImageGenerator.MAUI\comfy-workflows\` (the app creates the folder on first run). Every file there becomes its own entry in the model picker under provider "ComfyUI" — the folder is re-scanned on launch and on **Refresh Models**. Normal saves (Ctrl+S / PNG-embedded workflows) are the UI format and can't be queued over the API; the app detects them and tells you to re-export.

At generation time the app patches the exported graph and submits it:

- **Prompt** — plain mode writes your prompt into the lowest-id `CLIPTextEncode` node with a literal text value. With **Structured JSON prompt** checked, the JSON goes into every `Ideogram4PromptBuilderKJ` node (kjnodes pack) and/or replaces any `CLIPTextEncode` literal that is itself a JSON object (a frozen caption from an Ideogram-style workflow).
- **Seeds** — every literal `seed` / `noise_seed` is re-rolled per run. ComfyUI's "randomize after generate" lives in the browser frontend, so API submissions would otherwise reproduce the identical image forever.
- **Aspect ratio + resolution** — if the workflow has a `ResolutionSelector` node, the app's aspect-ratio and megapixels pickers write into it; without one the workflow keeps its own resolution (silently).
- **Model** — a workflow with a baked `CheckpointLoaderSimple` (or exactly one literal `UNETLoader`) gets a model picker fed live from the server's `/object_info`; your pick is patched in, remembered per workflow. Multi-UNET graphs (deliberate pairings like Ideogram 4's dual model) keep their baked models and hide the picker.
- **Quality preset** — a workflow with exactly one `CustomCombo` node (like the sample's Quality / Default / Turbo / Ultra table) gets a preset picker; choice and slot index are patched together. The option values are your workflow's own strings — the app treats them as opaque.
- **`%date:...%` filename tokens** — expanded app-side. The ComfyUI server takes `filename_prefix` literally (token expansion is frontend-only), and the `:` in an unexpanded token is path-invalid on Windows servers.

So the minimum a workflow needs: a `CLIPTextEncode` with a literal text prompt and a `SaveImage` node. Everything else is optional.

**Sample workflow**

[`comfy-workflows/Ideogram4-Sample.json`](comfy-workflows/Ideogram4-Sample.json) is a ready-to-use Ideogram 4 text-to-image workflow using **stock ComfyUI nodes only** (no custom node packs). Copy it into `Pictures\ImageGenerator.MAUI\comfy-workflows\`, and it appears in the picker as "Ideogram4-Sample (ComfyUI)". It supports both plain prompts and the structured-JSON mode (including the in-app visual structure editor), aspect-ratio/megapixels selection (4 MP default with an aspect-preserving 3072 px long-side cap), the quality-preset picker (Quality default · Default · Turbo · Ultra), and dated output subfolders on the server. Your server needs the Ideogram 4 model files the graph references, in the usual ComfyUI model folders: `ideogram4_fp8_scaled.safetensors` + `ideogram4_unconditional_fp8_scaled.safetensors` (diffusion models), `qwen3vl_8b_fp8_scaled.safetensors` (text encoder), `flux2-vae.safetensors` (VAE) — the same files ComfyUI's built-in Ideogram 4 template uses.

Generations run on your GPU and can take minutes; the job card shows live per-step progress streamed over the server's WebSocket. Canceling in the app removes pending jobs from the server queue and interrupts the active render when it's the app's own job — no orphaned GPU work.

### Posting to CivitAI (optional)

Check **Post to CivitAI** in the output options and every finished image is published on your CivitAI profile the moment it's saved — one step, no manual upload or publish click. A free account is enough.

**Setup (once):**

1. On [civitai.com/user/account](https://civitai.com/user/account), create an API key. A **Full** key works; a scoped key needs at least **Media Write**.
2. In **Settings**, pick "CivitAI" in the API Tokens dropdown and paste the key (same per-slot DPAPI secure storage as the other providers).
3. Click **Test connection** on the CivitAI card — it should greet you by username.

**Per generation:**

- **Post to model gallery (optional)** — paste a CivitAI model page URL (it carries `modelVersionId=…`) or the bare version id, and the post is associated with that model, appearing in its gallery. The field is remembered across sessions; leave it empty for a plain profile post.
- **Include generation data** — attaches prompt, seed, and model as structured metadata so the post shows proper generation info on CivitAI. This travels in the API call only; the saved local file keeps the app's own metadata format untouched. (Note: CivitAI never parses metadata out of API-uploaded files — structured metadata is the only way generation data reaches a post.)
- The post title is derived from the prompt (for structured-JSON prompts, from its description field); the upload is the saved file, byte-identical.

Posting runs after the image is safely on disk and never blocks or fails the generation itself — the job card gets its own status line and an **Open post** button. The checkboxes reset to off on every launch, so nothing is ever published by accident. Under the hood this uses CivitAI's official [MCP server](https://mcp.civitai.com/) for the upload plus one direct API call for the post itself — plain HTTP either way, your key never goes anywhere but civitai.com.

### Costs

**Replicate**: roughly **$0.003 – $0.05** per generation depending on the model — Flux Pro is on the cheaper end; `gpt-image-1.5` at high quality and `nano-banana-2` at 4K land on the higher end. See [replicate.com/pricing](https://replicate.com/pricing) for current per-model rates. Pay-as-you-go — no monthly commitment.

**Pollinations**: the seeded models (`flux`, `zimage`, `qwen-image`) and every other model the catalog returns are **free** at both anonymous and Seed tiers. Paid-tier models (Pollinations marks them `paid_only` in `/models`) are filtered out and never appear in the picker.

### Where images are saved

Generated images land in `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames (timestamp + truncated prompt + seed). Each completed job card has its own **Show in folder** button that opens Explorer with that specific file highlighted; the bottom action bar also has an **Open output folder** shortcut and a **Gallery** button that opens the in-app browser.

### Browsing past images (gallery)

The **Gallery** button on the main page opens an in-app grid of every image in the output folder, sorted however you like (newest first by default). Click a tile to open the image in your default OS viewer; click **Show metadata** to open the detail page with a larger preview, the parsed metadata as selectable text, and one-click actions: **Copy metadata** to clipboard, **Use as input** (sends the image back to the generator as an input-image attachment), **Open in viewer**, **Show in folder**. The gallery uses the OS shell thumbnail cache, so opening a folder with many large PNGs is instant.

### Running a batch from a textfile

Pick a model and configure the parameters you want to apply to the batch (aspect ratio, output format, seed mode, and so on), then click **Import prompts…** in the bottom action bar. Choose a `.txt` file shaped like this:

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

The app embeds the full prompt and generation parameters into the saved file so you can recover the recipe months later. For **PNG** the metadata lives in the standard `Comment` text chunk; for **JPG/WebP** it's written as EXIF `UserComment`. (CivitAI posting doesn't rely on this — it sends the generation data as structured fields in the API call, because CivitAI ignores file metadata on API uploads.) The fastest way to inspect it is the in-app gallery's **Show metadata** button (also lets you copy it to the clipboard); externally, **[MediaInfo](https://mediaarea.net/en/MediaInfo)** (GUI + CLI, cross-platform, free) or `exiftool` work too. Besides the prompt and seed you'll see the actual pixel dimensions produced by the API and the model-specific options (GPT quality/background/moderation/input-fidelity, nano-banana resolution, Flux Ultra raw/image-prompt-strength, Ideogram resolution/JSON-mode/copyright-detection) so two people with the metadata can reproduce the same image.

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
│   ├── ViewModels/           # GeneratorViewModel, GalleryViewModel, GalleryItemDetailViewModel,
│   │                         # IdeogramStructureEditorViewModel
│   ├── Views/                # MainPage, SettingsPage, GalleryPage, GalleryItemDetailPage,
│   │                         # IdeogramStructureEditorPage (.xaml + .cs)
│   ├── Behaviors/            # NumericOnlyBehavior
│   └── Converters/           # ShellThumbnail / ShellPreview / StringToEnum / Inverse / NonEmptyString
├── Shared/Constants/         # ModelConstants, ValidationConstants, OutputPaths
├── Resources/                # Styles, Colors, Fonts, Images
├── Extensions/               # RefitServiceExtensions (serializer + resilience pipeline)
├── AppShell.xaml             # Shell + route registration (gallery, detail, ideogram-editor, settings)
└── MauiProgram.cs            # DI registration + app bootstrap
```

## 🧪 Testing

```bash
dotnet test
```

858 tests covering: model factory payload shapes, the ComfyUI provider (workflow patcher incl. seed re-roll / `%date%` expansion / structured-JSON targeting, the shipped sample workflow's contract, catalog folder scan, HTTP service flows), Replicate service HTTP flows (via Refit mocks), the `NullSkippingDictionaryConverter`, model catalog filtering and persistence (both Replicate + Pollinations branches), the Ideogram V4 payload (prompt vs `json_prompt` string, resolution omit-on-`Auto`, PNG-locked output, structured-JSON validation), image file naming + EXIF round-trip, the CivitAI posting pipeline (JSON-RPC envelope + Bearer auth, upload→create ordering, structured meta and model-gallery association, model-URL parsing, JSON-prompt title derivation, the local file staying byte-identical, failures never demoting a saved job), GeneratorViewModel commands and state machine (including batch order, partial failure, distinct seeds, Cancel-batch leaves the in-flight job alone, the latest-result hero tracking, and the tabbed token slots stay independent across providers), UI-state persistence (prompt debounce, per-family resolution keys, window bounds round-trip incl. malformed-value fallback), prompt batch parser (delimiter, comments, multi-line, BOM, CRLF, hard-cap), GalleryService enumeration + metadata reads + partial-write guard, GalleryViewModel sort modes + watcher debounce, GalleryItemDetailViewModel actions, and CrashLogger smoke + concurrency.

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
- **GPT 1.5 `input_fidelity=high` with an input image** is not regression-tested. The other GPT options (`quality`, `background`, `moderation`, `input_fidelity=low`) have been verified.
- **Job queue has no eviction policy** — finished job cards accumulate until the app is closed. Cosmetic for short sessions; a "Clear finished" control is on the [roadmap](#-roadmap--outlook).
- **Gallery has no in-app delete / rename / search yet** — multi-select is in (for CivitAI batch posting), but managing files still means Explorer (the gallery picks the change up via `FileSystemWatcher` within ~1 s). On the [roadmap](#-roadmap--outlook).
- **Pollinations `kontext` (image-to-image) isn't supported** — the Pollinations route has no input-image plumbing yet, so the `kontext` model surfaces in the catalog but won't accept reference images. Use a Replicate Flux model for image-prompted edits today.

If you hit any of the above, please [open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) with the status text from `Pictures\ImageGenerator.MAUI\app.log` and what you were trying to generate — that's the fastest way these get tightened up.

## 🔭 Roadmap / Outlook

This is a small side project, so treat these as directions under consideration rather than commitments or a dated plan — roughly in order of interest:

- **Remix from an image** — reload a saved image's embedded recipe (prompt, seed, model, parameters) straight back into the generator, closing the reproducibility loop the app is built around.
- **One-click upscale** — upscale a finished result through an existing provider (a Replicate upscaler, or a ComfyUI upscale workflow), reusing plumbing that's already there.
- **Saved parameter presets** — name and recall a model + parameters combo.
- **Clear finished jobs** — a queue-eviction control so completed cards don't pile up over a long session.
- **Configurable output folder** — choose where images are saved instead of the fixed `Pictures\ImageGenerator.MAUI\`.
- **Faster, byte-identical saves** — splice the metadata into the PNG instead of decoding and re-encoding it: quicker, and it keeps the API's exact bytes (so a CivitAI upload is truly byte-for-byte the original).

Got a feature request? [Open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) — see [Feedback & contributions](#-feedback--contributions).

## 📄 License

MIT — see the LICENSE file for details.

## 👥 Authors

- Silmas — Initial work

## 🙏 Acknowledgments

- .NET MAUI team for the framework
- Replicate and OpenAI for the image generation APIs
