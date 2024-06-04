using Diginsight.CAOptions;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Diginsight.SmartCache;

public sealed class SmartCacheCoreOptions : ISmartCacheCoreOptions, IDynamicallyPostConfigurable
{
    private Expiration maxAge = Expiration.Never;
    private bool forceDynamicMaxAge;
    private DateTimeOffset? minimumCreationDate;
    private Expiration absoluteExpiration = Expiration.Never;
    private Expiration slidingExpiration = Expiration.Never;
    private TimeSpan localEntryTolerance = TimeSpan.FromSeconds(10);

    public SmartCacheMode Mode { get; set; }

    public Expiration MaxAge
    {
        get => maxAge;
        set => maxAge = value >= Expiration.Zero ? value : Expiration.Zero;
    }

    bool ISmartCacheCoreOptions.ForceDynamicMaxAge => forceDynamicMaxAge;

    DateTimeOffset? ISmartCacheCoreOptions.MinimumCreationDate => minimumCreationDate;

    public Expiration AbsoluteExpiration
    {
        get => absoluteExpiration;
        set => absoluteExpiration = value >= Expiration.Zero ? value : Expiration.Zero;
    }

    public Expiration SlidingExpiration
    {
        get => slidingExpiration;
        set => slidingExpiration = value >= Expiration.Zero ? value : Expiration.Zero;
    }

    public int LocationPrefetchCount { get; set; } = 5;
    public int LocationMaxParallelism { get; set; } = 2;

    public int MissValueSizeThreshold { get; set; } = 5_000;

    public long LowPrioritySizeThreshold { get; set; } = 20_000;
    public long MidPrioritySizeThreshold { get; set; } = 10_000;

    public TimeSpan LocalEntryTolerance
    {
        get => localEntryTolerance;
        set => localEntryTolerance = value >= TimeSpan.Zero ? value : TimeSpan.Zero;
    }

    object IDynamicallyPostConfigurable.MakeFiller() => new Filler(this);

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private sealed class Filler
    {
        private readonly SmartCacheCoreOptions filled;

        public Filler(SmartCacheCoreOptions filled)
        {
            this.filled = filled;
        }

        public SmartCacheMode SmartCacheMode
        {
            get => filled.Mode;
            set => filled.Mode = value;
        }

        public ExpirationForceable MaxAge
        {
            get => new (filled.MaxAge, filled.forceDynamicMaxAge);
            set => (filled.MaxAge, filled.forceDynamicMaxAge) = value;
        }

        public DateTimeOffset? MinimumCreationDate
        {
            get => filled.minimumCreationDate;
            set => filled.minimumCreationDate = value;
        }

        public Expiration AbsoluteExpiration
        {
            get => filled.AbsoluteExpiration;
            set => filled.AbsoluteExpiration = value;
        }

        public Expiration SlidingExpiration
        {
            get => filled.SlidingExpiration;
            set => filled.SlidingExpiration = value;
        }

        public int MissValueSizeThreshold
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

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out Expiration expiration, out bool force)
        {
            expiration = Expiration;
            force = Force;
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
