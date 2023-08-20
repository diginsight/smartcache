namespace Common.SmartCache;

public enum PersistedCacheUsage : byte
{
    Disabled,
    EnabledWithCreationDate,
    EnabledWithoutCreationDate,
}
