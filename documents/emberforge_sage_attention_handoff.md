# Handoff: SageAttention for Emberforge's Ideogram-4 workflow

Concept transfer from the `ltxmv` project (2026-07-13). Self-contained — everything needed to
apply it lives in this document; nothing references ltxmv code at runtime.

## What it is, what it buys

SageAttention is a drop-in quantized attention kernel. Enabling it in a ComfyUI workflow is a
**pure speed lever** — output is perceptually identical (it is not a sampler, LoRA, or quality
setting). Measured on fireengine: an LTX video render went 2m07s → 1m34s (~26% faster); the same
patch on the Ideogram-4 t2i graph shortens still generation proportionally. It applies per
workflow run — no global ComfyUI setting, no side effects on workflows that don't opt in.

## Host prerequisites (fireengine: ALREADY MET)

- ComfyUI **KJNodes** custom-node pack (provides the patch node) — installed.
- The `sageattention` python library in ComfyUI's venv + `triton-windows` — installed on
  fireengine 2026-07-13. The patch node exists even without the library, but then **errors at
  run time naming the missing lib** — keep the feature opt-in so that error surfaces instead of
  breaking a default path.

## The recipe

Insert one **`PathchSageAttentionKJ`** node (KJNodes — note the *typo in the upstream class
name*: "Pathch", not "Patch"; the editor shows it as "Patch Sage Attention KJ") on the MODEL
edge of **each** diffusion-model loader, then repoint everything that consumed the loader's
MODEL output at the patch node instead:

```
UNETLoader ──MODEL──> PathchSageAttentionKJ(sage_attention="auto") ──MODEL──> (whatever consumed it)
```

Rules that matter:

1. **Only MODEL edges move** (output slot 0). A `CheckpointLoaderSimple` also emits CLIP (1) and
   VAE (2) — leave those edges untouched.
2. Place the patch **before any LoRA loader** in the chain, so the whole sampling stack runs
   through it.
3. **Ideogram-4 gotcha — TWO loaders.** The Ideogram-4 graph drives a `DualModelGuider` with a
   base+refiner model pair, i.e. two `UNETLoader` nodes. Patch **both** (one patch node each) or
   the speedup is partial and easy to misread as "sage doesn't help".
4. `sage_attention: "auto"` is the safe mode — it probes what the host's sageattention install
   supports. The full mode list, if you ever want to pin a kernel:
   `auto`, `sageattn_qk_int8_pv_fp16_cuda`, `sageattn_qk_int8_pv_fp16_triton`,
   `sageattn_qk_int8_pv_fp8_cuda`, `sageattn_qk_int8_pv_fp8_cuda++`, `sageattn3`,
   `sageattn3_per_block_mean`.

## Two ways to apply it in Emberforge

### A. In the ComfyUI editor (if Emberforge loads saved workflow files)

Open the Ideogram-4 workflow, add two "Patch Sage Attention KJ" nodes (KJNodes category), wire
each UNETLoader's MODEL through one, set `sage_attention` to `auto`, reconnect the downstream
MODEL inputs, save/re-export. Done — the change is baked into the workflow file.

### B. Programmatically at send time (what ltxmv does — keeps workflow files byte-identical)

If Emberforge posts API-format JSON to `/prompt`, patch the graph in code behind an opt-in flag.
Self-contained, stdlib-only (dict in, dict out; API-format graphs only — node-id keyed dicts,
inputs referencing `[node_id, slot]`):

```python
# Nodes that originate the diffusion MODEL (extend if your graphs use other loaders).
MODEL_LOADER_TYPES = ("CheckpointLoaderSimple", "UNETLoader", "UnetLoaderGGUF")


def inject_sage_attention(graph: dict, *, mode: str = "auto") -> dict:
    """Insert a PathchSageAttentionKJ node on every model loader's MODEL edge (before any
    LoRA) so the whole sampling stack runs SageAttention. Pure speed lever, ~same output.
    Mutates `graph` in place and returns it."""
    loaders = [nid for nid, n in graph.items()
               if n.get("class_type") in MODEL_LOADER_TYPES]
    if not loaders:
        raise SystemExit("inject_sage_attention: no model loader found.")
    for loader in loaders:
        nid = str(max(int(k) for k in graph) + 1)   # fresh node id
        graph[nid] = {"class_type": "PathchSageAttentionKJ", "inputs": {
            "model": [loader, 0], "sage_attention": mode,
        }}
        # Repoint every consumer of the loader's MODEL output (slot 0) at the patch node.
        # CLIP/VAE outputs of a checkpoint (slots 1/2) are untouched -- only MODEL edges move.
        for onid, node in graph.items():
            if onid == nid:
                continue
            for key, ref in node.get("inputs", {}).items():
                if isinstance(ref, list) and len(ref) == 2 and ref[0] == loader and ref[1] == 0:
                    node["inputs"][key] = [nid, 0]
    return graph
```

Note the loop over `loaders` patches every loader it finds — that is what makes the Ideogram-4
dual-loader graph work. (ltxmv additionally enforces a strict exactly-one-loader invariant for
its LTX graphs and requires an explicit opt-in for multi-loader graphs; for Emberforge the
simple patch-all-loaders version above is the right shape.)

## Verifying it took

Same seed, patched vs unpatched: the wall-clock drops and the image is perceptually identical.
If the `sageattention` lib is missing, the run fails loudly with an error naming it — that is
the intended failure mode for an opt-in flag, not something to catch and hide.
