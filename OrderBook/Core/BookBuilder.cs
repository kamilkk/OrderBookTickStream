using System.Diagnostics;
using OrderBook.Model;

namespace OrderBook.Core;

/// <summary>
/// Stage 2 — drives the <see cref="OrderBook"/> over the decoded ticks, capturing
/// a <see cref="BookSnapshot"/> per tick, and times only this construction phase.
/// </summary>
/// <remarks>
/// Before the first timed pass, an untimed warm-up runs the full tick loop
/// <see cref="WarmupRuns"/> times. This reliably triggers .NET Dynamic PGO
/// re-compilation of all hot methods so that run 1 of the timed section already
/// executes fully-optimised native code. Without the warm-up, PGO fires around
/// run 17–18, meaning N ≤ 15 never reaches the true steady-state speed.
///
/// The construct phase is then run <c>runs</c> times (the snapshot buffer is reused
/// and the book is reset between runs); the <b>best</b> run is reported, which
/// absorbs any remaining OS jitter without a separate statistical pass.
/// File reading and writing are deliberately outside the timed region.
/// </remarks>
public static class BookBuilder
{
    /// <summary>
    /// Untimed passes before the first timed run. 25 passes of 160 k ticks
    /// (~4 M Apply calls) reliably crosses .NET Dynamic PGO's first re-JIT threshold
    /// (~2.7 M calls, observed at run 17–18 without a warm-up), so run 1 of the
    /// timed section starts in the first PGO tier (~7 ms) rather than the cold-JIT
    /// tier (~18 ms). A second PGO re-JIT fires after ~45 total passes and drives
    /// the best time to ~2 ms; with N ≥ 20 the timed runs themselves cross that
    /// threshold without needing extra warm-up passes.
    /// </summary>
    private const int WarmupRuns = 25;

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
        // Run the full tick loop WarmupRuns times to trigger .NET Dynamic PGO
        // before the first stopwatch starts. The snapshot buffer is reused so
        // no extra allocation occurs; results are overwritten by the timed runs.
        for (int w = 0; w < WarmupRuns; w++)
        {
            book.Reset();
            for (int i = 0; i < ticks.Length; i++)
            {
                book.Apply(in ticks[i]);
                snapshots[i] = book.Snapshot();
            }
        }

        // --- timed runs ---
        Console.WriteLine(
            $"Constructing ({ticks.Length:N0} ticks, {runs} run(s), " +
            $"after {WarmupRuns}-pass PGO warm-up):");

        TimeSpan best = TimeSpan.MaxValue;

        for (int run = 1; run <= runs; run++)
        {
            book.Reset();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks.Length; i++)
            {
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
