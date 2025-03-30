# INTRODUCTION 
diginsight `SmartCache` provides __hybrid, distributed, multilevel caching__ based on __age sensitive data management__.<br>
- `SmartCache` is __hybrid__ as it caches data __in-memory__ and on __external RedIs databases__.<br>
In-memory cache ensure __0-latency__ for most recently used data and ensures __low pressure (and reduced cost)__ on the external RedIs database.<br>
- `SmartCache` is __distributed__ as cache entries on different nodes of a multiinstance application are sinchronized automatically, to avoid flickering of values when querying the same data on different nodes.<br>
- `SmartCache` is based on __age sensitive data management__ as cache entries are returned based on a requested __MaxAge__ parameter.<br>
Data is returned from the cache __if the cache entry corresponding to the request is compatible with the requested MaxAge__.<br>
Otherwise data is obtained by the cache __data source provided as a delegate__.
<br>Any application, at any time, can access data with __different age criteria, according to the specific use for which data is requested__.<br>

The image bleow illustrates shows an application __requesting data with age 5 minutes__ to a multinode application:<br>
    ![alt text](<src/docs/001.02 SmartCache Basic Tenets.png>)


Data loaded by any request, is made available for the benefit of further requests (as long as compatible with their MaxAge requirement).<br>
As an example, an __immediately successive request__ for the same data with __age 1 minute__ will be satisfied by the cache entry loaded by the first request.



- `SmartCache` is __Multilevel__: The same entries can be cached in multiple levels (frontend, backend or further levels). <br>At any level, __data is returned from the cache if the requested MaxAge is compatible with the cache entry__. otherwise data is requested to the further levels.<br>
In case all levels entries contains old data, incompatible with the request MaxAge requirement, data is requested to the real data provider.

- `SmartCache` is __Optimized__: as:
    - Privileges __In-memory cache__ => it is faster as in memory cache hits are __'0-Latency'__
    - __Minimizes use of external backing storage__ (e.g. RedIS) => it is __cheaper__ and __scalable__ as accesses to the backing storage are minimized
    - Replicas synchronize always __keys__ and __small values__, __bigger values__ are synchronized on demand
    - SmartCache supports __data preloading__ and __automatic invalidation__ of the cache entries so, __data load latencies can be cut since the first call__.
<br>
<br>

> SmartCache supports caching data with __low cost__ and __high performance__.<br>
> In particular, __0 latency__ is ensured on in-memory cache hits.
> also, __pressure on external RedIS resource is low__ as most frequently used entries are managed in-memory.
> 
> Also, __0 latency__ can be obtained __since the first and for every call__ by means of __Cache Preloading__ and __Cache Invalidation__.
<br>

the following image illustrates the five SmartCache tenets:
![alt text](<src/docs/001.03a SmartCache Tenets Full.png>)


# ADDITIONAL INFORMATION 
Using Smartcache the following events are involved when interacting with data:<br>

- __Cache hit__ or __cache miss__:
    - a  __cache hit__: occurs when a __cache entry__ exists with key and age compatible with the requested data.<br>
    In case the cache value is taken from the External (RedIs) backing storage, we call it a  __hybrid cache hit__.<br>
    - a  __cache miss__: occurs when no __cache entry__ exists for the key or its age is older than requested __MaxAge__ for data.<br>
- __Miss notification__: every time a __cache miss__ occurs, __all instances are notified__ about it so that in case they receive a request for the same key, they can obtain the value from the instance that owns it, without need to retrieve it from the server again. 
- __Entry eviction__: every time the in-memory cache eccedes the __configured quota__ older and bigger entries are __evicted__, and __off-loaded to the external (RedIs) backing storage__.
- __Entry invalidation__: specific application conditions, may requires cache entries to be invalidated.
Cache keys can be marked implementing interface `IInvalidatable` are notified every time `Invalidate` action is triggered so that they can be evicted when needed.
- __Entry (re)load__: a cache key can be assigned a __reload delegate__ so that when invalidation happens, the value is reloaded, to avoid the cache miss latency on the next incoming call.


The following image illustrates the described SmartCache events:<br>
![alt text](<src/docs/002.01 SmartCache events.png>)

The following paragraph:<br>
[STEPS TO USE SMARTCACHE](#steps-to-use-smartcache) <br>
discusses basic steps to start using `Diginsight.SmartCache`.<br>


# STEPS TO USE SMARTCACHE
The __steps__, __code snippets__ and __images__ below were created by means of the working __SampleWebAPI__ available into [smartcache.samples](https://github.com/diginsight/smartcache.samples) repository.<br>

![alt text](<src/docs/00.1 SampleWebAPI sample.png>)

## STEP 01: add a reference to `Diginsight.SmartCache`
In the first step you can just add a `Diginsight.SmartCache` reference to your code:<br>
![alt text](<src/docs/01.1 Add a reference to Diginsight.SmartCache.png>)

In case of multiinstance applications `Diginsight.SmartCache.Externalization.ServiceBus` may be needed to support instances synchronization.
In case of AspNetCore applications `Diginsight.SmartCache.Externalization.AspNetCore` may be useful to support dynamic `MaxAge` specification from http request headers.

## STEP 02: register SmartCache services into the startup sequence
SmartCache services and default settings must be registered into the startup sequence __ConfigureServices methdod__.<br>
The code snippets below are available as working samples within the [smartcache.samples](https://github.com/diginsight/smartcache.samples) repository.


```c#
public void ConfigureServices(IServiceCollection services)
{
    ...
    // (optional) reads RedIs connection string
    services.ConfigureRedisCacheSettings(configuration); 
    ...
    // configures Diginsight:SmartCache config section with default             
    services.ConfigureClassAware<SmartCacheCoreOptions>(configuration.GetSection("Diginsight:SmartCache"));
    var smartCacheBuilder = services.AddSmartCache(configuration, environment, loggerFactory)
                                    .AddHttp();

    // (optional) ServiceBus connection 
    IConfigurationSection smartCacheServiceBusConfiguration = configuration.GetSection("Diginsight:SmartCache:ServiceBus");
    if (!string.IsNullOrEmpty(smartCacheServiceBusConfiguration[nameof(SmartCacheServiceBusOptions.ConnectionString)]) && !string.IsNullOrEmpty(smartCacheServiceBusConfiguration[nameof(SmartCacheServiceBusOptions.TopicName)]))
    {
        smartCacheBuilder.SetServiceBusCompanion(
            static (c, _) =>
            {
                IConfiguration sbc = c.GetSection("Diginsight:SmartCache:ServiceBus");
                return !string.IsNullOrEmpty(sbc[nameof(SmartCacheServiceBusOptions.ConnectionString)])
                    && !string.IsNullOrEmpty(sbc[nameof(SmartCacheServiceBusOptions.TopicName)]);
            },
            sbo =>
            {
                configuration.GetSection("Diginsight:SmartCache:ServiceBus").Bind(sbo);
                sbo.SubscriptionName = SmartCacheServiceBusSubscriptionName;
            });
    }

    services.TryAddSingleton<ICacheKeyProvider, MyCacheKeyProvider>();

}

```
The image below shows `Diginsight.SmartCache` settings with default `MaxAge` and `Expiration` values for cache entries.

```json
"SmartCache": {
    "MaxAge": "00:05:00",
    //"MaxAge@...": "00:01:00",
    //"MaxAge@...": "00:10:00",
    "AbsoluteExpiration": "1.00:00",
    "SlidingExpiration": "04:00:00",
    "ServiceBus": {
    "ConnectionString": "", // Key Vault
    "TopicName": "smartcache-commonapi"
    }
}
```

> NB. 
> - __ServiceBus configuration__ is required only in case of __multiinstance applications__ where instances cache entries need to be synchronized.
> - __RedIs configuration__ is required only in case external backing storage is available to save evicted cache entries. this allows __reducing cache miss rate__ and __mininize access to data sources__.


__Diginsight.SmartCache__ will manage cache entries synchronization across application instances by means of the `SetServiceBusCompanion`.<br>
HowTo: Configure SmartCache synchronization across application instances



## STEP 03: load data by means of `cacheService`

load your data by means of `Diginsight.SmartCache` `cacheService`
```c#
[HttpGet("getplantscached", Name = "GetPlantsCachedAsync")]
[ApiVersion(ApiVersions.V_2024_04_26.Name)]
public async Task<IEnumerable<Plant>> GetPlantsCachedAsync()
{
    using var activity = Program.ActivitySource.StartMethodActivity(logger);

    // defines a key for the cache entry
    // NB. the cache key should include all imput parameters (that may cause different responses)
    // in this case the key is defined as a record including all relevant input parameters
    var cacheKey = new MethodCallCacheKey(cacheKeyService, 
                       typeof(PlantsController), nameof(GetPlantsCachedAsync));

    // data with max-age 10 minutes is requested
    var options = new SmartCacheOperationOptions() { MaxAge = TimeSpan.FromSeconds(600) }; 

    // Calls GetPlantsAsync by means of smartCache service
    var plants = await smartCache.GetAsync(cacheKey,
        _ => GetPlantsAsync(), 
        options);

    activity.SetOutput(plants);
    return plants;
}

```

the image below show the log of the `SampleWebApi` `GetPlantsCachedAsync` method.<br>
The first call finds a `cache miss` and resolves to calling the `GetPlantsAsync` method.
the following calla find a `cache miss` obtaining the result in __2/3ms__ instead of more than __1sec__ (about __1 to 1000 ratio__).
![alt text](<src/docs/03.01a cached call log with cache miss and cache hit.png>)


# Reference 
The following articles discuss the details of `Diginsight.SmartCache` use and configuration:

- [HowTo: Cache data, Invalidate entries and reload cache on invalidation](<src/docs/articles/01. Concepts/01. Cache data, Invalidate entries and reload cache on invalidation.md>)<br>
discusses how to cache calls, and add support for invalidation and reload to cached data. 

- [HowTo: Synchronize cache entries across application instances with ServiceBusCompanion or KubernetesCompanion](<src/docs/articles/01. Concepts/02. Synchronize cache entries across application instances.md>).<br>
discusses how to configure the ServiceBusCompanion or the KubernetesCompanion to support distributed cache entries across application instances. 

- [HowTo: Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration](<src/docs/articles/01. Concepts/03. Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration.md>).<br>
discusses how to configure cache size, expiration latencies and connection to external RedIs backing storage. 

- [HowTo: Boost application performance with age sensitive data management](<src/docs/articles/01. Concepts/10. Boost application performance with age sensitive data management.md>).<br>
discusses how performance of our applications can be boosted by using smartcache.  

- [HowTo: Enable data preloading by means of AI assisted algorithms.md]<br> 
(TODO): explores how to enable AI assisted preloading to improve data preloading efficiency.<br><br>

<!-- - [HowTo:  Boost application performance with age sensitive data management](<docs/articles/10. Leverage age sensitive data management to boost application performance.md>):<br>explores how to use `Diginsight.SmartCache` to boost application performance by means age conscious data magagement.<br>
 -->


For more information visit:
[SmartCache](https://github.com/diginsight/smartcache)


# Contribute
Contribute to the repository with your pull requests. 

- [SmartCache](https://github.com/diginsight/smartcache)
- [Diagnostics](https://github.com/diginsight/telemetry)

# License
See the [LICENSE](<LICENSE>) file for license rights and limitations (MIT).
