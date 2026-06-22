using System.Runtime.InteropServices;
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
/// Clear only zeroes the sub-range of prices that were actually touched
/// (<c>[_minUsed, _maxUsed]</c>) rather than the full 16 384-entry array.
/// </para>
/// <para>
/// Orders are tracked in a <see cref="Dictionary{TKey,TValue}"/> keyed on
/// <c>OrderId</c>. Upsert uses <see cref="CollectionsMarshal.GetValueRefOrAddDefault"/>
/// to find or insert a slot in a <b>single hash probe</b>, returning a managed
/// reference directly into the dictionary's internal array. This eliminates the
/// second lookup that the BCL indexer would otherwise require.
/// The delete record's own price field is never trusted; the stored price is authoritative.
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
    private const byte ActionAdd    = (byte)'A';
    private const byte ActionModify = (byte)'M';
    private const byte ActionDelete = (byte)'D';

    private readonly Ladder                      _bid    = new(isBid: true);
    private readonly Ladder                      _ask    = new(isBid: false);
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

        bool isBid = tick.Side switch
        {
            SideBid => true,
            SideAsk => false,
            _ => throw new InvalidDataException(
                $"Order carries side byte 0x{tick.Side:X2}, expected '1' (BID) or '2' (ASK).")
        };

        Ladder newLadder = isBid ? _bid : _ask;

        // Single-probe ref-access: finds or inserts the slot in one pass.
        // On an existing order (A-replace or M), back out the old level contribution first.
        ref OrderEntry slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_orders, tick.OrderId, out bool existed);
        if (existed)
        {
            (slot.IsBid ? _bid : _ask).Remove(slot.Price, slot.Qty);
        }
        slot = new OrderEntry(isBid, (ushort)price, (ushort)tick.Qty);
        newLadder.Add(price, tick.Qty);
    }

    private void Remove(long orderId)
    {
        // Delete of an unknown id is a no-op, per spec.
        if (_orders.Remove(orderId, out OrderEntry old))
        {
            (old.IsBid ? _bid : _ask).Remove(old.Price, old.Qty);
        }
    }

    // -------------------------------------------------------------------------
    // Nested types
    // -------------------------------------------------------------------------

    /// <summary>
    /// A resting order's current location in the book.
    /// Packed to 6 bytes (vs 12 for the previous byte+int+int layout):
    /// <c>bool IsBid</c> (1 B) + 1 B pad + <c>ushort Price</c> (2 B) + <c>ushort Qty</c> (2 B).
    /// Price ≤ 16383 and Qty ≤ 300 both fit comfortably in ushort.
    /// </summary>
    private readonly struct OrderEntry(bool isBid, ushort price, ushort qty)
    {
        public bool   IsBid { get; } = isBid;
        public ushort Price { get; } = price;
        public ushort Qty   { get; } = qty;
    }

    /// <summary>
    /// One side of the book as a price-indexed ladder. <see cref="_best"/> caches
    /// the current best price (<c>-1</c> when the side is empty) so reads are O(1)
    /// and the ladder is only scanned when the best level is emptied.
    /// <c>_minUsed</c>/<c>_maxUsed</c> track the dirty price range so Clear only
    /// zeroes the sub-array that was actually touched.
    /// </summary>
    private sealed class Ladder(bool isBid)
    {
        private readonly int[] _qty   = new int[PriceCount];
        private readonly int[] _count = new int[PriceCount];
        private readonly bool  _isBid = isBid;
        private int _best    = -1;
        private int _minUsed = MaxPrice + 1;  // sentinel: no entries yet
        private int _maxUsed = -1;

        public bool HasBest  => _best >= 0;
        public int  BestPrice => _best;
        public int  BestQty   => _best >= 0 ? _qty[_best]   : 0;
        public int  BestCount => _best >= 0 ? _count[_best] : 0;

        public void Add(int price, int qty)
        {
            _qty[price]   += qty;
            _count[price]++;

            if (price < _minUsed) _minUsed = price;
            if (price > _maxUsed) _maxUsed = price;

            if (_best < 0 || (_isBid ? price > _best : price < _best))
                _best = price;
        }

        public void Remove(int price, int qty)
        {
            _qty[price] -= qty;

            // Level still has orders, or it wasn't the best: nothing to re-scan.
            if (--_count[price] != 0 || price != _best)
                return;

            // The best level just emptied — walk to the next populated price.
            _best = _isBid ? NextDown(price - 1) : NextUp(price + 1);
        }

        public void Clear()
        {
            if (_best < 0) return; // already empty; skip the array clears

            // Zero only the price range that was actually written to.
            // Active prices for this instrument cluster in a narrow band (~30–50
            // ticks near the mid), so this clears ~200 bytes instead of 128 KB.
            int len = _maxUsed - _minUsed + 1;
            Array.Clear(_qty,   _minUsed, len);
            Array.Clear(_count, _minUsed, len);
            _best    = -1;
            _minUsed = MaxPrice + 1;
            _maxUsed = -1;
        }

        private int NextDown(int from)
        {
            for (int p = from; p >= 0; p--)
            {
                if (_count[p] != 0) return p;
            }
            return -1;
        }

        private int NextUp(int from)
        {
            for (int p = from; p <= MaxPrice; p++)
            {
                if (_count[p] != 0) return p;
            }
            return -1;
        }
    }
}
