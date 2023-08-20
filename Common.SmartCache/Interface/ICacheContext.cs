using System;

namespace Common.SmartCache;

public interface ICacheContext
{
    bool Enabled { get; }
    int? MaxAge { get; }
    int? AbsoluteExpiration { get; }
    int? SlidingExpiration { get; }
    Type InterfaceType { get; }
}
