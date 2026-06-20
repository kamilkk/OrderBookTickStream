using OrderBook.IO;
using OrderBook.Model;
using Book = OrderBook.Core.OrderBook;

namespace OrderBook.Tests;

/// <summary>
/// Lightweight, dependency-free test runner for the <see cref="Book"/> core.
/// Deliberately uses no external test framework so it restores and runs anywhere
/// the .NET SDK is present. Returns a non-zero exit code if any case fails.
/// </summary>
internal static class Program
{
    private const byte Bid = (byte)'1';
    private const byte Ask = (byte)'2';

    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run(nameof(Add_Builds_Best_Bid), Add_Builds_Best_Bid);
        Run(nameof(Best_Bid_Is_Highest), Best_Bid_Is_Highest);
        Run(nameof(Best_Ask_Is_Lowest), Best_Ask_Is_Lowest);
        Run(nameof(Same_Price_Level_Aggregates), Same_Price_Level_Aggregates);
        Run(nameof(Modify_Existing_Moves_Level), Modify_Existing_Moves_Level);
        Run(nameof(Modify_Missing_Is_Add), Modify_Missing_Is_Add);
        Run(nameof(Add_Reuses_Id_As_Replace), Add_Reuses_Id_As_Replace);
        Run(nameof(Delete_Removes_Order), Delete_Removes_Order);
        Run(nameof(Delete_Unknown_Is_Noop), Delete_Unknown_Is_Noop);
        Run(nameof(Best_Falls_To_Next_Level), Best_Falls_To_Next_Level);
        Run(nameof(Clear_Empties_Both_Sides), Clear_Empties_Both_Sides);
        Run(nameof(Clear_Y_Action_Empties_Both_Sides), Clear_Y_Action_Empties_Both_Sides);
        Run(nameof(Trusts_Stored_Price_On_Delete), Trusts_Stored_Price_On_Delete);
        Run(nameof(Clear_Removes_From_Id_Map), Clear_Removes_From_Id_Map);
        Run(nameof(Price_Out_Of_Range_Throws), Price_Out_Of_Range_Throws);
        Run(nameof(Invalid_Side_On_Add_Throws), Invalid_Side_On_Add_Throws);
        Run(nameof(Writer_Empty_Side_Renders_As_Empty_Fields), Writer_Empty_Side_Renders_As_Empty_Fields);
        Run(nameof(Writer_Populated_Side_Renders_Values), Writer_Populated_Side_Renders_Values);

        Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static Tick T(byte side, char action, long id, int price, int qty) =>
        new(sourceTime: 0, side, (byte)action, id, price, qty);

    private static void Add_Builds_Best_Bid()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1676, 9));
        var s = b.Snapshot();
        Assert(s.HasBid && s.B0 == 1676 && s.BQ0 == 9 && s.BN0 == 1, "bid populated");
        Assert(!s.HasAsk, "ask still empty");
    }

    private static void Best_Bid_Is_Highest()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1676, 9));
        b.Apply(T(Bid, 'A', 2, 1680, 4));
        b.Apply(T(Bid, 'A', 3, 1670, 1));
        var s = b.Snapshot();
        Assert(s.B0 == 1680 && s.BQ0 == 4 && s.BN0 == 1, "highest bid wins");
    }

    private static void Best_Ask_Is_Lowest()
    {
        var b = new Book();
        b.Apply(T(Ask, 'A', 1, 1690, 2));
        b.Apply(T(Ask, 'A', 2, 1685, 5));
        b.Apply(T(Ask, 'A', 3, 1700, 3));
        var s = b.Snapshot();
        Assert(s.A0 == 1685 && s.AQ0 == 5 && s.AN0 == 1, "lowest ask wins");
    }

    private static void Same_Price_Level_Aggregates()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'A', 2, 1700, 3));
        var s = b.Snapshot();
        Assert(s.B0 == 1700 && s.BQ0 == 8 && s.BN0 == 2, "qty summed, count = 2");
    }

    private static void Modify_Existing_Moves_Level()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'M', 1, 1710, 2));
        var s = b.Snapshot();
        Assert(s.B0 == 1710 && s.BQ0 == 2 && s.BN0 == 1, "moved to new price");
    }

    private static void Modify_Missing_Is_Add()
    {
        var b = new Book();
        b.Apply(T(Ask, 'M', 99, 1685, 7)); // no prior order with id 99
        var s = b.Snapshot();
        Assert(s.HasAsk && s.A0 == 1685 && s.AQ0 == 7 && s.AN0 == 1, "M treated as add");
    }

    private static void Add_Reuses_Id_As_Replace()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'A', 1, 1700, 2)); // same id, replace -> not additive
        var s = b.Snapshot();
        Assert(s.B0 == 1700 && s.BQ0 == 2 && s.BN0 == 1, "replace, not double-count");
    }

    private static void Delete_Removes_Order()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'D', 1, 1700, 5));
        Assert(!b.Snapshot().HasBid, "side empty after delete");
    }

    private static void Delete_Unknown_Is_Noop()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'D', 42, 0, 0)); // unknown id
        var s = b.Snapshot();
        Assert(s.B0 == 1700 && s.BN0 == 1, "unknown delete left book intact");
    }

    private static void Best_Falls_To_Next_Level()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Bid, 'A', 2, 1690, 4));
        b.Apply(T(Bid, 'D', 1, 1700, 5)); // remove the top
        var s = b.Snapshot();
        Assert(s.B0 == 1690 && s.BQ0 == 4 && s.BN0 == 1, "fell to next level");
    }

    private static void Clear_Empties_Both_Sides()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Ask, 'A', 2, 1710, 5));
        b.Apply(T(0, 'F', 0, 0, 0)); // clear
        var s = b.Snapshot();
        Assert(!s.HasBid && !s.HasAsk, "both sides cleared");
    }

    private static void Clear_Y_Action_Empties_Both_Sides()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(Ask, 'A', 2, 1710, 5));
        b.Apply(T(0, 'Y', 0, 0, 0)); // 'Y' also clears
        var s = b.Snapshot();
        Assert(!s.HasBid && !s.HasAsk, "both sides cleared by Y");
    }

    private static void Trusts_Stored_Price_On_Delete()
    {
        // Delete record carries a bogus price; the book must use the stored price.
        var b = new Book();
        b.Apply(T(Ask, 'A', 1, 1685, 3));
        b.Apply(T(Ask, 'D', 1, 9999, 0)); // wrong price in the delete record
        Assert(!b.Snapshot().HasAsk, "deleted by id regardless of record price");
    }

    private static void Clear_Removes_From_Id_Map()
    {
        var b = new Book();
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        b.Apply(T(0, 'F', 0, 0, 0)); // clear removes the id-map entry for order 1

        // D for the now-unknown id must be a no-op (not corrupt the book).
        b.Apply(T(Bid, 'D', 1, 1700, 5));
        Assert(!b.Snapshot().HasBid, "delete after clear is no-op, side stays empty");

        // Re-adding the same id must not double-count.
        b.Apply(T(Bid, 'A', 1, 1700, 5));
        var s = b.Snapshot();
        Assert(s.HasBid && s.BQ0 == 5 && s.BN0 == 1, "re-add after clear counts exactly once");
    }

    private static void Price_Out_Of_Range_Throws()
    {
        var b = new Book();
        AssertThrows<ArgumentOutOfRangeException>(
            () => b.Apply(T(Bid, 'A', 1, 20000, 1)), "price above ladder ceiling rejected");
    }

    private static void Invalid_Side_On_Add_Throws()
    {
        var b = new Book();
        AssertThrows<InvalidDataException>(
            () => b.Apply(T(0, 'A', 1, 1700, 1)), "add with no side rejected");
    }

    private static void Writer_Empty_Side_Renders_As_Empty_Fields()
    {
        // A tick whose snapshot has no resting orders on either side must emit
        // six consecutive empty fields (not literal "0" values) in the aggregate columns.
        var ticks = new[] { T(0, 'F', 0, 0, 0) };
        var snapshots = new[] { new BookSnapshot(false, 0, 0, 0, false, 0, 0, 0) };

        string path = Path.GetTempFileName();
        try
        {
            ResultWriter.Write(path, ticks, snapshots);
            string[] lines = File.ReadAllLines(path);
            // lines[0] = header, lines[1] = data row
            Assert(lines.Length == 2, "header + one data row");
            string[] fields = lines[1].Split(';');
            Assert(fields.Length == 12, "12 fields");
            // B0, BQ0, BN0, A0, AQ0, AN0 must all be empty strings
            for (int i = 6; i < 12; i++)
            {
                Assert(fields[i] == string.Empty, $"field {i} must be empty, got '{fields[i]}'");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Writer_Populated_Side_Renders_Values()
    {
        // A snapshot with both sides populated must emit numeric values (not empty strings).
        var ticks = new[] { T(Bid, 'A', 1, 1700, 5) };
        var snapshots = new[] { new BookSnapshot(true, 1700, 5, 1, true, 1710, 3, 2) };

        string path = Path.GetTempFileName();
        try
        {
            ResultWriter.Write(path, ticks, snapshots);
            string[] lines = File.ReadAllLines(path);
            Assert(lines.Length == 2, "header + one data row");
            string[] fields = lines[1].Split(';');
            Assert(fields.Length == 12, "12 fields");
            Assert(fields[6] == "1700" && fields[7] == "5" && fields[8] == "1", "bid aggregates");
            Assert(fields[9] == "1710" && fields[10] == "3" && fields[11] == "2", "ask aggregates");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- tiny assertion harness ---

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL  {name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string because)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"expected {because}");
        }
    }

    private static void AssertThrows<TException>(Action action, string because)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"expected {typeof(TException).Name} ({because})");
    }
}
