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
| 1 | 41001 | AB | | | | |
| 2 | 41002 | BA | | | | |
| 3 | 41003 | AB | | | | |
| 4 | 41004 | BA | | | | |
| 5 | 41005 | AB | | | | |
| 6 | 41006 | BA | | | | |
| 7 | 41007 | AB | | | | |
| 8 | 41008 | BA | | | | |
| 9 | 41009 | AB | | | | |
| 10 | 41010 | BA | | | | |

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
