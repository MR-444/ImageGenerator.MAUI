A feature release for the ComfyUI side of the app: your workflows can now take an **input image**, a finished render can be **chained straight into an upscale pass**, and Krea-2 workflows get a **LoRA picker** that reads your server's catalog.

## What's new

**ComfyUI upscaling & image-to-image**

- A workflow containing a `LoadImage` node is treated as an **image-to-image** workflow: the Input Image card appears for it, your image is uploaded to the server (`/upload/image`), and the stored name is patched into every `LoadImage`. **Use as input** on any gallery image feeds it in directly.
- When such a workflow's file name contains *upscale*, it also becomes the target of a new **Upscale after render** checkbox (ComfyUI models only). The finished render is fed into it automatically and **both files are kept** — the second one with an `_upscaled` suffix.
- **Selectable upscale factor.** When the upscale workflow has a single literal `upscale_by` (Ultimate SD Upscale), a factor picker appears — the workflow's own baked value plus 1.5× / 2× / 3× / 4× — and the choice is remembered per workflow. The JSON on disk is never modified.
- [`comfy-workflows/Upscale-Sample.json`](../comfy-workflows/Upscale-Sample.json) ships in the box: a tiled [Ultimate SD Upscale](https://github.com/ssitu/ComfyUI_UltimateSDUpscale) 2× pass (SDXL `zavychromaxl` + TTPlanet tile ControlNet + `4x_foolhardy_Remacri`).

**Krea-2 LoRA support**

- A workflow whose `CLIPLoader` has `type: "krea2"` now gets a **LoRA picker** in the Output card. It reads the configured host's native `GET /models/loras` catalog (subfolders included), has a **Refresh** button, and remembers the selection per workflow.
- A **LoRA strength** slider (−2 … 2) appears once a LoRA is picked.
- **None** is an explicit choice — picking it clears the LoRA and is persisted, so a workflow you deliberately run bare stays bare across launches.
- At queue time the app injects ComfyUI's built-in `LoraLoaderModelOnly`; your workflow file stays untouched. Only pick LoRAs trained for Krea-2 — the ComfyUI catalog lists every file in `models/loras` and carries no compatibility labels.

## Fixes

- **Reference images now decode on every vision backend.** A WebP dragged out of a browser used to fail *Describe an idea* with an opaque server-side error (Ollama's image loader reads only JPEG and PNG). Reference images are now sniffed by their actual bytes — never the file extension, since a `.jfif` is really JPEG and a browser's `.png` may really be WebP — passed through untouched when they're already JPEG/PNG, and transcoded to PNG when they're WebP, GIF, BMP, or TIFF. Genuinely undecodable formats (AVIF) now say so up front instead of failing at the backend.
- **No more stale ideas.** The idea box clears when you switch the source between text and reference image, so a previous idea can't quietly ride along with a new picture. The **Structured JSON prompt** checkbox now persists across launches instead of resetting.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1368 tests green.*

---

*Also, after three months in the queue: Emberforge is now listed on the [Pollinations app showcase](https://pollinations.ai/apps) — submission [#11432](https://github.com/pollinations/pollinations/issues/11432) was approved and merged on 2026-08-05.*
