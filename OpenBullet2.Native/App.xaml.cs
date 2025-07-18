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
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Globalization;
using System.Text;

namespace OpenBullet2.Native;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IConfiguration _config;

    public App()
    {
        Trace("App constructor START");
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskException;

        Trace("After exception handlers");
        // Get the directory where the executable is located
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Create UserData directory in the executable's directory
        var userDataPath = Path.Combine(appDirectory, "UserData");
        Directory.CreateDirectory(userDataPath);

        Trace($"UserDataPath: {userDataPath}");

        var builder = new ConfigurationBuilder()
            .SetBasePath(appDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        _config = builder.Build(); // Build the config once and assign it to the field

        Trace("Configuration built");

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient(_ => _config);
        ConfigureServices(serviceCollection);
        _serviceProvider = serviceCollection.BuildServiceProvider();
        SP.Init(_serviceProvider);

        Trace("ServiceProvider built");

        var workerThreads = _config.GetSection("Resources").GetValue("WorkerThreads", 50);
        var ioThreads = _config.GetSection("Resources").GetValue("IOThreads", 50);
        var connectionLimit = _config.GetSection("Resources").GetValue("ConnectionLimit", 100);
        var lowSpecMode = _config.GetSection("Performance").GetValue("LowSpecMode", true);
        var enableGcOptimization = _config.GetSection("Performance").GetValue("EnableGarbageCollectionOptimization", true);

        // Dynamically cap the min threads based on the number of logical processors to avoid
        // spawning an excessive amount of threads which can hurt performance on low-core systems.
        var logicalCores = Environment.ProcessorCount;
        
        if (lowSpecMode)
        {
            // For low-spec systems, use conservative thread limits
            workerThreads = Math.Min(workerThreads, Math.Max(logicalCores, 4));
            ioThreads = Math.Min(ioThreads, Math.Max(logicalCores, 4));
            connectionLimit = Math.Min(connectionLimit, 50);
        }
        else
        {
            workerThreads = Math.Min(workerThreads, logicalCores * 4);
            ioThreads = Math.Min(ioThreads, logicalCores * 4);
        }

        ThreadPool.SetMinThreads(workerThreads, ioThreads);
        ServicePointManager.DefaultConnectionLimit = connectionLimit;
        
        // Apply GC optimizations for low-spec systems
        if (enableGcOptimization)
        {
            ApplyGarbageCollectionOptimizations();
        }

        Trace($"ThreadPool min threads set to W:{workerThreads} IO:{ioThreads} on {logicalCores} cores");

        // Apply DB migrations or create a DB if it doesn't exist
        using var serviceScope = _serviceProvider.GetService<IServiceScopeFactory>().CreateScope();
        var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Attempt to apply pending migrations. If the database is locked because another
        // instance is already using it, catch the exception and continue so that the
        // current process can still start (it will operate on the existing schema).
        try
        {
            Trace("Applying migrations");
            context.Database.Migrate();
            Trace("Migrations complete");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            // SQLite error code 5 = "database is locked". This happens when another
            // process keeps the DB open. We log the issue and move on – the schema is
            // assumed to be up-to-date because the first instance already applied any
            // pending migrations.
            Debug.WriteLine($"SQLite database locked, skipping migrations: {ex.Message}");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("journal mode", StringComparison.OrdinalIgnoreCase))
        {
            // Some versions of Microsoft.Data.Sqlite throw if the connection string contains an
            // unsupported keyword (e.g. "journal mode" in lower case). This is harmless for our
            // purposes, so we log and keep going.
            Debug.WriteLine($"SQLite connection string issue, skipping migrations: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Any other migration exception should not crash the whole application when
            // launching additional instances. Log the exception and proceed.
            Debug.WriteLine($"Database migration failed: {ex.Message}");
        }

        // Load the configs
        var configService = _serviceProvider.GetService<ConfigService>();
        try
        {
            Trace("Starting async config reload");
            _ = Task.Run(async () =>
            {
                try
                {
                    await configService.ReloadConfigsAsync();
                    Trace("Configs reloaded (async)");
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine($"Config reload failed: {ex2.Message}");
                    Trace($"Config reload failed: {ex2}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Config reload task start failed: {ex.Message}");
            Trace($"Config reload task start failed: {ex}");
        }

        AutocompletionProvider.Init();
        Trace("AutocompletionProvider.Init complete");

        // Start the job monitor at the start of the application,
        // otherwise it will only be started when navigating to the page
        try
        {
            _ = _serviceProvider.GetService<JobMonitorService>();
            Trace("JobMonitorService retrieved");
        }
        catch (Exception ex)
        {
            Trace($"JobMonitorService retrieval failed: {ex}");
        }

        Trace("App constructor END");
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
        var dbConnectionString = _config.GetConnectionString("DefaultConnection");
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
        services.AddSingleton<OpenBullet2.Native.Services.PerformanceMonitorService>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Call base implementation first
        base.OnStartup(e);

        // Allow multiple instances without confirmation
        // Note: Mutex is no longer used for instance control

        try
        {
            Trace("Creating MainWindow");
            var mainWindow = _serviceProvider.GetService<MainWindow>();
            mainWindow.NavigateTo(MainWindowPage.Home);
            mainWindow.Show();
            Trace("MainWindow shown");

            // Ensure the application doesn't shut down immediately
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Apply conservative GPU optimizations to reduce laptop heating
            ApplyConservativeGpuSettings();
        }
        catch (Exception ex)
        {
            Trace($"Startup exception: {ex}");
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var errorLogPath = Path.Combine(appDirectory, $"startup-error-{Process.GetCurrentProcess().Id}.log");
            File.WriteAllText(errorLogPath, $"Startup error on {DateTime.Now}\r\n{ex}");
            MessageBox.Show($"Startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ApplyConservativeGpuSettings()
    {
        try
        {
            var reducedAnimations = _config.GetSection("Performance").GetValue("ReducedAnimations", true);
            
            if (reducedAnimations)
            {
                // Set conservative animation settings (reduce from 60fps to 20fps for low-spec)
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = 20 }
                );
                
                // Disable hardware acceleration for low-spec systems to reduce GPU load
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            else
            {
                // Standard performance mode
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = 30 }
                );
            }

            Debug.WriteLine($"Conservative GPU settings applied: {(reducedAnimations ? "20fps animations, software rendering" : "30fps animations")} to reduce laptop heating");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not apply conservative GPU settings: {ex.Message}");
        }
    }
    
    private void ApplyGarbageCollectionOptimizations()
    {
        try
        {
            // Configure GC for low-latency, low-memory scenarios
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
            
            // Force an initial garbage collection to clean up startup overhead
            GC.Collect(2, GCCollectionMode.Optimized, false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Optimized, false);
            
            Debug.WriteLine("Garbage collection optimizations applied for low-spec systems");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not apply GC optimizations: {ex.Message}");
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportCrash(e.Exception);
        e.Handled = true; // Set to false to close the app on exception
    }

    private static void OnTaskException(object sender, UnobservedTaskExceptionEventArgs e)
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
        if (_serviceProvider is IDisposable disposable)
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

        Alert.Error("Unhandled exception", "An unhandled exception was thrown, the application will try to continue running." +
            " Please open the crash.log file, copy the error message inside it and open an issue on the official github repository." +
            $" A few details about the exception: {ex.Message}");
    }

    // Simple file trace helper: writes only in DEBUG builds or when OB2_TRACE symbol is defined
#if DEBUG || OB2_TRACE
    private static void Trace(string msg)
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-trace.log");
            File.AppendAllText(path, $"{DateTime.Now.ToString("o", CultureInfo.InvariantCulture)} | {msg}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* ignore */ }
    }
#else
    [System.Diagnostics.Conditional("OB2_TRACE")]
    private static void Trace(string msg) { }
#endif
}
