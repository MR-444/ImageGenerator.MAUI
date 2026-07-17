# SageAttention A/B benchmark

This benchmark compares Emberforge's ordinary ComfyUI submission path with the runtime
`PathchSageAttentionKJ` injection. It does not edit the workflow file.

## Variants and pass criteria

- **A — baseline:** Settings > ComfyUI server > Use SageAttention is off.
- **B — SageAttention:** the setting is on; the injected nodes use `sage_attention: "auto"`.
- Use `Ideogram_wf_san_v2` on fireengine. Every B log line must report
  `SageApplied=True CoveredLoaderCount=2`.
- Pass when B is at least 15% faster by the median paired reduction, no B run has a
  Sage-specific failure, and no paired image has a visible regression.

The app-wide default remains off even after a passing fireengine result, because other hosts
may not have KJNodes, `sageattention`, or `triton-windows` installed.

## Controlled setup

1. Confirm fireengine is otherwise idle and the ComfyUI queue is empty.
2. Select `comfyui/Ideogram_wf_san_v2` and keep prompt, aspect ratio, megapixels, preset,
   base/unconditional models, and every other generation option unchanged throughout.
3. Turn off **Free GPU memory after rendering** so model unload/reload time does not dominate.
4. Turn off random seed generation. Use one distinct predetermined seed for each measured
   pair, but use the same seed for A and B inside that pair. Distinct seeds prevent ComfyUI's
   execution cache from turning later repetitions into cache hits.
5. Run one unmeasured warm-up for A and one for B, using seeds not present in the table below.
   Wait for each job to finish before starting the next.

## Measured runs

Run ten pairs sequentially. Alternate order to reduce heat and order bias:

| Pair | Seed | Order | A ms | B ms | Reduction % | Visual pass |
|---:|---:|:---:|---:|---:|---:|:---:|
| 1 | 41001 | AB | 52569 | 36425 | 30.7 | |
| 2 | 41002 | BA | 52559 | 36341 | 30.9 | |
| 3 | 41003 | AB | 52487 | 36403 | 30.6 | |
| 4 | 41004 | BA | 52502 | 36438 | 30.6 | |
| 5 | 41005 | AB | 52449 | 36298 | 30.8 | |
| 6 | 41006 | BA | 52499 | 36348 | 30.8 | |
| 7 | 41007 | AB | 52478 | 36381 | 30.8 | |
| 8 | 41008 | BA | 52490 | 36371 | 30.7 | |
| 9 | 41009 | AB | 52478 | 36381 | 30.7 | |
| 10 | 41010 | BA | 52653 | 36309 | 31.0 | |

### Result (executed 2026-07-17)

**Median paired reduction: 30.8% — PASS** (threshold 15%). No B run had a Sage-specific
failure; every B graph carried `PathchSageAttentionKJ` on both UNETLoaders. Visual pass
column awaits the user's side-by-side review of the downloaded pairs — all 10 same-seed
pairs plus raw timings are archived at `D:\mr\Emberforge\sage-ab-benchmark\`.

Executed via direct `/prompt` submission of the exact graph Emberforge's
`ComfyUiWorkflowPatcher` produces, with three deviations from the protocol above:

- **2 MP / 20 steps** instead of the baked 4 MP / Quality (48 steps), applied identically
  to A and B (user's call — the paired reduction stays valid; absolute times are ~4× lower
  than production Quality renders). SageAttention's benefit grows with sequence length, so
  4 MP reductions are likely at least this large.
- **Variant A carried the patch node with `sage_attention: "disabled"`** rather than no node
  at all: the KJ node mutates ComfyUI's cached model object outside the model-patching
  system, so after any B run a node-absent baseline would silently still run saged.
  `"disabled"` actively reverts it. (Protocol gap worth keeping in mind for reruns.)
- Static `filename_prefix` — ComfyUI does not expand `%date:...%` tokens server-side; the
  app normally expands them client-side in `PatchFilenamePrefixDates`.

For each run, copy `QueueToCompleteMs` from the `ComfyUI benchmark` entry in
`%LOCALAPPDATA%\Emberforge\app.log`. This duration begins immediately before `POST /prompt`
and ends when terminal `/history` state arrives; image download is excluded. Discard and rerun
a pair if an unrelated server job was queued concurrently.

Calculate each pair as:

```text
reduction % = (A_ms - B_ms) / A_ms * 100
```

Use the median of the ten paired reductions as the primary performance result.

## Image review and decision

Compare every same-seed A/B image side-by-side at fit-to-window and zoomed views. Check text,
fine edges, repeated patterns, faces/hands when present, and smooth gradients. Pixel identity is
not required; mark failure only for a visible quality regression.

If all pass criteria hold, leave **Use SageAttention** enabled in fireengine's Emberforge
preferences. Otherwise leave it off and retain the run table and relevant log lines for diagnosis.
