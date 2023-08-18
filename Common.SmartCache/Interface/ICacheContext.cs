using System;

namespace Common;

public interface ICacheContext
{
    bool Enabled { get; }
    int? MaxAge { get; }
    int? AbsoluteExpiration { get; }
    int? SlidingExpiration { get; }
    Type InterfaceType { get; }
}
