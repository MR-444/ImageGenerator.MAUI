A focused structure-editor polish release: the Ideogram V4 builder is easier to use on both wide and narrower windows, and the Local enrichment model can now be changed in place.

## What changed

**Structure editor workspace**

- The editor now uses a **three-pane workspace**: left inspector, centered responsive canvas stage, and right JSON/output panel.
- The workspace adapts when the window gets tighter, so the inspector no longer crowds out the canvas/output area.
- Element actions wrap cleanly instead of letting the destructive **Delete selected** button overflow the card.
- The JSON output panel shows a live pretty-printed preview while preserving the existing compact JSON handoff to the generator.

**Local enrichment model picker**

- When **Enrich** is set to **Local**, the editor now shows the same **Local model (Ollama)** picker and **Refresh** button used on the Idea Prompt and Mutation pages.
- The picker writes to the shared Ollama model setting, so the chosen model is used by the next Local enrichment call.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1252 tests green.*
