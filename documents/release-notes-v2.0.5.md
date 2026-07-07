A small patch release: no new features, but several real bugs and rough edges in **Describe an idea**, the Ollama model pickers, and a few workflow pages are fixed. Image generation behavior is unchanged.

## What changed

**Describe an idea**

- **Stale results are gone.** Results now clear the instant you press **Build prompt**, so a new run starts from a clean slate and old prose/observation text never lingers while a build runs — or when a build errors or is cancelled before producing output. (The read-only editors were also switched off Windows' unreliable auto-size repaint.)
- **Result actions moved up.** *Copy prose*, *Use prose as prompt*, and *Use JSON prompt* now sit in the pinned action bar next to **Build prompt**, reachable without scrolling to the bottom of the results pane.
- **The prompt writer is remembered.** Your Local/Sonnet/Opus choice now survives an app restart instead of resetting to the placeholder each launch.
- The Text/Image source picker is now a segmented toggle, the results pane has an empty state, and the "build JSON" checkbox sits next to the prompt writer.

**Ollama model pickers**

- **gemma vision models now show up.** Ollama's `/api/tags` under-reports the `vision` capability for gemma3/gemma4, so those models were silently missing from the vision-model picker. Refresh now confirms vision via `/api/show`, so any genuinely vision-capable model appears.
- Hardened model selection and image drops, and improved diagnostics when Ollama returns empty content.

**Workflow page polish**

- Gallery image detail: the preview gets its own bounded row so it fits between the header and action bar, and the metadata card is visible without scrolling.
- Structure editor: proportional inspector/output columns, fixed tall scrollable fields, and collapsible Description/Style/Elements sections.
- Shared chrome: consolidated status-color styling and small layout/token cleanups.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1298 tests green.*
