namespace Diginsight.SmartCache;

public sealed class SmartCacheCoreOptions : ISmartCacheCoreOptions
{
    private TimeSpan localEntryTolerance = TimeSpan.FromSeconds(10);

    public SmartCacheMode Mode { get; set; }

    public Expiration MaxAge { get; set; } = Expiration.Never;

    public Expiration AbsoluteExpiration { get; set; } = Expiration.Never;
    public Expiration SlidingExpiration { get; set; } = Expiration.Never;

    public int LocationPrefetchCount { get; set; } = 5;
    public int LocationMaxParallelism { get; set; } = 2;

    public int MissValueSizeThreshold { get; set; } = 5_000;

    public long LowPrioritySizeThreshold { get; set; } = 20_000;
    public long MidPrioritySizeThreshold { get; set; } = 10_000;

    public TimeSpan LocalEntryTolerance
    {
        get => localEntryTolerance;
        set => localEntryTolerance = value >= TimeSpan.Zero ? value : TimeSpan.Zero;
    }
}
