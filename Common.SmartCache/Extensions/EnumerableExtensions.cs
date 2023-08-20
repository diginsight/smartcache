
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Common.SmartCache;

public static class EnumerableExtensions
{
    public static IEnumerable<TResult> ConcatAndCast<TResult>(this IEnumerable source, IEnumerable other)
    {
        return source.Cast<TResult>()
            .Concat(other.Cast<TResult>()).ToList();
    }

    public static IEnumerable<T>? NullIfEmpty<T>(this IEnumerable<T> source)
    {
        if (source is null || !source.Any())
        {
            return null;
        }

        return source;
    }
}
