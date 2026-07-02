# 🔥 Emberforge

> **Why "Emberforge"?** It started as a plain image generator and grew into a *forge* — a desktop workbench where you don't just press "generate", you heat raw ideas into shape. It melts together many engines (cloud APIs **and** your own local ComfyUI/Ollama rig), hammers prompts into structured, spatially-aware Ideogram V4 captions, and lets variants *evolve* through a mutation/breeding engine. *Forge* for the craft and heat; *ember* nods to the home GPU box ("fireEngine") that does the local rendering. *(The repo and project keep their original `ImageGenerator.MAUI` identity; only the app name, the shipped binary — `Emberforge.exe` — and the icon are Emberforge.)*

A Windows desktop (.NET MAUI) image-generation workbench that routes one prompt to many backends — **Replicate** (Flux, Replicate-hosted OpenAI, Google, Ideogram V4), **Pollinations.ai** (Flux, Zimage, Qwen, plus anything their `/models` lists), and **ComfyUI** (your own local/LAN server, any API-format workflow becomes a model) — and adds an LLM-assisted prompt pipeline on top.

## What makes it interesting

- **Three providers, one app.** Pick a model and the request routes itself; each provider's token lives in its own OS-secure slot. New Replicate/Pollinations models appear via **Refresh Models** without recompiling.
- **Your own ComfyUI as a first-class backend.** Drop an API-format workflow JSON in a folder and it becomes a selectable model. The app patches prompt / seed / resolution / checkpoint / quality-preset into the graph and streams live per-step progress over the server WebSocket. No cloud, no cost beyond your GPU. → [details](#your-own-comfyui-server)
- **Describe an idea → prompt (Claude or Ollama).** Type a plain idea; **Claude Opus/Sonnet** or a **local Ollama** model writes a polished prose prompt that works with any model, and optionally maps it to a schema-valid **Ideogram V4 structured caption**. → [details](#describe-an-idea-claude-or-ollama)
- **Visual structured-prompt editor.** Build Ideogram V4's `json_prompt` on a canvas — drag/resize element boxes on the 0–1000 grid, set style/palette, and **Enrich from layout**: a geometry-grounded LLM rewrites each element's description to reflect its real spatial relationships (what it rests on, sits beside, or is behind). → [details](#mutation--enrichment)
- **Mutation & breeding engine.** Turn any structured caption into a batch of one-change variants along a **LOOK** (style) or **SCENE** (composition) axis — fully deterministic, seeded, offline, no key. Or switch on **AI mode** to steer mutations and breed from your favourites via Claude (Sonnet/Opus) or a **local Ollama** model. → [details](#mutation--enrichment)
- **Post to CivitAI.** One checkbox publishes a finished image — optionally into a specific model's gallery with structured generation data attached. → [details](#posting-to-civitai)

**Everything else, briefly:** a self-contained single-file exe (no installer); a concurrent generation queue with per-job cancel; batch a `.txt` of prompts sequentially; an in-app gallery (sortable, live-watched, multi-select, copyable metadata, *use as input*, *remix*); every saved file embeds its full prompt + parameters (PNG `Comment` / EXIF `UserComment`) so any output is reproducible; per-provider tokens in Windows `SecureStorage` (DPAPI); persisted prompt/model/resolution/window bounds; and a diagnostics log at `%LOCALAPPDATA%\Emberforge\app.log` (NLog, rolls at 5 MB).

## 📸 Screenshots

![Main page — settings cards left, latest-result hero and generation queue right](<documents/MainPage_Screenshot 2026-06-12.png>)

![Ideogram structure editor — visual builder for the structured JSON prompt with a layout grid for element placement](<documents/Ideogram_structure_editor.png>)

## 🖥️ Getting started

**Download.** Grab `Emberforge.exe` from the latest [release](https://github.com/MR-444/ImageGenerator.MAUI/releases) — self-contained, no installer or runtime prerequisite. It ships unsigned, so on first run Windows SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**. (Or [build from source](#-building-from-source).)

**Tokens & providers.** The **Settings** page has one API-Tokens picker with an independent, DPAPI-secured slot per provider — set whichever you use and switch models freely on the main page:

| Provider | What it's for | Where to get it |
|---|---|---|
| **Replicate** | Flux · OpenAI `gpt-image` · Google `nano-banana-2` · Ideogram V4 | [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens) |
| **Pollinations** | `flux` / `zimage` / `qwen-image` etc. — works anonymously (rate-limited); a token raises the limit | [auth.pollinations.ai](https://auth.pollinations.ai) |
| **Anthropic** | Claude tiers for *Describe an idea* + AI mutation/enrichment (never image gen) | [console.anthropic.com](https://console.anthropic.com) |
| **CivitAI** | publishing finished images (Media-Write scope) | [civitai.com/user/account](https://civitai.com/user/account) |
| **ComfyUI** | a server base URL (and optional `Authorization` header) instead of a token | your own server |

> Reproducibility note: Pollinations honours `seed` only on `flux`, `zimage`, `seedream`, `klein`, `seedance`, `nova-reel`; other models ignore it.

### Your own ComfyUI server

The ComfyUI provider talks to a [ComfyUI](https://github.com/comfyanonymous/ComfyUI) instance you run (localhost or LAN — start it with `--listen` for LAN, and don't expose a bare port to the internet). The app never builds graphs; you bring your own. In ComfyUI use **Workflow → Export (API)** and save the JSON into your data folder's `comfy-workflows\` subfolder (default `Pictures\Emberforge\comfy-workflows\`, or `<output folder>\comfy-workflows\` when you've set one in Settings) — every file there becomes a model entry, re-scanned on launch / Refresh Models. (Normal Ctrl+S saves are the UI format and can't be queued; the app tells you to re-export.)

At submit time the app patches the graph: your **prompt** into the lowest-id `CLIPTextEncode` (or, in structured mode, into `Ideogram4PromptBuilderKJ` / a JSON-literal `CLIPTextEncode`); re-rolls every literal `seed` / `noise_seed`; writes **aspect-ratio + megapixels** into a `ResolutionSelector` if present; offers a **checkpoint** picker for a baked `CheckpointLoaderSimple` or single literal `UNETLoader` (multi-UNET pairings keep their models); offers a **quality-preset** picker for a single `CustomCombo`; and expands `%date:…%` filename tokens app-side. Minimum a workflow needs: a `CLIPTextEncode` with literal text and a `SaveImage`.

[`comfy-workflows/Ideogram4-Sample.json`](comfy-workflows/Ideogram4-Sample.json) is a ready-to-use Ideogram 4 workflow using **stock ComfyUI nodes only**; it needs the usual Ideogram 4 model files on the server (`ideogram4_fp8_scaled` + `ideogram4_unconditional_fp8_scaled`, `qwen3vl_8b_fp8_scaled` text encoder, `flux2-vae`). Canceling in the app drains pending jobs and interrupts the active render when it's the app's own.

### Describe an idea (Claude or Ollama)

**Describe an idea…** runs two passes: Pass 1 always turns a plain-English idea into a polished **prose** prompt (good for any model); Pass 2 optionally maps that onto a schema-valid **Ideogram V4 JSON** caption. The result card lets you copy the prose, use it as the prompt, or use the JSON. Pick **Claude Opus**, **Claude Sonnet**, or **Local** (your configured Ollama server/model). Anthropic tiers are billed per pass; Local needs no token and quality depends on the installed model. Power users can override the bundled clean-room instructions with private `vpe-prompt.md` / `system-prompt.md` files in your data folder's `prompt-builder\` subfolder (default `Pictures\Emberforge\prompt-builder\`) (read fresh, never enter the repo — an open-core split).

### Mutation & enrichment

From any Ideogram V4 *structured* caption (hand-built, from *Describe an idea*, or remixed from a saved image):

- **Mutate** it into a batch of one-change variants — **LOOK** (style/ornament; 24 built-in style fragments) or **SCENE** (background, placement, element presence; aspect-ratio-aware bbox moves) — one axis per run so every difference is traceable. Deterministic, seeded, offline by default. Per-element **slot tags** decide what a mutation may touch; the subject's identity is always preserved.
- **AI mode** (optional, needs an Anthropic key or a local Ollama endpoint) lets you steer mutations in plain language and **breed** new captions from the variants you liked.
- **Enrich from layout** (in the structure editor) rewrites each element's description to read its spatial place in the scene. A deterministic pass computes the geometry (relative position, support/contact, background band, overlap) and the LLM turns it into natural relational prose — deciding *front/behind* from the geometry **and** each description's own wording, never from list order. You preview before/after per element and accept or discard.

Render a batch at one fixed seed (so only the change differs), pick winners in the gallery, and promote one with **Remix**.

### Posting to CivitAI

Check **Post to CivitAI** and a finished image is published the moment it's saved — optionally into a model's gallery (paste the model URL or version id) with prompt/seed/model attached as structured metadata (CivitAI ignores file metadata on API uploads, so this is how generation data reaches a post). It runs after the file is on disk and never blocks or fails the generation; the checkbox resets off each launch. Uses CivitAI's official [MCP server](https://mcp.civitai.com/) for the upload plus one direct API call for the post.

### Costs

**Replicate** is pay-as-you-go, roughly **$0.003–$0.05** per image by model ([pricing](https://replicate.com/pricing)). **Pollinations** free models stay free; `paid_only` models are filtered out of the picker. **Anthropic** applies only when you pick a Claude tier for the prompt builder / AI mutation, billed per call.

## 🚀 Building from source

Needs the **.NET 10 SDK** + the MAUI workload (VS 2022 17.12+ or Rider), Windows 10 1809+.

```bash
git clone https://github.com/MR-444/ImageGenerator.MAUI.git
```

Open `ImageGenerator.MAUI.sln`, restore, build, run. For the self-contained single-file release exe, run `pwsh ./publish.ps1` from the repo root. Tests: `dotnet test` (1219 tests — provider payloads, the ComfyUI patcher, catalog filtering/persistence, the V4 structured-prompt model + validator, the deterministic mutation operators and the RegionGraph geometry, the LLM seams via fakes, CivitAI posting, gallery + UI-state persistence).

## 🛠️ Stack

.NET MAUI 10 (Windows) · CommunityToolkit.Mvvm · Refit + `System.Text.Json` · Microsoft.Extensions.Http.Resilience (Polly) · SixLabors.ImageSharp 3 · raw `HttpClient` for the Anthropic/Ollama LLM calls · xUnit + Moq + FluentAssertions.

## ⚠️ Known issues

- Error-path UX (401s, mid-generation network drops, 5xx/quota) isn't exhaustively audited — happy path, Cancel, and concurrent jobs work as expected.
- Gallery has no in-app delete/rename/search yet (manage files in Explorer; the watcher picks changes up within ~1 s).
- Pollinations `kontext` (image-to-image) has no input-image plumbing — use a Replicate Flux model for image-prompted edits.

Hitting something? [Open an issue](https://github.com/MR-444/ImageGenerator.MAUI/issues) with the status text from `%LOCALAPPDATA%\Emberforge\app.log` (i.e. `C:\Users\<you>\AppData\Local\Emberforge\app.log`) and what you were generating.

## 🔭 Roadmap

Directions under consideration (a small side project, not commitments): one-click upscale (via a Replicate/ComfyUI upscaler) · saved parameter presets · finer per-element control in the enrichment preview.

## 📄 License

MIT — see [LICENSE](LICENSE). Initial work by **Silmas**. Thanks to the .NET MAUI team and the image-generation API providers.
