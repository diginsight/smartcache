# INTRODUCTION 
`Common.SmartCache` provides intelligent loading for data providers such as __external api__ or __databases__.
__Age conscious data management__ is applied to cache or preload data automatically.
__AI assisted algorithms__ can be used to ensure data preloading, based on application use.

Articles:
- [HOWTO - Leverage age conscious data management to boost application performance.md]
  (TODO): explores how to use Common.SmartCache to boost application performance by means age conscious data magagement.
- [HOWTO - Enable data preloading by means of Artificial Intelligence.md]
  (TODO): explores how to enable AI assisted preloading to improve data preloading efficiency.

NB: __Common.SmartCache is currently under development and use ov versions 0.x.x.x is not supported__

# STEPS TO USE SMARTCACHE:
- add Common.SmartCache to your application 

- load your data by means of Common.SmartCache cacheService
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
var cacheContext = new CacheContext() { Enabled = true, MaxAge = 300 }; 
var userProfile = await userProfileService.FindUserByEmailAddressAsync(context.Account.Email, cacheContext).ConfigureAwait(false);
```

__Common.SmartCache__ will provide automatic caching based on payload size, retrieval latency etc.
Common.SmartCache will support data preloading based on application use and age required for the loaded data.

__Common.SmartCache__ component is supported on .Net Framework 4.6.2+ and .Net Core 3.0+.<br>
For more information visi:.
[smartcache]: https://github.com/diginsight/smartcache/
