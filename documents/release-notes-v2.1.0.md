A feature release for the ComfyUI side of the app: renders on your own GPU get roughly **30% faster** with one Settings toggle, a Krea-2 sample workflow ships in the box, and the creative studio pages got a visual refresh.

## What's new

**SageAttention — faster local renders**

- A new **Use SageAttention** toggle in Settings injects KJ's attention patch into every ComfyUI submit at queue time. Your workflow files stay untouched — the patch is added to the graph that gets sent, in front of the model loader, so it lands before any LoRA or guider node.
- Measured on a 4090 over 10 same-seed pairs at 2 MP/20 steps: **median 30.8% less render time** (≈52.5 s → ≈36.4 s) with no visual quality loss. The full A/B protocol and results are in `documents/sage_attention_ab_test.md`.
- Requires the `sageattention` library on your ComfyUI host. If a workflow has no supported model loader, the app says so instead of silently skipping the patch.

**Krea-2 sample workflow**

- `comfy-workflows/Krea2-Sample.json` — a minimal plain-prompt text-to-image graph for Krea 2 Turbo (Qwen3-VL text encoder, Krea2T enhancer, ClownsharKSampler at 8 steps). Drop it in your workflows folder and it shows up as a model.
- Krea-2 is plain-prompt only: with **Structured JSON prompt** checked the app now explains why instead of feeding raw JSON to the text encoder (which renders it as literal text).

**A fresher studio**

- Refreshed layout, color tokens, and new icons across Create, Gallery, Settings, Describe an idea, the mutation engine, and the structure editor.
- The MainPage header is now a single **workspace summary** line — jobs, model, image count, and GPU state at a glance — with flash messages taking visual precedence when something happens. **About** moved to the flyout menu.

## Changed behavior

- **The ComfyUI model picker is now a read-only line.** A workflow file *is* the model choice, so swapping the checkpoint/diffusion model inside a tuned workflow only broke the settings built around it. The baked-in model is still shown (labeled *Checkpoint* or *Diffusion model*), and it's recorded in the image metadata — it just isn't editable. The quality-preset picker is unchanged.
- **Resolution is remembered per workflow.** Workflow models tolerate different resolutions, so one workflow's megapixel pick no longer follows you to the next. Your existing saved value carries over until you make a per-workflow choice.

## Fixes

- Pasting a reference image now prefers the clipboard's bitmap over a file path, so screenshots and copied image regions paste correctly.
- **Describe an idea** results reset when you swap the reference image, so old observations can't linger next to a new picture.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1307 tests green.*
