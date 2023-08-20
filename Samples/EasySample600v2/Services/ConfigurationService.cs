using Common;
using Common.SmartCache;
using log4net.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EasySample600v2
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<ConfigurationService> logger;
        private readonly ICacheService cacheService;

        public ConfigurationService(
            ILogger<ConfigurationService> logger,
            IConfiguration configuration,
            ICacheService cacheService
            )
        {
            this.logger = logger;
            this.configuration = configuration;
            this.cacheService = cacheService;
        }

        public async Task<string> GetSetting(string key, string defaultValue, CacheContext cacheContext, CancellationToken cancellationToken)
        {
            var scope = logger.BeginMethodScope(new { key, defaultValue, cacheContext = cacheContext.GetLogString() });

            EnsureCacheContext<IConfigurationService>(ref cacheContext);

            var cacheKey = new GetByOrgSiteGroupIdCacheKey(key, defaultValue);

            var result = await cacheService.GetAsync(
                        cacheKey,
                        async () => {
                            var ret = defaultValue;
                            ret = configuration.GetValue<string>($"AppSettings:{key}", defaultValue);
                            return ret;
                        },
                        cacheContext);
            scope.Result = result.GetLogString();

            return result;
        }


        protected static void EnsureCacheContext<T>([NotNull] ref CacheContext? cacheContext)
        {
            cacheContext ??= new();
            cacheContext.InterfaceType = typeof(T);
        }

        private sealed record GetByOrgSiteGroupIdCacheKey(string key, string defaultValue) : ICacheKey //, IInvalidatable
        {
            //public bool IsInvalidatedBy(IInvalidationRule r, out Func<Task>? ic)
            //{
            //    ic = null;
            //    return r is GroupInvalidationRule gir
            //        && gir.OrganizationId == OrganizationId
            //        && gir.SiteId == SiteId
            //        && gir.GroupId == GroupId;
            //}

            public string ToLogString() => ToString();
        }


    }
}
