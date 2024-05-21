namespace Diginsight.SmartCache;

public interface ICacheKeyProvider
{
    object? ToKey(ICacheKeyService service, object? obj);
}
