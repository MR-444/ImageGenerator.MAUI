# 🎨 Image Generator MAUI

A Windows desktop image generation app built on .NET MAUI that drives the Replicate API. Generates images through the Flux family, Replicate-hosted OpenAI, and Google models, then saves them locally with the prompt and generation parameters embedded as EXIF metadata.

## 🌟 Features

- Windows 10/11 desktop (MAUI Windows target, `net10.0-windows10.0.22621.0`)
- **Concurrent generation queue** — click Generate while a previous job is still running; each click snapshots the current parameters into its own in-flight job. A scrollable queue below the form shows per-job prompt, status, spinner, thumbnail, and independent Cancel / Open / Show-in-folder / Use-as-input actions.
- **In-app gallery** — browse previously generated images without leaving the app. Tile grid with sortable order (newest/oldest, name A→Z / Z→A, largest/smallest), live updates via `FileSystemWatcher` when new images are saved or removed, and a detail page with a larger preview, copyable metadata, and one-click **Use as input**, **Open in viewer**, and **Show in folder** actions. Thumbnails come from the Windows shell cache so a directory of multi-megabyte PNGs doesn't blow process memory.
- **Persisted UI state** — the last prompt and selected model are restored on next launch (per-user `Preferences`). Pick up where you left off.
- **Dynamic model catalog** — "Refresh Models" queries Replicate's `text-to-image` collection (filtered to `black-forest-labs`, `openai`, and `google` owners) so new models surface without recompiling. The catalog is cached to `FileSystem.AppDataDirectory/model-catalog.json` and restored on launch.
- **Flux 2 family** — `flux-2-klein-4b`, `flux-2-flex`, `flux-2-pro`, `flux-2-max` with per-model payload shaping and optional `images` input
- **OpenAI** — `openai/gpt-image-1.5` and `openai/gpt-image-2` (via Replicate). The UI exposes the model-specific knobs: `quality`, `background` (incl. transparent PNGs), `moderation`, `input_fidelity`.
- **Google nano-banana-2** — `google/nano-banana-2` with its 15-value aspect enum, a resolution picker (1K / 2K / 4K), and image-input support. `webp` is auto-coerced to `jpg` (the model doesn't accept webp).
- **Flux classic** — 1.1 Pro / 1.1 Pro Ultra
- API tokens persisted via `SecureStorage` (Windows DPAPI under the hood)
- Per-job cancellation with retry/backoff (Polly via `Microsoft.Extensions.Http.Resilience`)
- Images saved to `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames
- Prompt + generation parameters embedded in the file — PNG `Comment` text chunk, or EXIF `UserComment` for JPG/WebP. Includes the actual pixel dimensions and model-specific fields (GPT options, resolution, etc.) so every image carries a complete reproducible recipe.
- **Diagnostics log** — `app.log` next to the saved images captures startup confirmation and any caught exception. Easy to find, no spelunking through `%LocalAppData%\Packages\…`.
- MVVM via CommunityToolkit.Mvvm (`[RelayCommand]`, `[ObservableProperty]`)

## 📸 Screenshots

![Initial state — three-column layout: API token + input images, prompt + output settings, run/result column](<documents/Initial_Screenshot 2026-04-19 162057.png>)

![After generation — result thumbnail with "Use as input for next generation" and "Show in folder" actions](<documents/Result_Screenshot 2026-04-19 162259.png>)

> Note: screenshots above are from v0.3.x. The current 1.0.0 build adds a concurrent queue, persisted prompt + model, and an in-app gallery (separate page, not visible in these shots). Refreshed screenshots will follow.

## 🖥️ Using the app (end users)

### Download

Grab `ImageGenerator.MAUI.exe` from the latest [release](https://github.com/MR-444/ImageGenerator.MAUI/releases). It's a self-contained single-file executable — no installer, no .NET runtime prerequisite. First launch unpacks the bundle once (a few seconds of apparent hang); subsequent launches are fast.

### Windows SmartScreen warning

The exe ships unsigned — buying a code-signing certificate is expensive for a hobby project. On first run, Windows SmartScreen will show **"Windows protected your PC"**. That's expected. Click **More info → Run anyway**. Reputation builds with downloads over time, so the warning will soften eventually.

If you'd rather not click through a SmartScreen warning, you can always [build from source](#-building-from-source).

### Getting a Replicate API token

The app calls Replicate under the hood — including the OpenAI-hosted `gpt-image-1.5` and Google's `nano-banana-2`, which are accessible through Replicate's catalog.

1. Sign up at [replicate.com](https://replicate.com).
2. Go to **Account → API tokens** (or directly [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens)).
3. Create a token, copy it.
4. Paste it into the **API Token** field in the app. It's stored locally with Windows `SecureStorage` (DPAPI, per-user) — never leaves your machine except to call the Replicate endpoint.

### Costs

Each generation costs roughly **$0.003 – $0.05** depending on the model. Flux Pro is on the cheaper end; `gpt-image-1.5` at high quality and `nano-banana-2` at 4K land on the higher end. See [replicate.com/pricing](https://replicate.com/pricing) for current per-model rates.

Replicate pay-as-you-go means no monthly commitment — you only get charged for successful calls.

### Where images are saved

Generated images land in `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames (timestamp + truncated prompt + seed). Each completed job card has its own **Show in folder** button that opens Explorer with that specific file highlighted; the Run card also has an **Open output folder** shortcut and a **Gallery** button that opens the in-app browser.

### Browsing past images (gallery)

The **Gallery** button on the main page opens an in-app grid of every image in the output folder, sorted however you like (newest first by default). Click a tile to open the image in your default OS viewer; click **Show metadata** to open the detail page with a larger preview, the parsed metadata as selectable text, and one-click actions: **Copy metadata** to clipboard, **Use as input** (sends the image back to the generator as an input-image attachment), **Open in viewer**, **Show in folder**. The gallery uses the OS shell thumbnail cache, so opening a folder with many large PNGs is instant.

### Reading back the embedded metadata

The app embeds the full prompt and generation parameters into the saved file so you can recover the recipe months later. For **PNG** the metadata lives in the standard `Comment` text chunk; for **JPG/WebP** it's written as EXIF `UserComment`. The fastest way to inspect it is the in-app gallery's **Show metadata** button (also lets you copy it to the clipboard); externally, **[MediaInfo](https://mediaarea.net/en/MediaInfo)** (GUI + CLI, cross-platform, free) or `exiftool` work too. Besides the prompt and seed you'll see the actual pixel dimensions produced by the API and the model-specific options (GPT quality/background/moderation/input-fidelity, nano-banana resolution, Flux Ultra raw/image-prompt-strength) so two people with the metadata can reproduce the same image.

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
│   ├── Application/          # IImageGenerationService, IModelCatalogService, IGalleryService
│   └── Domain/               # Entities (incl. GalleryItem), value objects, ModelCapabilities
├── Infrastructure/
│   ├── Diagnostics/          # CrashLogger (app.log + WinUI dispatcher hook)
│   ├── External/
│   │   ├── OpenAi/           # Refit client + DTOs + service
│   │   └── Replicate/        # Refit client + DTOs + service + image encoding helpers
│   └── Services/             # ImageFileService, ModelCatalogService, GalleryService,
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

229 tests covering: model factory payload shapes, Replicate service HTTP flows (via Refit mocks), the `NullSkippingDictionaryConverter`, model catalog filtering and persistence, image file naming + EXIF round-trip, GeneratorViewModel commands and state machine, GalleryService enumeration + metadata reads + partial-write guard, GalleryViewModel sort modes + watcher debounce, GalleryItemDetailViewModel actions, and CrashLogger smoke + concurrency.

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

If you hit any of the above, please [open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) with the status text from `Pictures\ImageGenerator.MAUI\app.log` and what you were trying to generate — that's the fastest way these get tightened up.

## 📄 License

MIT — see the LICENSE file for details.

## 👥 Authors

- Silmas — Initial work

## 🙏 Acknowledgments

- .NET MAUI team for the framework
- Replicate and OpenAI for the image generation APIs
