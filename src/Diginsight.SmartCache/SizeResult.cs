using System.Runtime.CompilerServices;

namespace Diginsight.SmartCache;

public readonly ref struct SizeResult
{
    public long Sz { get; }
    public bool Fxd { get; private init; }
    public Exception? Exc { get; }

    public SizeResult(long sz, Exception exc)
        : this(sz, false, exc) { }

    public SizeResult(long sz, bool fxd = false)
        : this(sz, fxd, null) { }

    private SizeResult(long sz, bool fxd, Exception? exc)
    {
        Sz = sz;
        Fxd = fxd;
        Exc = exc;
    }

    internal static long SafeAdd(long l1, long l2)
    {
        try
        {
            return checked(l1 + l2);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    public static SizeResult operator +(SizeResult r1, SizeResult r2)
    {
        return new SizeResult(
            SafeAdd(r1.Sz, r2.Sz),
            r1.Fxd && r2.Fxd,
            r1.Exc is { } exc1 ? r2.Exc is { } exc2 ? new AggregateException(exc1, exc2) : exc1 : r2.Exc
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SizeResult operator +(SizeResult r, Exception e) => new SizeResult(0, e) + r;

    public static SizeResult operator ~(SizeResult r) => r.Fxd ? r with { Fxd = false } : r;
}
