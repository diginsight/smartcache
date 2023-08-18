#nullable enable

using Common;

namespace Common;

[CacheInterchangeName("IR")]
public interface IInvalidationRule : ISupportLogString
{
    InvalidationReason Reason { get; }
}
