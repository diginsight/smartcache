﻿#region using
using Common;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using static System.Formats.Asn1.AsnWriter;
using Microsoft.AspNetCore.Http;
using EasySample600v2;
using Common.SmartCache;
#endregion

namespace EasySample
{
    /// <summary>Interaction logic for App.xaml</summary>
    public partial class App : Application
    {
        static Type T = typeof(App);
        const string CONFIGVALUE_APPINSIGHTSKEY = "AppInsightsKey", DEFAULTVALUE_APPINSIGHTSKEY = "";

        public static IHost Host;
        private ILogger<App> _logger;

        static App()
        {
            using (var scope = TraceLogger.BeginMethodScope(T))
            {
                try
                {
                    // sec.Debug("this is a debug trace");
                    // sec.Information("this is a Information trace");
                    // sec.Warning("this is a Warning trace");
                    // sec.Error("this is a error trace");

                    throw new InvalidOperationException("this is an exception");
                }
                catch (Exception /*ex*/) { /*sec.Exception(ex);*/ }
            }
        }

        public App()
        {
            using (var scope = Host.BeginMethodScope(T))
            {
            }
        }
        protected override async void OnStartup(StartupEventArgs e)
        {
            var logger = Host.GetLogger<App>();
            using (var scope = logger.BeginMethodScope())
            {
                var configuration = TraceLogger.GetConfiguration();
                var classConfigurationGetter = new ClassConfigurationGetter<App>(configuration);
                //var appInsightKey = ConfigurationHelper.GetClassSetting<App, string>(CONFIGVALUE_APPINSIGHTSKEY, DEFAULTVALUE_APPINSIGHTSKEY); // , CultureInfo.InvariantCulture

                Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                        .ConfigureAppConfiguration(builder =>
                        {
                            builder.Sources.Clear();
                            builder.AddConfiguration(configuration);
                            builder.AddEnvironmentVariables();
                        }).ConfigureServices((context, services) =>
                        {
                            //context.HostingEnvironment
                            ConfigureServices(context.Configuration, services, context.HostingEnvironment);
                        })
                        .ConfigureLogging((context, loggingBuilder) =>
                        {
                            //var classConfigurationGetter = new ClassConfigurationGetter<App>(context.Configuration);
                            var appInsightKey = classConfigurationGetter.Get(CONFIGVALUE_APPINSIGHTSKEY, DEFAULTVALUE_APPINSIGHTSKEY);

                            loggingBuilder.AddConfiguration(context.Configuration.GetSection("Logging"));

                            loggingBuilder.ClearProviders();

                            //var consoleProvider = new TraceLoggerConsoleProvider();
                            //loggingBuilder.AddDiginsightFormatted(consoleProvider, configuration);

                            //var actionsRecorderProvider = new ActionsRecorderProvider();
                            //var diginsightProvider = new TraceLoggerFormatProvider();
                            //diginsightProvider.AddProvider(actionsRecorderProvider);
                            //// TraceEntry
                            ////    classname
                            ////    methodname
                            ////    Start(payload), End (result), log

                            var options = new Log4NetProviderOptions();
                            options.Log4NetConfigFileName = "log4net.config";
                            var log4NetProvider = new Log4NetProvider(options);
                            //loggingBuilder.AddProvider(log4NetProvider);
                            loggingBuilder.AddDiginsightFormatted(log4NetProvider, configuration);

                            var telemetryConfiguration = new TelemetryConfiguration(appInsightKey);
                            var appinsightOptions = new ApplicationInsightsLoggerOptions();
                            var tco = Options.Create<TelemetryConfiguration>(telemetryConfiguration);
                            var aio = Options.Create<ApplicationInsightsLoggerOptions>(appinsightOptions);
                            //loggingBuilder.AddDiginsightJson(new ApplicationInsightsLoggerProvider(tco, aio), configuration);
                            loggingBuilder.AddDiginsightFormatted(new ApplicationInsightsLoggerProvider(tco, aio), configuration);

                        }).Build();

                Host.InitTraceLogger();

                // LogStringExtensions.RegisterLogstringProvider(this);

                await Host.StartAsync(); scope.LogDebug($"await Host.StartAsync();");

                var mainWindow = Host.Services.GetRequiredService<MainWindow>(); scope.LogDebug($"Host.Services.GetRequiredService<MainWindow>(); returns {mainWindow.GetLogString()}");

                mainWindow.Show(); scope.LogDebug($"mainWindow.Show();");

                base.OnStartup(e); scope.LogDebug($"base.OnStartup(e);");
            }
        }
        private void ConfigureServices(IConfiguration configuration, IServiceCollection services, IHostEnvironment hostEnvironment)
        {
            //services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddHttpContextAccessor();
            services.AddClassConfiguration();
            services.AddParallelService(configuration);
            services.AddCacheService(configuration, hostEnvironment);
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            services.AddSingleton<MainWindow>();


            // TODO: register as a singleton ILogger

        }
        protected override async void OnExit(ExitEventArgs e)
        {
            using (Host)
            {
                await Host.StopAsync(TimeSpan.FromSeconds(5));
            }

            base.OnExit(e);
        }

        private string GetMethodName([CallerMemberName] string memberName = "") { return memberName; }
    }

}
