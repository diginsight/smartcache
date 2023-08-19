#nullable enable

using DotNext;
using System;
using System.Threading.Tasks;

namespace Common;

public interface ICachePersistence
{
    Task PersistAsync(ICacheKey key, IValueEntry valueEntry);

    Task<Optional<T>> TryRetrieveAsync<T>(ICacheKey key, DateTime? minimumCreationDate);

    Task RemoveAsync(ICacheKey key);
}
