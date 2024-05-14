# INTRODUCTION 
diginsight `SmartCache` provides __hybrid, distributed, multilevel caching__ based on __age sensitive data management__.<br> 

this article discusses how we can use `SmartCache` to:
- Cache data from a call and associate a suitable key to it
- Invalidate entries 
- reload cache entries upon invalidation

# Cache data from a call and associate a suitable key to it

lets assume we need to cache data from a long latency operation such as:
```c#
public async Task<SiteLicensesResponse> GetSiteLicensesAsync(string plantId, string plantType, ContextBase context)
{
    using var activity = DiginsightDefaults.ActivitySource.StartMethodActivity(logger, new { plantId, plantType });

    ...
    /// Implementation
    ...

    activity.SetOutput(siteLicensesResponse);
    return siteLicensesResponse;
}
```

result from the method execution can be cached with the following steps:
- inject `smartCache` and `cacheKeyService` into the current class
![alt text](<01. inject smartCache and cacheKeyService.png>)
- move method `GetSiteLicensesAsync` to `GetSiteLicensesAsyncImpl`
- create a new method `GetSiteLicensesAsync` calling `GetSiteLicensesAsyncImpl` by means of smartCache service

    ```c#
    public async Task<SiteLicensesResponse> GetSiteLicensesAsyncImpl(string plantId, string plantType, ContextBase context)
    {
        using var activity = DiginsightDefaults.ActivitySource.StartMethodActivity(logger, new { plantId, plantType });

        ...
        /// Implementation
        ...

        activity.SetOutput(siteLicensesResponse);
        return siteLicensesResponse;
    }



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

the `smartCache.GetAsyc()` manages cached call of `GetSiteLicensesImplAsync`:
- cacheKey
- delegate with the call to `GetSiteLicensesImplAsync`
- (opt) options with required MaxAge 

    ```c#
    var siteLicensesResponse = await smartCache.GetAsync(cacheKey,
        _ => GetSiteLicensesImplAsync(plantId, plantType, context), options
    );
    ```
