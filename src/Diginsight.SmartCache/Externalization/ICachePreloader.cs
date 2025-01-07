namespace Diginsight.SmartCache.Externalization;

public interface ICachePreloader
{
    Task PreloadAsync<T>(object key, Func<Task<T>> fetchAsync);
}
