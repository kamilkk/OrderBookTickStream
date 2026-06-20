using System.Text;
using OrderBook.Model;

namespace OrderBook.IO;

/// <summary>
/// Stage 3 — writes the result CSV. Copies the raw tick fields and appends the
/// computed top-of-book columns, following the file's conventions:
/// <list type="bullet">
///   <item><description><c>;</c> field separator, <c>\r\n</c> (CRLF) line endings.</description></item>
///   <item><description>A missing side (side byte 0) is an empty field.</description></item>
///   <item><description>An empty book side renders its three aggregate columns as empty fields.</description></item>
/// </list>
/// Numeric formatting is culture-invariant (the project also enables
/// <c>InvariantGlobalization</c>), so output is identical regardless of host locale.
/// </summary>
public static class ResultWriter
{
    private const string Header =
        "SourceTime;Side;Action;OrderId;Price;Qty;B0;BQ0;BN0;A0;AQ0;AN0";

    private const char Sep = ';';
    private const string Crlf = "\r\n";

    /// <summary>Writes <paramref name="ticks"/> joined with their <paramref name="snapshots"/> to <paramref name="path"/>.</summary>
    public static void Write(string path, Tick[] ticks, BookSnapshot[] snapshots)
    {
        if (ticks.Length != snapshots.Length)
        {
            throw new ArgumentException(
                $"Tick count ({ticks.Length}) and snapshot count ({snapshots.Length}) differ.");
        }

        // UTF-8 without a BOM: the sample files have no byte-order mark, and the
        // payload is pure ASCII, so a leading BOM would break a byte-for-byte diff.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var writer = new StreamWriter(path, append: false, utf8NoBom, bufferSize: 1 << 20);
        var sb = new StringBuilder(128);

        writer.Write(Header);
        writer.Write(Crlf);

        for (int i = 0; i < ticks.Length; i++)
        {
            ref readonly Tick t = ref ticks[i];
            ref readonly BookSnapshot s = ref snapshots[i];

            sb.Clear();
            sb.Append(t.SourceTime).Append(Sep);

            if (t.Side != 0)
            {
                sb.Append((char)t.Side);
            }
            sb.Append(Sep);

            sb.Append((char)t.Action).Append(Sep);
            sb.Append(t.OrderId).Append(Sep);
            sb.Append(t.Price).Append(Sep);
            sb.Append(t.Qty).Append(Sep);

            if (s.HasBid)
            {
                sb.Append(s.B0).Append(Sep).Append(s.BQ0).Append(Sep).Append(s.BN0).Append(Sep);
            }
            else
            {
                sb.Append(Sep).Append(Sep).Append(Sep);
            }

            if (s.HasAsk)
            {
                sb.Append(s.A0).Append(Sep).Append(s.AQ0).Append(Sep).Append(s.AN0);
            }
            else
            {
                sb.Append(Sep).Append(Sep);
            }

            sb.Append(Crlf);
            writer.Write(sb);
        }
    }
}
