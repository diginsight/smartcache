#region using
using Common;
using Common.SmartCache;
using EasySample600v2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Metrics = System.Collections.Generic.Dictionary<string, object>; // $$$
#endregion

namespace EasySample
{
    //public class C : WeakEventManager { }
    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        static Type T = typeof(MainWindow);
        private ILogger<MainWindow> logger;
        private IClassConfigurationGetter<MainWindow> classConfigurationGetter;
        private IConfigurationService configurationService;

        private string GetScope([CallerMemberName] string memberName = "") { return memberName; }

        static MainWindow()
        {
            var host = App.Host;
            //var logger = host.GetLogger<MainWindow>();
            //using (var scope = logger.BeginMethodScope())
            //{
            //}
            using var scope = host.BeginMethodScope<MainWindow>();
        }
        public MainWindow(
            ILogger<MainWindow> logger,
            IClassConfigurationGetter<MainWindow> classConfigurationGetter,
            IConfigurationService configurationService
            )
        {
            this.logger = logger;
            this.classConfigurationGetter = classConfigurationGetter;
            this.configurationService = configurationService;   
            // using (_logger.BeginMethodScope())
            using (logger.BeginScope(TraceLogger.GetMethodName()))
            {
                InitializeComponent();
            }
        }
        private async void MainWindow_Initialized(object sender, EventArgs e)
        {
            using var scope = logger.BeginMethodScope(() => new { sender, e });

            classConfigurationGetter.Get("SampleConfig", "");

        }
        void sampleMethod()
        {
            logger.LogDebug("pippo");

        }

        int i = 0;
        private async void btnRun_Click(object sender, RoutedEventArgs e)
        {
            // _logger.PushOperationId();
            using var scope = logger.BeginMethodScope(() => new { sender = sender.GetLogString(), e = e.GetLogString() }, SourceLevels.Verbose, LogLevel.Debug, null, new Dictionary<string, object>() { { "OperationId", Guid.NewGuid().ToString() } });

            var time = DateTime.Now;

            try
            {
                var cacheContext = new CacheContext() { Enabled = true, MaxAge = 600 };
                var sampleValue = await configurationService.GetSetting("SampleConfig", "", cacheContext, CancellationToken.None);
                scope.LogDebug(new { sampleValue });




            }
            catch (Exception ex)
            {
                scope.LogException(ex);
            }
        }

        public int SampleMethodWithResult(int i, string s)
        {
            using var scope = logger.BeginMethodScope(new { i, s });

            var result = 0;

            var j = i++; scope.LogDebug(new { i, j });

            Thread.Sleep(100); scope.LogDebug($"Thread.Sleep(100); completed");
            SampleMethodNested(); scope.LogDebug($"SampleMethodNested(); completed");
            SampleMethodNested1(); scope.LogDebug($"SampleMethodNested1(); completed");

            scope.Result = result;
            return result;

        }
        public void SampleMethod()
        {
            using (var sec = logger.BeginMethodScope())
            {
                Thread.Sleep(100);
                SampleMethodNested();
                SampleMethodNested1();

            }
        }
        public void SampleMethodNested()
        {
            using var scope = logger.BeginMethodScope();
            Thread.Sleep(100);
        }
        public void SampleMethodNested1()
        {
            using var scope = logger.BeginMethodScope();
            Thread.Sleep(10);
        }
        async Task<bool> sampleMethod1Async()
        {
            using (var scope = logger.BeginMethodScope())
            {
                var res = true;

                await Task.Delay(0); scope.LogDebug($"await Task.Delay(0);");

                return res;
            }
        }
    }
}
