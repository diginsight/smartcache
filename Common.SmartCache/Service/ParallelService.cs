using ABB.Ability.ELSP.CommonPlatform.Shared;
using Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ABB.EL.Common.Commons.Helpers
{
    public class ParallelService : IParallelService
    {
        private const int LOWCONCURRENCY_DEFAULT = 3;
        private const int MEDIUMCONCURRENCY_DEFAULT = 6;
        private const int HIGHCONCURRENCY_DEFAULT = 12;

        private readonly ILogger<ParallelService> logger;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IParallelServiceOptions options;

        private int? lowConcurrency;
        private int? mediumConcurrency;
        private int? highConcurrency;

        public int LowConcurrency => lowConcurrency ??= options.LowConcurrency is > 0 and var lc ? lc : LOWCONCURRENCY_DEFAULT;
        public int MediumConcurrency => mediumConcurrency ??= options.MediumConcurrency is > 0 and var mc ? mc : MEDIUMCONCURRENCY_DEFAULT;
        public int HighConcurrency => highConcurrency ??= options.HighConcurrency is > 0 and var hc ? hc : HIGHCONCURRENCY_DEFAULT;

        public ParallelService(
            ILogger<ParallelService> logger,
            IHttpContextAccessor httpContextAccessor,
            IOptions<ParallelServiceOptions> options)
        {
            this.logger = logger;
            this.options = options.Value;
            this.httpContextAccessor = httpContextAccessor;
        }

        public void ForEach<TSource>(
            IEnumerable<TSource> source,
            ParallelOptions parallelOptions,
            Action<TSource> body)
        {
            using var scope = logger.BeginMethodScope(() => new { source = source.GetLogString(), parallelOptions = parallelOptions.GetLogString() });
            if (source?.Any() != true)
            {
                return;
            }

            SetMaxConcurrency(parallelOptions, scope);

            Parallel.ForEach(source, parallelOptions ?? new ParallelOptions(), body);
        }

        public async Task ForEachAsync<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TSource, Task> body)
        {
            using var scope = logger.BeginMethodScope(() => new { source = source.GetLogString(), parallelOptions = parallelOptions.GetLogString() });
            if (source?.Any() != true)
            {
                return;
            }

            SetMaxConcurrency(parallelOptions, scope);

            await Parallel.ForEachAsync(
                source,
                parallelOptions ?? new ParallelOptions(),
                async (item, _) => { await body(item); });
        }

        async Task IParallelService.WhenAllAsync(IEnumerable<Func<Task>> taskFactories, ParallelOptions parallelOptions)
        {
            using var scope = logger.BeginMethodScope(() => new { tasks = taskFactories.GetLogString(), parallelOptions = parallelOptions.GetLogString() });
            if (taskFactories?.Any() != true)
            {
                return;
            }

            SetMaxConcurrency(parallelOptions, scope);

            await Parallel.ForEachAsync(
                taskFactories,
                parallelOptions ?? new ParallelOptions(),
                static async (taskFactory, _) => { await taskFactory(); });
        }

        private void SetMaxConcurrency(ParallelOptions parallelOptions, CodeSectionScope scope)
        {
            if (parallelOptions == null)
            {
                return;
            }

            const string headerName = "MaxConcurrency";

            int maxConcurrency;
            if (httpContextAccessor?.HttpContext?.Request.Headers.TryGetValue(headerName, out StringValues headerValues) == true
                && int.TryParse(headerValues.LastOrDefault(), out int headerMax))
            {
                scope.LogInformation($"From header: {headerName}={headerMax}");
                maxConcurrency = headerMax;
            }
            else
            {
                maxConcurrency = parallelOptions.MaxDegreeOfParallelism;
            }

            parallelOptions.MaxDegreeOfParallelism = maxConcurrency;
        }
    }
}
