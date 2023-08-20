using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Common.SmartCache;

public class CacheEntityComparer : IEqualityComparer<ISupportKey>
{
    private readonly ICacheKeyService cacheKeyService;
    private readonly IEqualityComparer<object> keyComparer;

    public CacheEntityComparer(ICacheKeyService cacheKeyService, IEqualityComparer<object> keyComparer = null)
    {
        this.cacheKeyService = cacheKeyService;
        this.keyComparer = keyComparer ?? EqualityComparer<object>.Default;
    }

    public bool Equals(ISupportKey x, ISupportKey y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x?.GetType() != y?.GetType())
        {
            return false;
        }

        return keyComparer.Equals(x.GetKey(cacheKeyService), y.GetKey(cacheKeyService));
    }

    public int GetHashCode(ISupportKey obj)
    {
        return obj.GetKey(cacheKeyService) is { } key
            ? keyComparer.GetHashCode(key)
            : RuntimeHelpers.GetHashCode(obj);
    }
}
