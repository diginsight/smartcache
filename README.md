# INTRODUCTION 
diginsight `SmartCache` provides __hybrid, distributed, multilevel caching__ based on __age sensitive data management__.<br> 
- `SmartCache` is __hybrid__ as it caches data __in-memory__ and on __external RedIs databases__.<br>
In-memory cache ensure __0-latency__ for most recently used data and ensures __low pressure (and reduced cost)__ on the external RedIs database.
- `SmartCache` is __distributed__ as cache entries on different nodes of a multiinstance application are sinchronized automatically, to avoid flickering of values when querying the same data on different nodes.
- `SmartCache` is based on __age sensitive data management__ as cache entries are returned based on a requested __MaxAge__ parameter.<br>
__Data is returned from the cache if the requested MaxAge is compatible with the cache entry__.<br>Otherwise data is requested to the real data provider.
<br>This allows requesting data with __different MaxAge criteria, according to the specific application condition__.<br>
Data loaded by any request, is made available for the benefit of further requests (as long as compatible with their MaxAge requirement).

- `SmartCache` is __Multilevel__: The same entries can be cached in multiple levels (frontend, backend or further levels). <br>At any level, __data is returned from the cache if the requested MaxAge is compatible with the cache entry__. otherwise data is requested to the further levels.<br>
In case all levels entries contains old data, incompatible with the request MaxAge requirement, data is requested to the real data provider.

- `SmartCache` is __Optimized__: as:
    - Privileges __In-memory cache__ => it is faster as in memory cache hits are __'0-Latency'__
    - __Minimizes use of external backing storage__ (e.g. RedIS) => it is __cheaper__ and __scalable__ as accesses to the backing storage are minimized
    - Replicas synchronize always __keys__ and __small values__, __bigger values__ are synchronized on demand
    - SmartCache supports __data preloading__ and __automatic invalidation__ of the cache entries so, __data load latencies can be cut since the first call__.
<br><br>
> SmartCache supports caching data with __low cost__ and __high performance__.<br>
> In particular, __0 latency__ is ensured on in-memory cache hits.
> also, __pressure on external RedIS resource is low__ as most frequently used entries are managed in-memory.
> 
> Also, __0 latency__ can be obtained __since the first and for every call__ by means of __Cache Preloading__ and __Cache Invalidation__.
<br>

# License
See the [LICENSE](<LICENSE>) file for license rights and limitations (MIT).
