namespace Diginsight.SmartCache;

public interface ICachable
{
    object ToKey(ICacheKeyService service);
}
