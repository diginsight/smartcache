using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Common.SmartCache;

public sealed class CachePersistenceFileProvider : ICachePersistenceFileProvider
{
#nullable enable
    private readonly IFileProvider decoratee;

    public CachePersistenceFileProvider(IFileProvider decoratee)
    {
        this.decoratee = decoratee;
    }
#nullable restore

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        return decoratee.GetDirectoryContents(subpath);
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        return decoratee.GetFileInfo(subpath);
    }

    public IChangeToken Watch(string filter)
    {
        return decoratee.Watch(filter);
    }
}
