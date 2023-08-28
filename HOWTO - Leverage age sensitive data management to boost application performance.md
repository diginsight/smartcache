# INTRODUCTION 
`Common.SmartCache` introduces __Age sensitive data management__: a new approach to managing data.<br><br> 
In highly distributed environments, data is inherently __disconnected__ and __often loaded across multiple boundaries__.<br>
for this reasons, loading data from a remote location may be an __expensive operation__ and __loading data efficiently__ may become __a critical challenge__.<br>
<br>

At any given timeframe, data managed by our application has an __Age__ and a __Creation Date__.<br>
Not always fresh data data is strictly needed to obtain the expected behaviour.<br>
Often, applications may work with data that is __not up to date__.<br><br>
This is a great opportunity to __boost application performance__: when fresh data is not strictly required data cached from previous calls or data pre-loaded in background may be used to ensure the shortest possible latencies.<br> 
<br><br>

# WHAT IS AGE SENSITIVE DATA MANAGEMENT 
Every time the application loads data, it can specify the __maximum age__ that is expected for it.<br><br>

```c#
var cacheContext = new CacheContext() { Enabled = true, MaxAge = 300 }; // required age is expressed in seconds
var userProfile = await userProfileService.FindUserByEmailAddressAsync(context.Account.Email, cacheContext).ConfigureAwait(false);
```

When loading data with Common.SmartCache, data is always tagged with its __creation date__.<br>
