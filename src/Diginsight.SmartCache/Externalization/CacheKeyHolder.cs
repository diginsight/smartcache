using Diginsight.Strings;

namespace Diginsight.SmartCache.Externalization;

public sealed class CacheKeyHolder : CachePayloadHolder<object>, ILogStringable
{
    bool ILogStringable.IsDeep => false;
    object ILogStringable.Subject => Payload;

    public CacheKeyHolder(object key)
        : base(key, SmartCacheObservability.Tags.Subject.Key) { }

    public void AppendTo(AppendingContext appendingContext) => appendingContext.ComposeAndAppend(Payload);
}
