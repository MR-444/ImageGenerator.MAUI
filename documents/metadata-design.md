# Image Metadata Design — why the recipe lives where it lives

*Written 2026-06-12, after re-deriving the original ~2025 research from scratch. This is the
record so it never has to be re-derived (or re-argued) again.*

## The decision

Every image the app saves carries its full generation recipe (prompt, seed, model, knobs) in
**the format's own native, spec-registered free-text field** — and deliberately **not** in any
AI-platform dialect.

| Format | Where | Mechanism |
|---|---|---|
| PNG | `Comment` textual chunk (iTXt) | `PngTextData("Comment", text, "en", "")` |
| JPG / WebP | EXIF `UserComment` (tag 0x9286) | `EncodedString` with the 8-byte `UNICODE\0` charset prefix required by EXIF 2.3 |

Implementation: `ImageGenerator.MAUI/Infrastructure/Services/ImageFileService.cs`.
Read-back: `GalleryService` parses the same fields for the in-app recipe display.

## Why this is correct at the container layer

- The PNG specification **registers** a fixed set of textual-chunk keywords: Title, Author,
  Description, Copyright, Creation Time, Software, Disclaimer, Warning, Source, **Comment**.
  Using `Comment` is canonical PNG, readable by `exiftool`, XnView, IrfanView, MediaInfo —
  anything that speaks PNG.
- EXIF `UserComment` is *the* designated free-text field in the EXIF spec. The 8-byte
  `UNICODE\0` prefix is mandatory for UTF-16 content; tools that skip it (or that stuff text
  into `ImageDescription` / Windows `XPComment` instead) hit encoding quirks. We write it
  correctly — Explorer Properties and IrfanView show the recipe as proper text.
- On save the app **clears all source metadata** (EXIF profile + PNG text chunks) before
  writing its own, so the prompt never appears twice in a viewer. Side effect, accepted:
  ComfyUI's `workflow`/`prompt` chunks are stripped from app-saved copies — the
  drag-PNG-into-ComfyUI trick only works with the server's own output files.

## Why NOT the AI-platform dialects (deliberate, not an oversight)

There is **no standard for AI generation metadata** — only incompatible de-facto dialects,
each invisible to the photography world's tooling:

- **Automatic1111**: tEXt chunk, key `parameters`, prompt + `Steps: …, CFG scale: …, Seed: …`
  line. This is what **CivitAI auto-parses** on upload.
- **ComfyUI**: tEXt chunks `prompt` + `workflow` (full graph JSON; enables drag-to-rebuild).
- **tensor.art**: tEXt chunk `generation_data` (own JSON, "TAM2").
- (The photography world had the right answer all along — XMP, ISO 16684, extensible
  namespaces — but the AI tools never adopted it, and at this point won't.)

**Decision (reaffirmed 2026-06-12): the app stays out of all of these.** Uploads to CivitAI
etc. are intentionally *not* auto-parseable — the recipe is for the author and for anyone
curious enough to inspect the file, not for platform scrapers. "Only guys who are smart
enough will read it."

If a specific image should ever cross-post to CivitAI *with* parseable metadata, the path is
an **external per-file conversion**, not an app feature: `documents/xpng.go` (friend's Go CLI)
rewrites PNG chunks into the A1111 `parameters` dialect incl. AutoV2 model hashes. Caveat: it
parses ComfyUI/tensor.art chunks, not this app's `Comment` format — it would need a small
added parser first.

## Quick reference

- Read the recipe of any app image: `exiftool <file>` → PNG: `Comment`; JPG/WebP: `User Comment`.
- The recipe text itself is the app's own compact format (prompt, seed, model id, parameters)
  — see `ImageFileService` for the exact composition.
- Don't "fix" this by adding a `parameters` chunk. It was considered and rejected. Twice.
