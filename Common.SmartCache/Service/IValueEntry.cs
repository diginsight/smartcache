#nullable enable

using System;

namespace Common;

public interface IValueEntry
{
    object? Data { get; }
    Type Type { get; }
    DateTime CreationDate { get; }

    public static IValueEntry Create(object? data, Type type, DateTime? creationDate = null)
    {
        return (IValueEntry)Activator.CreateInstance(typeof(ValueEntry<>).MakeGenericType(type), data, creationDate)!;
    }
}
