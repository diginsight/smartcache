# Diginsight SmartCache - Copilot Instructions

## Overview
Diginsight SmartCache is a .NET library providing **hybrid, distributed, multilevel caching** with **age-sensitive data management**. The library supports in-memory caching with external backing storage (Redis), distributed synchronization across instances (ServiceBus/Kubernetes), and intelligent cache invalidation/preloading.

## Architecture

### Core Components
- **`SmartCache`** (src/Diginsight.SmartCache/SmartCache.cs) - Main cache implementation managing memory cache, companions, and age-based retrieval
- **`ICacheCompanion`** - Abstraction for distributed cache synchronization (ServiceBus, Kubernetes, HTTP, Local)
- **`CacheKeyService`** - Handles cache key generation, wrapping `ICachable` objects and using `ICacheKeyProvider` extensions
- **`MethodCallCacheKey`** - Standard cache key for method calls including type, method name, and wrapped arguments

### Key Concepts
1. **Age-Sensitive Data**: Cache entries are returned only if compatible with requested `MaxAge` (e.g., request with MaxAge=5min can use cache entry from 3min ago)
2. **Hybrid Storage**: In-memory cache (0-latency) + external Redis backing (evicted/larger entries)
3. **Distributed Sync**: Cache miss notifications and invalidations propagate across instances via companions
4. **Multilevel**: Supports frontend/backend/data-source hierarchy where each level checks age compatibility

### Project Structure
```
src/
├── Diginsight.SmartCache/                    # Core library
│   ├── SmartCache.cs                         # Main implementation
│   ├── ICacheKeyService.cs, CacheKeyService.cs
│   ├── MethodCallCacheKey.cs                 # Standard cache key
│   ├── Externalization/                      # Companion abstractions
│   │   ├── ICacheCompanion.cs
│   │   ├── PassiveCacheLocation.cs           # Redis/backing storage
│   │   └── ActiveCacheLocation.cs            # Other instances
│   └── IInvalidationRule.cs                  # Cache invalidation
├── Diginsight.SmartCache.Externalization.ServiceBus/  # Azure ServiceBus companion
├── Diginsight.SmartCache.Externalization.Kubernetes/  # Kubernetes companion
├── Diginsight.SmartCache.Externalization.Redis/       # Redis backing storage
├── Diginsight.SmartCache.Externalization.Http/        # HTTP companion
├── Diginsight.SmartCache.Externalization.AspNetCore/  # ASP.NET middleware
└── docs/                                      # Quarto-based documentation
    └── 01. Concepts/                          # Detailed HowTo articles
        ├── 01. Cache data, Invalidate entries and reload cache on invalidation.md
        ├── 02. Synchronize cache entries across application instances.md
        └── 03. Configure SmartCache size, latencies, expiration, instances synchronization and RedIs integration.md
```

## Development Workflows

### Building
- **Solution**: `src/Diginsight.SmartCache.slnx` (.slnx format, not .sln)
- **Build**: `dotnet restore src/Diginsight.SmartCache.slnx && dotnet build src/Diginsight.SmartCache.slnx`
- **Target Frameworks**: netstandard2.0, netstandard2.1, net6.0-net10.0 (multi-targeting)
- **CI**: `.github/workflows/v3.yml` - builds on push to tags `v3*`, `v4*`, etc.

### Configuration Patterns
All projects use:
- **Directory.Build.props** for shared settings (LangVersion=preview, Nullable=enable, lock files)
- **Package lock files** (`packages.lock.json`) - always committed
- **Strong naming**: `diginsight.snk` key file

### Service Registration
```csharp
services.AddSmartCache(configuration, environment, loggerFactory)
    .SetServiceBusCompanion(...)  // or .SetKubernetesCompanion() or .SetLocalCompanion()
    .AddHttp();                    // HTTP downstream caching support

services.TryAddSingleton<ICacheKeyProvider, MyCustomKeyProvider>();
```

## Code Conventions

### Cache Key Design
- Use `MethodCallCacheKey` for method result caching
- Include **all parameters that affect output** in cache keys
- Implement `ICachable` interface on domain objects for custom key generation
- Custom key providers via `ICacheKeyProvider` for framework types

Example:
```csharp
var cacheKey = new MethodCallCacheKey(
    cacheKeyService, 
    typeof(MyController), 
    nameof(GetDataAsync),
    userId, filters  // All relevant parameters
);
```

### Cache Operations
```csharp
// Request data with specific MaxAge
var options = new SmartCacheOperationOptions { MaxAge = TimeSpan.FromMinutes(5) };
var result = await smartCache.GetAsync(cacheKey, async _ => FetchFromSourceAsync(), options);

// Invalidation
smartCache.Invalidate(invalidationRule);  // IInvalidationRule marks keys for eviction
```

### Externalization Patterns
- **Companion Selection**: Choose ONE companion (Local/ServiceBus/Kubernetes) - defaults to Local if none specified
- **PassiveCacheLocation**: External storage (Redis) for evicted entries
- **ActiveCacheLocation**: Other running instances to query before fetching from source
- **CacheEventNotifier**: Sends invalidation/miss events to other instances

### Configuration Settings
```json
{
  "Diginsight:SmartCache": {
    "MaxAge": "00:05:00",           // Default max age for cache entries
    "AbsoluteExpiration": "1.00:00", // Hard expiration
    "SlidingExpiration": "04:00:00", // Sliding window
    "ServiceBus": {
      "ConnectionString": "...",
      "TopicName": "smartcache-topic"
    }
  }
}
```

## Critical Implementation Details

### Timestamp Truncation
All timestamps are truncated to seconds (see `SmartCache.Truncate()`) to ensure consistent age comparisons across instances

### Age Compatibility Logic
Cache entry returned if: `entry.Timestamp >= request.MinimumCreationDate` where `MinimumCreationDate = now - MaxAge`

### Cache Entry Events
1. **Cache Hit** - Entry exists with compatible age (memory hit = 0-latency)
2. **Hybrid Hit** - Entry retrieved from Redis backing storage
3. **Cache Miss** - No entry or too old → fetch from source + notify other instances
4. **Eviction** - Memory pressure → offload to Redis (PassiveCacheLocation)
5. **Invalidation** - Manual trigger → remove entries matching `IInvalidationRule`
6. **Reload** - After invalidation, reload delegate re-populates cache

## Important Notes

### DO
- Include exhaustive parameters in `MethodCallCacheKey` to avoid cache poisoning
- Configure ServiceBus/Kubernetes companion for multi-instance deployments
- Use `ConfigureClassAware<SmartCacheCoreOptions>` for per-type MaxAge overrides
- Check `docs/` folder for detailed HowTo articles on invalidation, synchronization, configuration

### DON'T
- Don't use non-deterministic or mutable objects as cache keys
- Don't skip Redis configuration in production (memory-only works for single instance only)
- Don't forget to implement `IInvalidationRule` on cache keys if invalidation is needed
- Don't test across instances without companion configured (Local companion doesn't sync)

## Reference Documentation
- Primary docs: `README.md` and `src/docs/` 
- Working samples: https://github.com/diginsight/smartcache.samples
- Related telemetry library: https://github.com/diginsight/telemetry
