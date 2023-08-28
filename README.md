# INTRODUCTION 
`Common.SmartCache` provides __intelligent loading for data providers__ such as __external api__ or __databases__.<br> 
__Age sensitive data management__ is applied to cache or preload data automatically.<br>
__AI assisted algorithms__ can be used to ensure data preloading, based on application use.<br>
 
__Data load latencies for cached data are (completely) cut for any data provider__.<br>
When data preloading happens, __data load latencies are cut since the first call__.<br>

Articles:
- [HOWTO - Leverage age sensitive data management to boost application performance.md: explores how to use Common.SmartCache to boost application performance by means age conscious data magagement.]<br>
/HOWTO%20-%20Leverage%20age%20sensitive%20data%20management%20to%20boost%20application%20performance.md

- [HOWTO - Enable data preloading by means of AI assisted algorithms.md]<br> 
(TODO): explores how to enable AI assisted preloading to improve data preloading efficiency.<br><br>

NB: __Common.SmartCache is currently under development and use ov versions 0.x.x.x is not supported__<br><br>

# STEPS TO USE SMARTCACHE:
- add Common.SmartCache to your application 

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
