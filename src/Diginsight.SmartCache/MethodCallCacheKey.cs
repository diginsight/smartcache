using Diginsight.SmartCache.Externalization;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;

namespace Diginsight.SmartCache;

[CacheInterchangeName("MCCK")]
public sealed record MethodCallCacheKey
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MethodCallCacheKey(ICacheKeyService cacheKeyService, Type type, string methodName, params object?[]? arguments)
        : this(type, methodName, cacheKeyService.Wrap(arguments ?? [ ])) { }

    [JsonConstructor]
    public MethodCallCacheKey(Type type, string methodName, object arguments)
    {
        Type = type;
        MethodName = methodName;
        Arguments = arguments;
    }

    public Type Type { get; }
    public string MethodName { get; }
    public object Arguments { get; }
}
