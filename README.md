# INTRODUCTION 
diginsight `SmartCache` provides __hybrid, distributed, multilevel caching__ based on __age sensitive data management__.<br> 
- `SmartCache` is __hybrid__ as it caches data __in-memory__ and on __external RedIs databases__.<br>
In-memory cache ensure __0-latency__ for most recently used data and ensures __low pressure (and reduced cost)__ on the external RedIs database.
- `SmartCache` is __distributed__ as cache entries on different nodes of a multiinstance application are sinchronized automatically, to avoid flickering of values when querying the same data on different nodes.
- `SmartCache` is based on __age sensitive data management__ as cache entries are returned based on a requested __MaxAge__ parameter.<br>
__data is returned from the cache if the requested MaxAge is compatible with the cache entry__.<br>Otherwise data is requested to the real data provider.
<br>This allows requesting data with __different MaxAge criteria, according to the specific application condition__.<br>
Data loaded by any request, is made available for the benefit of further requests as long as compatible with the request MaxAge requirement.
- `SmartCache` is __multilevel__ as cache entries are returned based on a requested __MaxAge__ parameter. <br>The same entries can be cached in multiple levels (frontend, backend or further levels). <br>At any level, __data is returned from the cache if the requested MaxAge is compatible with the cache entry__. otherwise data is requested to the further levels.

SmartCache supports __data preloading__ and __automatic invalidation__ of the cache entries so, __data load latencies can be cut since the first call__.<br>

# ADDITIONAL INFORMATION 


Articles:
- [HOWTO - Boost application performance with age sensitive data management](/HOWTO%20-%20Leverage%20age%20sensitive%20data%20management%20to%20boost%20application%20performance.md):<br>explores how to use Common.SmartCache to boost application performance by means age conscious data magagement.<br>


- [HOWTO - Enable data preloading by means of AI assisted algorithms.md]<br> 
(TODO): explores how to enable AI assisted preloading to improve data preloading efficiency.<br><br>

# STEPS TO USE SMARTCACHE:
- add Diginsight.SmartCache to your application 

- load your data by means of Common.SmartCache `cacheService`
```c#
public async Task<UserProfileResponse> GetUserByEmailAddressAsync(string emailAddress, CacheContext cacheContext)
{
    using var scope = logger.BeginMethodScope(() => new { emailAddress });

    var cacheKey = new GetUserByEmailAddressAsyncCacheKey(emailAddress);

    var result = await cacheService.GetAsync(
        cacheKey,
        () => wrapped.GetUserByEmailAddressAsyncAsync(emailAddress, MakeRequestOptions(scope)), cacheContext);

    return result;
}
```

- load your data expressing the required age for it:

```c#
var cacheContext = new CacheContext() { Enabled = true, MaxAge = 300 }; // required age is expressed in seconds
var userProfile = await userProfileService.FindUserByEmailAddressAsync(context.Account.Email, cacheContext).ConfigureAwait(false);
```

__Common.SmartCache__ will provide automatic caching based on payload size, retrieval latency etc.<br>
Common.SmartCache will support data preloading based on application use and age required for the loaded data.

__Common.SmartCache__ component is supported on .Net Core 6.0+.<br>
For more information visit:
[SmartCache](https://github.com/diginsight/smartcache)


# Contribute
Contribute to the repository with your pull requests. 

- [SmartCache](https://github.com/diginsight/smartcache)
- [Diagnostics](https://github.com/diginsight/telemetry)

# License
See the [LICENSE](LICENSE.md) file for license rights and limitations (MIT).
