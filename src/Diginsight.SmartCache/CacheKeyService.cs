using Diginsight.SmartCache.Externalization;
using Newtonsoft.Json;
using System.Collections;

namespace Diginsight.SmartCache;

internal sealed class CacheKeyService : ICacheKeyService
{
    public static readonly ICacheKeyService Empty = new CacheKeyService([ ]);

    private readonly IEnumerable<ICacheKeyProvider> cacheKeyProviders;

    public CacheKeyService(IEnumerable<ICacheKeyProvider> cacheKeyProviders)
    {
        this.cacheKeyProviders = cacheKeyProviders;
    }

    public object? ToKey(object? obj)
    {
        switch (obj)
        {
            case null:
                return null;

            case string:
                return obj;

            case ICachable x:
                return x.ToKey(this);
        }

        foreach (ICacheKeyProvider provider in cacheKeyProviders)
        {
            if (provider.ToKey(this, obj) is { } result)
            {
                return result;
            }
        }

        return obj switch
        {
            IEnumerable<object> x => this.Wrap(x),
            IEnumerable x => this.Wrap(x.Cast<object>()),
            IEquatable<object> x => x,
            IStructuralEquatable x => new StructuralEquatable(x),
            _ => null,
        };
    }

    [CacheInterchangeName("SE")]
    private sealed class StructuralEquatable
        : IEquatable<StructuralEquatable>, IUnwrappable
    {
        [JsonConverter(typeof(DetailedJsonConverter))]
        private readonly IStructuralEquatable underlying;

        public StructuralEquatable(IStructuralEquatable underlying)
        {
            this.underlying = underlying;
        }

        public bool Equals(StructuralEquatable? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return underlying.Equals(other.underlying, EqualityComparer<object>.Default);
        }

        public override bool Equals(object? obj) => Equals(obj as StructuralEquatable);

        public override int GetHashCode()
        {
            return underlying.GetHashCode(EqualityComparer<object>.Default);
        }

        public object Unwrap() => underlying is IUnwrappable u ? u.Unwrap() : underlying;
    }
}
