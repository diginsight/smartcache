#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Common;
//using Dapr.Client;
using DotNext;
using DotNext.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;
using Common.SmartCache;

[assembly: CacheInterchangeExternalName("2", typeof(ValueTuple<,>))]
[assembly: CacheInterchangeExternalName("3", typeof(ValueTuple<,,>))]
[assembly: CacheInterchangeExternalName("4", typeof(ValueTuple<,,,>))]
[assembly: CacheInterchangeExternalName("D", typeof(Dictionary<,>))]
[assembly: CacheInterchangeExternalName("ID", typeof(IDictionary<,>))]
[assembly: CacheInterchangeExternalName("IE", typeof(IEnumerable<>))]
[assembly: CacheInterchangeExternalName("IL", typeof(IList<>))]
[assembly: CacheInterchangeExternalName("KV", typeof(KeyValuePair<,>))]
[assembly: CacheInterchangeExternalName("L", typeof(List<>))]
[assembly: CacheInterchangeExternalName("b", typeof(byte))]
[assembly: CacheInterchangeExternalName("d", typeof(double))]
[assembly: CacheInterchangeExternalName("f", typeof(float))]
[assembly: CacheInterchangeExternalName("g", typeof(Guid))]
[assembly: CacheInterchangeExternalName("i", typeof(int))]
[assembly: CacheInterchangeExternalName("l", typeof(long))]
[assembly: CacheInterchangeExternalName("o", typeof(object))]
[assembly: CacheInterchangeExternalName("s", typeof(string))]

namespace Common.SmartCache;

public class CacheService : ICacheService
{
    public const string ClusterCachePubsubName = "pubsub";
    public const string ClusterCacheCacheMissTopicName = "elcpem_cachemiss";
    public const string ClusterCacheInvalidateTopicName = "elcpem_invalidate";
    private const string RedisLocation = "<redis>";
    private const string RedisKeyPrefix = "EnergyManagerApi";

    private static readonly string PodIp = Environment.GetEnvironmentVariable("POD_IP")!;

    private readonly ICacheServiceOptions cacheServiceOptions;
    private readonly ILogger<CacheService> logger;
    private readonly IMemoryCache memoryCache;
    private readonly IDatabase redisDatabase;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ICachePersistence cachePersistence;
    private readonly IClassConfigurationGetter classConfigurationGetter;
    //private readonly DaprClient? daprClient;

    private readonly AsyncLazy<bool> isDaprEnabledLazy;
    private readonly ISet<ICacheKey> keys = new HashSet<ICacheKey>();
    private readonly ExternalMissDictionary externalMissDictionary = new();
    private readonly AsyncReaderWriterLock rwLock = new();
    private readonly ConcurrentDictionary<string, Latency> locationLatencies = new();

    private long memoryCacheSize = 0;

    public CacheService(
        IOptions<MemoryCacheOptions> memoryCacheOptionsOptions,
        ILoggerFactory loggerFactory,
        IOptions<CacheServiceOptions> cacheServiceOptionsOptions,
        ILogger<CacheService> logger,
        IDatabase redisDatabase,
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        ICachePersistence cachePersistence,
        IClassConfigurationGetter<CacheService> classConfigurationGetter
        //DaprClient? daprClient = null
        )
    {
        cacheServiceOptions = cacheServiceOptionsOptions.Value;
        this.logger = logger;

        MemoryCacheOptions initalMemoryCacheOptions = memoryCacheOptionsOptions.Value;
        MemoryCacheOptions memoryCacheOptions = new()
        {
            Clock = initalMemoryCacheOptions.Clock,
            CompactionPercentage = initalMemoryCacheOptions.CompactionPercentage,
            ExpirationScanFrequency = initalMemoryCacheOptions.ExpirationScanFrequency,
            SizeLimit = cacheServiceOptions.SizeLimit,
        };
        memoryCache = new MemoryCache(memoryCacheOptions, loggerFactory);

        this.redisDatabase = redisDatabase;
        this.httpContextAccessor = httpContextAccessor;
        this.httpClientFactory = httpClientFactory;
        this.cachePersistence = cachePersistence;
        this.classConfigurationGetter = classConfigurationGetter;
        //this.daprClient = daprClient;

        //isDaprEnabledLazy = daprClient is null
        //    ? new AsyncLazy<bool>(false)
        //    : new AsyncLazy<bool>(
        //        async _ =>
        //        {
        //            try
        //            {
        //                return await daprClient.CheckHealthAsync();
        //            }
        //            catch (Exception)
        //            {
        //                return false;
        //            }
        //        });
    }

    //private Task<bool> IsDaprEnabledAsync() => isDaprEnabledLazy.WithCancellation(CancellationToken.None);

    public async Task<T> GetAsync<T>(ICacheKey key, Func<Task<T>> fetchAsync, ICacheContext? cacheContext = null)
    {
        using var scope = logger.BeginMethodScope(() => new { key = key.ToLogString(), cacheContext });

        if (cacheContext?.Enabled != true)
        {
            return await fetchAsync();
        }

        return await GetAsync(
            key,
            fetchAsync,
            cacheContext.MaxAge,
            cacheContext.InterfaceType,
            cacheContext.AbsoluteExpiration,
            cacheContext.SlidingExpiration);
    }

    // TODO Try get from redis unconditionally, after reading memory cache
    private async Task<TValue> GetAsync<TValue>(
        ICacheKey key,
        Func<Task<TValue>> fetchAsync,
        int? maxAge,
        Type? callerType,
        int? absExpiration = null,
        int? sldExpiration = null)
    {
        string keyLogString = key.ToLogString();

        using var scope = logger.BeginMethodScope(() => new { key = keyLogString });

        DateTime utcNow = DateTime.UtcNow;
        DateTime minimumCreationDate = GetMinimumCreationDate(scope, ref maxAge, callerType, utcNow);
        bool forceFetch = maxAge.Value <= 0 || minimumCreationDate >= utcNow;

        ValueEntry<TValue>? valueEntry;
        ExternalMissDictionary.Entry? externalEntry;

        if (forceFetch)
        {
            valueEntry = null;
            externalEntry = null;
        }
        else
        {
            using (await AcquireReadLockAsync())
            {
                valueEntry = memoryCache.Get<ValueEntry<TValue>?>(key);
                externalEntry = externalMissDictionary.Get(key);
            }

            if (valueEntry is not null)
            {
                scope.LogInformation($"Cache entry found for key: {keyLogString}.");
            }
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<TValue> FetchAndSetValueAsync()
        {
            TValue value;
            bool skipPersist;
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                value = await fetchAsync();
                long latencyMsec = sw.ElapsedMilliseconds;
                skipPersist = false;

                scope.LogInformation($"Fetched in {latencyMsec} ms");
            }
            catch (Exception)
            {
                var usage = cacheServiceOptions.PersistedCacheUsageOnFailure;
                if (!cacheServiceOptions.PersistCache || usage == PersistedCacheUsage.Disabled)
                {
                    throw;
                }

                Optional<TValue> persistedValueOpt = await cachePersistence.TryRetrieveAsync<TValue>(
                    key, usage == PersistedCacheUsage.EnabledWithCreationDate ? minimumCreationDate : null);
                if (persistedValueOpt.IsUndefined)
                {
                    throw;
                }

                value = persistedValueOpt.Value;
                skipPersist = true;
            }

            using (await AcquireWriteLockAsync())
            {
                SetValue(key, value, absExpiration, sldExpiration, skipPersist: skipPersist);
                return value;
            }
        }

        DateTime? localCreationDate = valueEntry?.CreationDate;

        if (externalEntry is var (othersCreationDate, locations) && !(othersCreationDate <= localCreationDate))
        {
            if (othersCreationDate >= minimumCreationDate)
            {
                scope.LogInformation($"Key {keyLogString} is also available and up-to-date in other locations: {locations.GetLogString()}");

                HttpClient httpClient = httpClientFactory.CreateClient(nameof(CacheService));

                string rawKey = CacheSerialization.SerializeToString(key);
                HttpContent content = new StringContent(rawKey, CacheSerialization.HttpEncoding, "application/json");

                ConcurrentBag<string> invalidLocations = new();

                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                async Task<Optional<(TValue, double)>> GetFromOtherPodAsync(string podIp, CancellationToken ct)
                {
                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        using HttpResponseMessage responseMessage = await httpClient.PostAsync($"http://{podIp}/api/v1/clusterCache/get", content, ct);
                        responseMessage.EnsureSuccessStatusCode();

                        HttpContent responseContent = responseMessage.Content;
                        long contentLength = responseContent.Headers.ContentLength!.Value;

                        TValue item;
                        await using (Stream contentStream = await responseContent.ReadAsStreamAsync(ct))
                        {
                            item = CacheSerialization.Deserialize<TValue>(contentStream, true);
                        }

                        long latencyMsec = sw.ElapsedMilliseconds;

                        scope.LogDebug($"Cache hit: Returning up-to-date value for {keyLogString} from pod {podIp}. Latency: {latencyMsec}");

                        return new Optional<(TValue, double)>((item, (double)latencyMsec / contentLength));
                    }
                    catch (Exception e) when (e is InvalidOperationException or HttpRequestException || e is TaskCanceledException tce && tce.CancellationToken != ct)
                    {
                        invalidLocations.Add(podIp);
                        scope.LogDebug($"Partial cache miss: Failed to retrieve value for {keyLogString} from pod {podIp}");
                    }

                    return default;
                }

                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                async Task<Optional<(TValue, double)>> GetFromRedisAsync()
                {
                    RedisKey redisKey = RedisKeyPrefix + rawKey;

                    Stopwatch sw = Stopwatch.StartNew();
                    RedisValue redisEntry = await redisDatabase.StringGetAsync(redisKey);
                    if (redisEntry.IsNull)
                    {
                        return default;
                    }

                    ValueEntry<TValue> entry = CacheSerialization.Deserialize<ValueEntry<TValue>>((byte[])redisEntry!);
                    long latencyMsec = sw.ElapsedMilliseconds;

                    if (entry.CreationDate < minimumCreationDate)
                    {
                        scope.LogDebug($"Partial cache miss: Value for {keyLogString} found in Redis but CreationDate is invalid. Latency: {latencyMsec}");

                        invalidLocations.Add(RedisLocation);
                        _ = await redisDatabase.KeyDeleteAsync(redisKey);
                        return default;
                    }

                    long redisEntryLength = redisEntry.Length();
                    scope.LogDebug($"Cache hit (Latency:{latencyMsec}, Size:{redisEntryLength:#,##0}): Returning up-to-date value for {keyLogString} from Redis.");
                    return new Optional<(TValue, double)>((entry.Data, (double)latencyMsec / redisEntryLength));
                }

                Func<CancellationToken, Task<Optional<TValue>>> UpdatingLatency(
                    string location,
                    Func<CancellationToken, Task<Optional<(TValue, double)>>> getFromLocationAsync)
                {
                    return async ct =>
                    {
                        Optional<(TValue Item, double RelativeLatency)> outputOpt = await getFromLocationAsync(ct);
                        if (!outputOpt.IsUndefined)
                        {
                            Latency latency = locationLatencies.GetOrAdd(location, static _ => new Latency());
                            latency.Add(outputOpt.Value.RelativeLatency);
                        }

                        return outputOpt.Convert(static x => x.Item);
                    };
                }

                IEnumerable<Func<CancellationToken, Task<Optional<TValue>>>> taskFactories = locations
                    .GroupJoin(
                        locationLatencies,
                        static l => l,
                        static kv => kv.Key,
                        static (l, kvs) => (Location: l, Latency: kvs.FirstOrDefault().Value ?? new Latency()))
                    .OrderBy(static kv => kv.Latency)
                    .Select(
                        kv =>
                        {
                            string location = kv.Location;
                            Func<CancellationToken, Task<Optional<(TValue, double)>>> getFromLocationAsync =
                                location == RedisLocation
                                    ? _ => GetFromRedisAsync()
                                    : ct => GetFromOtherPodAsync(location, ct);

                            return UpdatingLatency(location, getFromLocationAsync);
                        })
                    .ToArray();

                LockReleaser? lockReleaser = null;
                try
                {
                    Optional<TValue> outputOpt;
                    try
                    {
                        outputOpt = await TaskExtensions.WhenAnyValid(
                            taskFactories.ToArray(),
                            cacheServiceOptions.CrossPodPrefetchCount,
                            cacheServiceOptions.CrossPodMaxParallelism,
                            // ReSharper disable once AsyncApostle.AsyncWait
                            isValid: static t => new ValueTask<bool>(!t.IsCompletedSuccessfully || !t.Result.IsUndefined));
                    }
                    catch (InvalidOperationException)
                    {
                        outputOpt = default;
                    }
                    finally
                    {
                        if (invalidLocations.Any())
                        {
                            lockReleaser = await AcquireWriteLockAsync();
                            externalMissDictionary.RemoveSub(key, invalidLocations);
                        }
                    }

                    if (!outputOpt.IsUndefined)
                    {
                        lockReleaser ??= await AcquireWriteLockAsync();

                        TValue item = outputOpt.Value;
                        SetValue(key, item, absExpiration, sldExpiration, othersCreationDate);
                        return item!;
                    }
                }
                finally
                {
                    lockReleaser?.Dispose();
                }
            }
            else
            {
                scope.LogInformation($"Cache miss: CreationDate validation failed (minimumCreationDate: '{minimumCreationDate:O}', older entry CreationDate: '{localCreationDate ?? DateTime.MinValue:O}').");
            }

            return await FetchAndSetValueAsync();
        }

        if (localCreationDate >= minimumCreationDate && valueEntry!.Data is { } data)
        {
            scope.LogDebug($"Cache hit: valid creation date (minimumCreationDate: '{minimumCreationDate:O}', newer entry CreationDate: '{localCreationDate.Value:O}')");
            return data;
        }

        scope.LogInformation($"Cache miss: CreationDate validation failed (minimumCreationDate: '{minimumCreationDate:O}', older entry CreationDate: '{localCreationDate ?? DateTime.MinValue:O}').");
        return await FetchAndSetValueAsync();
    }

    private void SetValue<TValue>(
        ICacheKey key,
        TValue value,
        int? absExpirationSec = null,
        int? sldExpirationSec = null,
        DateTime? creationDate = null,
        bool skipPersist = false,
        bool skipPublish = false)
    {
        SetValue(key, typeof(TValue), value, absExpirationSec, sldExpirationSec, creationDate, skipPersist, skipPublish);
    }

    private void SetValue(
        ICacheKey key,
        Type valueType,
        object? value,
        int? absExpirationSec = null,
        int? sldExpirationSec = null,
        DateTime? creationDate = null,
        bool skipPersist = false,
        bool skipPublish = false)
    {
        using var scope = logger.BeginMethodScope(() => new { key = key.ToLogString() });

        keys.Add(key);
        RemoveExternalMiss(key);

        IValueEntry entry = IValueEntry.Create(value, valueType, creationDate);
        DateTime finalCreationDate = entry.CreationDate;

        int finalAbsExpirationSecs = absExpirationSec ?? cacheServiceOptions.AbsoluteExpiration;
        TimeSpan finalAbsExpiration = TimeSpan.FromSeconds(finalAbsExpirationSecs);

        if (classConfigurationGetter.Get("RedisOnlyCache", false))
        {
            WriteToRedis(scope, key, entry, finalAbsExpiration, skipPublish);
        }
        else
        {
            int finalSldExpirationSecs = Math.Min(sldExpirationSec ?? cacheServiceOptions.SlidingExpiration, finalAbsExpirationSecs);
            long size = Size.Get(value);

            CacheItemPriority priority =
                size >= cacheServiceOptions.LowPrioritySizeThreshold ? CacheItemPriority.Low
                : size >= cacheServiceOptions.MidPrioritySizeThreshold ? CacheItemPriority.Normal
                : CacheItemPriority.High;

            MemoryCacheEntryOptions entryOptions = new()
            {
                AbsoluteExpirationRelativeToNow = finalAbsExpiration,
                SlidingExpiration = TimeSpan.FromSeconds(finalSldExpirationSecs),
                Size = size,
                Priority = priority,
            };

            entryOptions.RegisterPostEvictionCallback((k, v, r, _) =>
            {
                Interlocked.Add(ref memoryCacheSize, -size);
                OnEvicted((ICacheKey)k, (IValueEntry)v, r, finalAbsExpiration);
            });

            memoryCache.Set(key, entry, entryOptions);
            Interlocked.Add(ref memoryCacheSize, size);

            if (!skipPersist)
            {
                Persist(key, entry);
            }

            if (!skipPublish)
            {
                PublishMiss(key, finalCreationDate, (value, valueType), false);
            }
        }
    }

    private void OnEvicted(ICacheKey key, IValueEntry entry, EvictionReason reason, TimeSpan expiration)
    {
        if (reason is EvictionReason.None or EvictionReason.Replaced)
        {
            return;
        }

        using var scope = logger.BeginMethodScope(() => new { reason, expiration, key, entry });

        using (AcquireWriteLock())
        {
            keys.Remove(key);
            if (cacheServiceOptions.PersistCache)
            {
                _ = Task.Run(() => cachePersistence.RemoveAsync(key));
            }
        }

        if (reason != EvictionReason.Capacity)
        {
            return;
        }

        WriteToRedis(scope, key, entry, expiration);
    }

    private void WriteToRedis(CodeSectionScope scope, ICacheKey key, IValueEntry entry, TimeSpan expiration, bool skipPublish = false)
    {
        Stopwatch sw = Stopwatch.StartNew();
        RedisKey redisKey = CacheSerialization.SerializeToBytes(key);

        byte[] rawEntry = CacheSerialization.SerializeToBytes(entry);
        redisDatabase.StringSet(redisKey.Prepend(RedisKeyPrefix), rawEntry, expiration);

        long elapsedMs = sw.ElapsedMilliseconds;
        scope.LogDebug($"redisDatabase.StringSet completed ({elapsedMs} ms, {rawEntry.LongLength} bytes)");

        if (!skipPublish)
        {
            PublishMiss(key, entry.CreationDate, null, true);
        }
    }

    private void PublishMiss(ICacheKey key, DateTime creationDate, (object?, Type)? valueHolder, bool onRedis)
    {
        _ = Task.Run(PublishMissAsync);

        async Task PublishMissAsync()
        {
            using var localScope = logger.BeginMethodScope(() => new { key = key.ToLogString(), creationDate }, memberName: nameof(PublishMissAsync));

            if (onRedis)
            {
                using (await AcquireWriteLockAsync())
                {
                    externalMissDictionary.Add(key, creationDate, RedisLocation);
                }
            }

            //if (!await IsDaprEnabledAsync())
            //{
            //    return;
            //}

            //byte[] rawKey = CacheSerialization.SerializeToBytes(key);

            //byte[]? rawValue;
            //string? typeName;
            //if (valueHolder is var (value, valueType) && cacheServiceOptions.MissValueSizeThreshold is > 0 and var size)
            //{
            //    byte[] tempRawValue = new byte[size];
            //    await using MemoryStream valueStream = new(tempRawValue);

            //    try
            //    {
            //        CacheSerialization.SerializeToStream(value, valueType, valueStream);
            //        Array.Resize(ref tempRawValue, (int)valueStream.Position);

            //        rawValue = tempRawValue;
            //        typeName = CacheSerialization.SerializeType(valueType);
            //    }
            //    catch (NotSupportedException) // In case the serialized value is longer than 'size'
            //    {
            //        rawValue = null;
            //        typeName = null;
            //    }
            //}
            //else
            //{
            //    rawValue = null;
            //    typeName = null;
            //}

            //await daprClient!.PublishEventAsync(
            //    ClusterCachePubsubName,
            //    ClusterCacheCacheMissTopicName,
            //    new CacheMissDescriptor(PodIp, rawKey, creationDate, onRedis ? RedisLocation : PodIp, rawValue, typeName));
        }
    }

    private void Persist(ICacheKey key, IValueEntry entry)
    {
        if (cacheServiceOptions.PersistCache)
        {
            _ = Task.Run(() => cachePersistence.PersistAsync(key, entry));
        }
    }

    private DateTime GetMinimumCreationDate(CodeSectionScope scope, [NotNull] ref int? maxAge, Type? callerType, DateTime utcNow)
    {
        int finalMaxAge = maxAge ?? cacheServiceOptions.DefaultMaxAge;

        if (httpContextAccessor.HttpContext is { } httpContext)
        {
            bool ExtractMaxAgeFromHeader(string headerName)
            {
                if (!httpContext.Request.Headers.TryGetValue(headerName, out StringValues headerMaxAges)
                    || !int.TryParse(headerMaxAges.LastOrDefault(), out int headerMaxAge))
                {
                    return false;
                }

                scope.LogInformation($"From request header: {headerName}={headerMaxAge}");
                if (headerMaxAge >= finalMaxAge)
                {
                    return false;
                }

                finalMaxAge = headerMaxAge;
                return true;
            }

            var namespaceName = callerType?.Namespace;
            string[] maxAgeHeaderNames = namespaceName != null
                ? new[] { $"{namespaceName}.{callerType!.Name}.MaxAge", $"{namespaceName}.MaxAge", "MaxAge" }
                : new[] { "MaxAge" };

            foreach (string maxAgeHeaderName in maxAgeHeaderNames)
            {
                if (ExtractMaxAgeFromHeader(maxAgeHeaderName))
                    break;
            }
        }

        DateTime requestStartedOn =
            (httpContextAccessor.HttpContext?.Items.TryGetValue("RequestStartedOn", out var rawRequestStartedOn) == true
                ? rawRequestStartedOn as DateTime? : null)
            ?? utcNow;

        DateTime minimumCreationDate = requestStartedOn.Subtract(TimeSpan.FromSeconds(finalMaxAge));
        if (httpContextAccessor.HttpContext?.Request.Headers.TryGetValue("MinimumCreationDate", out StringValues headerMinimumCreationDates) == true
            && DateTime.TryParse(
                headerMinimumCreationDates.LastOrDefault(),
                null,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime headerMinimumCreationDate))
        {
            scope.LogInformation($"From header: {nameof(minimumCreationDate)}={headerMinimumCreationDate}");

            if (headerMinimumCreationDate > minimumCreationDate)
            {
                minimumCreationDate = headerMinimumCreationDate;
            }
        }

        maxAge = finalMaxAge;
        return minimumCreationDate;
    }

    private void RemoveExternalMiss(ICacheKey key)
    {
        if (externalMissDictionary.Remove(key))
        {
            RedisKey redisKey = CacheSerialization.SerializeToString(key);
            _ = redisDatabase.KeyDelete(redisKey.Prepend(RedisKeyPrefix));
        }
    }

    public bool TryGetDirectFromMemory(ICacheKey key, [NotNullWhen(true)] out Type? type, out object? value)
    {
        using var scope = logger.BeginMethodScope(() => new { key = key.ToLogString() });

        IValueEntry? entry;
        using (AcquireReadLock())
        {
            entry = memoryCache.Get<IValueEntry?>(key);
        }

        if (entry is not null)
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

    public void Invalidate(IInvalidationRule invalidationRule, bool broadcast)
    {
        using var scope = logger.BeginMethodScope(() => new { invalidationRule = invalidationRule.ToLogString() });

        ICollection<Func<Task>> invalidationCallbacks = new List<Func<Task>>();

        void CoreInvalidate(IEnumerable<ICacheKey> ks, Action<ICacheKey> remove)
        {
            foreach (ICacheKey k in ks.ToArray())
            {
                if (k is not IInvalidatable invalidatable ||
                    !invalidatable.IsInvalidatedBy(invalidationRule, out var invalidationCallback))
                {
                    continue;
                }

                scope.LogDebug($"invalidating cache key {k.ToLogString()}");

                remove(k);
                if (invalidationCallback is not null)
                {
                    invalidationCallbacks.Add(invalidationCallback);
                }
            }
        }

        using (AcquireWriteLock())
        {
            CoreInvalidate(keys, memoryCache.Remove);
            CoreInvalidate(externalMissDictionary.Keys, RemoveExternalMiss);
        }

        if (broadcast)
        {
            PublishInvalidation(invalidationRule);
        }

        _ = Task.Run(
            async () =>
            {
                foreach (Func<Task> invalidationCallback in invalidationCallbacks)
                {
                    await invalidationCallback();
                }
            });
    }

    private void PublishInvalidation(IInvalidationRule invalidationRule)
    {
        _ = Task.Run(PublishInvalidationAsync);

        async Task PublishInvalidationAsync()
        {
            using var localScope = logger.BeginMethodScope(() => new { invalidationRule = invalidationRule.ToLogString() }, memberName: nameof(PublishInvalidationAsync));

            //if (!await IsDaprEnabledAsync())
            //{
            //    return;
            //}

            //byte[] rawInvalidationRule = CacheSerialization.SerializeToBytes(invalidationRule);

            //await daprClient!.PublishEventAsync(
            //    ClusterCachePubsubName,
            //    ClusterCacheInvalidateTopicName,
            //    new InvalidationDescriptor(rawInvalidationRule, PodIp));
        }
    }

    public void Invalidate(InvalidationDescriptor descriptor)
    {
        if (descriptor.PodIp == PodIp)
        {
            return;
        }

        IInvalidationRule rule = CacheSerialization.Deserialize<IInvalidationRule>(descriptor.RawRule);
        Invalidate(rule, false);
    }

    public void AddExternalMiss(CacheMissDescriptor descriptor)
    {
        (string emitter,
            byte[]? rawKey,
            DateTime timestamp,
            string location,
            byte[]? rawValue,
            string? typeName) = descriptor;

        if (emitter == PodIp)
        {
            return;
        }

        ICacheKey key = CacheSerialization.Deserialize<ICacheKey>(rawKey);

        using (AcquireWriteLock())
        {
            if (rawValue is not null)
            {
                Type type = CacheSerialization.DeserializeType(typeName!);
                object? value = CacheSerialization.Deserialize(rawValue, type);

                SetValue(key, type, value, creationDate: timestamp, skipPublish: true);
            }
            else
            {
                externalMissDictionary.Add(key, timestamp, location);
            }
        }
    }

    private async Task<LockReleaser> AcquireReadLockAsync()
    {
        await rwLock.EnterReadLockAsync();
        return new LockReleaser(rwLock);
    }

    private IDisposable AcquireReadLock() => AcquireReadLockAsync().GetAwaiter().GetResult();

    private async Task<LockReleaser> AcquireWriteLockAsync()
    {
        await rwLock.EnterWriteLockAsync();
        return new LockReleaser(rwLock);
    }

    private IDisposable AcquireWriteLock() => AcquireWriteLockAsync().GetAwaiter().GetResult();

    private sealed class ExternalMissDictionary
    {
        private readonly IDictionary<ICacheKey, Entry> underlying = new Dictionary<ICacheKey, Entry>();

        public IEnumerable<ICacheKey> Keys => underlying.Keys;

        public Entry? Get(ICacheKey key)
        {
            return underlying.TryGetValue(key, out Entry? entry) ? entry : null;
        }

        public bool Remove(ICacheKey key)
        {
            if (!underlying.TryGetValue(key, out Entry? entry))
            {
                return false;
            }

            underlying.Remove(key);
            return entry.Locations.Contains(RedisLocation);
        }

        public void RemoveSub(ICacheKey key, IEnumerable<string> locations)
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
                    underlying.Remove(key);
                    return;
                }
            }

            underlying[key] = entry;
        }

        public void Add(ICacheKey key, DateTime timestamp, string location)
        {
            if (!underlying.TryGetValue(key, out Entry? entry) || entry.Timestamp < timestamp)
            {
                underlying[key] = new Entry(timestamp, new[] { location });
            }
            else if (!(entry.Timestamp > timestamp))
            {
                underlying[key] = entry with { Locations = entry.Locations.Append(location).Distinct().ToArray() };
            }
        }

        public sealed record Entry(DateTime Timestamp, IEnumerable<string> Locations);
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly AsyncReaderWriterLock rwLock;

        public LockReleaser(AsyncReaderWriterLock rwLock)
        {
            this.rwLock = rwLock;
        }

        public void Dispose()
        {
            rwLock.Release();
        }
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
                average = ((average * count) + latency) / ++count;
            }
        }

        public int CompareTo(Latency? other) => average.CompareTo(other?.average ?? double.PositiveInfinity);
    }

    private static class Size
    {
        private static readonly MethodInfo GetUnmanagedSizeMethod = typeof(Size)
            .GetMethod(nameof(GetUnmanagedSize), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly ConcurrentDictionary<Type, long?> UnmanagedSizeCache = new();
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsCache = new();

        public static long Get(object? obj)
        {
            ISet<object> seen = new HashSet<object>();

            long CoreGet(object? current)
            {
                if (current is null)
                {
                    return 0;
                }

                if (current is Pointer or Delegate)
                {
                    throw new ArgumentException("pointers and delegates not supported");
                }

                Type type = current.GetType();
                if (UnmanagedSizeCache.GetOrAdd(type, TryGetUnmanagedSize) is { } sz)
                {
                    return sz;
                }

                if (current is string str)
                {
                    return str.Length * sizeof(char);
                }

                if (!seen.Add(current))
                {
                    return IntPtr.Size;
                }

                try
                {
                    sz = 0;
                    if (current is IEnumerable enumerable)
                    {
                        IEnumerator enumerator = enumerable.GetEnumerator();
                        try
                        {
                            while (enumerator.MoveNext())
                            {
                                sz += CoreGet(enumerator.Current);
                            }
                        }
                        finally
                        {
                            (enumerator as IDisposable)?.Dispose();
                        }
                    }
                    else
                    {
                        FieldInfo[] fields = FieldsCache.GetOrAdd(type, static t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                        foreach (FieldInfo field in fields)
                        {
                            sz += CoreGet(field.GetValue(current));
                        }
                    }

                    return sz;
                }
                finally
                {
                    seen.Remove(current);
                }
            }

            return CoreGet(obj);
        }

        private static long? TryGetUnmanagedSize(Type type)
        {
            try
            {
                return (long)GetUnmanagedSizeMethod.MakeGenericMethod(type).Invoke(null, Array.Empty<object>())!;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static unsafe long GetUnmanagedSize<T>()
            where T : unmanaged
        {
            return sizeof(T);
        }
    }
}
