#nullable enable

using Newtonsoft.Json;
using System;

namespace Common.SmartCache;

[CacheInterchangeName("VE")]
public sealed class ValueEntry<T> : IValueEntry
{
    public DateTime CreationDate { get; }
    public T Data { get; }
    public Type Type { get; } = typeof(T);

    object? IValueEntry.Data => Data;

    [JsonConstructor]
    public ValueEntry(T data, DateTime? creationDate = null)
    {
        Data = data;
        CreationDate = creationDate ?? DateTime.UtcNow;
    }
}
