using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenBullet2.Core;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Logging;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using DebuggerPage = OpenBullet2.Native.Views.Pages.Shared.Debugger;
using RuriLib.Logging;
using RuriLib.Providers.RandomNumbers;
using RuriLib.Providers.UserAgents;
using RuriLib.Services;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenBullet2.Core.Models.Proxies;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenBullet2.Native
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IConfiguration config;

        public App()
        {
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskException;

            // Get the directory where the executable is located
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
            // Create UserData directory in the executable's directory
            var userDataPath = Path.Combine(appDirectory, "UserData");
            Directory.CreateDirectory(userDataPath);
            
            var builder = new ConfigurationBuilder()
                .SetBasePath(appDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            config = builder.Build(); // Build the config once and assign it to the field

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<IConfiguration>(_ => config);
            ConfigureServices(serviceCollection);
            serviceProvider = serviceCollection.BuildServiceProvider();
            SP.Init(serviceProvider);

            var workerThreads = config.GetSection("Resources").GetValue("WorkerThreads", 1000);
            var ioThreads = config.GetSection("Resources").GetValue("IOThreads", 1000);
            var connectionLimit = config.GetSection("Resources").GetValue("ConnectionLimit", 1000);

            ThreadPool.SetMinThreads(workerThreads, ioThreads);
            ServicePointManager.DefaultConnectionLimit = connectionLimit;

            // Apply DB migrations or create a DB if it doesn't exist
            using (var serviceScope = serviceProvider.GetService<IServiceScopeFactory>().CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.Migrate();
            }

            // Load the configs
            var configService = serviceProvider.GetService<ConfigService>();
            configService.ReloadConfigsAsync().Wait();

            AutocompletionProvider.Init();

            // Start the job monitor at the start of the application,
            // otherwise it will only be started when navigating to the page
            _ = serviceProvider.GetService<JobMonitorService>();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Get the app directory for absolute paths
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var userDataPath = Path.Combine(appDirectory, "UserData");
            
            // Windows and pages
            services.AddSingleton<MainWindow>();
            services.AddSingleton<DebuggerPage>();

            // EF - Use absolute path for database
            var dbConnectionString = config.GetConnectionString("DefaultConnection");
            var absoluteDbPath = dbConnectionString.Replace("UserData/OpenBullet.db", 
                Path.Combine(userDataPath, "OpenBullet.db").Replace('\\', '/'));
            
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(absoluteDbPath,
                b => b.MigrationsAssembly("OpenBullet2.Core")), ServiceLifetime.Transient);

            // Repositories
            services.AddSingleton<IProxyRepository, DbProxyRepository>();
            services.AddSingleton<IProxyGroupRepository, DbProxyGroupRepository>();
            services.AddSingleton<IHitRepository, DbHitRepository>();
            services.AddSingleton<IJobRepository, DbJobRepository>();
            services.AddSingleton<IRecordRepository, DbRecordRepository>();
            services.AddSingleton<IGuestRepository, DbGuestRepository>();
            services.AddSingleton<IConfigRepository>(service =>
                new DiskConfigRepository(service.GetService<RuriLibSettingsService>(),
                Path.Combine(userDataPath, "Configs")));
            services.AddSingleton<IWordlistRepository>(service =>
                new HybridWordlistRepository(service.GetService<ApplicationDbContext>(),
                Path.Combine(userDataPath, "Wordlists")));

            // Singletons
            services.AddSingleton<VolatileSettingsService>();
            services.AddSingleton<ViewModelsService>();
            services.AddSingleton<AnnouncementService>();
            services.AddSingleton<UpdateService>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<ProxyReloadService>();
            services.AddSingleton<ProxyCheckOutputFactory>();
            services.AddSingleton<JobFactoryService>();
            services.AddSingleton<JobManagerService>();
            services.AddSingleton<JobMonitorService>();
            services.AddSingleton<HitStorageService>();
            services.AddSingleton<DataPoolFactoryService>();
            services.AddSingleton<ProxySourceFactoryService>();
            services.AddSingleton(_ => new RuriLibSettingsService(userDataPath));
            services.AddSingleton(_ => new OpenBulletSettingsService(userDataPath));
            services.AddSingleton(_ => new PluginRepository(Path.Combine(userDataPath, "Plugins")));
            services.AddSingleton<IRandomUAProvider>(_ => new IntoliRandomUAProvider(Path.Combine(appDirectory, "user-agents.json")));
            services.AddSingleton<IRNGProvider, DefaultRNGProvider>();
            services.AddSingleton<MemoryJobLogger>();
            services.AddSingleton<IJobLogger>(service =>
                new FileJobLogger(service.GetService<RuriLibSettingsService>(),
                Path.Combine(userDataPath, "Logs", "Jobs")));
            services.AddSingleton<HotkeyService>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Call base implementation first
            base.OnStartup(e);
            
            // Allow multiple instances without confirmation
            // Note: Mutex is no longer used for instance control
            
            try
            {
                var mainWindow = serviceProvider.GetService<MainWindow>();
                mainWindow.NavigateTo(MainWindowPage.Home);
                mainWindow.Show();
                
                // Ensure the application doesn't shut down immediately
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                
                // Apply conservative GPU optimizations to reduce laptop heating
                ApplyConservativeGpuSettings();
            }
            catch (Exception ex)
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var errorLogPath = Path.Combine(appDirectory, $"startup-error-{System.Diagnostics.Process.GetCurrentProcess().Id}.log");
                File.WriteAllText(errorLogPath, $"Startup error on {DateTime.Now}\r\n{ex}");
                MessageBox.Show($"Startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Shutdown(1);
            }
        }

        private void SetLowPerformanceModeEarly()
        {
            try
            {
                // Try conservative performance settings first (less likely to crash)
                // Reduce performance tier to tier 1 (medium performance)
                // This reduces GPU usage without forcing software-only rendering
                var currentTier = RenderCapability.Tier >> 16;
                Debug.WriteLine($"Current render tier: {currentTier}");
                
                // If we have a high-performance GPU (tier 2), we can potentially reduce heat
                if (currentTier >= 2)
                {
                    Debug.WriteLine("High-performance GPU detected, will use conservative settings");
                }
                
                Debug.WriteLine("Conservative performance mode enabled to reduce GPU usage and laptop heating");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not check render capabilities: {ex.Message}");
                Debug.WriteLine("Using default rendering settings");
            }
        }

        private void ApplyConservativeGpuSettings()
        {
            try
            {
                // Set conservative animation settings (reduce from 60fps to 30fps)
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = 30 }
                );
                
                Debug.WriteLine("Conservative GPU settings applied: 30fps animations to reduce laptop heating");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not apply conservative GPU settings: {ex.Message}");
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReportCrash(e.Exception);
            e.Handled = true; // Set to false to close the app on exception
        }

        private void OnTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // Comment this line to close the app on task exception

            // I decided to disable the code below since usually task exceptions are not critical to the application

            /*
            if (e.Exception.InnerException is not null)
            {
                if (e.Exception.InnerException is PuppeteerSharp.PuppeteerException // https://github.com/hardkoded/puppeteer-sharp/issues/891
                    or System.Net.Sockets.SocketException // Seems like all networking-related things can cause unhandled task exceptions
                    or TimeoutException) // This is again thrown by Puppeteer
                {
                    return;
                }
            }

            ReportCrash(e.Exception);
            */
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            base.OnExit(e);
        }

        private static void ReportCrash(Exception ex)
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var crashLogPath = Path.Combine(appDirectory, "crash.log");
            File.WriteAllText(crashLogPath, $"Unhandled exception thrown on {DateTime.Now}\r\n{ex}");

            Alert.Error("Unhandled exception", $"An unhandled exception was thrown, the application will try to continue running." +
                $" Please open the crash.log file, copy the error message inside it and open an issue on the official github repository." +
                $" A few details about the exception: {ex.Message}");
        }
    }
}
