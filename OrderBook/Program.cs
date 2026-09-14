using System.Diagnostics;
using OrderBook.Core;
using OrderBook.IO;
using OrderBook.Model;

namespace OrderBook;

/// <summary>
/// Entry point. Wires the three stages — read, construct (timed), write — and
/// prints only the construction timings, as the task requires.
/// </summary>
internal static class Program
{
    private const string DefaultInput = "ticks.raw";
    private const string DefaultOutput = "ticks_result.csv";

    // Timing runs are kept in the small [5, 15] band: enough to discard a cold pass,
    // few enough to stay quick.
    private const int DefaultRuns = 15;
    private const int MinRuns = 5;
    private const int MaxRuns = 50;

    private static int Main(string[] args)
    {
        // Anchor the defaults to the executable's directory (where ticks.raw is
        // copied) so the app runs out-of-the-box whether launched as the built exe
        // or via `dotnet run`, regardless of the current working directory.
        // Explicit path arguments are honoured as given.
        string baseDir = AppContext.BaseDirectory;
        string input = args.Length > 0 ? args[0] : Path.Combine(baseDir, DefaultInput);
        string output = args.Length > 1 ? args[1] : Path.Combine(baseDir, DefaultOutput);
        int runs = ResolveRuns(args);

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input file not found: '{input}'.");
            Console.Error.WriteLine("Usage: OrderBook [input.raw] [output.csv] [runs]");
            return 1;
        }

        try
        {
            // Stage 1 — read (not timed).
            var readSw = Stopwatch.StartNew();
            Tick[] ticks = TickReader.Read(input);
            readSw.Stop();
            Console.WriteLine($"Read    {ticks.Length:N0} ticks from '{input}' in {readSw.ElapsedMilliseconds} ms.");

            // Stage 2 — construct (timed, best of N).
            BookSnapshot[] snapshots = BookBuilder.Build(ticks, runs);

            // Stage 3 — write (not timed).
            var writeSw = Stopwatch.StartNew();
            ResultWriter.Write(output, ticks, snapshots);
            writeSw.Stop();
            Console.WriteLine($"Wrote   '{output}' in {writeSw.ElapsedMilliseconds} ms.");

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int ResolveRuns(string[] args)
    {
        if (args.Length > 2 && int.TryParse(args[2], out int n))
        {
            return Math.Clamp(n, MinRuns, MaxRuns);
        }
        return DefaultRuns;
    }
}
