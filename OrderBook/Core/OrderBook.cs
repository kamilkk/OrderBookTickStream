using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
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
/// Orders are tracked in a flat open-addressing hash table (<see cref="OrderMap"/>)
/// keyed on <c>OrderId</c>, replacing the BCL <see cref="Dictionary{TKey,TValue}"/>.
/// The order lookup is the single largest construct-phase cost (it is memory-latency
/// bound — keys hash to scattered slots), so the map is laid out structure-of-arrays
/// with a cheap multiplicative hash and a managed-ref upsert that finds or inserts in
/// one probe and returns a <c>ref</c> straight into the value slot.
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

    private readonly Ladder   _bid    = new(isBid: true);
    private readonly Ladder   _ask    = new(isBid: false);
    private readonly OrderMap _orders = new();

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

    /// <summary>
    /// Whether <see cref="Prefetch"/> does anything on this CPU. <c>true</c> only where
    /// x86 SSE is available; the JIT folds it to a constant so callers can gate the
    /// prefetch call site and have the whole thing eliminated on other architectures.
    /// </summary>
    public static bool PrefetchSupported => Sse.IsSupported;

    /// <summary>
    /// Issues a non-blocking hint to start pulling the order-map slot that
    /// <paramref name="orderId"/> will probe into cache, so the miss is in flight
    /// before the next <see cref="Apply"/> touches it. Pure hint: no effect on results,
    /// and a no-op where <see cref="PrefetchSupported"/> is <c>false</c>.
    /// </summary>
    public void Prefetch(long orderId) => _orders.Prefetch(orderId);

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
        ref OrderEntry slot = ref _orders.GetOrAdd(tick.OrderId, out bool existed);
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
    /// Flat open-addressing hash table mapping <c>long</c> order IDs to
    /// <see cref="OrderEntry"/> values. Replaces <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structure-of-arrays layout — a dense <c>long[]</c> of keys probed on its own
    /// (8 keys per 64-byte cache line) plus a parallel <c>OrderEntry[]</c> of values
    /// touched only once a slot is located. This replaces both the dictionary's
    /// bucket→entry hop and an interleaved key+value slot (which would drag value
    /// bytes through cache during probing). Capacity is fixed at 4096 (power of two);
    /// peak concurrent live orders are ~1 150, a ~28 % load factor giving an expected
    /// probe length near 1.
    /// </para>
    /// <para>
    /// Hashing is a single multiply by the 64-bit golden ratio (Fibonacci hashing)
    /// followed by a shift to keep the high bits — cheap, and well-dispersed for the
    /// near-sequential, timestamp-derived order IDs in this stream. <c>OrderId == 0</c>
    /// never occurs (ids are ~2.4 × 10¹⁰), so a zero key marks an empty slot and no
    /// separate state byte is needed.
    /// </para>
    /// <para>
    /// Deletion uses <b>backward-shift compaction</b> (no tombstones): subsequent
    /// entries in the cluster are pulled back to close the gap, so every non-empty
    /// slot is reachable by a probe that stops at the first zero key. This keeps the
    /// table correct across the 79 319 unique ids seen over the session without ever
    /// accumulating dead slots.
    /// </para>
    /// </remarks>
    private sealed class OrderMap
    {
        private const int  Capacity  = 4096;                 // power of two
        private const int  Mask      = Capacity - 1;
        private const int  HashShift = 64 - 12;              // keep top 12 bits → [0, 4096)
        private const ulong Golden   = 0x9E3779B97F4A7C15UL; // 2^64 / φ

        // Structure-of-arrays: keys are probed in their own dense array (8 keys per
        // 64-byte cache line), so a probe scan drags no value bytes through cache.
        // The values array is touched only once the slot is located. _keys[i] == 0
        // marks an empty slot (OrderId 0 never occurs; ids are ~2.4 × 10¹⁰).
        private readonly long[]       _keys   = new long[Capacity];
        private readonly OrderEntry[] _values = new OrderEntry[Capacity];

        private static int Hash(long key) => (int)((ulong)key * Golden >> HashShift);

        /// <summary>
        /// Returns a managed reference to the value slot for <paramref name="key"/>,
        /// inserting an empty slot if absent (<paramref name="existed"/> = <c>false</c>);
        /// the caller must write a valid value before the next call. One probe sequence
        /// serves both lookup and insert.
        /// </summary>
        public ref OrderEntry GetOrAdd(long key, out bool existed)
        {
            int i = Hash(key);
            while (true)
            {
                long k = _keys[i];
                if (k == key) { existed = true;  return ref _values[i]; }
                if (k == 0)   { _keys[i] = key; existed = false; return ref _values[i]; }
                i = (i + 1) & Mask;
            }
        }

        /// <summary>
        /// Removes <paramref name="key"/> and returns its value, closing the cluster
        /// gap by backward-shift compaction. No-op (returns <c>false</c>) if absent.
        /// </summary>
        public bool Remove(long key, out OrderEntry value)
        {
            int i = Hash(key);
            while (true)
            {
                long k = _keys[i];
                if (k == 0)   { value = default; return false; }
                if (k == key) { value = _values[i]; break; }
                i = (i + 1) & Mask;
            }

            // Backward-shift: pull each following entry into the hole if its natural
            // home is at or before the hole (so a probe from that home still finds it).
            int hole = i;
            int j    = (i + 1) & Mask;
            while (_keys[j] != 0)
            {
                int home = Hash(_keys[j]);
                if (((hole - home) & Mask) < ((j - home) & Mask))
                {
                    _keys[hole]   = _keys[j];
                    _values[hole] = _values[j];
                    hole = j;
                }
                j = (j + 1) & Mask;
            }
            _keys[hole] = 0;   // mark hole empty (stale value is unreachable)
            return true;
        }

        // Zeroing the keys marks every slot empty; values need no clearing.
        public void Clear() => Array.Clear(_keys);

        /// <summary>
        /// Prefetches (T0 — into all cache levels) the key slot <paramref name="key"/>
        /// hashes to. The lookup is memory-latency bound (confirmed by a stride sweep,
        /// see performance-findings.md §9); issuing this a few ticks ahead of the
        /// matching <see cref="GetOrAdd"/>/<see cref="Remove"/> lets the miss overlap
        /// useful work. x86 only — <see cref="Sse.IsSupported"/> folds to <c>false</c>
        /// elsewhere and the body disappears. The raw address may go stale if the GC
        /// moves the array, but this loop is allocation-free and a prefetch to a stale
        /// address is a harmless hint, so no pinning is needed.
        /// </summary>
        public unsafe void Prefetch(long key)
        {
            if (Sse.IsSupported)
                Sse.Prefetch0(Unsafe.AsPointer(ref _keys[Hash(key)]));
        }
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
        // Summed qty and order count per price kept together in one array, so a
        // level touch hits a single cache line (8 levels per 64-byte line) rather
        // than the two lines two parallel arrays would cost.
        private struct Level { public int Qty; public int Count; }

        private readonly Level[] _levels = new Level[PriceCount];
        private readonly bool  _isBid = isBid;
        private int _best    = -1;
        private int _minUsed = MaxPrice + 1;  // sentinel: no entries yet
        private int _maxUsed = -1;

        // The best level's summed qty and order count, cached as scalars so the
        // per-tick Snapshot() read needs no array indexing. Kept in lock-step with
        // _levels[_best]; both 0 when the side is empty.
        private int _bestQty;
        private int _bestCount;

        public bool HasBest  => _best >= 0;
        public int  BestPrice => _best;
        public int  BestQty   => _bestQty;
        public int  BestCount => _bestCount;

        public void Add(int price, int qty)
        {
            ref Level lvl = ref _levels[price];
            int newQty   = lvl.Qty += qty;
            int newCount = ++lvl.Count;

            if (price < _minUsed) _minUsed = price;
            if (price > _maxUsed) _maxUsed = price;

            if (_best < 0 || (_isBid ? price > _best : price < _best))
            {
                _best      = price;          // new best level
                _bestQty   = newQty;
                _bestCount = newCount;
            }
            else if (price == _best)
            {
                _bestQty   = newQty;         // added into the existing best level
                _bestCount = newCount;
            }
        }

        public void Remove(int price, int qty)
        {
            ref Level lvl = ref _levels[price];
            int newQty   = lvl.Qty -= qty;
            int newCount = --lvl.Count;

            if (price != _best)
                return;                      // not the best level — cache unaffected

            if (newCount != 0)
            {
                _bestQty   = newQty;         // best level still populated
                _bestCount = newCount;
                return;
            }

            // The best level just emptied — walk to the next populated price.
            _best = _isBid ? NextDown(price - 1) : NextUp(price + 1);
            if (_best >= 0)
            {
                _bestQty   = _levels[_best].Qty;
                _bestCount = _levels[_best].Count;
            }
            else
            {
                _bestQty   = 0;
                _bestCount = 0;
            }
        }

        public void Clear()
        {
            if (_best < 0) return; // already empty; skip the array clear

            // Zero only the price range that was actually written to.
            // Active prices for this instrument cluster in a narrow band (~30–50
            // ticks near the mid), so this clears ~400 bytes instead of 128 KB.
            int len = _maxUsed - _minUsed + 1;
            Array.Clear(_levels, _minUsed, len);
            _best      = -1;
            _bestQty   = 0;
            _bestCount = 0;
            _minUsed   = MaxPrice + 1;
            _maxUsed   = -1;
        }

        private int NextDown(int from)
        {
            for (int p = from; p >= 0; p--)
            {
                if (_levels[p].Count != 0) return p;
            }
            return -1;
        }

        private int NextUp(int from)
        {
            for (int p = from; p <= MaxPrice; p++)
            {
                if (_levels[p].Count != 0) return p;
            }
            return -1;
        }
    }
}
