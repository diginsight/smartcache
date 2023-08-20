#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Common.SmartCache;

public static class DictionaryExtensions
{
    public static IDictionary<string, object?> Concat(this IDictionary<string, object?> firstDict, IDictionary<string, object?> secondDict)
    {
        return Concat(new[] { firstDict, secondDict });
    }

    public static IDictionary<string, object?> Concat(IEnumerable<IDictionary<string, object?>> dictionaries)
    {
        if (!dictionaries.Any())
        {
            return new Dictionary<string, object?>();
        }

        IDictionary<string, object?> result = new Dictionary<string, object?>(dictionaries.First().Where(static kv => kv.Value is not null));
        foreach (IDictionary<string, object?> dictionary in dictionaries.Skip(1))
        {
            foreach ((string key, object? value) in dictionary)
            {
                if (value is null || result.Keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result[key] = value;
            }
        }

        return result;
    }

    public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue>? source, TKey key, TValue defaultValue)
    {
        return source is null ? default : source.TryGetValue(key, out TValue? outValue) ? outValue : defaultValue;
    }

    public static T? TryGetValue<T>(this IDictionary<string, object?>? source, string key)
    {
        if (source is null)
        {
            return default;
        }

        Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (!source.TryGetValue(key, out object? rawValue))
        {
            if (source.Keys.FirstOrDefault(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } altKey)
            {
                return default;
            }

            rawValue = source[altKey];
        }

        return rawValue switch
        {
            null => default,
            T value => value,
            _ => (T)Convert.ChangeType(rawValue, type),
        };
    }

    public static object? TryGetValue(this IDictionary<string, object?>? source, string key, Type origType)
    {
        static object? GetDefault(Type type)
        {
            return type.IsValueType
                ? Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Default(type), typeof(object))).Compile()()
                : null;
        }

        if (source is null)
        {
            return GetDefault(origType);
        }

        Type type = Nullable.GetUnderlyingType(origType) ?? origType;

        if (!source.TryGetValue(key, out object? rawValue))
        {
            if (source.Keys.FirstOrDefault(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } altKey)
            {
                return GetDefault(origType);
            }

            rawValue = source[altKey];
        }

        return rawValue is null ? GetDefault(origType) : Convert.ChangeType(rawValue, type);
    }

    public static bool TryGetValue<TKey, TValue>(this IDictionary<TKey, TValue> source, IEnumerable<TKey> keyList, out TValue? value)
    {
        value = default;
        foreach(var key in keyList)
        {
            if(source.TryGetValue(key, out value) && value != null)
            {
                return true;
            }
        }
        return false;
    }

}
