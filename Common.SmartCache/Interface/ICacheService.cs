﻿#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Common.SmartCache;

public interface ICacheService
{
    Task<T> GetAsync<T>(ICacheKey key, Func<Task<T>> fetchAsync, ICacheContext? cacheContext = null);

    bool TryGetDirectFromMemory(ICacheKey key, [NotNullWhen(true)] out Type? type, out object? value);

    void Invalidate(IInvalidationRule invalidationRule, bool broadcast);

    void Invalidate(InvalidationDescriptor descriptor);

    void AddExternalMiss(CacheMissDescriptor descriptor);
}
