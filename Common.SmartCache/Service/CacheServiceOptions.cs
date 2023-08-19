using System;

namespace Common;

public sealed class CacheServiceOptions : ICacheServiceOptions
{
    public int DefaultMaxAge { get; set; }

    public int AbsoluteExpiration { get; set; }
    public int SlidingExpiration { get; set; }

    public TimeSpan CrossPodRequestTimeout { get; set; }
    public int CrossPodPrefetchCount { get; set; } = 5;
    public int CrossPodMaxParallelism { get; set; } = 2;

    public long SizeLimit { get; set; } = 1_000_000;
    public int MissValueSizeThreshold { get; set; } = 5_000;

    public bool PersistCache { get; set; }
    public PersistedCacheUsage PersistedCacheUsageOnFailure { get; set; }

    public long LowPrioritySizeThreshold { get; set; } = 20_000;
    public long MidPrioritySizeThreshold { get; set; } = 10_000;
}
