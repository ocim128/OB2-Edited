using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Flux.Native.Services;
using Flux.Native.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Models.Settings;
using RuriLib.Services;

namespace Flux.Native.ViewModels.Tools;

public sealed class FirefoxToolViewModel : ToolCardViewModelBase, IDisposable
{
    private readonly List<LaunchedZipProfile> launchedZipProfiles = new();
    private readonly ZipProfileLauncher zipProfileLauncher = new();
    private readonly object zipProfileLock = new();
    private readonly RelayCommand clearCommand;
    private readonly RelayCommand<ZipFolderOption> copyOptionNameCommand;
    private readonly RelayCommand<ZipFolderOption> launchOptionCommand;

    private string zipArchivePath = string.Empty;
    private string archiveFileName = "No file loaded";
    private string statusMessage = string.Empty;
    private Brush statusBrush = Brushes.LightSteelBlue;
    private bool hasStatus;
    private bool isLaunching;

    public FirefoxToolViewModel()
        : base("Firefox Switcher", "Browsers", "profile", "browser", "automation", "firefox", "zip", "launcher", "profile manager")
    {
        clearCommand = new RelayCommand(Clear);
        copyOptionNameCommand = new RelayCommand<ZipFolderOption>(CopyOptionName, option => option is not null);
        launchOptionCommand = new RelayCommand<ZipFolderOption>(option => _ = LaunchOptionAsync(option), option => option is not null && !IsLaunching);
    }

    public ObservableCollection<ZipFolderOption> Options { get; } = new();

    public RelayCommand ClearCommand => clearCommand;

    public RelayCommand<ZipFolderOption> CopyOptionNameCommand => copyOptionNameCommand;

    public RelayCommand<ZipFolderOption> LaunchOptionCommand => launchOptionCommand;

    public string ArchiveFileName
    {
        get => archiveFileName;
        private set => SetProperty(ref archiveFileName, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => SetProperty(ref statusBrush, value);
    }

    public bool HasStatus
    {
        get => hasStatus;
        private set
        {
            if (SetProperty(ref hasStatus, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLaunching
    {
        get => isLaunching;
        private set
        {
            if (SetProperty(ref isLaunching, value))
            {
                launchOptionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void LoadArchive(string fileName)
    {
        try
        {
            using var stream = File.OpenRead(fileName);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var folderNames = CollectTopLevelFolders(archive);

            Options.Clear();
            foreach (var folder in folderNames)
            {
                Options.Add(folder);
            }

            zipArchivePath = fileName;
            ArchiveFileName = Path.GetFileName(fileName);

            if (Options.Count == 0)
            {
                SetStatus("No folders were detected in this archive.", Brushes.OrangeRed);
            }
            else
            {
                SetStatus($"Loaded {Options.Count} folder option(s).", Brushes.LawnGreen);
            }
        }
        catch (Exception ex)
        {
            zipArchivePath = string.Empty;
            Options.Clear();
            ArchiveFileName = "No file loaded";
            SetStatus($"Failed to read archive: {ex.Message}", Brushes.OrangeRed);
        }
    }

    public void Clear()
    {
        Options.Clear();
        zipArchivePath = string.Empty;
        ArchiveFileName = "No file loaded";
        HasStatus = false;
        StatusMessage = string.Empty;
    }

    public void CopyOptionName(ZipFolderOption? option)
    {
        if (option is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(option.Name);
            SetStatus($"Copied '{option.Name}' to clipboard.", Brushes.LawnGreen);
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to copy: {ex.Message}", Brushes.OrangeRed);
        }
    }

    public async Task LaunchOptionAsync(ZipFolderOption? option)
    {
        if (option is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(zipArchivePath) || !File.Exists(zipArchivePath))
        {
            SetStatus("Load a ZIP archive before launching a profile.", Brushes.OrangeRed);
            return;
        }

        if (IsLaunching)
        {
            SetStatus("Another launch is already in progress.", Brushes.OrangeRed);
            return;
        }

        var settingsService = App.ServiceProvider.GetRequiredService<RuriLibSettingsService>();
        var playwrightSettings = settingsService.RuriLibSettings?.PlaywrightSettings ?? new PlaywrightSettings();
        var firefoxBinary = playwrightSettings.FirefoxBinaryLocation;

        if (string.IsNullOrWhiteSpace(firefoxBinary) || !File.Exists(firefoxBinary))
        {
            SetStatus("Firefox binary path in RL settings is invalid.", Brushes.OrangeRed);
            return;
        }

        var progress = new Progress<ZipLaunchStatus>(ReportLaunchStatus);

        try
        {
            IsLaunching = true;

            var request = new ZipProfileLaunchRequest(
                zipArchivePath,
                option.Name,
                playwrightSettings,
                firefoxBinary,
                "https://gmail.com");

            var profile = await zipProfileLauncher.LaunchAsync(request, progress);
            RegisterZipProfile(profile);
        }
        catch (Exception ex)
        {
            var errorMessage = ex is TimeoutException ? ex.Message : $"Launch failed: {ex.Message}";
            SetStatus(errorMessage, Brushes.OrangeRed);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    public async Task CleanupAsync()
    {
        List<LaunchedZipProfile> profiles;
        lock (zipProfileLock)
        {
            profiles = launchedZipProfiles.ToList();
            launchedZipProfiles.Clear();
        }

        foreach (var profile in profiles)
        {
            await CloseZipProfileAsync(profile, closeContext: true);
        }
    }

    public void Dispose()
    {
    }

    private void RegisterZipProfile(LaunchedZipProfile profile)
    {
        lock (zipProfileLock)
        {
            launchedZipProfiles.Add(profile);
        }

    }

    private void ReportLaunchStatus(ZipLaunchStatus status)
    {
        var brush = status.Level switch
        {
            ZipLaunchStatusLevel.Success => Brushes.LawnGreen,
            ZipLaunchStatusLevel.Warning => Brushes.Khaki,
            ZipLaunchStatusLevel.Error => Brushes.OrangeRed,
            _ => Brushes.LightSteelBlue
        };

        SetStatus(status.Message, brush);
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusMessage = message;
        StatusBrush = brush;
        HasStatus = true;
    }

    private static IEnumerable<ZipFolderOption> CollectTopLevelFolders(ZipArchive archive)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 1)
            {
                folders.Add(segments[0]);
            }
            else if (segments.Length == 1 && entry.FullName.EndsWith('/'))
            {
                folders.Add(segments[0]);
            }
        }

        return folders
            .OrderBy(static folder => folder, StringComparer.OrdinalIgnoreCase)
            .Select(static name => new ZipFolderOption(name));
    }

    private static async Task CloseZipProfileAsync(LaunchedZipProfile profile, bool closeContext)
    {
        try
        {
            if (closeContext)
            {
                await profile.Context.CloseAsync();
            }
        }
        catch
        {
        }

        TryDeleteDirectory(profile.ProfilePath);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    public sealed class ZipFolderOption
    {
        public ZipFolderOption(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public override string ToString() => Name;
    }
}
