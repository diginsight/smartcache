namespace Diginsight.SmartCache;

internal interface IDynamicSmartCacheCoreOptions
{
    SmartCacheMode? Mode { get; }

    Expiration? MaxAge { get; }
    bool ForceDynamicMaxAge { get; }
    DateTimeOffset? MinimumCreationDate { get; }

    Expiration? AbsoluteExpiration { get; }
    Expiration? SlidingExpiration { get; }

    int? MissValueSizeThreshold { get; }
}
