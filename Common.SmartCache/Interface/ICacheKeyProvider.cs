#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace Common.SmartCache;

public interface ICacheKeyProvider
{
    bool TryToKey(ICacheKeyService service, object? obj, [NotNullWhen(true)] out ICacheKey? key);
}
 