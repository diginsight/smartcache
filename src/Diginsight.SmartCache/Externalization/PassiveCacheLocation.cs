namespace Diginsight.SmartCache.Externalization;

public abstract class PassiveCacheLocation : CacheLocation
{
    protected PassiveCacheLocation(string id)
        : base(id) { }

    public void WriteAndForget(CachePayloadHolder<object> keyHolder, IValueEntry entry, Expiration expiration, Func<Task> notifyMissAsync)
    {
        TaskUtils.RunAndForget(
            async () =>
            {
                if (await TryWriteAsync(keyHolder, entry, expiration))
                {
                    await notifyMissAsync();
                }
            }
        );
    }

    protected abstract Task<bool> TryWriteAsync(CachePayloadHolder<object> keyHolder, IValueEntry entry, Expiration expiration);

    public void DeleteAndForget(CachePayloadHolder<object> keyHolder)
    {
        TaskUtils.RunAndForget(() => DeleteAsync(keyHolder));
    }

    protected abstract Task DeleteAsync(CachePayloadHolder<object> keyHolder);
}
