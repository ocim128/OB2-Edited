using Microsoft.Scripting.Utils;
using Flux.Core.Models.Settings;
using Flux.Core.Repositories;
using RuriLib.Models.Configs;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;
using RuriLib.Helpers;
using System.IO;
using RuriLib.Functions.Conversion;

namespace Flux.Core.Services;

// TODO: The config service should also be in charge of calling methods of the IConfigRepository
/// <summary>
/// Manages the list of available configs.
/// </summary>
public class ConfigService
{
    private static readonly HttpClient SharedHttpClient = new();

    /// <summary>
    /// The list of available configs.
    /// </summary>
    public IEnumerable<Config> Configs
    {
        get
        {
            lock (_configsLock)
            {
                return _configs.ToList();
            }
        }
    }
    private readonly List<Config> _configs = new List<Config>();
    private readonly object _configsLock = new object();

    /// <summary>
    /// Called when a new config is selected.
    /// </summary>
    public event EventHandler<Config> OnConfigSelected;

    /// <summary>
    /// Called when all configs from configured remote endpoints are loaded.
    /// </summary>
    public event EventHandler OnRemotesLoaded;

    private Config _selectedConfig = null;
    private readonly IConfigRepository _configRepo;
    private readonly FluxSettingsService _fluxSettingsService;

    /// <summary>
    /// The currently selected config.
    /// </summary>
    public Config SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            _selectedConfig = value;
            OnConfigSelected?.Invoke(this, _selectedConfig);
        }
    }

    public ConfigService(IConfigRepository configRepo, FluxSettingsService fluxSettingsService)
    {
        this._configRepo = configRepo;
        this._fluxSettingsService = fluxSettingsService;
    }

    /// <summary>
    /// Reloads all configs from the <see cref="IConfigRepository"/> and remote endpoints.
    /// </summary>
    public async Task ReloadConfigsAsync()
    {
        try
        {
            // Load from the main repository
            var newConfigs = (await _configRepo.GetAllAsync()).ToList();
            
            lock (_configsLock)
            {
                _configs.Clear();
                _configs.AddRange(newConfigs);
            }
            
            SelectedConfig = null;

            // Load from remotes (fire and forget)
            _ = Task.Run(async () => await LoadFromRemotesAsync());
        }
        catch (Exception ex)
        {
            // Log the exception but don't let it bubble up to cause startup errors
            Console.WriteLine($"Error reloading configs: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a new config to the list.
    /// </summary>
    public void AddConfig(Config config)
    {
        lock (_configsLock)
        {
            _configs.Add(config);
        }
    }

    /// <summary>
    /// Removes a config from the list.
    /// </summary>
    public bool RemoveConfig(Config config)
    {
        lock (_configsLock)
        {
            return _configs.Remove(config);
        }
    }

    /// <summary>
    /// Gets a copy of the configs list for modification purposes.
    /// </summary>
    public List<Config> GetConfigsList()
    {
        lock (_configsLock)
        {
            return _configs.ToList();
        }
    }

    private async Task LoadFromRemotesAsync()
    {
        try
        {
            List<Config> remoteConfigs = new();

            var func = new Func<RemoteConfigsEndpoint, Task>(async endpoint =>
            {
                try
                {
                    // Get the file
                    var client = SharedHttpClient;
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                    request.Headers.Add("Api-Key", endpoint.ApiKey);
                    using var response = await client.SendAsync(request);

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new UnauthorizedAccessException();
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new FileNotFoundException();
                    }

                    var fileStream = await response.Content.ReadAsStreamAsync();

                    // Unpack the archive in memory
                    using ZipArchive archive = new(fileStream, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.Name.EndsWith(".opk"))
                        {
                            continue;
                        }

                        try
                        {
                            using var entryStream = entry.Open();
                            var config = await ConfigPacker.UnpackAsync(entryStream);

                            // Calculate the hash of the metadata of the remote config to use as id.
                            // This is done to have a consistent id through successive pulls of configs
                            // from remotes, so that jobs can reference the id and retrieve the correct one
                            config.Id = HexConverter.ToHexString(config.Metadata.GetUniqueHash());
                            config.IsRemote = true;

                            // If a config with the same hash is not already present (e.g. same exact config
                            // from another source) add it to the list
                            lock (remoteConfigs)
                            {
                                if (!remoteConfigs.Any(c => c.Id == config.Id))
                                {
                                    remoteConfigs.Add(config);
                                }
                            }
                        }
                        catch
                        {

                        }
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{endpoint.Url}] Failed to pull configs from endpoint: {ex.Message}");
                }
            });

            var tasks = _fluxSettingsService.Settings.RemoteSettings.ConfigsEndpoints
                .Select(endpoint => func.Invoke(endpoint));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            lock (_configsLock)
            {
                _configs.AddRange(remoteConfigs);
            }

            OnRemotesLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading remote configs: {ex.Message}");
        }
    }
}
