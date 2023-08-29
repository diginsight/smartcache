# INTRODUCTION 
`Common.SmartCache` introduces __age sensitive data management__: a new approach to managing data.<br><br> 
In highly distributed environments, data is inherently __disconnected__ and __often loaded across multiple boundaries__.<br>
for this reasons, loading data from a remote location may be an __expensive operation__ and __loading data efficiently__ may become __a critical challenge__.<br>

Not always fresh data data is strictly needed in our applications, to obtain the expected behaviour.<br>
Often, applications may work with data that is __not up to date__.<br><br>
This is a great opportunity to __boost application performance__: when fresh data is not strictly required data cached from previous calls or data pre-loaded in background may be used to ensure the shortest possible latencies.<br> 
<br>

# WHAT IS AGE SENSITIVE DATA MANAGEMENT 
When loading data a developer can specify the __maximum age__ that is required for it.<br>
In `Common.SmartCache` this can be done by means of the following notation:<br>

```c#
var cacheContext = new CacheContext() { Enabled = true, MaxAge = 300 }; // 300 seconds
var userProfile = await userProfileService.FindUserByEmailAddressAsync(context.Account.Email, cacheContext).ConfigureAwait(false);
```

`Common.SmartCache` __tags every cache entry with its `Creation Date`__.<br>

If the required age for a data request is compatible with the creation date of the corresponding cache entry, the data is returned from the cache (__cache hit__).<br>
In case the required age is not compatible with the creation date of the corresponding cache entry, the data is loaded from the remote location (__cache miss__) and the cache entry is updated.<br>

In `common caching systems`, the __cache entries lifetime is defined at startup and it cannot be changed across different calls__.<br> A cache hit or a cache miss is determined by the static cache entry lifetime defined at startup.<br>
With `Common.SmartCache` the cache entry lifetime may be indefinite, and a __cache hit or a cache miss is determined by the `required age`, provided at every single call__, depending on the application need.<br>

# USE CASES
`Common.SmartCache` can be used with the following type of data:
- data that is __not frequently updated__ (eg. configuration data or static data)
- data that is __updated more frequently__ (eg. user profile or user permissions)
- data that is __updated very frequently__ (eg. notifications, messages or real time data)
<br><br>

Accessing __configuration data or static data__ that is not frequently updated is a typical use case for all caching system.<br>
In this cases data doesn't change and the developer can request data with a MaxAge of hours or days (eg. MaxAge = 14400, 4 hours).<br>

__Data that is updated more frequently__ can be accessed with a __shorter MaxAge__ (eg. MaxAge = 300, 5 minutes).<br>
A typical example for this scenario can be access to the __user profile__ or the __user permissions__.<br>
By default, changes to the User Profile or the User Permissions will not be perceived by the application for a latency of 5 minutes.<br>

In some circunstances the developer may need to be sure about the exact value of such data.
In these cases, the developer can just raise a request with __MaxAge = 0__.<br> 
It may happen that, notifications are available for changes to the __user profile__ or the __user permissions__.<br> 
Upon such notifications, the developer may invalidate the cache entry or raise a request with __MaxAge = 0__ to load a cache entry for the same user, with the updated data.<br>
__When change notifications are handled properly__, even if data changes frequently, the developer can use a __longer maxage__ (eg. hours or days) and take maximum benefit from the cache, __still without delays upon data changes__.<br>

Age sensitive data management becomes very useful when __data changes very frequently__.
as an example, consider an application showing notifications, messages or real time data.
When navigating across the pages speed of navigation may be a priority, so __using cached data, still with a shorter maxage (eg. 120 secs) may be a good choice__.

In these cases navigation will take benefit from the cache hits.
After the navigation completes, the developer may raise a request with __MaxAge = 0__ to load fresh data for the user.<br>
In this way navigation will take advantage of cache hits speed and the user will still see fresh data, when the navigation ends.<br>

# USE CASES

# SUMMARY
`Common.SmartCache` introduces __age sensitive data management__: a new approach to managing data: 

When loading data a developer can specify the __maximum age__ that is required for it.<br>
A __cache hit or a cache miss is determined by the `required age`, provided at every single call__ and not by the cache entries lifetime that is defined at startup.<br>

This __allows using cache with non static data__ such as User profiles or User Permissions or real time data, without requiring delays on perceiving data changes and allowing access to fresh data at any time.<br>


.




