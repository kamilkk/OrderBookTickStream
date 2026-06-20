namespace OrderBook.Model;

/// <summary>
/// Top-of-book aggregates produced for a single tick: best bid/ask price, the
/// summed quantity at that price, and the number of orders at that price, for
/// both sides. A side with no resting orders is represented by
/// <see cref="HasBid"/>/<see cref="HasAsk"/> being <c>false</c>, which the writer
/// renders as empty CSV fields (as opposed to a literal zero).
/// </summary>
public readonly struct BookSnapshot(
    bool hasBid, int b0, int bq0, int bn0,
    bool hasAsk, int a0, int aq0, int an0)
{
    /// <summary>Whether the BID side has any resting order.</summary>
    public bool HasBid { get; } = hasBid;

    /// <summary>BEST BID — highest BID price. Valid only when <see cref="HasBid"/>.</summary>
    public int B0 { get; } = b0;

    /// <summary>Summed quantity of all orders at <see cref="B0"/>.</summary>
    public int BQ0 { get; } = bq0;

    /// <summary>Count of orders at <see cref="B0"/>.</summary>
    public int BN0 { get; } = bn0;

    /// <summary>Whether the ASK side has any resting order.</summary>
    public bool HasAsk { get; } = hasAsk;

    /// <summary>BEST ASK — lowest ASK price. Valid only when <see cref="HasAsk"/>.</summary>
    public int A0 { get; } = a0;

    /// <summary>Summed quantity of all orders at <see cref="A0"/>.</summary>
    public int AQ0 { get; } = aq0;

    /// <summary>Count of orders at <see cref="A0"/>.</summary>
    public int AN0 { get; } = an0;
}
