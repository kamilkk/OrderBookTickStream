using System.Diagnostics;
using OrderBook.Model;

namespace OrderBook.Core;

/// <summary>
/// Stage 2 — drives the <see cref="OrderBook"/> over the decoded ticks, capturing
/// a <see cref="BookSnapshot"/> per tick, and times only this construction phase.
/// </summary>
/// <remarks>
/// The construct phase is run <c>runs</c> times (the snapshot buffer is reused and
/// the book is reset between runs); the <b>best</b> run is reported, which absorbs
/// JIT/first-touch cost without a separate warm-up. File reading and writing are
/// deliberately outside the timed region.
/// </remarks>
public static class BookBuilder
{
    /// <summary>Builds all per-tick snapshots and prints per-run and best timings.</summary>
    /// <param name="ticks">Decoded input stream.</param>
    /// <param name="runs">Number of timed construction passes (best is reported).</param>
    /// <returns>The per-tick snapshots (identical across runs; the final pass is returned).</returns>
    public static BookSnapshot[] Build(Tick[] ticks, int runs)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);

        var snapshots = new BookSnapshot[ticks.Length];
        var book = new OrderBook();
        TimeSpan best = TimeSpan.MaxValue;

        Console.WriteLine($"Constructing pass ({ticks.Length:N0} ticks, {runs} run(s)):");

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

            if (elapsed < best)
            {
                best = elapsed;
            }
        }

        Console.WriteLine(
            $"  best   : {best.TotalMilliseconds,8:F3} ms total | " +
            $"{PerTickMicros(best, ticks.Length),7:F4} us/tick");

        return snapshots;
    }

    private static double PerTickMicros(TimeSpan elapsed, int tickCount) =>
        tickCount == 0 ? 0 : elapsed.TotalMicroseconds / tickCount;
}
