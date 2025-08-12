using RuriLib.Models.Configs;
using RuriLib.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using RuriLib.Helpers.Transpilers;
using RuriLib.Services;
using System.Text;
using OpenBullet2.Core.Exceptions;

namespace OpenBullet2.Core.Repositories;

/// <summary>
/// Stores configs on disk.
/// </summary>
public class DiskConfigRepository : IConfigRepository
{
    private readonly RuriLibSettingsService _rlSettings;

    private string BaseFolder { get; init; }

    public DiskConfigRepository(RuriLibSettingsService rlSettings, string baseFolder)
    {
        _rlSettings = rlSettings;
        BaseFolder = baseFolder;
        Directory.CreateDirectory(baseFolder);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Config>> GetAllAsync()
    {
        var tasks = Directory.GetFiles(BaseFolder).Where(file => file.EndsWith(".opk"))
            .Select(async file => 
            {
                try
                {
                    return await GetAsync(Path.GetFileNameWithoutExtension(file));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not unpack {file} properly: {ex.Message}");
                    return null;
                }
            });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null);
    }

    /// <inheritdoc/>
    public async Task<Config> GetAsync(string id)
    {
        var file = GetFileName(id);

        if (!File.Exists(file))
        {
            throw new FileNotFoundException();
        }
        
        await using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);

        var config = await ConfigPacker.UnpackAsync(fileStream);
        config.Id = id;
        return config;

    }

    /// <inheritdoc/>
    public async Task<byte[]> GetBytesAsync(string id)
    {
        var file = GetFileName(id);

        if (!File.Exists(file))
        {
            throw new FileNotFoundException();
        }
        
        await using FileStream fileStream = new(file, FileMode.Open, FileAccess.Read);
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);

        return ms.ToArray();

    }

    /// <inheritdoc/>
    public async Task<Config> CreateAsync(string id = null)
    {
        var config = new Config { Id = id ?? Guid.NewGuid().ToString() };

        config.Settings.DataSettings.AllowedWordlistTypes = [
            _rlSettings.Environment.WordlistTypes.First().Name
        ];

        await SaveAsync(config);
        return config;
    }

    /// <inheritdoc/>
    public async Task UploadAsync(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName);

        // Only .opk configs are supported
        if (extension == ".opk")
        {
            var config = await ConfigPacker.UnpackAsync(stream);
            await File.WriteAllBytesAsync(GetFileName(config), await ConfigPacker.PackAsync(config));
        }
        else
        {
            throw new UnsupportedFileTypeException($"Unsupported file type: {extension}");
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Config config)
    {
        // Update the last modified date
        config.Metadata.LastModified = DateTime.Now;

        // If it's possible to retrieve the block descriptors, get required plugins
        if (config.Mode is ConfigMode.Stack or ConfigMode.LoliCode)
        {
            try
            {
                var stack = config.Mode is ConfigMode.Stack
                    ? config.Stack
                    : Loli2StackTranspiler.Transpile(config.LoliCodeScript);

                // Write the required plugins in the config's metadata
                config.Metadata.Plugins = stack.Select(b => b.Descriptor.AssemblyFullName)
                    .Where(n => n != null && !n.Contains("RuriLib")).ToList();
            }
            catch
            {
                // Don't do anything, it's not the end of the world if we don't write some metadata ^_^
            }
        }

        await File.WriteAllBytesAsync(GetFileName(config), await ConfigPacker.PackAsync(config));
    }

    /// <inheritdoc/>
    public void Delete(Config config)
    {
        var file = GetFileName(config);

        if (File.Exists(file))
            File.Delete(file);
    }

    private string GetFileName(Config config)
        => GetFileName(config.Id);

    private string GetFileName(string id)
        => Path.Combine(BaseFolder, $"{id}.opk").Replace('\\', '/');
}
