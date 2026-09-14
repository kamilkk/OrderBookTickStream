using System.Diagnostics;
using OrderBook.Model;

namespace OrderBook.Core;

/// <summary>
/// Stage 2 — drives the <see cref="OrderBook"/> over the decoded ticks, capturing
/// a <see cref="BookSnapshot"/> per tick, and times only this construction phase.
/// </summary>
/// <remarks>
/// Tiered compilation is disabled for this project (see the csproj), so every
/// hot method is JIT-compiled straight to the fully-optimised tier on first call;
/// run 1 of the timed section already executes optimal native code. The warm-up
/// below therefore only primes CPU caches and branch predictors — a few passes
/// suffice. (Previously, with tiering on, ~25–30 warm-up passes were needed to
/// drag the JIT through Dynamic PGO before timing, and N ≤ 15 was unstable because
/// the tier-0 → PGO transition straddled the timed window.)
///
/// The construct phase is then run <c>runs</c> times (the snapshot buffer is reused
/// and the book is reset between runs); the <b>best</b> run is reported, which
/// absorbs any remaining OS jitter without a separate statistical pass.
/// File reading and writing are deliberately outside the timed region.
/// </remarks>
public static class BookBuilder
{
    /// <summary>
    /// Untimed passes before the first timed run. With tiered compilation disabled
    /// the code is already fully optimised on run 1, so these passes serve only to
    /// warm CPU caches and branch predictors; 3 is ample. (The book's working set —
    /// two 64 KB ladders plus the order dictionary — is brought resident and the
    /// hot branches are exercised before the stopwatch starts.)
    /// </summary>
    private const int WarmupRuns = 3;

    /// <summary>
    /// How many ticks ahead to prefetch the order-map slot (see
    /// <see cref="OrderBook.Prefetch"/>). The lookup is memory-latency bound, so the
    /// hint must lead the matching <c>Apply</c> by enough work to hide a cache miss.
    /// Effective only on x86; folded away entirely elsewhere. The best value is
    /// hardware-specific and should be swept (2–6) when tuning on real x86 silicon.
    /// </summary>
    private const int PrefetchDistance = 3;

    /// <summary>Builds all per-tick snapshots and prints per-run and best timings.</summary>
    /// <param name="ticks">Decoded input stream.</param>
    /// <param name="runs">Number of timed construction passes (best is reported).</param>
    /// <returns>The per-tick snapshots from the final timed pass.</returns>
    public static BookSnapshot[] Build(Tick[] ticks, int runs)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);

        var snapshots = new BookSnapshot[ticks.Length];
        var book      = new OrderBook();

        // --- untimed warm-up ---
        // Run the full tick loop WarmupRuns times to bring the book's working set
        // resident and warm the branch predictors before the first stopwatch starts.
        // The snapshot buffer is reused so no extra allocation occurs; results are
        // overwritten by the timed runs.
        for (int w = 0; w < WarmupRuns; w++)
        {
            book.Reset();
            for (int i = 0; i < ticks.Length; i++)
            {
                // Prefetch a few ticks ahead so the order-map miss is in flight before
                // Apply needs it. Gated on PrefetchSupported (a JIT constant), so on
                // non-x86 the whole block — bounds check and all — is eliminated.
                if (OrderBook.PrefetchSupported)
                {
                    int p = i + PrefetchDistance;
                    if (p < ticks.Length) book.Prefetch(ticks[p].OrderId);
                }

                book.Apply(in ticks[i]);
                snapshots[i] = book.Snapshot();
            }
        }

        // --- timed runs ---
        Console.WriteLine(
            $"Constructing ({ticks.Length:N0} ticks, {runs} run(s), " +
            $"after {WarmupRuns}-pass cache warm-up):");

        TimeSpan best = TimeSpan.MaxValue;

        for (int run = 1; run <= runs; run++)
        {
            book.Reset();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks.Length; i++)
            {
                // Prefetch a few ticks ahead so the order-map miss is in flight before
                // Apply needs it. Gated on PrefetchSupported (a JIT constant), so on
                // non-x86 the whole block — bounds check and all — is eliminated.
                if (OrderBook.PrefetchSupported)
                {
                    int p = i + PrefetchDistance;
                    if (p < ticks.Length) book.Prefetch(ticks[p].OrderId);
                }

                book.Apply(in ticks[i]);
                snapshots[i] = book.Snapshot();
            }
            sw.Stop();

            TimeSpan elapsed = sw.Elapsed;
            Console.WriteLine(
                $"  run {run}/{runs}: {elapsed.TotalMilliseconds,8:F3} ms total | " +
                $"{PerTickMicros(elapsed, ticks.Length),7:F4} us/tick");

            if (elapsed < best) best = elapsed;
        }

        Console.WriteLine(
            $"  best   : {best.TotalMilliseconds,8:F3} ms total | " +
            $"{PerTickMicros(best, ticks.Length),7:F4} us/tick");

        return snapshots;
    }

    private static double PerTickMicros(TimeSpan elapsed, int tickCount) =>
        tickCount == 0 ? 0 : elapsed.TotalMicroseconds / tickCount;
}
