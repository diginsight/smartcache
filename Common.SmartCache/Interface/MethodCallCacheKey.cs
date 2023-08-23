using Newtonsoft.Json;
using System;

namespace Common.SmartCache;


[CacheInterchangeName("MCCK")]
public sealed record MethodCallCacheKey : ICacheKey
{
    public MethodCallCacheKey(ICacheKeyService cacheKeyService, Type type, string methodName, params object?[]? arguments)
        : this(type, methodName, cacheKeyService.Wrap(arguments ?? Array.Empty<object?>()))
    {
    }

    [JsonConstructor]
    public MethodCallCacheKey(Type type, string methodName, ICacheKey arguments)
    {
        Type = type;
        MethodName = methodName;
        Arguments = arguments;
    }

    public Type Type { get; }
    public string MethodName { get; }
    public ICacheKey Arguments { get; }

    public string ToLogString() => ToString();
}
