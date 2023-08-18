#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace Common;

public interface ICacheKeyService
{
    static readonly ICacheKeyService Empty = new EmptyCacheKeyService();

    bool TryToKey(object? obj, [NotNullWhen(true)] out ICacheKey? key);

    private sealed class EmptyCacheKeyService : ICacheKeyService
    {
        public bool TryToKey(object? obj, [NotNullWhen(true)] out ICacheKey? key)
        {
            switch (obj)
            {
                case ICacheKey key0:
                    key = key0;
                    return true;

                case ISupportKey supportKey:
                    key = supportKey.GetKey(this);
                    return true;

                default:
                    key = null;
                    return false;
            }
        }
    }
}
