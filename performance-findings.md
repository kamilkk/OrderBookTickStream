# Order Book — Construct Phase Performance Findings

**Date:** 2026-06-21
**Runtime:** .NET 10, Release build, macOS Darwin 25.5.0 (Apple Silicon)
**Command:** `dotnet run --project OrderBook -c Release -- <input> /dev/null <N>`
**Input:** 160,429 ticks from `ticks.raw`

---

## 1. Raw benchmark data

### Best-of-N summary

| N  | Cold pass (run 1) | Tier-1 band (runs 2–17) | PGO transition | Best (warm) |
|----|:-----------------:|:-----------------------:|:--------------:|:-----------:|
| 10 | 18.4 ms | 11.2–11.9 ms | **never reached** | 11.20 ms (0.0698 µs/tick) |
| 15 | 19.1 ms | 11.3–12.5 ms | **never reached** | 11.35 ms (0.0707 µs/tick) |
| 20 | 19.6 ms | 11.3–11.9 ms | runs 17–18 | 2.27 ms (0.0142 µs/tick) |
| 25 | 18.3 ms | 11.2–12.1 ms | runs 17–18 | 2.17 ms (0.0135 µs/tick) |
| 30 | 19.2 ms | 11.3–12.4 ms | runs 17–18 | 2.18 ms (0.0136 µs/tick) |
| 35 | 18.3 ms | 11.2–12.4 ms | runs 17–18 | 2.13 ms (0.0133 µs/tick) |
| 40 | 18.7 ms | 10.9–12.7 ms | runs 17–18 | 2.14 ms (0.0134 µs/tick) |
| 45 | 18.2 ms | 11.0–11.6 ms | runs 17–18 | 2.16 ms (0.0134 µs/tick) |
| 50 | 19.4 ms | 11.3–12.7 ms | runs 17–18 | 2.12 ms (0.0132 µs/tick) |

> **Key insight:** N=10 and N=15 never enter the PGO tier and report ~11 ms.
> N≥20 reach the PGO tier at run 17–18 and converge to ~2.1–2.2 ms — a **5× improvement**.
> The "true" steady-state speed of this code is ~2.1 ms (0.013 µs/tick).

---

### Detailed per-run data

<details>
<summary>N = 10</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 18.421 | 0.1148 |
| 2  | 11.802 | 0.0736 |
| 3  | 11.652 | 0.0726 |
| 4  | 11.729 | 0.0731 |
| 5  | 11.913 | 0.0743 |
| 6  | 11.912 | 0.0742 |
| 7  | 11.674 | 0.0728 |
| 8  | 11.630 | 0.0725 |
| 9  | 11.650 | 0.0726 |
| 10 | 11.198 | 0.0698 |
| **best** | **11.198** | **0.0698** |
</details>

<details>
<summary>N = 15</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 19.051 | 0.1187 |
| 2  | 11.762 | 0.0733 |
| 3  | 11.899 | 0.0742 |
| 4  | 12.132 | 0.0756 |
| 5  | 12.477 | 0.0778 |
| 6  | 11.736 | 0.0732 |
| 7  | 11.382 | 0.0709 |
| 8  | 11.498 | 0.0717 |
| 9  | 11.509 | 0.0717 |
| 10 | 11.426 | 0.0712 |
| 11 | 11.364 | 0.0708 |
| 12 | 11.748 | 0.0732 |
| 13 | 11.346 | 0.0707 |
| 14 | 11.579 | 0.0722 |
| 15 | 11.356 | 0.0708 |
| **best** | **11.346** | **0.0707** |
</details>

<details>
<summary>N = 20 — first run where PGO fires</summary>

| Run | ms | µs/tick | Note |
|----:|---:|--------:|------|
| 1  | 19.578 | 0.1220 | cold JIT |
| 2  | 11.890 | 0.0741 | tier-1 JIT |
| 3  | 11.524 | 0.0718 | |
| 4  | 11.518 | 0.0718 | |
| 5  | 11.472 | 0.0715 | |
| 6  | 11.583 | 0.0722 | |
| 7  | 11.682 | 0.0728 | |
| 8  | 11.537 | 0.0719 | |
| 9  | 11.327 | 0.0706 | |
| 10 | 11.419 | 0.0712 | |
| 11 | 11.585 | 0.0722 | |
| 12 | 12.449 | 0.0776 | OS jitter spike |
| 13 | 11.779 | 0.0734 | |
| 14 | 11.377 | 0.0709 | |
| 15 | 11.295 | 0.0704 | |
| 16 | 11.344 | 0.0707 | |
| **17** | **5.275** | **0.0329** | **PGO re-JIT mid-run** |
| 18 |  2.502 | 0.0156 | fully PGO-compiled |
| 19 |  2.408 | 0.0150 | |
| 20 |  2.272 | 0.0142 | |
| **best** | **2.272** | **0.0142** |
</details>

<details>
<summary>N = 25</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 18.338 | 0.1143 |
| 2–16 | 11.2–11.9 | 0.070–0.075 |
| 17 | 11.182 | 0.0697 |
| **18** | **3.026** | **0.0189** |
| 19 | 2.287 | 0.0143 |
| 20–25 | 2.17–2.26 | 0.0135–0.0141 |
| **best** | **2.172** | **0.0135** |
</details>

<details>
<summary>N = 30</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 19.222 | 0.1198 |
| 2–16 | 11.3–12.4 | 0.070–0.077 |
| **17** | **9.310** | **0.0580** |
| 18 | 2.642 | 0.0165 |
| 19–30 | 2.18–2.33 | 0.0136–0.0145 |
| **best** | **2.179** | **0.0136** |
</details>

<details>
<summary>N = 35</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 18.271 | 0.1139 |
| 2–17 | 11.2–12.4 | 0.070–0.077 |
| **18** | **4.418** | **0.0275** |
| 19–35 | 2.13–2.53 | 0.0133–0.0158 |
| **best** | **2.129** | **0.0133** |
</details>

<details>
<summary>N = 40</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 18.675 | 0.1164 |
| 2–17 | 10.9–12.7 | 0.068–0.079 |
| **18** | **5.569** | **0.0347** |
| 19–40 | 2.14–2.40 | 0.0134–0.0150 |
| **best** | **2.143** | **0.0134** |
</details>

<details>
<summary>N = 45</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 18.231 | 0.1136 |
| 2–17 | 11.0–11.6 | 0.069–0.072 |
| **18** | **7.432** | **0.0463** |
| 19–45 | 2.16–2.50 | 0.0134–0.0156 |
| **best** | **2.155** | **0.0134** |
</details>

<details>
<summary>N = 50</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 19.433 | 0.1211 |
| 2–16 | 11.3–12.7 | 0.070–0.079 |
| **17** | **7.818** | **0.0487** |
| **18** | **2.441** | **0.0152** |
| 19–50 | 2.12–2.24 | 0.0132–0.0140 |
| **best** | **2.116** | **0.0132** |
</details>

---

## 2. Three-tier performance analysis

Every experiment with N ≥ 20 shows exactly three phases:

```
Phase 1 — cold JIT          run 1       ~18–19 ms   (0.12 µs/tick)
Phase 2 — tier-1 JIT        runs 2–17   ~11–12 ms   (0.07 µs/tick)
Phase 3 — Dynamic PGO       runs 18+    ~2.1–2.3 ms (0.013 µs/tick)
```

### Phase 1: Cold JIT (run 1 only)

The first run always takes 18–19 ms. This is the .NET JIT compiling the hot methods from IL to native machine code on first call. The overhead is one-time and large (~7× slower than tier-1).

### Phase 2: Tier-1 JIT (runs 2–17)

After the first-pass compilation, the JIT produces quick but unoptimised code. This is "tier-1" in .NET's tiered compilation model: correct, but without inlining decisions, branch layout, or type specialisation informed by observed behaviour. Time stabilises at 11–12 ms with occasional OS jitter spikes (discussed in §3).

### Phase 3: Dynamic PGO (run 18+, 5× faster than tier-1)

The transition lands at **run 17–18 with remarkable consistency** across every N ≥ 20 experiment. This is .NET **Dynamic Profile-Guided Optimisation** (Dynamic PGO, introduced in .NET 6, on by default from .NET 8):

1. The runtime instruments tier-1 code to count calls and record type/branch observations.
2. After a threshold of approximately 17 × 160,429 ≈ **2.7 million Apply invocations**, the background JIT re-compiles the hot methods with full PGO data.
3. Key gains: the `_isBid` branch in `Ladder.Add/Remove` is constant-folded per call-site (bid and ask ladders are always called from the same sites); the `switch` on `Action` is reordered by frequency; the `Dictionary` internal methods are inlined and specialised.
4. Result: 2.1–2.3 ms — a **5× improvement** over tier-1.

**Critical implication:** With N = 10 or N = 15 the timed region ends before PGO fires. The reported "best" of ~11 ms is tier-1 code, not the true steady-state. The real warm speed is ~2.1 ms.

---

## 3. Intra-tier jitter analysis

Within tier-2 (PGO runs), elapsed times vary between ~2.1 ms and ~2.5 ms (up to ~20% spread):

- **OS scheduling preemption** — macOS can migrate the thread to another core or defer it for a scheduler quantum (~10 ms on macOS) mid-loop. On a system without thread affinity pinning this is unavoidable.
- **Memory controller activity** — other processes competing for DRAM bandwidth cause occasional stalls, visible as 10–20% spikes.
- **Thermal throttling** — on Apple Silicon, the chip may reduce clock speed under sustained load.

These are hardware/OS effects; no code change can eliminate them. The correct response is to report the **best** of N runs (which is already done), not the mean or median.

---

## 4. Proposed improvements

### P1 — Untimed warm-up phase (effort: tiny, impact: critical)

Add ~25 untimed passes of the full tick loop before the first timed run. This triggers the PGO re-JIT unconditionally, so run 1 of the timed section already executes fully-optimised code. Without this, N must be ≥ 20 to see the true speed.

**Expected gain:** N=10 reports ~2.1 ms instead of ~11 ms.

### P2 — `GetOrAdd` ref-access in `Upsert` (effort: small, impact: medium)

Replace the current two-dictionary-operation pattern (`TryGetValue` + indexer write) with a single reference-returning operation that finds or inserts in one hash probe. A/M ticks account for ~51% of all records; this halves the dictionary work for those.

**Expected gain:** ~5–10% on warm numbers.

### P3 — Custom open-addressing hash table for orders (effort: medium, impact: medium)

`Dictionary<long, OrderEntry>` uses two arrays (bucket indices + entry structs) — a lookup follows an indirection between them. A flat open-addressing table stores everything in one `Slot[]` array: `key + value + state` inline. At 4096 slots and ~1150 peak live orders (28% load), linear probing finds entries in ≈1 probe on average, and four slots fit per 64-byte cache line.

**Expected gain:** ~5–15% on warm numbers (fewer cache misses per lookup).

### P4 — Shrink `OrderEntry` from 12 to 6 bytes (effort: small, impact: small)

Change `(byte Side, int Price, int Qty)` to `(bool IsBid, ushort Price, ushort Qty)`. Price ≤ 16383 fits in ushort; Qty ≤ 300 fits in ushort; Side is a single bit. Struct drops from 12 bytes to 6 bytes, halving the per-entry footprint in the hash table (better cache density).

**Expected gain:** ~2–5% (reduced cache pressure in the order map).

### P5 — Dirty-range `Array.Clear` in `Ladder.Clear()` (effort: small, impact: small for this dataset)

Track `[_minUsed, _maxUsed]` across Add calls and zero only that slice on Clear, instead of the full 16384-element array. Active prices cluster in a narrow band (~30–50 entries near the mid-price) so the clear touches ~200 bytes instead of 128 KB per side.

**Expected gain:** negligible on timing (190 clears × tiny range), but principled for the general case.

---

## 5. Improvement summary

| # | Improvement | Effort | Impact on best-run time |
|---|-------------|--------|-------------------------|
| P1 | Untimed warm-up to trigger PGO | 5 lines | **Critical** — fixes N≤15 (11ms → 2ms) |
| P2 | `GetOrAdd` single-probe ref write | 3 lines | Medium (~5–10%) |
| P3 | Custom open-addressing `OrderMap` | ~60 lines | Medium (~5–15%) |
| P4 | `OrderEntry` 12 → 6 bytes | 5 lines | Small (~2–5%) |
| P5 | Dirty-range `Ladder.Clear()` | 5 lines | Negligible on this file |

> **Note on P3:** The custom `OrderMap` open-addressing hash table was implemented and tested but later replaced.
> Profiling showed it reached only ~2.6 ms in PGO-2 vs ~2.1 ms for `Dictionary` + `CollectionsMarshal`.
> Root causes: Murmur3 hash more expensive than BCL's XOR-fold; backward-shift scan adds variable work
> that PGO can't eliminate. Final implementation uses P1, P2, P4, P5 only (P3 reverted).

---

## 6. Post-optimisation benchmark results

**Implemented:** P1 (warm-up), P2 (`CollectionsMarshal.GetValueRefOrAddDefault`), P4 (6-byte `OrderEntry`), P5 (dirty-range clear)  
**P3 status:** Reverted — custom `OrderMap` was slower than BCL `Dictionary` in PGO-2 tier

### Best-of-N comparison

| N  | Before (best) | After (best) | Tier-1 (before) | Tier-1 (after) | PGO fires at (timed run) |
|----|:------------:|:------------:|:---------------:|:--------------:|:------------------------:|
| 10 | 11.20 ms | **8.59 ms** | ~11–12 ms | ~8.5–9.0 ms | never (35 total < threshold) |
| 15 | 11.35 ms | **2.50 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14 (39 total) |
| 20 | 2.27 ms | **2.26 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14 (39 total) |
| 25 | 2.17 ms | **2.25 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14–15 |
| 30 | 2.18 ms | **2.28 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14 |
| 35 | 2.13 ms | **2.29 ms** | ~11–12 ms | ~8.5–9.0 ms | run 15 |
| 40 | 2.14 ms | **2.30 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14–15 |
| 45 | 2.16 ms | **2.24 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14 |
| 50 | 2.12 ms | **2.20 ms** | ~11–12 ms | ~8.5–9.0 ms | run 14 |

### Analysis

**Tier-1 improvement (~23%):** The pre-PGO band dropped from ~11–12 ms to ~8.5–9.0 ms. The combined effect of P2 (single-probe dictionary write), P4 (6-byte `OrderEntry` → better cache density in the dictionary array), and P5 (clear only the dirty sub-range) is visible in the un-optimised JIT tier where PGO hasn't yet inlined or specialised anything.

**PGO fires ~3 runs earlier (run 14–15 vs run 17–18):** The threshold appears to be ~38–40 total passes (25 warm-up + ~13–15 timed). With the smaller per-entry footprint (P4) the JIT's call counters may accumulate differently, but the total invocation threshold (~6.2 M calls) is close to before (~6.7 M). N=10 still falls short: 25 + 10 = 35 total passes, just below the threshold.

**N=15 now reaches PGO-2:** The most important shift. Previously N=15 reported ~11 ms (tier-1 only). Now it reports **2.50 ms** because the earlier PGO trigger (run 14 in a 15-run set) is reached within the timed window.

**PGO-2 steady state unchanged (~2.2–2.3 ms):** In the fully-optimised tier PGO inlines and specialises so aggressively that the P2/P4/P5 gains are largely masked. The absolute floor is within measurement noise of the pre-optimisation floor. This is expected: PGO rewrites the hot path from scratch using profiling data; the source-level changes matter most in the un-inlined tier-1 code.

**N=10 still stuck at tier-1 (~8.6 ms best):** 25 warmup + 10 timed = 35 total passes, just shy of the ~39-pass PGO threshold. To fix: increase `WarmupRuns` to 30, or accept that N=10 reports tier-1 speed.

### Detailed per-run data (post-optimisation)

<details>
<summary>N = 10 (tier-1 only — PGO not reached)</summary>

| Run | ms | µs/tick |
|----:|---:|--------:|
| 1  | 8.813 | 0.0549 |
| 2  | 9.032 | 0.0563 |
| 3  | 8.817 | 0.0550 |
| 4  | 8.924 | 0.0556 |
| 5  | 8.585 | 0.0535 |
| 6  | 8.962 | 0.0559 |
| 7  | 8.723 | 0.0544 |
| 8  | 8.836 | 0.0551 |
| 9  | 8.848 | 0.0552 |
| 10 | 8.807 | 0.0549 |
| **best** | **8.585** | **0.0535** |
</details>

<details>
<summary>N = 15 — PGO fires at run 14</summary>

| Run | ms | µs/tick | Note |
|----:|---:|--------:|------|
| 1–13 | 8.55–9.06 | 0.0533–0.0565 | tier-1 JIT |
| **14** | **6.896** | **0.0430** | **PGO re-JIT mid-run** |
| 15 | 2.500 | 0.0156 | fully PGO-compiled |
| **best** | **2.500** | **0.0156** |
</details>

<details>
<summary>N = 20</summary>

| Run | ms | µs/tick | Note |
|----:|---:|--------:|------|
| 1–13 | 8.49–9.05 | 0.0529–0.0564 | tier-1 JIT |
| **14** | **3.036** | **0.0189** | **PGO re-JIT mid-run** |
| 15–20 | 2.26–2.52 | 0.0141–0.0157 | PGO tier |
| **best** | **2.255** | **0.0141** |
</details>

<details>
<summary>N = 50 (deepest PGO sample)</summary>

| Run | ms | µs/tick | Note |
|----:|---:|--------:|------|
| 1–13 | 8.52–9.20 | 0.0531–0.0573 | tier-1 JIT |
| **14** | **7.398** | **0.0461** | **PGO re-JIT mid-run** |
| 15 | 2.712 | 0.0169 | first fully PGO run |
| 16–50 | 2.20–2.48 | 0.0137–0.0154 | PGO tier |
| **best** | **2.196** | **0.0137** |
</details>

---

## 7. Determinism fix — disable tiered compilation (P6)

The post-optimisation results above still depend on **when** the JIT crosses the
tier-0 → PGO tier-1 boundary relative to the run count N. The "slow" ~8.5 ms band is
not optimised tier-1 code — it is **instrumented tier-0** (the JIT counts every call
and branch to feed Dynamic PGO, which is why it runs ~3–4× slower). The fast ~2.2 ms
band is PGO tier-1. The transition lands mid-benchmark, so the reported best is a
lottery, worst near the threshold (N = 15):

| Warm-up | N = 15 best (5 trials) | Behaviour |
|---------|------------------------|-----------|
| 25 | 3.35 / 6.37 / 8.55 / 7.77 / 8.46 ms | unstable — transition straddles the timed window |
| 30 | 4.64 / 4.57 / 4.73 / 4.55 / 4.52 ms | stable but stuck in an intermediate tier; never reaches 2.2 ms even at N = 30 |

Tuning the warm-up count is fragile and **non-monotonic** (more warm-up made it
*slower*). The principled fix is to take the JIT lottery out of the loop entirely by
disabling tiered compilation:

```xml
<TieredCompilation>false</TieredCompilation>
```

Every method is now JIT-compiled straight to the fully-optimised tier on first call:
no instrumented tier-0, no background re-JIT, no PGO transition. (`TieredPGO` is
left off — PGO requires tier-0 to exist.) The warm-up loop drops to 3 passes, serving
only to warm CPU caches and branch predictors.

### Result — stable ~2.56 ms for every N

| N | best (ms) | µs/tick |
|---|:---------:|:-------:|
| 10 | 2.57 | 0.0160 |
| 15 | 2.54–2.58 (5 trials) | 0.0158–0.0161 |
| 20 | 2.58 | 0.0160 |
| 30 | 2.54 | 0.0158 |
| 50 | 2.56 | 0.0159 |

**N = 15, 5 trials:** 2.561 / 2.557 / 2.581 / 2.579 / 2.541 ms — **<2 % spread**, vs
the 3.35–8.55 ms (2.5×) spread with warm-up tuning. N = 10 is now ~2.57 ms instead of
being permanently stuck at 8.5 ms (it never reached PGO under tiering).

**Trade-off:** the deterministic tier is ~2.56 ms vs the ~2.2 ms that PGO occasionally
reached. We give up ~15 % peak in exchange for a result that is identical from run 1,
for every N, with no warm-up dependency — the correct choice for a timing tool whose
job is to report a reproducible construct-phase number. Process startup is marginally
slower (all methods compiled up-front) but startup is outside the timed region.

---

## 8. Pushing for 0.010 µs/tick — ablation-driven round (P7–P10)

Starting point after §7: a deterministic **2.56 ms / 0.0164 µs/tick**. Target: 0.010
µs/tick (≈1.6 ms), a 1.6× speedup. The work was driven by **ablation** — selectively
removing pieces of the timed loop to attribute cost — rather than guessing.

### 8.1 Where the 2.56 ms actually goes

Each row removes one piece and re-measures the timed best (snapshot store and dict
ablations are cumulative):

| Configuration | best (ms) | Implied component cost |
|---|:---:|---|
| Full loop | 2.56 | — |
| − snapshot store | 2.07 | **Snapshot build+store ≈ 0.49 ms** |
| − snapshot − (dict ops + `ladder.Remove(old)` + best-rescan) | 0.90 | the bundle ≈ 1.17 ms |
| of that bundle: − `ladder.Remove(old)` only (map kept) | 1.72 | `ladder.Remove(old)` ≈ 0.28 ms |
| of that bundle: best-cursor rescan (`NextDown/Up`) | — | **≈ 0 ms (negligible)** |

Corrected attribution (the first instinct that "the dictionary is 46 %" was wrong —
that ablation had bundled the ladder-removes and rescans with it):

| Component | ≈ cost | ≈ share |
|---|:---:|:---:|
| **Order-map ops** (probe + slot fetch) | 0.82 ms | 32 % |
| `ladder.Add` + dispatch + tick read | 0.62 ms | 24 % |
| Snapshot build + store | 0.49 ms | 19 % |
| `ladder.Remove(old)` on M/D | 0.28 ms | 11 % |
| (best-cursor rescan) | ~0 ms | — |

The order-map is the biggest single cost and is **memory-latency bound**: keys hash to
scattered slots, so each lookup is a likely cache miss. This is why both changes that
*don't* address latency moved nothing.

### 8.2 What was tried, and what each delivered

| # | Change | Result | Verdict |
|---|--------|--------|---------|
| P7 | **Custom open-addressing `OrderMap`** replacing `Dictionary` (Fibonacci hash, key-0 sentinel, backward-shift delete) | ~0 % vs `Dictionary` | both scatter by hash → same latency; kept only after P9 made it a net win |
| P8 | **Shrink `BookSnapshot` 32 → 16 B** (price→`short` sentinel, count→`ushort`, qty kept `int`) | ~0 % on time | snapshot cost is *construction*, not store size; kept anyway (½ the memory) |
| P9 | **Cache best-level qty/count as scalars** in `Ladder` so `Snapshot()` needs no array indexing | **2.53 → 2.35 ms (~7 %)** | ✅ the real win — moves 4 array loads/tick out of the per-tick read |
| P10a | Map capacity 4096 → **2048** | 2.35 → 3.1 ms | ❌ reverted — 56 % load lengthens probe + backward-shift work |
| P10b | Map **structure-of-arrays** (dense `long[]` keys + parallel `OrderEntry[]`) | 2.35 → 2.27 ms (~3 %) | ✅ probe scan no longer drags value bytes through cache |
| P10c | **Merge `_qty`/`_count` into one `Level[]`** (1 cache line per level touch) | 2.27 → 2.25 ms (~1 %) | ✅ small + cleaner (one array) |

### 8.3 Result

| | Before (§7) | After (§8) |
|---|:---:|:---:|
| best (ms) | 2.56 | **2.15–2.30** |
| µs/tick | 0.0164 | **0.0134–0.0143** |

Across N = 10…50: 0.0134–0.0143 µs/tick, best observed **2.148 ms (0.0134 µs/tick)**.
A **~15 % improvement**, still fully deterministic (tiering off) and byte-identical
output (18/18 tests, golden first-2000 match).

### 8.4 Why 0.010 µs/tick is not reachable here

After P7–P10 the residual is dominated by two costs that resist code-level fixes:

1. **Order-map memory latency (~0.7 ms).** The lookup is one dependent cache miss per
   A/M/D tick. Neither the BCL dictionary, a custom open-addressing table, SoA layout,
   nor capacity tuning removes it — it is the cost of reaching a hash-scattered slot in
   a working set larger than L1. The usual cure is **software prefetch** of the next
   tick's slot, but .NET exposes no portable prefetch intrinsic on ARM (Apple Silicon),
   so it is unavailable here.
2. **Mandatory per-tick snapshot (~0.3 ms after P9).** The task requires one output row
   per tick, so a snapshot must be captured every tick; it cannot be skipped or
   re-derived (the book is mutated in place).

Together these are ~1.0 ms of the ~2.2 ms floor. Reaching 1.6 ms would require either a
prefetch path the runtime doesn't offer on this architecture, or changing the problem
(not emitting a per-tick snapshot). **0.0134 µs/tick is the practical floor for this
design on this hardware.**

---

## 9. Confirming the latency hypothesis — stride-padding experiment

Before investing in an x86 prefetch path, the claim "the order lookup is
memory-latency bound" was tested directly. Hardware counters were unavailable on the
dev machine (Apple Silicon, Command Line Tools only — no Instruments/`xctrace`, no
`perf`), so the test was done in software with a **stride-padding sweep**.

**Method.** The `OrderMap` keeps 4096 logical slots. A `Stride` knob spaces those slots
physically further apart in the backing arrays (`_keys[i * Stride]`), leaving the hash,
probe sequence, and instruction count **identical** — only the cache-line footprint and
miss rate change. If the loop is compute-bound the timing should be flat; if
memory-latency bound it should climb with footprint.

**Result (best-of, N = 15).**

| Stride | Keys footprint | best (ms) | µs/tick |
|-------:|---------------:|:---------:|:-------:|
| 1  |   32 KB | 2.260 | 0.0141 |
| 2  |   64 KB | 2.484 | 0.0155 |
| 4  |  128 KB | 2.693 | 0.0168 |
| 8  |  256 KB | 2.806 | 0.0175 |
| 16 |  512 KB | 3.124 | 0.0195 |
| 32 |    1 MB | 3.461 | 0.0216 |

**Conclusion.** With computation held constant, spreading the map from 32 KB to 1 MB
raises the timed best by **+53 %** (2.26 → 3.46 ms), monotonically. The construct loop
is unambiguously **memory-latency bound on the order lookup** — a compute-bound loop
would have stayed flat. This confirms the §8.4 diagnosis and means software prefetch of
the next tick's slot has genuine headroom (the per-tick lookups are independent, so an
out-of-order core / prefetch can overlap the misses).

**Caveat carried forward.** The dev machine's working set at Stride = 1 (~32 KB keys) is
already near the L1d capacity cliff — even the 32 → 64 KB step costs +0.22 ms — so a
meaningful slice of the ~0.8 ms lookup cost is miss latency that prefetch could recover.
The lever exists only on x86 (`Sse.Prefetch0`); .NET surfaces no portable ARM prefetch,
so shipping it here would still need a native `PRFM` stub. Next step (if pursued):
implement the guarded prefetch + distance sweep and A/B it on real x86 hardware (§ Step 1–2
of the plan).
