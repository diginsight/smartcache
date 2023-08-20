using System;
using System.Text;
using Common;

namespace Common.SmartCache;

public class CacheContext : ICacheContext, ISupportLogString
{
    public bool Enabled { get; set; }
    public int? MaxAge { get; set; }
    public int? AbsoluteExpiration { get; set; }
    public int? SlidingExpiration { get; set; }
    public Type InterfaceType { get; set ; }

    public CacheContext Clone()
    {
        return new ()
        {
            Enabled = Enabled,
            MaxAge = MaxAge,
            AbsoluteExpiration = AbsoluteExpiration,
            SlidingExpiration = SlidingExpiration,
        };
    }

    public string ToLogString()
    {
        StringBuilder sb = new StringBuilder($"{{{nameof(CacheContext)}:{{Enabled:").Append(Enabled);

        if (MaxAge is { } maxAge)
        {
            sb.Append($",MaxAge:{maxAge}");
        }

        if (AbsoluteExpiration is { } absoluteExpiration)
        {
            sb.Append($",AbsoluteExpiration:{absoluteExpiration}");
        }

        if (SlidingExpiration is { } slidingExpiration)
        {
            sb.Append($",SlidingExpiration:{slidingExpiration}");
        }

        return sb.Append("}}").ToString();
    }
}
