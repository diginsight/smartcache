# INTRODUCTION 
diginsight `SmartCache` provides __hybrid, distributed, multilevel caching__ based on __age sensitive data management__.<br> 
- `SmartCache` is __hybrid__ as it caches data __in-memory__ and on __external RedIs databases__.<br>
In-memory cache ensure __0-latency__ for most recently used data and ensures __low pressure (and reduced cost)__ on the external RedIs database.
- `SmartCache` is __distributed__ as cache entries on different nodes of a multiinstance application are sinchronized automatically, to avoid flickering of values when querying the same data on different nodes.
- `SmartCache` is based on __age sensitive data management__ as cache entries are returned based on a requested __MaxAge__ parameter.<br>
__Data is returned from the cache if the requested MaxAge is compatible with the cache entry__.<br>Otherwise data is requested to the real data provider.
<br>This allows requesting data with __different MaxAge criteria, according to the specific application condition__.<br>
Data loaded by any request, is made available for the benefit of further requests (as long as compatible with their MaxAge requirement).

![alt text](<001.01 SmartCache Basic Tenets.png>)

- `SmartCache` is __Multilevel__: The same entries can be cached in multiple levels (frontend, backend or further levels). <br>At any level, __data is returned from the cache if the requested MaxAge is compatible with the cache entry__. otherwise data is requested to the further levels.<br>
In case all levels entries contains old data, incompatible with the request MaxAge requirement, data is requested to the real data provider.

- `SmartCache` is __Optimized__: as:
    - Privileges __In-memory cache__ => it is faster as in memory cache hits are __'0-Latency'__
    - __Minimizes use of external backing storage__ (e.g. RedIS) => it is __cheaper__ and __scalable__ as accesses to the backing storage are minimized
    - Replicas synchronize always __keys__ and __small values__, __bigger values__ are synchronized on demand

![alt text](<001.02 SmartCache Tenets Full.png>)

SmartCache supports __data preloading__ and __automatic invalidation__ of the cache entries so, __data load latencies can be cut since the first call__.<br>

# ADDITIONAL INFORMATION 
SmartCache supports caching data with __low cost__ and __high performance__.<br>
In particular, __0 latency__ is ensured on in-memory cache hits.
also, __pressure on external RedIS resource is low__ as most frequently used entries are managed in-memory.

Also, __0 latency__ can be obtained __since the first and for every call__ by means of __Cache Preloading__ and __Cache Invalidation__.

Paragraph [STEPS TO USE SMARTCACHE](#steps-to-use-smartcache) discusses basic steps to start using `Diginsight.SmartCache`.<br>
The following articles discuss the details of `Diginsight.SmartCache` use and configuration:
- [HowTo: Cache data, Invalidate entries and reload cache on invalidation.md](<articles/01. Cache data, Invalidate entries and reload cache on invalidation/Cache data, Invalidate entries and reload cache on invalidation.md>).

- [HowTo: Synchronize cache entries across application instances with ServiceBusCompanion or KubernetesCompanion.md](<articles/02. Synchronize cache entries across application instances/Synchronize cache entries across application instances.md>).

- [HowTo: Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration.md](<articles/03. Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration/Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration.md>).

- [HowTo: Boost application performance with age sensitive data management.md](<articles/10. Boost application performance with age sensitive data management/Boost application performance with age sensitive data management.md>).

- [HowTo: Enable data preloading by means of AI assisted algorithms.md]<br> 
(TODO): explores how to enable AI assisted preloading to improve data preloading efficiency.<br><br>

<!-- - [HowTo:  Boost application performance with age sensitive data management](articles/10. Leverage age sensitive data management to boost application performance.md):<br>explores how to use `Diginsight.SmartCache` to boost application performance by means age conscious data magagement.<br>
 -->


# STEPS TO USE SMARTCACHE

## STEP 01: add a reference to `Diginsight.SmartCache`
In the first step you can just add a `Diginsight.SmartCache` reference to your code:<br>
![alt text](<01. Add a reference to Diginsight.SmartCache.png>)

## STEP 02: register SmartCache services into the startup sequence
SmartCache services and default settings must be registered into the startup sequence __ConfigureServices methdod__.<br>
The code snippets below are available as working samples within the [smartcache_samples](https://github.com/diginsight/smartcache_samples) repository.


```c#
public void ConfigureServices(IServiceCollection services)
{
    ...
    ...

    // configures SmartCache config section with default settings and support of Dynamic-Configuration for MaxAge, expirations etc
    services.Configure<SmartCacheCoreOptions>(configuration.GetSection("Diginsight:SmartCache"))
            .PostConfigureClassAwareFromHttpRequestHeaders<SmartCacheCoreOptions>();

    // adds smartCache services (ISmartCache, ICacheKeyService and other internal services)
    var smartCacheBuilder = services.AddSmartCache();

    IConfigurationSection smartCacheServiceBusConfiguration = configuration.GetSection("Diginsight:SmartCache:ServiceBus");
    if (!string.IsNullOrEmpty(smartCacheServiceBusConfiguration[nameof(SmartCacheServiceBusOptions.ConnectionString)]) &&
        !string.IsNullOrEmpty(smartCacheServiceBusConfiguration[nameof(SmartCacheServiceBusOptions.TopicName)]))
    {
        // (opt) registers ServiceBus companion for synchronization of SmartCache entries across application instances
        smartCacheBuilder
            .SetServiceBusCompanion(
                sbo =>
                {
                    smartCacheServiceBusConfiguration.Bind(sbo);
                    sbo.SubscriptionName = SmartCacheServiceBusSubscriptionName;
                }
            );
    }

}

```
the image below shows `Diginsight.SmartCache` settings with default MaxAge and Expiration values for cache entries.

![alt text](<02. Diginsight.SmartCache settings.png>)

> NB. STEP02 only installs smartCache as an in-memory service.<br>
> An additional step can be added to install RedIS support to SmartCache distributed caching.

__Diginsight.SmartCache__ will manage cache entries synchronization across application instances by means of the `SetServiceBusCompanion`.<br>
HowTo: Configure SmartCache synchronization across application instances



## STEP 03: load data by means of `Diginsight.SmartCache`

load your data by means of `Diginsight.SmartCache` `cacheService`
```c#
public async Task<SiteLicensesResponse> GetSiteLicensesAsync(string plantId, string plantType, ContextBase context)
{
    using var activity = DiginsightDefaults.ActivitySource.StartMethodActivity(logger, new { plantId, plantType });

    // define a key for the cache entry
    // NB. the cache key should include all imput parameters (that may cause different responses)
    // in this case the key is defined as a record including all relevant input parameters
    var cacheKey = new MethodCallCacheKey(cacheKeyService, typeof(PermissionServiceAdapter), nameof(GetSiteLicensesAsync), plantId, plantType);
    // data with max-age 10 minutes is requested
    var options = new SmartCacheOperationOptions() { MaxAge = TimeSpan.FromSeconds(600) }; 
    // load data by means of smartCache service
    // delegate to load real data from the back end must be passed to smartCache service
    var siteLicensesResponse = await smartCache.GetAsync(cacheKey,
        _ => GetSiteLicensesImplAsync(plantId, plantType, context), options
    );

    activity.SetOutput(siteLicensesResponse);
    return siteLicensesResponse;
}
```

## STEP 04: Add RedIS companion for support of Distributed Hybrid cache
an additional companion can be installed to make 
```c#
```


__Diginsight.SmartCache__ will manage caching to in-memory cache or red-is cache based on cache entry size, retrieval latency etc.<br>

For more information visit:
[SmartCache](https://github.com/diginsight/smartcache)


# Contribute
Contribute to the repository with your pull requests. 

- [SmartCache](https://github.com/diginsight/smartcache)
- [Diagnostics](https://github.com/diginsight/telemetry)

# License
See the [LICENSE](LICENSE.md) file for license rights and limitations (MIT).
