#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Common;

public class CacheKeyService : ICacheKeyService
{
    private readonly IEnumerable<ICacheKeyProvider> cacheKeyProviders;

    public CacheKeyService(IEnumerable<ICacheKeyProvider> cacheKeyProviders)
    {
        this.cacheKeyProviders = cacheKeyProviders;
    }

    public bool TryToKey(object? obj, [NotNullWhen(true)] out ICacheKey? key)
    {
        if (obj is null)
        {
            key = null;
            return false;
        }

        if (obj is ICacheKey key0)
        {
            key = key0;
            return true;
        }

        if (obj is ISupportKey supportKey)
        {
            key = supportKey.GetKey(this);
            return true;
        }

        foreach (ICacheKeyProvider provider in cacheKeyProviders)
        {
            if (provider.TryToKey(this, obj, out ICacheKey? key1))
            {
                key = key1;
                return true;
            }
        }

        key = null;
        return false;
    }
}
