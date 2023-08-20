using System;

namespace Common.SmartCache;

public interface ICacheServiceOptions
{
    int DefaultMaxAge { get; }

    int AbsoluteExpiration { get; }
    int SlidingExpiration { get; }

    TimeSpan CrossPodRequestTimeout { get; }
    int CrossPodPrefetchCount { get; }
    int CrossPodMaxParallelism { get; }

    long SizeLimit { get; }
    int MissValueSizeThreshold { get; }

    bool PersistCache { get; }
    PersistedCacheUsage PersistedCacheUsageOnFailure { get; }

    long LowPrioritySizeThreshold { get; }
    long MidPrioritySizeThreshold { get; }
}
