namespace OrderBook.Model;

/// <summary>
/// Top-of-book aggregates produced for a single tick: best bid/ask price, the
/// summed quantity at that price, and the number of orders at that price, for
/// both sides. A side with no resting orders is represented by
/// <see cref="HasBid"/>/<see cref="HasAsk"/> being <c>false</c>, which the writer
/// renders as empty CSV fields (as opposed to a literal zero).
/// </summary>
/// <remarks>
/// Packed to <b>16 bytes</b> (down from 32) because one of these is stored per tick
/// — ~160 k per pass, several MB of sequential writes that the construct loop pays
/// for on every run. Prices fit in a <c>short</c> (range <c>[0, 16383]</c>) with
/// <c>-1</c> doubling as the "no resting order" sentinel, so the two <c>bool</c>
/// flags cost no storage; order counts fit in a <c>ushort</c>. The summed
/// quantities stay <c>int</c> to match the ladder's overflow-safe accumulation
/// (see design note D3) — a best level could in principle sum past 65 535.
/// Layout (2×<c>int</c> + 4×<c>short</c>) packs with no padding.
/// </remarks>
public readonly struct BookSnapshot
{
    private readonly int    _bq0;   // bid qty sum  (int: overflow-safe, per D3)
    private readonly int    _aq0;   // ask qty sum
    private readonly short  _b0;    // best bid price; -1 = no bid
    private readonly short  _a0;    // best ask price; -1 = no ask
    private readonly ushort _bn0;   // bid order count
    private readonly ushort _an0;   // ask order count

    /// <summary>
    /// Builds a snapshot. When <paramref name="hasBid"/>/<paramref name="hasAsk"/>
    /// is <c>false</c> the corresponding price is stored as the <c>-1</c> sentinel,
    /// regardless of the price argument, so <see cref="HasBid"/>/<see cref="HasAsk"/>
    /// read back correctly.
    /// </summary>
    public BookSnapshot(
        bool hasBid, int b0, int bq0, int bn0,
        bool hasAsk, int a0, int aq0, int an0)
    {
        _b0  = hasBid ? (short)b0 : (short)-1;
        _bq0 = bq0;
        _bn0 = (ushort)bn0;
        _a0  = hasAsk ? (short)a0 : (short)-1;
        _aq0 = aq0;
        _an0 = (ushort)an0;
    }

    /// <summary>Whether the BID side has any resting order.</summary>
    public bool HasBid => _b0 >= 0;

    /// <summary>BEST BID — highest BID price. Valid only when <see cref="HasBid"/>.</summary>
    public int B0 => _b0;

    /// <summary>Summed quantity of all orders at <see cref="B0"/>.</summary>
    public int BQ0 => _bq0;

    /// <summary>Count of orders at <see cref="B0"/>.</summary>
    public int BN0 => _bn0;

    /// <summary>Whether the ASK side has any resting order.</summary>
    public bool HasAsk => _a0 >= 0;

    /// <summary>BEST ASK — lowest ASK price. Valid only when <see cref="HasAsk"/>.</summary>
    public int A0 => _a0;

    /// <summary>Summed quantity of all orders at <see cref="A0"/>.</summary>
    public int AQ0 => _aq0;

    /// <summary>Count of orders at <see cref="A0"/>.</summary>
    public int AN0 => _an0;
}
