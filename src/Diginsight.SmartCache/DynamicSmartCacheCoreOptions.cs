using Diginsight.Options;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Diginsight.SmartCache;

internal sealed class DynamicSmartCacheCoreOptions : IDynamicSmartCacheCoreOptions, IDynamicallyConfigurable
{
    public SmartCacheMode? Mode { get; private set; }

    public Expiration? MaxAge { get; private set; }
    public bool ForceDynamicMaxAge { get; private set; }
    public DateTimeOffset? MinimumCreationDate { get; private set; }

    public Expiration? AbsoluteExpiration { get; private set; }
    public Expiration? SlidingExpiration { get; private set; }

    public int? MissValueSizeThreshold { get; private set; }

    object IDynamicallyConfigurable.MakeFiller() => new Filler(this);

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private sealed class Filler
    {
        private readonly DynamicSmartCacheCoreOptions filled;

        public Filler(DynamicSmartCacheCoreOptions filled)
        {
            this.filled = filled;
        }

        public SmartCacheMode? SmartCacheMode
        {
            get => filled.Mode;
            set => filled.Mode = value;
        }

        public ExpirationForceable? MaxAge
        {
            get => filled.MaxAge is { } maxAge ? new ExpirationForceable(maxAge, filled.ForceDynamicMaxAge) : null;
            set
            {
                if (value is { } maxAge)
                {
                    filled.MaxAge = maxAge.Expiration;
                    filled.ForceDynamicMaxAge = maxAge.Force;
                }
                else
                {
                    filled.MaxAge = null;
                    filled.ForceDynamicMaxAge = false;
                }
            }
        }

        public DateTimeOffset? MinimumCreationDate
        {
            get => filled.MinimumCreationDate;
            set => filled.MinimumCreationDate = value;
        }

        public Expiration? AbsoluteExpiration
        {
            get => filled.AbsoluteExpiration;
            set => filled.AbsoluteExpiration = value;
        }

        public Expiration? SlidingExpiration
        {
            get => filled.SlidingExpiration;
            set => filled.SlidingExpiration = value;
        }

        public int? MissValueSizeThreshold
        {
            get => filled.MissValueSizeThreshold;
            set => filled.MissValueSizeThreshold = value;
        }
    }

    [TypeConverter(typeof(ExpirationForceableConverter))]
    private readonly struct ExpirationForceable
    {
        public Expiration Expiration { get; }
        public bool Force { get; }

        public ExpirationForceable(Expiration expiration, bool force)
        {
            Expiration = expiration;
            Force = force;
        }
    }

    private sealed class ExpirationForceableConverter : TypeConverter
    {
        private static readonly Regex Regex = new("^(.*?)(; *force *)?$", RegexOptions.IgnoreCase);

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof (string) || base.CanConvertFrom(context, sourceType);
        }

#if NET
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
#else
        public override object? ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object? value)
#endif
        {
            if (value is not string s)
            {
                return base.ConvertFrom(context, culture, value);
            }

            Match match = Regex.Match(s);
            return new ExpirationForceable(Expiration.Parse(match.Groups[1].Value, culture), match.Groups[2].Success);
        }
    }
}
