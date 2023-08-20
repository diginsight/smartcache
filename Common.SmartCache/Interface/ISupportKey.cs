namespace Common.SmartCache;

public interface ISupportKey
{
    ICacheKey GetKey(ICacheKeyService cacheKeyService);
}
