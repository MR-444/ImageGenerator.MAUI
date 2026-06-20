A polish release focused on **less scrolling**, **seeing which model is actually in use**, and a **ComfyUI + local-mutation GPU reliability fix**. Nothing changes about how images are generated.

## What changed

**Roomier pages with pinned actions**

- The **Mutate** page and the **Structure editor** were rebuilt on the same layout as the main screen: a fixed header, two independently-scrolling columns, and a **pinned action bar** so the primary button (Generate / Mutate / Apply) and the status line never scroll out of view. Far less scrolling to reach the controls.
- On the main screen, the three prompt tools (*Describe an idea*, *Edit structure*, *Mutate current prompt*) are now a compact wrapping toolbar instead of three stacked rows.
- Verbose helper text throughout both pages was condensed.

**See which model produced an image**

- Generation cards now show the **actual ComfyUI model** — the selected checkpoint / diffusion model, including the workflow's built-in default — plus a non-default quality preset, instead of just the opaque workflow filename.
- In the Structure editor's **Enrich** action, choosing the **Local** tier now names the **Ollama model** it will use, right beside the picker.

**ComfyUI and local mutation no longer fight over the GPU**

- When ComfyUI and the local (Ollama) mutation tier run on the **same machine**, they share one GPU. Starting an AI mutation during a render used to co-load both and could stall the mutation until a 5-minute timeout. A new **GPU gate** serializes the two, so a render finishes before a same-host local mutation begins. Cloud mutation tiers (Sonnet / Opus) and a remote ComfyUI server are unaffected.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1236 tests green.*
