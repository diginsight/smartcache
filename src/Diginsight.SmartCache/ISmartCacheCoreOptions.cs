namespace Diginsight.SmartCache;

public interface ISmartCacheCoreOptions
{
    SmartCacheMode Mode { get; }

    Expiration MaxAge { get; }

    Expiration AbsoluteExpiration { get; }
    Expiration SlidingExpiration { get; }

    int LocationPrefetchCount { get; }
    int LocationMaxParallelism { get; }

    int MissValueSizeThreshold { get; }

    long LowPrioritySizeThreshold { get; }
    long MidPrioritySizeThreshold { get; }

    TimeSpan LocalEntryTolerance { get; }
}
