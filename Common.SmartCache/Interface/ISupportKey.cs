namespace Common;

public interface ISupportKey
{
    ICacheKey GetKey(ICacheKeyService cacheKeyService);
}
