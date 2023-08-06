using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace Common
{
    public sealed class TraceSourceConfig 
    {
        public string name { get; set; }
        public string switchName { get; set; }
        public string switchType { get; set; }
        public ListenerConfig[] listeners { get; set; }
    }
    public sealed class SwitchConfig
    {
        public string name { get; set; }
        public SourceLevels value { get; set; }
    }
    public sealed class ListenerFilterConfig
    {
        public string type { get; set; }
        public string initializeData { get; set; }
    }
    public sealed class ListenerConfig
    {
        public string action { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public ListenerConfig innerListener { get; set; }
        public ListenerFilterConfig filter { get; set; }
    }
    public sealed class SystemDiagnosticsConfig
    {
        public TraceSourceConfig[] sources { get; set; }
        public SwitchConfig[] switches { get; set; }
        public ListenerConfig[] sharedListeners { get; set; }
    }
}
