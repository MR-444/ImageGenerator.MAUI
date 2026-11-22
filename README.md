# Emberforge

A Windows desktop app (.NET MAUI) for generating images through several backends, with LLM-assisted prompt tools on top.

The repository and project are named `ImageGenerator.MAUI`; the app and the shipped binary are called Emberforge (`Emberforge.exe`).

![Main page](<documents/MainPage_Screenshot 2026-06-12.png>)

## Features

- **Image providers:** Replicate (Flux 1.1 Pro / Ultra, Flux 2, GPT Image, Nano Banana 2, Ideogram V4), Pollinations, and your own ComfyUI server. **Refresh Models** loads each provider's current model list.
- **ComfyUI workflows as models:** every API-format workflow in the workflows folder shows up as a model. See [ComfyUI](#comfyui).
- **Describe an idea:** turns a plain idea or a reference image into a prose prompt, and optionally into an Ideogram V4 structured (JSON) caption.
- **Structure editor:** a visual editor for Ideogram V4 structured captions. You place element boxes on a grid, and *Enrich from layout* rewrites element descriptions from their positions.
- **Mutation engine:** makes batches of one-change variants of a structured caption. The default mode is deterministic and offline. AI mode lets you steer mutations in plain language and breed new captions from favourites.
- **Gallery:** shows saved images with their metadata. From there you can *Use as input*, *Remix*, mutate, or post to CivitAI.
- **CivitAI posting:** publishes finished images, optionally into a model's gallery.
- **Reproducible output:** each saved image embeds its prompt and parameters (PNG `Comment` / EXIF `UserComment`).
- **Generation:** a queue with per-job cancel, and batch runs from a `.txt` file of prompts.

## Getting started

Download `Emberforge.exe` from the latest [release](https://github.com/MR-444/ImageGenerator.MAUI/releases). It is a self-contained single file and needs no installer or runtime. The exe is unsigned, so on first run Windows SmartScreen shows *"Windows protected your PC"*: choose **More info → Run anyway**.

On the first start of each new version, the exe unpacks its runtime (about 440 MB) to `%TEMP%\.net\Emberforge\`, so that start is slower. Folders left by older versions are not removed automatically; you can delete them while the app is closed.

### Settings

The Settings page holds connections only: API keys, server URLs, and a few switches. Each tool picks its own model on its own page. Keys are stored per provider in Windows secure storage.

| Connection | Used for | Where to get it |
|---|---|---|
| Replicate | Image generation | [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens) |
| Pollinations | Image generation (a key is required) | [auth.pollinations.ai](https://auth.pollinations.ai) |
| ComfyUI | Image generation on your own server: base URL, optional `Authorization` header | your server |
| Ollama | Local text and vision models for the prompt tools: base URL | your server |
| Anthropic | Claude models for the prompt tools (text only) | [console.anthropic.com](https://console.anthropic.com) |
| OpenRouter | Hosted text and vision models for the prompt tools; *Free models only* limits every OpenRouter list to free models | [openrouter.ai/keys](https://openrouter.ai/keys) |
| CivitAI | Posting images (needs the Media Write scope) | [civitai.com/user/account](https://civitai.com/user/account) |

Replicate, Pollinations, Anthropic and OpenRouter (except free models) bill per request. ComfyUI and Ollama run on your own hardware.

### Folders

- **Data folder:** `Pictures\Emberforge\` by default. You can change it under Settings → Output. It contains `pictures\`, `json-prompts\`, `comfy-workflows\`, `mutation-library\` and `prompt-builder\`.
- **Log:** `%LOCALAPPDATA%\Emberforge\app.log`. It rolls over at 5 MB and keeps 5 archives.

## ComfyUI

Emberforge talks to a [ComfyUI](https://github.com/comfyanonymous/ComfyUI) server that you run, either on localhost or on your LAN (start ComfyUI with `--listen`). Don't expose the port to the internet. The app doesn't build graphs; it uses workflows that you export.

1. In ComfyUI, use **Workflow → Export (API)**. A normal save produces the UI format, which the app rejects with a message.
2. Save the JSON in `<data folder>\comfy-workflows\`. Each file becomes a model, and the folder is rescanned on launch and on Refresh Models.

When a job is queued, the app patches a copy of the graph; the file on disk is never changed:

- **Prompt:** written into the lowest-id `CLIPTextEncode`. In structured mode it goes into `Ideogram4PromptBuilderKJ` or a JSON-literal `CLIPTextEncode` instead.
- **Seed:** every literal `seed` / `noise_seed` is re-rolled.
- **Resolution:** aspect ratio and megapixels go into a `ResolutionSelector`, if the workflow has one. The resolution is remembered per workflow.
- **Quality preset:** a picker appears for a single `CustomCombo`.
- **File names:** `%date:…%` tokens in file names are expanded by the app.
- **Shown, not editable:** the checkpoint or diffusion model baked into the workflow is displayed and recorded in the image metadata.

At minimum, a workflow needs a `CLIPTextEncode` with literal text and a `SaveImage`.

The app also handles a few special cases:

- **Image-to-image:** a workflow with a `LoadImage` node shows the Input Image card. The image is uploaded to the server and patched into every `LoadImage`.
- **Upscaling:** a `LoadImage` workflow whose file name contains *upscale* is the target of **Upscale after render**. The finished render is fed into it, and both files are kept (the second with an `_upscaled` suffix). If the workflow has a single literal `upscale_by`, a factor picker appears.
- **Krea-2 LoRAs:** a workflow whose `CLIPLoader` has `type: "krea2"` gets a LoRA picker and a strength slider. The list comes from the server's `/models/loras`, and the LoRA is injected as `LoraLoaderModelOnly`. The catalog lists every file in `models/loras` without compatibility labels, so pick only LoRAs trained for Krea-2.
- **SageAttention:** an optional Settings switch injects the KJNodes SageAttention patch into compatible model loaders. This requires KJNodes and the `sageattention` library on the server.
- **Free GPU memory after rendering:** an optional Settings switch that unloads ComfyUI models when idle, so Ollama has VRAM.

Sample workflows are in [`comfy-workflows/`](comfy-workflows/) (Ideogram 4, Krea-2, Moss-Image, upscale). Each one needs the model files it references on your server.

## Prompt tools

Describe an idea, the mutation engine's AI mode, and the structure editor's enrichment each have their own provider and model choice: Local (Ollama), Anthropic or OpenRouter. Image observation in Describe an idea uses Ollama or OpenRouter. Each tool remembers its choice.

**Describe an idea** runs up to three passes:

1. Observe a reference image (image mode only).
2. Write a prose prompt that works with any model.
3. Optionally, map the prose onto an Ideogram V4 structured caption.

You can replace the bundled instructions for each pass with your own files in `<data folder>\prompt-builder\`: `image-observation.md`, `vpe-prompt.md` and `system-prompt.md`. The files are read fresh on every run, and a `README.txt` in that folder explains them.

**Mutation engine** works on any Ideogram V4 structured caption:

- **Axes:** each run changes one axis. *LOOK* covers style and ornament; *SCENE* covers background, placement and element presence.
- **Deterministic:** the default mode is seeded, offline and needs no key. Per-element slot tags control what a mutation may change, and the subject's identity is kept.
- **AI mode:** steer mutations in plain language, or breed new captions from selected variants.
- **Testing variants:** render a batch at one fixed seed so only the change differs, then pick winners in the gallery and *Remix* one.

**Enrich from layout** (in the structure editor) works in two steps:

1. A deterministic pass computes each element's geometry: relative position, contact, background band and overlap.
2. The LLM rewrites each description to match.

You review the result before and after for each element, then accept or discard it.

![Structure editor](<documents/Ideogram_structure_editor.png>)

## CivitAI posting

With **Post to CivitAI** checked, each image is posted after it is saved, optionally into a model's gallery (paste the model URL or version id). The prompt, seed and model are attached as structured data, because CivitAI ignores file metadata on API uploads. Posting never blocks or fails the generation, and the checkbox is off on every launch. Gallery images can also be posted afterwards.

The upload goes through CivitAI's [MCP server](https://mcp.civitai.com/), plus one direct API call that creates the post.

## Building from source

Requirements: Windows 10 1809 or later, the .NET 11 SDK, and the MAUI Windows workload (`dotnet workload restore`).

ImageSharp 4 is a licensed build. Supply the key through the `SIXLABORS_LICENSE_KEY` environment variable / MSBuild property, or a local `sixlabors.lic` (git-ignored).

```bash
git clone https://github.com/MR-444/ImageGenerator.MAUI.git
cd ImageGenerator.MAUI
dotnet build ImageGenerator.MAUI.sln
dotnet test ImageGenerator.MAUI.sln
```

For the release exe, run `powershell -File ./publish.ps1`, or `pwsh ./publish.ps1` with PowerShell 7. It writes `ImageGenerator.MAUI/bin/Release/net11.0-windows10.0.22621.0/win-x64/publish/Emberforge.exe`.

Stack: .NET MAUI 11 (Windows), CommunityToolkit.Mvvm, Refit + `System.Text.Json`, Microsoft.Extensions.Http.Resilience, SixLabors.ImageSharp 4, NLog, plain `HttpClient` for Anthropic / Ollama / OpenRouter. Tests use xUnit, Moq and FluentAssertions.

## Known limitations

- The gallery has no delete, rename or search. Manage files in Explorer; the gallery picks up changes within about a second.
- Windows only; other platforms are not built or tested.

To report a problem, [open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues). Include the relevant lines from `%LOCALAPPDATA%\Emberforge\app.log` and what you were generating.

## License

MIT, see [LICENSE](LICENSE).
