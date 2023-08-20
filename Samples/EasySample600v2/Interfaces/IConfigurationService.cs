using Common.SmartCache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasySample600v2
{
    public interface IConfigurationService
    {
        Task<string> GetSetting(string key, string defaultValue, CacheContext cacheContext, CancellationToken cancellationToken = default);
    }
}
