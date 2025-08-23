using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenBullet2.Core;
using OpenBullet2.Core.Models.Proxies;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Logging;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
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
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Globalization;
using System.Text;
using OpenBullet2.Native.Infrastructure.Diagnostics;

namespace OpenBullet2.Native;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly CancellationTokenSource _startupCts = new();


    public App()
    {
        Trace("App constructor START");
        // Legacy handlers kept for backward compatibility; centralized handler is initialized in OnStartup.
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskException;

        Trace("After exception handlers");
        // Get the directory where the executable is located
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Create UserData directory in the executable's directory
        var userDataPath = Path.Combine(appDirectory, "UserData");
        Directory.CreateDirectory(userDataPath);

        // Ensure the directory is writable
        try
        {
            var testFile = Path.Combine(userDataPath, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"UserData directory '{userDataPath}' is not writable. Please check folder permissions.");
        }

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
        ServiceLocator.Initialize(_serviceProvider);

        Trace("ServiceProvider built");

        // Apply critical optimizations immediately for faster startup (config-driven)
        try
        {
            var resources = _config.GetSection("Resources");
            int workerThreads = resources.GetValue("WorkerThreads", Environment.ProcessorCount);
            int ioThreads = resources.GetValue("IOThreads", Environment.ProcessorCount);
            ThreadPool.SetMinThreads(workerThreads, ioThreads);

            // ConnectionLimit applies to legacy handlers (WinHTTP/ServicePointManager)
            // Still useful for some libraries; modern SocketsHttpHandler honors per-handler limits
            ServicePointManager.DefaultConnectionLimit = resources.GetValue("ConnectionLimit", Environment.ProcessorCount * 16);
        }
        catch (Exception tpEx)
        {
            Debug.WriteLine($"Failed to apply threadpool/network resource settings: {tpEx.Message}");
        }

        // Defer heavy operations to background with priority-based loading
        _ = Task.Run(async () =>
        {
            try
            {
                // Priority 1: Essential network optimizations
                ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, Environment.ProcessorCount * 4);
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.UseNagleAlgorithm = false;

                if (_startupCts.IsCancellationRequested) return;

                // Priority 2: Database migration (critical for app functionality)
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.MigrateAsync().ConfigureAwait(false);
                Trace("Database migration completed");

                if (_startupCts.IsCancellationRequested) return;

                // Priority 3: Load configurations (needed for UI)
                var configService = _serviceProvider.GetRequiredService<ConfigService>();
                await configService.ReloadConfigsAsync().ConfigureAwait(false);
                Trace("Configuration loaded successfully");

                if (_startupCts.IsCancellationRequested) return;

                // Priority 4: Initialize services with delay to avoid resource contention
                await Task.Delay(100, _startupCts.Token).ConfigureAwait(false); // Small delay to let UI render

                // Background optimizations (lower priority)
                ThreadPool.SetMaxThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 2);

                // Initialize remaining services
                AutocompletionProvider.Init();
                _ = _serviceProvider.GetService<JobMonitorService>();
                Trace("Deferred initialization completed");
            }
            catch (OperationCanceledException)
            {
                // App is shutting down; ignore
            }
            catch (Exception ex)
            {
                Trace($"Startup optimization error: {ex.Message}");
                _ = Dispatcher.BeginInvoke(() => Alert.Error("Startup Error", $"Some background initialization failed: {ex.Message}"));
            }
        }, _startupCts.Token);

        Trace($"Priority-based startup optimization initiated");

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
        if (string.IsNullOrEmpty(dbConnectionString))
        {
            throw new InvalidOperationException("DefaultConnection connection string is missing from configuration");
        }

        var absoluteDbPath = dbConnectionString?.Replace("UserData/OpenBullet.db",
            Path.Combine(userDataPath, "OpenBullet.db").Replace('\\', '/')) ??
            throw new InvalidOperationException("Failed to construct database path from connection string");

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

        // Initialize centralized exception handling with rolling logs under UserData/Logs/Crashes
        try
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var userDataPath = System.IO.Path.Combine(appDirectory, "UserData");
            var logsRoot = System.IO.Path.Combine(userDataPath, "Logs");
            System.IO.Directory.CreateDirectory(logsRoot);

            // Keep a single static instance for app lifetime by stashing into App.Resources
            var geh = new GlobalExceptionHandler(logsRoot);
            geh.Initialize();
            Resources["GlobalExceptionHandler"] = geh;
        }
        catch (Exception gehEx)
        {
            Debug.WriteLine($"GlobalExceptionHandler init failed: {gehEx.Message}");
        }

        // Allow multiple instances without confirmation
        // Note: Mutex is no longer used for instance control

        try
        {
            Trace("Creating MainWindow");
            var mainWindow = _serviceProvider.GetService<MainWindow>();

            // Set as the main window before showing
            MainWindow = mainWindow;

            // Show window immediately to improve first-paint perception
            mainWindow.Show();
            Trace("MainWindow shown");

            // Navigate to Home after first render to avoid blocking UI thread
            mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await mainWindow.NavigateTo(MainWindowPage.Home).ConfigureAwait(true);
                }
                catch (Exception navEx)
                {
                    Debug.WriteLine($"Initial navigation failed: {navEx.Message}");
                }
            }), DispatcherPriority.Background);

            // Ensure the application doesn't shut down immediately
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Apply conservative GPU optimizations to reduce laptop heating
            ApplyConservativeGpuSettings();

            // Set UI text rendering defaults for crisp typography
            TextOptions.TextFormattingModeProperty.OverrideMetadata(
                typeof(System.Windows.Controls.Control),
                new FrameworkPropertyMetadata(TextFormattingMode.Display));

            TextOptions.TextRenderingModeProperty.OverrideMetadata(
                typeof(System.Windows.Controls.Control),
                new FrameworkPropertyMetadata(TextRenderingMode.ClearType));

            TextOptions.TextHintingModeProperty.OverrideMetadata(
                typeof(System.Windows.Controls.Control),
                new FrameworkPropertyMetadata(TextHintingMode.Fixed));

            RenderOptions.ClearTypeHintProperty.OverrideMetadata(
                typeof(System.Windows.Controls.Control),
                new FrameworkPropertyMetadata(ClearTypeHint.Enabled));
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
            var perf = _config.GetSection("Performance");
            var reducedAnimations = perf.GetValue("ReducedAnimations", true);
            var lowSpecMode = perf.GetValue("LowSpecMode", false);
            var frameRate = lowSpecMode ? 15 : (reducedAnimations ? 20 : 30);

            // Set animation frame rate based on performance mode
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = frameRate }
            );

            if (lowSpecMode || reducedAnimations)
            {
                // Disable hardware acceleration for low-spec systems to reduce GPU load
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }

            Debug.WriteLine($"Conservative GPU settings applied: {frameRate}fps animations{(lowSpecMode || reducedAnimations ? ", software rendering" : "")} to reduce laptop heating");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not apply conservative GPU settings: {ex.Message}");
        }
    }

    // Removed unused ApplyGarbageCollectionOptimizations method for optimization

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
        try
        {
            // Signal background startup work to stop
            try { _startupCts.Cancel(); } catch { }
            try { _startupCts.Dispose(); } catch { }

            // Dispose centralized exception handler if present
            if (Resources["GlobalExceptionHandler"] is IDisposable geh)
            {
                try { geh.Dispose(); } catch { }
            }

            (_serviceProvider as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ServiceProvider dispose error: {ex.Message}");
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private static void ReportCrash(Exception ex)
    {
        try
        {
            var crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(crashLogPath, $"Unhandled exception thrown on {DateTime.Now}\r\n{ex}");
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"Failed to write crash log: {logEx.Message}");
        }

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
