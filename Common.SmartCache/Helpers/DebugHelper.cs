using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common
{
    internal class DebugHelper
    {
        #region internal state
        private static bool _isDebugBuild = false;
        //private static ConcurrentDictionary<string, object> _dicOverrides = new ConcurrentDictionary<string, object>();
        #endregion

        #region properties
        public static bool IsDebugBuild { get => _isDebugBuild; set => _isDebugBuild = value; }
        public static bool IsReleaseBuild { get => !_isDebugBuild; set => _isDebugBuild = !value; }
        #endregion

        #region .ctor
        static DebugHelper()
        {
#if DEBUG
            IsDebugBuild = true;
#endif
        }
        #endregion

        public static void IfDebug(Action action)
        {
            if (!_isDebugBuild) { return; }
            action();
        }
    }

    internal class LogLevelHelper
    {
        public static TraceEventType ToTraceEventType(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace: return TraceEventType.Verbose;
                case LogLevel.Debug: return TraceEventType.Verbose;
                case LogLevel.Information: return TraceEventType.Information;
                case LogLevel.Warning: return TraceEventType.Warning;
                case LogLevel.Error: return TraceEventType.Error;
                case LogLevel.Critical: return TraceEventType.Critical;
                case LogLevel.None: return TraceEventType.Verbose;
                default: break;
            }
            return TraceEventType.Verbose;
        }
        public static SourceLevels ToSourceLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace: return SourceLevels.Verbose;
                case LogLevel.Debug: return SourceLevels.Verbose;
                case LogLevel.Information: return SourceLevels.Information;
                case LogLevel.Warning: return SourceLevels.Warning;
                case LogLevel.Error: return SourceLevels.Error;
                case LogLevel.Critical: return SourceLevels.Critical;
                case LogLevel.None: return SourceLevels.Verbose;
                default: break;
            }
            return SourceLevels.Verbose;
        }
    }

    internal static class ThreadHelper
    {
        public static void WaitUntil(Func<bool> condition, int ms = 20)
        {
            while (!condition()) { Thread.Sleep(ms); }
        }
    }
}
