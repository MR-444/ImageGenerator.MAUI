# 🎨 Image Generator MAUI

A Windows desktop image generation app built on .NET MAUI that drives the Replicate API. Generates images through the Flux family, Replicate-hosted OpenAI, and Google models, then saves them locally with the prompt and generation parameters embedded as EXIF metadata.

## 🌟 Features

- Windows 10/11 desktop (MAUI Windows target, `net10.0-windows10.0.22621.0`)
- **Dynamic model catalog** — "Refresh Models" queries Replicate's `text-to-image` collection (filtered to `black-forest-labs`, `openai`, and `google` owners) so new models surface without recompiling. The catalog is cached to `FileSystem.AppDataDirectory/model-catalog.json` and restored on launch.
- **Flux 2 family** — `flux-2-klein-4b`, `flux-2-flex`, `flux-2-pro`, `flux-2-max` with per-model payload shaping and optional `images` input
- **OpenAI** — `openai/gpt-image-1.5` (via Replicate). The UI exposes the 1.5-specific knobs: `quality`, `background` (incl. transparent PNGs), `moderation`, `input_fidelity`.
- **Google nano-banana-2** — `google/nano-banana-2` with its 15-value aspect enum, a resolution picker (1K / 2K / 4K), and image-input support. `webp` is auto-coerced to `jpg` (the model doesn't accept webp).
- **Flux classic** — 1.1 Pro / 1.1 Pro Ultra
- API tokens persisted via `SecureStorage` (Windows DPAPI under the hood)
- Cancellable generation with retry/backoff (Polly via `Microsoft.Extensions.Http.Resilience`)
- Images saved to `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames
- Prompt and parameters embedded as EXIF `UserComment`
- MVVM via CommunityToolkit.Mvvm (`[RelayCommand]`, `[ObservableProperty]`)

## 📸 Screenshots

![Initial state — three-column layout: API token + input images, prompt + output settings, run/result column](<documents/Initial_Screenshot 2026-04-19 162057.png>)

![After generation — result thumbnail with "Use as input for next generation" and "Show in folder" actions](<documents/Result_Screenshot 2026-04-19 162259.png>)

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

Generated images land in `%USERPROFILE%\Pictures\ImageGenerator.MAUI\` with collision-safe filenames (timestamp + model). The **Show in folder** button on the result card opens Explorer at that location with the new file highlighted.

### Reading back the embedded metadata

The app embeds the full prompt and generation parameters as EXIF `UserComment` so you can recover the recipe months later. To inspect the metadata on a saved image, the recommended tool is **[MediaInfo](https://mediaarea.net/en/MediaInfo)** — cross-platform, free, with both GUI and CLI. Load any generated image and the `UserComment` field shows the JSON-encoded parameters.

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
│   ├── Application/          # IImageGenerationService, IModelCatalogService, factory
│   └── Domain/               # Entities, value objects (Flux model payloads, ModelCapabilities)
├── Infrastructure/
│   ├── External/
│   │   ├── OpenAi/           # Refit client + DTOs + service
│   │   └── Replicate/        # Refit client + DTOs + service + image encoding helpers
│   └── Services/             # ImageFileService, ModelCatalogService (fetch + disk cache)
├── Presentation/
│   ├── ViewModels/           # GeneratorViewModel, ModelCapabilities
│   ├── Views/                # MainPage.xaml
│   ├── Behaviors/            # NumericOnlyBehavior
│   └── Converters/           # StringToBool / Inverse / StringToEnum
├── Shared/Constants/         # ModelConstants, ValidationConstants
├── Resources/                # Styles, Colors, Fonts, Images
├── Extensions/               # RefitServiceExtensions (serializer + resilience pipeline)
└── MauiProgram.cs            # DI registration + app bootstrap
```

## 🧪 Testing

```bash
dotnet test
```

Covers the model factory payload shapes, Replicate + OpenAI service HTTP flows (via Refit mocks), the `NullSkippingDictionaryConverter`, model catalog filtering, image file naming + EXIF, and the GeneratorViewModel commands.

## 📱 Supported Platforms

Windows 10 (build 17763 / 1809) and later, including Windows 11. Compiled against the Windows 11 22H2 SDK.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Commit (`git commit -m 'Add your feature'`)
4. Push to the branch (`git push origin feature/your-feature`)
5. Open a Pull Request

## 📄 License

MIT — see the LICENSE file for details.

## 👥 Authors

- Silmas — Initial work

## 🙏 Acknowledgments

- .NET MAUI team for the framework
- Replicate and OpenAI for the image generation APIs
