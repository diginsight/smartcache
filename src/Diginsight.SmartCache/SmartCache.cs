using Diginsight.Diagnostics;
using Diginsight.Options;
using Diginsight.Runtime;
using Diginsight.SmartCache.Externalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Diginsight.SmartCache;

internal sealed class SmartCache : ISmartCache
{
    private readonly ILogger logger;
    private readonly ICacheCompanion companion;
    private readonly ISmartCacheCoreOptions coreOptions;
    private readonly IClassAwareOptionsMonitor<DynamicSmartCacheCoreOptions> dynamicCoreOptionsMonitor;
    private readonly SmartCacheDownstreamSettings downstreamSettings;
    private readonly TimeProvider timeProvider;

    private readonly IMemoryCache memoryCache;
    private readonly IReadOnlyDictionary<string, PassiveCacheLocation> passiveLocations;

    private readonly IDictionary<object, ValueTuple> keys = new ConcurrentDictionary<object, ValueTuple>();
    private readonly ExternalMissDictionary externalMissDictionary = new ();
    private readonly ConcurrentDictionary<string, Latency> locationLatencies = new ();

    private long memoryCacheSize = 0;
    private bool warnedModeDowngrade = false;

    public SmartCache(
        ILogger<SmartCache> logger,
        ICacheCompanion companion,
        IOptions<SmartCacheCoreOptions> coreOptions,
        IClassAwareOptionsMonitor<DynamicSmartCacheCoreOptions> dynamicCoreOptionsMonitor,
        IOptionsMonitor<MemoryCacheOptions> memoryCacheOptionsMonitor,
        ILoggerFactory loggerFactory,
        SmartCacheDownstreamSettings downstreamSettings,
        TimeProvider? timeProvider = null
    )
    {
        this.logger = logger;
        this.companion = companion;
        this.coreOptions = coreOptions.Value;
        this.dynamicCoreOptionsMonitor = dynamicCoreOptionsMonitor;
        this.downstreamSettings = downstreamSettings;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        memoryCache = new MemoryCache(memoryCacheOptionsMonitor.Get(nameof(SmartCache)), loggerFactory);

        passiveLocations = companion.PassiveLocations.ToDictionary(static x => x.Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static DateTimeOffset Truncate(DateTimeOffset timestamp)
    {
        return new DateTimeOffset(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, timestamp.Second, timestamp.Offset);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<T> GetAsync<T>(
        object key,
        Func<CancellationToken, Task<T>> fetchAsync,
        SmartCacheOperationOptions? operationOptions,
        Type? callerType,
        CancellationToken cancellationToken
    )
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger, () => new { key, operationOptions, callerType });

        Type finalCallerType = callerType ?? RuntimeUtils.GetCallerType();
        SmartCacheOperationOptions finalOperationOptions = operationOptions ?? new SmartCacheOperationOptions();

        CachePayloadHolder<object> keyHolder = new CacheKeyHolder(key);

        SmartCacheObservability.Instruments.Calls.Add(1);

        if (finalOperationOptions.Disabled)
        {
            SmartCacheObservability.Instruments.Sources.Add(1, SmartCacheObservability.Tags.Type.Disabled);

            using (SmartCacheObservability.Instruments.FetchDuration.StartLap(SmartCacheObservability.Tags.Type.Disabled))
            {
                activity?.SetTag("cache.disabled", 1);
                return await fetchAsync(cancellationToken);
            }
        }

        IDynamicSmartCacheCoreOptions dynamicCoreOptions = dynamicCoreOptionsMonitor.Get(finalCallerType);

        Expiration? maxAge = finalOperationOptions.MaxAge;
        DateTimeOffset timestamp = Truncate(timeProvider.GetUtcNow());
        DateTimeOffset minimumCreationDate = GetMinimumCreationDate(ref maxAge, timestamp, dynamicCoreOptions);
        bool forceFetch = maxAge.Value == Expiration.Zero || minimumCreationDate >= timestamp;

        using (forceFetch ? downstreamSettings.WithZeroMaxAge() : downstreamSettings.WithMinimumCreationDate(minimumCreationDate))
        {
            return await GetAsync(
                keyHolder,
                fetchAsync,
                timestamp,
                forceFetch ? null : minimumCreationDate,
                finalOperationOptions.AbsoluteExpiration,
                finalOperationOptions.SlidingExpiration,
                dynamicCoreOptions,
                cancellationToken
            );
        }
    }

    private DateTimeOffset GetMinimumCreationDate([NotNull] ref Expiration? maxAge, DateTimeOffset timestamp, IDynamicSmartCacheCoreOptions dynamicCoreOptions)
    {
        Expiration finalMaxAge = dynamicCoreOptions is { ForceDynamicMaxAge: true, MaxAge: { } dynamicMaxAge }
            ? dynamicMaxAge
            : Choose(dynamicCoreOptions.MaxAge, maxAge, coreOptions.MaxAge);

        DateTimeOffset minimumCreationDate = finalMaxAge.IsNever ? DateTimeOffset.MinValue : timestamp - finalMaxAge.Value;
        if (dynamicCoreOptions.MinimumCreationDate is { } dynamicMinimumCreationDate && dynamicMinimumCreationDate > minimumCreationDate)
        {
            minimumCreationDate = dynamicMinimumCreationDate;
        }

        maxAge = finalMaxAge;

        return minimumCreationDate;
    }

    private async Task<T> GetAsync<T>(
        CachePayloadHolder<object> keyHolder,
        Func<CancellationToken, Task<T>> fetchAsync,
        DateTimeOffset timestamp,
        DateTimeOffset? maybeMinimumCreationDate,
        Expiration? absExpiration,
        Expiration? sldExpiration,
        IDynamicSmartCacheCoreOptions dynamicCoreOptions,
        CancellationToken cancellationToken
    )
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(
            logger, () => new { key = keyHolder.Payload, timestamp, maybeMinimumCreationDate, absExpiration, sldExpiration }
        );

        using TimerLap memoryLap = SmartCacheObservability.Instruments.FetchDuration.CreateLap(SmartCacheObservability.Tags.Type.Memory);
        memoryLap.DisableCommit = true;

        ValueEntry<T>? localEntry;
        ExternalMissDictionary.Entry? externalEntry;

        if (maybeMinimumCreationDate is null)
        {
            localEntry = null;
            externalEntry = null;
        }
        else
        {
            using (memoryLap.Start())
            {
                localEntry = memoryCache.Get<ValueEntry<T>?>(keyHolder.Payload);
                externalEntry = externalMissDictionary.Get(keyHolder.Payload);
            }

            if (localEntry is not null)
            {
                logger.LogDebug("Cache entry found");
            }
        }

        async Task<T> FetchAndSetValueAsync([SuppressMessage("ReSharper", "VariableHidesOuterVariable")] Activity? activity)
        {
            SmartCacheObservability.Instruments.Sources.Add(1, SmartCacheObservability.Tags.Type.Miss);
            activity?.SetTag("cache.hit", 0);

            T value;
            StrongBox<double> latencyMsecBox = new ();
            using (SmartCacheObservability.Instruments.FetchDuration.StartLap(latencyMsecBox, SmartCacheObservability.Tags.Type.Miss))
            {
                value = await fetchAsync(cancellationToken);
            }

            long latencyMsec = (long)latencyMsecBox.Value;

            logger.LogDebug("Fetched in {LatencyMsec} ms", latencyMsec);

            SetValue(keyHolder, value, timestamp, dynamicCoreOptions, absExpiration, sldExpiration);
            return value;
        }

        DateTimeOffset? localCreationDate = localEntry?.CreationDate;

        if (externalEntry is var (othersCreationDate, locationIds) && !(othersCreationDate - coreOptions.LocalEntryTolerance <= localCreationDate))
        {
            DateTimeOffset minimumCreationDate = maybeMinimumCreationDate!.Value;
            if (othersCreationDate >= minimumCreationDate)
            {
                logger.LogDebug("Key is also available and up-to-date in other locations: {LocationIds}", locationIds);

                ConcurrentBag<string> invalidLocations = [ ];

                IReadOnlyDictionary<string, CacheLocation> locations = (await companion.GetActiveLocationsAsync(locationIds))
                    .Concat<CacheLocation>(passiveLocations.Values)
                    .ToDictionary(static x => x.Id);

                IEnumerable<Func<CancellationToken, Task<(CacheLocationOutput<T>, KeyValuePair<string, object?>)?>>> taskFactories = locationIds
                    .GroupJoin(
                        locationLatencies,
                        static l => l,
                        static kv => kv.Key,
                        static (l, kvs) => (LocationId: l, Latency: kvs.FirstOrDefault().Value ?? new Latency())
                    )
                    .OrderBy(static kv => kv.Latency)
                    .Select(static kv => kv.LocationId)
                    .Select(
                        Func<CancellationToken, Task<(CacheLocationOutput<T>, KeyValuePair<string, object?>)?>> (locationId) =>
                        {
                            if (!locations.TryGetValue(locationId, out CacheLocation? location))
                            {
                                return static _ => Task.FromResult<(CacheLocationOutput<T>, KeyValuePair<string, object?>)?>(null);
                            }

                            return async ct =>
                            {
                                CacheLocationOutput<T>? maybeOutput =
                                    await location.GetAsync<T>(keyHolder, minimumCreationDate, () => invalidLocations.Add(locationId), ct);

                                if (maybeOutput is not { } output)
                                {
                                    if (invalidLocations.Contains(locationId) && location is ActiveCacheLocation)
                                    {
                                        locationLatencies.TryRemove(locationId, out _);
                                    }

                                    return null;
                                }

                                Latency latency = locationLatencies.GetOrAdd(locationId, static _ => new Latency());
                                latency.Add(output.LatencyMsec / output.ValueSerializedSize);

                                return (output, location.MetricTag);
                            };
                        }
                    )
                    .ToArray();

                (CacheLocationOutput<T> Output, KeyValuePair<string, object?> MetricTag)? maybeOutputTagged;
                try
                {
                    maybeOutputTagged = await TaskUtils.WhenAnyValid(
                        taskFactories.ToArray(),
                        coreOptions.LocationPrefetchCount,
                        coreOptions.LocationMaxParallelism,
                        Expiration.Never,
                        // ReSharper disable once AsyncApostle.AsyncWait
                        isValid: static t => new ValueTask<bool>(t.Status != TaskStatus.RanToCompletion || t.Result is not null),
                        cancellationToken: cancellationToken
                    );
                }
                catch (InvalidOperationException)
                {
                    maybeOutputTagged = default;
                }
                finally
                {
                    if (invalidLocations.Any())
                    {
                        externalMissDictionary.RemoveSub(keyHolder.Payload, invalidLocations);
                    }
                }

                if (maybeOutputTagged is var ((item, valueSerializedSize, latencyMsec), metricTag))
                {
                    SmartCacheObservability.Instruments.KeySerializedSize.Record(keyHolder.GetAsBytes().LongLength, metricTag);
                    SmartCacheObservability.Instruments.ValueSerializedSize.Record(valueSerializedSize, metricTag);
                    SmartCacheObservability.Instruments.Sources.Add(1, metricTag);
                    SmartCacheObservability.Instruments.CompanionFetchDuration.Underlying.Record(latencyMsec, metricTag);
                    SmartCacheObservability.Instruments.CompanionFetchRelativeDuration.Record(latencyMsec / valueSerializedSize * 1000, metricTag);

                    SetValue(keyHolder, item, othersCreationDate, dynamicCoreOptions, absExpiration, sldExpiration);
                    return item!;
                }
            }
            else
            {
                logger.LogDebug(
                    "Cache miss: creation date validation failed (minimum: {MinimumCreationDate:s}, older: {LocalCreationDate:s})",
                    minimumCreationDate,
                    localCreationDate ?? DateTimeOffset.MinValue
                );
            }

            return await FetchAndSetValueAsync(activity);
        }

        memoryLap.DisableCommit = false;

        if (localCreationDate >= maybeMinimumCreationDate)
        {
            logger.LogDebug(
                "Cache hit: valid creation date (minimum: {MaybeMinimumCreationDate:s}, newer: {LocalCreationDate:s})",
                maybeMinimumCreationDate,
                localCreationDate.Value
            );

            memoryLap.AddTags(SmartCacheObservability.Tags.Found.True);
            SmartCacheObservability.Instruments.Sources.Add(1, SmartCacheObservability.Tags.Type.Memory);
            activity?.SetTag("cache.hit", 1);

            return localEntry!.Data;
        }
        else
        {
            logger.LogDebug(
                "Cache miss: creation date validation failed (minimum: {MaybeMinimumCreationDate:s}, older: {LocalCreationDate:s})",
                maybeMinimumCreationDate,
                localCreationDate ?? DateTimeOffset.MinValue
            );

            memoryLap.AddTags(SmartCacheObservability.Tags.Found.False);

            return await FetchAndSetValueAsync(activity);
        }
    }

    private void SetValue<T>(
        CachePayloadHolder<object> keyHolder,
        T value,
        DateTimeOffset creationDate,
        IDynamicSmartCacheCoreOptions? dynamicCoreOptions,
        Expiration? absExpiration = null,
        Expiration? sldExpiration = null,
        bool skipNotify = false
    )
    {
        SetValue(keyHolder, typeof(T), value, creationDate, dynamicCoreOptions, absExpiration, sldExpiration, skipNotify);
    }

    private void SetValue(
        CachePayloadHolder<object> keyHolder,
        Type valueType,
        object? value,
        DateTimeOffset creationDate,
        IDynamicSmartCacheCoreOptions? dynamicCoreOptions = null,
        Expiration? absExpiration = null,
        Expiration? sldExpiration = null,
        bool skipNotify = false
    )
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(
            logger, () => new { key = keyHolder.Payload, valueType, creationDate, absExpiration, sldExpiration, skipNotify }
        );

        object key = keyHolder.Payload;

        keys[key] = default;
        RemoveExternalMiss(keyHolder);

        IValueEntry entry = ValueEntry.Create(value, valueType, creationDate);

        Expiration finalAbsExpiration = Choose(dynamicCoreOptions?.AbsoluteExpiration, absExpiration, coreOptions.AbsoluteExpiration);

        SmartCacheMode mode;
        if (passiveLocations.Count > 1)
        {
            mode = dynamicCoreOptions?.Mode ?? coreOptions.Mode;
        }
        else
        {
            if (!warnedModeDowngrade)
            {
                warnedModeDowngrade = true;
                logger.LogWarning($"{nameof(SmartCacheMode)} downgraded to {nameof(SmartCacheMode.InMemory)} because no passive location is available");
            }
            mode = SmartCacheMode.InMemory;
        }

        Expiration candidateSldExpiration = Choose(dynamicCoreOptions?.SlidingExpiration, sldExpiration, coreOptions.SlidingExpiration);
        Expiration finalSldExpiration = candidateSldExpiration < finalAbsExpiration ? candidateSldExpiration : finalAbsExpiration;

        long GetSize(object? obj, KeyValuePair<string, object?> lapTag, Histogram<long> histogram, string errorMessage)
        {
            long size;
            using (SmartCacheObservability.Instruments.SizeComputationDuration.StartLap(lapTag))
            {
                try
                {
                    size = obj.GetSizeHeuristically();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, errorMessage);
                    return long.MaxValue;
                }
            }

            histogram.Record(size);
            return size;
        }

        long keySize = GetSize(
            key, SmartCacheObservability.Tags.Subject.Key, SmartCacheObservability.Instruments.KeyObjectSize, "Error calculating key size"
        );
        long valueSize = GetSize(
            value, SmartCacheObservability.Tags.Subject.Value, SmartCacheObservability.Instruments.ValueObjectSize, "Error calculating value size"
        );

        long size;
        try
        {
            size = checked(keySize + valueSize);
        }
        catch (OverflowException)
        {
            size = long.MaxValue;
        }

        CacheItemPriority priority =
            size >= coreOptions.LowPrioritySizeThreshold ? CacheItemPriority.Low
            : size >= coreOptions.MidPrioritySizeThreshold ? CacheItemPriority.Normal
            : CacheItemPriority.High;

        MemoryCacheEntryOptions entryOptions = new ()
        {
            AbsoluteExpirationRelativeToNow = finalAbsExpiration.IsNever ? null : finalAbsExpiration.Value,
            SlidingExpiration = finalSldExpiration.IsNever ? null : finalSldExpiration.Value,
            Size = size,
            Priority = priority,
        };

        entryOptions.RegisterPostEvictionCallback(
            (k, v, r, _) =>
            {
                using (ActivityUtils.UnsetCurrent())
                {
                    Interlocked.Add(ref memoryCacheSize, -size);
                    SmartCacheObservability.Instruments.TotalSize.Add(-size);

                    OnEvicted(new CacheKeyHolder(k), (IValueEntry)v!, r, finalAbsExpiration);
                }
            }
        );

        memoryCache.Set(key, entry, entryOptions);

        Interlocked.Add(ref memoryCacheSize, size);
        SmartCacheObservability.Instruments.TotalSize.Add(size);

        if (skipNotify)
            return;

        int missValueSizeThreshold = dynamicCoreOptions?.MissValueSizeThreshold ?? coreOptions.MissValueSizeThreshold;

        switch (mode)
        {
            case SmartCacheMode.InMemory:
                NotifyMiss(keyHolder, creationDate, valueType, value, (vt, v) => IsSmallValue(vt, v, missValueSizeThreshold));
                break;

            case SmartCacheMode.MixedPassive:
                if (!IsSmallValue(valueType, value, missValueSizeThreshold))
                {
                    goto case SmartCacheMode.PurePassive;
                }

                NotifyMiss(keyHolder, creationDate, valueType, value, static (_, _) => true);
                break;

            case SmartCacheMode.PurePassive:
                foreach (PassiveCacheLocation passiveLocation in passiveLocations.Values)
                {
                    WriteToLocation(passiveLocation, keyHolder, entry, finalAbsExpiration);
                }
                break;

            default:
                throw new UnreachableException($"Unrecognized {nameof(SmartCacheMode)}");
        }
    }

    private static bool IsSmallValue(Type valueType, object? value, int missValueSizeThreshold)
    {
        if (missValueSizeThreshold <= 0)
        {
            return false;
        }

        byte[] valueBytes = new byte[missValueSizeThreshold];
        using MemoryStream valueStream = new(valueBytes);
        using (SmartCacheObservability.Instruments.SerializationDuration.StartLap(SmartCacheObservability.Tags.Operation.Serialization, SmartCacheObservability.Tags.Subject.Value))
        {
            try
            {
                SmartCacheSerialization.SerializeToStream(value, valueType, valueStream);
                return true;
            }
            catch (NotSupportedException) // In case the serialized value is too big for the buffer
            {
                return false;
            }
        }
    }

    private void OnEvicted(CachePayloadHolder<object> keyHolder, IValueEntry entry, EvictionReason reason, Expiration expiration)
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(
            logger, () => new { key = keyHolder.Payload, reason, expiration }
        );

        SmartCacheObservability.Instruments.Evictions.Add(
            1,
            reason switch
            {
                EvictionReason.Removed => SmartCacheObservability.Tags.Eviction.Removed,
                EvictionReason.Replaced => SmartCacheObservability.Tags.Eviction.Replaced,
                EvictionReason.Expired or EvictionReason.TokenExpired => SmartCacheObservability.Tags.Eviction.Expired,
                EvictionReason.Capacity => SmartCacheObservability.Tags.Eviction.Capacity,
                EvictionReason.None => throw new InvalidOperationException($"unexpected {nameof(EvictionReason)}"),
                _ => throw new ArgumentOutOfRangeException($"unrecognized {nameof(EvictionReason)}"),
            }
        );

        if (reason is EvictionReason.None or EvictionReason.Replaced)
        {
            return;
        }

        keys.Remove(keyHolder.Payload);

        if (reason != EvictionReason.Capacity)
        {
            return;
        }

        foreach (PassiveCacheLocation passiveLocation in passiveLocations.Values)
        {
            WriteToLocation(passiveLocation, keyHolder, entry, expiration);
        }
    }

    private void WriteToLocation(PassiveCacheLocation location, CachePayloadHolder<object> keyHolder, IValueEntry entry, Expiration expiration)
    {
        location.WriteAndForget(keyHolder, entry, expiration, () => NotifyMissAsync(keyHolder, entry.CreationDate, location.Id));
    }

    private void NotifyMiss(
        CachePayloadHolder<object> keyHolder, DateTimeOffset creationDate, Type valueType, object? value, Func<Type, object?, bool> isSmallValue
    )
    {
        TaskUtils.RunAndForget(() => NotifyMissAsync(keyHolder, creationDate, valueType, value, isSmallValue));
    }

    private Task NotifyMissAsync(
        CachePayloadHolder<object> keyHolder, DateTimeOffset creationDate, Type valueType, object? value, Func<Type, object?, bool> isSmallValue
    )
    {
        return NotifyMissAsync(keyHolder, creationDate, companion.SelfLocationId, () => isSmallValue(valueType, value) ? (valueType, value) : null);
    }

    private Task NotifyMissAsync(CachePayloadHolder<object> keyHolder, DateTimeOffset creationDate, string locationId)
    {
        externalMissDictionary.Add(keyHolder.Payload, creationDate, locationId);
        return NotifyMissAsync(keyHolder, creationDate, locationId, null);
    }

    private async Task NotifyMissAsync(
        CachePayloadHolder<object> keyHolder, DateTimeOffset creationDate, string locationId, Func<(Type, object?)?>? makeValueTuple
    )
    {
        IEnumerable<CacheEventNotifier> eventNotifiers = await companion.GetAllEventNotifiersAsync();
        if (!eventNotifiers.Any())
        {
            return;
        }

        CacheMissDescriptor descriptor = new (companion.SelfLocationId, keyHolder.Payload, creationDate, locationId, makeValueTuple?.Invoke());
        CachePayloadHolder<CacheMissDescriptor> descriptorHolder = new (descriptor, SmartCacheObservability.Tags.Subject.Value);

        foreach (CacheEventNotifier eventNotifier in eventNotifiers)
        {
            eventNotifier.NotifyCacheMissAndForget(descriptorHolder);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Expiration Choose(Expiration? maybeDynamic, Expiration? maybeOperation, Expiration fallback)
    {
        Expiration dynamic = maybeDynamic ?? Expiration.Never;
        Expiration operation = maybeOperation ?? fallback;
        return dynamic < operation ? dynamic : operation;
    }

    public bool TryGetDirectFromMemory(object key, [NotNullWhen(true)] out Type? type, out object? value)
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger, () => new { key });

        if (memoryCache.Get<IValueEntry?>(key) is { } entry)
        {
            type = entry.Type;
            value = entry.Data;
            return true;
        }
        else
        {
            type = null;
            value = null;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invalidate(IInvalidationRule invalidationRule)
    {
        Invalidate(invalidationRule, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invalidate(InvalidationDescriptor descriptor)
    {
        if (descriptor.Emitter != companion.SelfLocationId)
        {
            Invalidate(descriptor.Rule, false);
        }
    }

    private void Invalidate(IInvalidationRule invalidationRule, bool broadcast)
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger, () => new { invalidationRule, broadcast });

        ICollection<Func<Task>> invalidationCallbacks = new List<Func<Task>>();

        void CoreInvalidate(IEnumerable<object> ks, Action<object> remove)
        {
            foreach (object k in ks.ToArray())
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (k is not IInvalidatable invalidatable ||
                    !invalidatable.IsInvalidatedBy(invalidationRule, out var invalidationCallback))
                {
                    continue;
                }

                logger.LogDebug("Invalidating cache key");

                remove(k);
                if (invalidationCallback is not null)
                {
                    invalidationCallbacks.Add(invalidationCallback);
                }
            }
        }

        CoreInvalidate(keys.Keys, memoryCache.Remove);
        CoreInvalidate(externalMissDictionary.Keys, k => RemoveExternalMiss(new CacheKeyHolder(k)));

        if (broadcast)
        {
            NotifyInvalidation(invalidationRule);
        }

        TaskUtils.RunAndForget(
            async () =>
            {
                foreach (Func<Task> invalidationCallback in invalidationCallbacks)
                {
                    await invalidationCallback();
                }
            }
        );
    }

    private void NotifyInvalidation(IInvalidationRule invalidationRule)
    {
        TaskUtils.RunAndForget(NotifyInvalidationAsync);

        async Task NotifyInvalidationAsync()
        {
            IEnumerable<CacheEventNotifier> eventNotifiers = await companion.GetAllEventNotifiersAsync();
            if (!eventNotifiers.Any())
            {
                return;
            }

            InvalidationDescriptor descriptor = new (companion.SelfLocationId, invalidationRule);
            CachePayloadHolder<InvalidationDescriptor> descriptorHolder = new (descriptor, SmartCacheObservability.Tags.Subject.Value);
            foreach (CacheEventNotifier eventNotifier in eventNotifiers)
            {
                eventNotifier.NotifyInvalidationAndForget(descriptorHolder);
            }
        }
    }

    public void AddExternalMiss(CacheMissDescriptor descriptor)
    {
        (string emitter,
            object key,
            DateTimeOffset timestamp,
            string location,
            Type? valueType) = descriptor;

        if (emitter == companion.SelfLocationId)
        {
            return;
        }

        if (valueType is not null)
        {
            SetValue(new CacheKeyHolder(key), valueType, descriptor.Value, timestamp, skipNotify: true);
        }
        else
        {
            externalMissDictionary.Add(key, timestamp, location);
        }
    }

    private void RemoveExternalMiss(CachePayloadHolder<object> keyHolder)
    {
        foreach (string locationId in externalMissDictionary.Remove(keyHolder.Payload))
        {
            if (passiveLocations.TryGetValue(locationId, out PassiveCacheLocation? passiveLocation))
            {
                passiveLocation.DeleteAndForget(keyHolder);
            }
        }
    }

    private sealed class ExternalMissDictionary
    {
        private readonly ConcurrentDictionary<object, Entry> underlying = new ();

        public IEnumerable<object> Keys => underlying.Keys;

        private readonly object lockObject = new ();

        public Entry? Get(object key)
        {
            // ReSharper disable once CanSimplifyDictionaryTryGetValueWithGetValueOrDefault
            return underlying.TryGetValue(key, out Entry? entry) ? entry : null;
        }

        public IEnumerable<string> Remove(object key)
        {
            return underlying.TryRemove(key, out Entry? entry) ? entry.Locations : [ ];
        }

        public void RemoveSub(object key, IEnumerable<string> locations)
        {
            lock (lockObject)
            {
                if (!underlying.TryGetValue(key, out Entry? entry))
                {
                    return;
                }

                foreach (string location in locations)
                {
                    entry = entry with { Locations = entry.Locations.Where(x => x != location).ToArray() };

                    if (!entry.Locations.Any())
                    {
                        underlying.TryRemove(key, out _);
                        return;
                    }
                }

                underlying[key] = entry;
            }
        }

        public void Add(object key, DateTimeOffset timestamp, string location)
        {
            lock (lockObject)
            {
                if (!underlying.TryGetValue(key, out Entry? entry) || entry.Timestamp < timestamp)
                {
                    underlying[key] = new Entry(timestamp, [ location ]);
                }
                else if (!(entry.Timestamp > timestamp))
                {
                    underlying[key] = entry with { Locations = entry.Locations.Append(location).Distinct().ToArray() };
                }
            }
        }

        public sealed record Entry(DateTimeOffset Timestamp, IEnumerable<string> Locations);
    }

    private sealed class Latency : IComparable<Latency>
    {
        private double average = double.PositiveInfinity;
        private int count = 0;

        public void Add(double latency)
        {
            if (count == 0)
            {
                average = latency;
                count = 1;
            }
            else
            {
                average = (average * count + latency) / ++count;
            }
        }

        public int CompareTo(Latency? other) => average.CompareTo(other?.average ?? double.PositiveInfinity);
    }
}
