namespace Diginsight.SmartCache;

public sealed class SmartCacheOperationOptions
{
    public bool Disabled { get; set; }

    public Expiration? MaxAge { get; set; }

    public Expiration? AbsoluteExpiration { get; set; }
    public Expiration? SlidingExpiration { get; set; }

    /// <summary>
    /// Coalesce concurrent racing misses for the same key onto a single origin fetch (in-memory,
    /// single node). <see langword="null"/> inherits from configuration. Implied by
    /// <see cref="CoalesceRacingCacheMissesAcrossNodes"/>.
    /// </summary>
    public bool? CoalesceRacingCacheMisses { get; set; }

    /// <summary>
    /// Extend racing-miss coalescing across nodes (best-effort, over the companion). Implies
    /// <see cref="CoalesceRacingCacheMisses"/>. <see langword="null"/> inherits from configuration.
    /// </summary>
    public bool? CoalesceRacingCacheMissesAcrossNodes { get; set; }

    public SmartCacheOperationOptions Clone() => (SmartCacheOperationOptions)MemberwiseClone();
}
