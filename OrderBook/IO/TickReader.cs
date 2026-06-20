using System.Buffers.Binary;
using OrderBook.Model;

namespace OrderBook.IO;

/// <summary>
/// Stage 1 — reads <c>ticks.raw</c> and decodes its fixed 26-byte big-endian
/// records into a contiguous <see cref="Tick"/> array. Pure parsing: no book
/// logic, no formatting.
/// </summary>
public static class TickReader
{
    /// <summary>Size of one binary record in bytes.</summary>
    public const int RecordSize = 26;

    // Field offsets within a record.
    private const int OffSourceTime = 0;  // int64
    private const int OffSide = 8;         // byte
    private const int OffAction = 9;       // byte
    private const int OffOrderId = 10;     // int64
    private const int OffPrice = 18;       // int32
    private const int OffQty = 22;         // int32

    /// <summary>
    /// Reads and decodes every record in the file at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file length is not a whole multiple of <see cref="RecordSize"/>.
    /// </exception>
    public static Tick[] Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"'{path}' is {bytes.Length} bytes, not a multiple of the {RecordSize}-byte record size.");
        }

        int count = bytes.Length / RecordSize;
        var ticks = new Tick[count];
        ReadOnlySpan<byte> span = bytes;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> r = span.Slice(i * RecordSize, RecordSize);
            ticks[i] = new Tick(
                BinaryPrimitives.ReadInt64BigEndian(r.Slice(OffSourceTime, 8)),
                r[OffSide],
                r[OffAction],
                BinaryPrimitives.ReadInt64BigEndian(r.Slice(OffOrderId, 8)),
                BinaryPrimitives.ReadInt32BigEndian(r.Slice(OffPrice, 4)),
                BinaryPrimitives.ReadInt32BigEndian(r.Slice(OffQty, 4)));
        }

        return ticks;
    }
}
