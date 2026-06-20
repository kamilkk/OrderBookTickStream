namespace OrderBook.Model;

/// <summary>
/// A single decoded record from <c>ticks.raw</c>. Immutable value type so the
/// whole stream can live in one contiguous <see cref="Tick"/> array with no
/// per-record heap allocation.
/// </summary>
/// <remarks>
/// <see cref="Side"/> and <see cref="Action"/> are kept as the raw bytes read
/// from the file (ASCII), e.g. side <c>0x31</c> = '1' (BID), <c>0x32</c> = '2'
/// (ASK), <c>0x00</c> = none. Interpreting them is the order book's job, not the
/// reader's.
/// </remarks>
public readonly struct Tick(long sourceTime, byte side, byte action, long orderId, int price, int qty)
{
    /// <summary>Exchange timestamp.</summary>
    public long SourceTime { get; } = sourceTime;

    /// <summary>Raw side byte: <c>0x00</c> none, <c>0x31</c> BID, <c>0x32</c> ASK.</summary>
    public byte Side { get; } = side;

    /// <summary>Raw action byte: 'Y'/'F' clear, 'A' add, 'M' modify, 'D' delete.</summary>
    public byte Action { get; } = action;

    /// <summary>Unique order identifier assigned by the exchange.</summary>
    public long OrderId { get; } = orderId;

    /// <summary>Limit price.</summary>
    public int Price { get; } = price;

    /// <summary>Order quantity.</summary>
    public int Qty { get; } = qty;
}
