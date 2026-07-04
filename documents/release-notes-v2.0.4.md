A small workflow polish release for the prompt builder and page layout. Describe an idea now works across providers, reference images can feed the prompt pass, and the app chrome is more consistent from page to page. Image generation behavior is unchanged.

## What changed

**Prompt builder everywhere**

- **Describe an idea** is now available for every provider, not only structured-prompt models.
- The builder can start from typed notes or a **reference image**. Image mode observes the picture first, then turns that observation into a normal provider-friendly prompt.
- The page was rebuilt with a fixed header, two-pane workspace, constrained controls, and a pinned **Build prompt** / status bar. It also collapses to a stacked layout on narrow windows.

**Shared page layout polish**

- Pushed pages now use the app's own visible **Back** buttons and hide duplicate Shell navigation chrome.
- Workflow pages keep their primary actions and status lines pinned near the bottom: prompt building, structure apply/save/mutate, mutation runs, and gallery detail actions stay reachable while scrolling.
- Settings, Gallery, Detail, Mutation, and Structure Editor controls were tightened so short pickers/fields stay compact while editors, previews, canvases, and galleries get the room.

**Maintenance**

- Safe package updates were applied without changing generation behavior.

## Download

Grab `Emberforge.exe` below — self-contained single-file, no installer or runtime prerequisite. It ships unsigned, so SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.

*1265 tests green.*
