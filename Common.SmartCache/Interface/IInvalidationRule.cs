#nullable enable

using Common;

namespace Common.SmartCache;

[CacheInterchangeName("IR")]
public interface IInvalidationRule : ISupportLogString
{
    InvalidationReason Reason { get; }
}
