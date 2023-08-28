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
Also, loaded data is always tagged with its creation date.<br>
for this reasom 


 

The process of fetching data from remote sources can be both time-consuming and resource-intensive. Within this context, our application adopts an innovative strategy. Each piece of data in our system carries two important attributes: its Age and Creation Date. The uniqueness of this approach lies in acknowledging that real-time data may not always be a necessity for an application to perform as expected.

Consider this: not all applications demand the latest data snapshot for their functionality to unfold seamlessly. In fact, there are instances where working with slightly outdated data might yield equally accurate outcomes. This opens the gateway to significantly enhancing application performance.

With Age Sensitive Data Management, the data we load is meticulously tagged with its corresponding creation date. When the application retrieves data, it specifies the maximum acceptable age for the information required. This empowers the application to determine in real-time whether the loaded data is still valid or has crossed its acceptable age threshold.

In essence, Age Sensitive Data Management revolutionizes the traditional data loading paradigm by providing a more flexible and optimized approach. It ensures that data freshness aligns precisely with the application's needs, maximizing performance without compromising accuracy.




