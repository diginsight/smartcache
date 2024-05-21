namespace Diginsight.SmartCache.Externalization;

public sealed class CacheKeyHolder : CachePayloadHolder<object>
{
    public CacheKeyHolder(object key)
        : base(key, SmartCacheObservability.Tags.Subject.Key) { }
}
