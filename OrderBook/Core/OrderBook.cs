using OrderBook.Model;

namespace OrderBook.Core;

/// <summary>
/// In-memory limit order book that replays the tick stream and exposes the
/// current top-of-book on each side.
/// </summary>
/// <remarks>
/// <para>
/// Backend: a <b>direct-indexed price ladder</b> per side. The instrument's
/// prices are confirmed to lie in <c>[0, 16383]</c>, so each side keeps two flat
/// arrays indexed by price — summed quantity and order count — plus a single
/// "best" cursor. Adds/deletes are O(1); the cursor is only walked to the next
/// populated level when the current best empties. No hashing, no tree.
/// </para>
/// <para>
/// A <see cref="Dictionary{TKey,TValue}"/> maps <c>OrderId → (side, price, qty)</c>
/// so that modifies and deletes can locate an order's current level. The delete
/// record's own price field is never trusted; the stored price is authoritative.
/// </para>
/// </remarks>
public sealed class OrderBook
{
    private const int MaxPrice = 16383;        // 2^14 - 1, the confirmed price ceiling
    private const int PriceCount = MaxPrice + 1;

    private const byte SideBid = (byte)'1';    // 0x31
    private const byte SideAsk = (byte)'2';    // 0x32

    private const byte ActionClearY = (byte)'Y';
    private const byte ActionClearF = (byte)'F';
    private const byte ActionAdd = (byte)'A';
    private const byte ActionModify = (byte)'M';
    private const byte ActionDelete = (byte)'D';

    private readonly Ladder _bid = new(isBid: true);
    private readonly Ladder _ask = new(isBid: false);

    // Sized to comfortably exceed the observed peak of live orders (~1.1k) so the
    // hot loop never triggers a resize/rehash.
    private readonly Dictionary<long, OrderEntry> _orders = new(capacity: 2048);

    /// <summary>Applies a single tick to the book, mutating in place.</summary>
    public void Apply(in Tick tick)
    {
        switch (tick.Action)
        {
            case ActionAdd:
            case ActionModify:
                Upsert(tick);
                break;
            case ActionDelete:
                Remove(tick.OrderId);
                break;
            case ActionClearY:
            case ActionClearF:
                Reset();
                break;
            // Any other action byte is undefined by the spec; leave the book unchanged.
        }
    }

    /// <summary>Reads the current top-of-book on both sides. O(1).</summary>
    public BookSnapshot Snapshot() => new(
        _bid.HasBest, _bid.BestPrice, _bid.BestQty, _bid.BestCount,
        _ask.HasBest, _ask.BestPrice, _ask.BestQty, _ask.BestCount);

    /// <summary>Empties both sides and the order index (used by <c>Y</c>/<c>F</c> and between timing runs).</summary>
    public void Reset()
    {
        _bid.Clear();
        _ask.Clear();
        _orders.Clear();
    }

    private void Upsert(in Tick tick)
    {
        int price = tick.Price;
        if ((uint)price > MaxPrice)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tick), price,
                $"Price {price} is outside the supported ladder range [0, {MaxPrice}].");
        }

        Ladder ladder = LadderFor(tick.Side);

        // 'A' replacing an existing id, or 'M' on an existing order: back out the
        // old contribution first. 'M' on a missing id falls through to a plain add.
        if (_orders.TryGetValue(tick.OrderId, out OrderEntry old))
        {
            LadderFor(old.Side).Remove(old.Price, old.Qty);
        }

        _orders[tick.OrderId] = new OrderEntry(tick.Side, price, tick.Qty);
        ladder.Add(price, tick.Qty);
    }

    private void Remove(long orderId)
    {
        // Delete of an unknown id is a no-op, per spec.
        if (_orders.Remove(orderId, out OrderEntry old))
        {
            LadderFor(old.Side).Remove(old.Price, old.Qty);
        }
    }

    private Ladder LadderFor(byte side) => side switch
    {
        SideBid => _bid,
        SideAsk => _ask,
        _ => throw new InvalidDataException(
            $"Order carries side byte 0x{side:X2}, expected '1' (BID) or '2' (ASK).")
    };

    /// <summary>A resting order's current location in the book.</summary>
    private readonly struct OrderEntry(byte side, int price, int qty)
    {
        public byte Side { get; } = side;
        public int Price { get; } = price;
        public int Qty { get; } = qty;
    }

    /// <summary>
    /// One side of the book as a price-indexed ladder. <see cref="_best"/> caches
    /// the current best price (<c>-1</c> when the side is empty) so reads are O(1)
    /// and the ladder is only scanned when the best level is emptied.
    /// </summary>
    private sealed class Ladder(bool isBid)
    {
        private readonly int[] _qty = new int[PriceCount];
        private readonly int[] _count = new int[PriceCount];
        private readonly bool _isBid = isBid;
        private int _best = -1;

        public bool HasBest => _best >= 0;
        public int BestPrice => _best;
        public int BestQty => _best >= 0 ? _qty[_best] : 0;
        public int BestCount => _best >= 0 ? _count[_best] : 0;

        public void Add(int price, int qty)
        {
            _qty[price] += qty;
            _count[price]++;

            if (_best < 0 || (_isBid ? price > _best : price < _best))
            {
                _best = price;
            }
        }

        public void Remove(int price, int qty)
        {
            _qty[price] -= qty;

            // Level still has orders, or it wasn't the best: nothing to re-scan.
            if (--_count[price] != 0 || price != _best)
            {
                return;
            }

            // The best level just emptied — walk to the next populated price.
            _best = _isBid ? NextDown(price - 1) : NextUp(price + 1);
        }

        public void Clear()
        {
            if (_best < 0)
            {
                return; // already empty; skip the array clears
            }

            Array.Clear(_qty);
            Array.Clear(_count);
            _best = -1;
        }

        private int NextDown(int from)
        {
            for (int p = from; p >= 0; p--)
            {
                if (_count[p] != 0)
                {
                    return p;
                }
            }
            return -1;
        }

        private int NextUp(int from)
        {
            for (int p = from; p <= MaxPrice; p++)
            {
                if (_count[p] != 0)
                {
                    return p;
                }
            }
            return -1;
        }
    }
}
