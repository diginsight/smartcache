using Diginsight.Options;

namespace Diginsight.SmartCache;

public sealed class SmartCacheCoreOptions : ISmartCacheCoreOptions, IVolatilelyConfigurable
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

    object IVolatilelyConfigurable.MakeFiller() => new Filler(this);

    private sealed class Filler
    {
        private readonly SmartCacheCoreOptions filled;

        public Filler(SmartCacheCoreOptions filled)
        {
            this.filled = filled;
        }

        public SmartCacheMode SmartCacheMode
        {
            get => filled.Mode;
            set => filled.Mode = value;
        }

        public Expiration AbsoluteExpiration
        {
            get => filled.AbsoluteExpiration;
            set => filled.AbsoluteExpiration = value;
        }

        public Expiration SlidingExpiration
        {
            get => filled.SlidingExpiration;
            set => filled.SlidingExpiration = value;
        }

        public int LocationPrefetchCount
        {
            get => filled.LocationPrefetchCount;
            set => filled.LocationPrefetchCount = value;
        }

        public int LocationMaxParallelism
        {
            get => filled.LocationMaxParallelism;
            set => filled.LocationMaxParallelism = value;
        }

        public int MissValueSizeThreshold
        {
            get => filled.MissValueSizeThreshold;
            set => filled.MissValueSizeThreshold = value;
        }

        public long LowPrioritySizeThreshold
        {
            get => filled.LowPrioritySizeThreshold;
            set => filled.LowPrioritySizeThreshold = value;
        }

        public long MidPrioritySizeThreshold
        {
            get => filled.MidPrioritySizeThreshold;
            set => filled.MidPrioritySizeThreshold = value;
        }

        public TimeSpan LocalEntryTolerance
        {
            get => filled.LocalEntryTolerance;
            set => filled.LocalEntryTolerance = value;
        }
    }
}
