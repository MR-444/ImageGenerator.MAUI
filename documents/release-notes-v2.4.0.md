## New

- **Pick the model where you use it** — Idea to Prompt (writer and image observer), the Mutation Engine and the Structure Editor each have their own provider + model row: **Local (Ollama), Anthropic or OpenRouter**. Each tool remembers its own choice; picking a cheap model to test a mutation no longer changes the prompt writer
- **Anthropic and OpenRouter models are listed live** from your account instead of a fixed Sonnet/Opus choice. Switching a row to a provider with no list asks that provider automatically
- **OpenRouter as a text provider** for prompt writing, mutation and enrichment, not only for image observation
- **GPT Image 2.6 (sunburst)** added, with per-model settings: fields a model doesn't take (input fidelity, complexity) are hidden
- **Medium window layout** — the results pane stays visible on mid-sized windows

## Changes

- **Settings holds connections only**: server URLs, API keys, and the OpenRouter free-only switch. The model pickers moved to the tools (see above). The Ollama card has a **Check server** button
- **Free models only** now applies to every OpenRouter list, not just the image observer
- The structure editor's enrichment starts on **Local** and remembers its choice (it used to reopen on a paid Anthropic model every time)
- Cost estimates in dollars are gone: they were guesses. The rows now say whether a choice is free or billed, and how many calls an operation makes

## Fixes

- **Freeze when switching to OpenRouter** — loading hundreds of models no longer locks up the app
- **Model picks were overwritten** — refreshing the Ollama list could put an old pick back over the one just chosen in a tool, and turning on free-only cleared the observer's OpenRouter pick
- **Anthropic refusals** are reported as "Anthropic declined this request", with the reason, instead of a JSON error, and are not retried (and billed) again
- **Ollama**: a URL that isn't an Ollama server (for example an OpenAI-compatible `/v1` address) now says so, instead of looking like a server with no models
- **Replicate**: models this build can't drive are named on refresh and refused before any request is sent; Generate and AI mutation are disabled for them, instead of failing after paying. Rejected requests name the field instead of dumping the response
- **Pollinations**: model aliases resolve correctly, 401/402 errors say what's wrong, and custom sizes start at 1024×1024
- **Upscale metadata**: images no longer claim `UpscaleBy` when nothing was upscaled
- **Batches**: a ComfyUI batch no longer asks for a Replicate token, and the GPU is held for the whole batch and freed correctly afterwards
- **Saved settings**: the selected model is restored even when it's outside the active provider filter; vision model picks survive a restart
- **ComfyUI**: a slow workflow refresh can no longer overwrite a newer one's settings; the input-image card shows for LoadImage workflows
- **Input images** wrap onto a second row instead of disappearing off-screen
- **Gallery**: Windows grid layout fixed
- **Images**: all images sent to vision, LLM and upload services are now resized to fit each service's limits
- Status messages no longer clear each other or update off the UI thread

## Maintenance

- Upgraded to **.NET 11** (app, tests, CI), all NuGet packages updated; `actions/checkout` v6
- Anthropic API version **2026-10-21**
- `GeneratorViewModel` split further: app settings moved to their own coordinator (continuing)
- Publish script checks hardened; unused code removed

### Automation ids (UI test scripts)

- Removed: `OllamaModelPicker`, `OllamaVisionModelPicker`, `OllamaModelRefreshButton`, `OpenRouterVisionModelPicker`, `OpenRouterVisionModelRefreshButton`, `OpenRouterVisionStatusLabel`, `MutationModelTierPicker`, `IdeaPromptModelTierPicker`, `IdeaPromptOpenRouterVisionModelPicker`, `IdeaPromptOpenRouterVisionRefreshButton`, `IdeaPromptOpenRouterVisionStatusLabel`, `EditorEnrichTierPicker`, `EditorEnrichOllamaModelPicker`, `EditorEnrichOllamaRefreshButton`, `EditorEnrichOllamaStatusLabel`
- Added: `OllamaCheckServerButton`, `MutationModelProviderPicker`, `IdeaPromptWriterProviderPicker`, `EditorEnrichProviderPicker`, `EditorEnrichModelPicker`, `EditorEnrichModelRefreshButton`, `EditorEnrichModelStatusLabel`
