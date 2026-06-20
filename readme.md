# Order Book — Tick Stream Reconstruction

A standalone **.NET 10** console application that reads the binary tick stream
`ticks.raw`, reconstructs the limit order book tick-by-tick, computes the
top-of-book on both sides, and writes `ticks_result.csv`.

For every input record the program emits one output row that copies the raw
fields and appends:

| Column | Meaning |
|--------|---------|
| `B0`   | Best bid — the highest BID price |
| `BQ0`  | Summed quantity of all orders at `B0` |
| `BN0`  | Number of orders at `B0` |
| `A0`   | Best ask — the lowest ASK price |
| `AQ0`  | Summed quantity of all orders at `A0` |
| `AN0`  | Number of orders at `A0` |

---

## Build & run

Requires the **.NET 10 SDK**.

```bash
# from the solution root
dotnet build -c Release
dotnet run --project OrderBook -c Release
```

The input file is bundled and copied next to the executable, so the app runs
with no arguments and finds it automatically (whether launched via `dotnet run`
or as the built binary). On completion it prints the construct-phase timings and
writes `ticks_result.csv` next to the executable.

Optional arguments:

```bash
dotnet run --project OrderBook -c Release -- <input.raw> <output.csv> <runs>
```

- `input.raw` — path to the binary input (default: bundled `ticks.raw`)
- `output.csv` — output path (default: `ticks_result.csv` next to the exe)
- `runs` — number of timed construct passes, clamped to **[5, 15]** (default: 10)

### Run the tests

```bash
dotnet run --project OrderBook.Tests -c Release
```

A dependency-free self-test runner (no external test framework, so it restores
and runs anywhere) exercising the book's core behaviours. Exits non-zero on any
failure.

---

## Project structure

```
OrderBook.sln
├─ OrderBook/                 # the application
│  ├─ Program.cs              # entry point: wires the 3 stages, prints timings
│  ├─ Model/
│  │  ├─ Tick.cs             # decoded input record (readonly struct)
│  │  └─ BookSnapshot.cs     # per-tick top-of-book result (readonly struct)
│  ├─ Core/
│  │  ├─ OrderBook.cs        # the book: array-ladder backend + apply logic
│  │  └─ BookBuilder.cs      # Stage 2 orchestration + timing harness
│  ├─ IO/
│  │  ├─ TickReader.cs       # Stage 1: big-endian binary decode
│  │  └─ ResultWriter.cs     # Stage 3: CSV output
│  └─ ticks.raw              # bundled input (copied to output dir)
└─ OrderBook.Tests/           # self-test runner for the core
```

---

## Three stages

The work is split exactly as the task asks, and **only the construction phase is
timed** — reading and writing are excluded.

1. **Read** (`TickReader`) — `File.ReadAllBytes`, then decode the fixed 26-byte
   big-endian records into a contiguous `Tick[]` over a `ReadOnlySpan<byte>` with
   `BinaryPrimitives`. Zero per-record allocation.
2. **Construct** (`BookBuilder` + `OrderBook`) — replay every tick, capturing a
   `BookSnapshot` per tick. Run **N ∈ [5, 15]** times (default 10) reusing the
   snapshot buffer and resetting the book between runs; the **best** run is
   reported, which absorbs JIT/first-touch cost without a separate warm-up.
3. **Write** (`ResultWriter`) — stream the result CSV.

---

## Input format (`ticks.raw`)

Fixed **26-byte big-endian** records (file size is an exact multiple — no header,
no padding):

| Offset | Size | Field        | Type   | Notes |
|-------:|-----:|--------------|--------|-------|
| 0      | 8    | `SourceTime` | int64  | timestamp |
| 8      | 1    | `Side`       | byte   | `0x00` none, `0x31` (`'1'`) BID, `0x32` (`'2'`) ASK |
| 9      | 1    | `Action`     | byte   | ASCII `Y`/`F`/`A`/`M`/`D` |
| 10     | 8    | `OrderId`    | int64  | unique order id |
| 18     | 4    | `Price`      | int32  | |
| 22     | 4    | `Qty`        | int32  | |

> **Note:** `Side` is the ASCII *digit byte* (`0x31`/`0x32`), not the integer 1/2.

Actions: `Y`/`F` clear the entire book; `A` add (replace if id exists); `M`
modify (treated as add if the id is unknown); `D` delete (no-op if id unknown).

---

## Output format (`ticks_result.csv`)

Plain text, `;`-separated, **CRLF** line endings, no BOM, header row, numbers
formatted culture-invariantly (the project also sets `InvariantGlobalization`).
This byte-matches the provided `ticks_result_sample.csv`.

Conventions:
- A missing **Side** (side byte `0`) is an **empty field**.
- `OrderId`/`Price`/`Qty` are copied as-is (so a clear row shows real `0`s).
- When a book side has no resting orders, its three aggregate columns are
  **empty** (distinct from `0`).

---

## Design decisions

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **.NET 10**, single console solution | Target runtime; runs from the terminal after a release build. |
| D2 | **Array-ladder** book backend | Prices are confirmed to lie in `[0, 16383]`, so each side is two flat arrays indexed by price plus a best cursor — O(1) updates, no hashing, no tree. Fastest realistic design. |
| D3 | **`int`** for quantity sums | Best-level sums peak at a few hundred in this data; `int` is ample (overflow would need ~7M max-qty orders at one price). |
| D4 | **Byte-exact output** | `;` separator, CRLF, no BOM, invariant numerics — matches the sample for a clean grader diff. |
| D5 | **Best-of-N timing, N ∈ [5, 15]** (default 10) | More passes than the original spec called for; report the fastest so the figure reflects warm steady-state rather than a cold JIT pass. |

### Data structures

**Per side** (`OrderBook.Ladder`):
- `int[16384] qty` and `int[16384] count`, indexed directly by price.
- a single best cursor (`-1` = empty): highest populated index for BID, lowest
  for ASK.
- Add/modify/delete are O(1); the cursor is only walked to the next populated
  level when the current best empties. Clear is skipped entirely when the side is
  already empty.

**Order index** (`Dictionary<long, OrderEntry>`): maps `OrderId →
(side, price, qty)` so modifies/deletes can locate an order's level. Sized to
2048, comfortably above the observed peak of ~1,150 concurrent live orders, so
the hot loop never rehashes. The **delete record's own price is never trusted** —
the stored price is authoritative.

A price outside `[0, 16383]` is a hard, fail-fast error rather than silent
corruption; if a future instrument widened the range, the fix is a one-line
`PriceCount` bump (or a swap to a hash/sorted backend).

---

## Assumptions

These were verified by decoding the full 160,429-record file, not assumed blindly:

- The file is a whole number of 26-byte records (validated at read time).
- `Side` bytes are only `{0x00, 0x31, 0x32}`; `Action` only `{Y, F, A, M, D}`.
- `Price ∈ [0, 16383]`, `Qty ∈ [1, 300]`.
- Each input record produces exactly one output row.
- `Y`/`F` clear the **entire** book (both sides).
- For `SourceTime ∈ [24300006000, 53400000000]` the book is valid (`B0 < A0`).

---

## Performance

Construction is **O(T)** over the tick count with tiny constants and no hot-loop
allocation. On the development machine (a containerised Linux box, single
thread), the best run measured roughly **0.15 µs/tick** for the full 160,429-tick
stream (~25 ms total); the cold first pass is several times slower, which is why
the best-of-N figure is the one reported. Absolute numbers are machine-dependent.

---

## Validation

- **Golden-file match:** the first 2,000 output lines are byte-for-byte identical
  to the provided `ticks_result_sample.csv`.
- **Full-file cross-check:** all 160,429 output rows match an independent
  reference implementation of the algorithm (0 mismatches).
- **Invariant check:** across the 157,373 rows in the guaranteed validity window
  that have both sides populated, `B0 < A0` holds with 0 violations.
- **Unit behaviour:** 14 self-tests covering aggregation, replace/modify/delete
  semantics, best-level fall-through, clears, the trust-stored-price-on-delete
  rule, and the range/side guards — all passing.
