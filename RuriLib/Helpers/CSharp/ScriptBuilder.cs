using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs.Settings;
using RuriLib.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RuriLib.Models.Scripting;
using System.Runtime.Loader;

namespace RuriLib.Helpers.CSharp
{
    /// <summary>
    /// In charge of building the final executable C# script from a string of C# code.
    /// Implements script compilation caching for improved performance.
    /// </summary>
    public class ScriptBuilder
    {
        private static readonly Assembly _ruriLibAssembly = Assembly.GetAssembly(typeof(ScriptBuilder));
        private static readonly HashSet<string> _ruriLibReferenceNames = new(_ruriLibAssembly.GetReferencedAssemblies().Select(a => a.FullName));
        private static readonly ConcurrentDictionary<string, HashSet<string>> _pluginReferenceNamesCache = new();
        
        // Cache the standard usings to avoid recreating the list on every build
        private static readonly List<string> _standardUsings;

        #region Script Compilation Cache
        
        /// <summary>
        /// Cached compiled scripts indexed by their content hash.
        /// Key: SHA256 hash of (script + settings + optimization level + plugin names)
        /// Value: Tuple of (Script, compilation time in ms)
        /// </summary>
        private static readonly ConcurrentDictionary<string, CachedScript> _scriptCache = new();
        
        /// <summary>
        /// Maximum number of cached scripts before cleanup is triggered.
        /// </summary>
        private const int MaxCacheSize = 100;
        
        /// <summary>
        /// Statistics for cache performance monitoring.
        /// </summary>
        private static long _cacheHits;
        private static long _cacheMisses;
        
        /// <summary>
        /// Represents a cached script with metadata.
        /// </summary>
        private sealed class CachedScript
        {
            public IScript Script { get; init; }
            public DateTime CreatedAt { get; init; }
            public DateTime LastAccessedAt { get; set; }
            public long AccessCount;  // Field for Interlocked operations
            public long CompilationTimeMs { get; init; }
        }
        
        /// <summary>
        /// Gets cache statistics for monitoring performance.
        /// </summary>
        public static (long hits, long misses, int size, double hitRate) GetCacheStatistics()
        {
            var total = _cacheHits + _cacheMisses;
            var hitRate = total > 0 ? (double)_cacheHits / total * 100 : 0;
            return (_cacheHits, _cacheMisses, _scriptCache.Count, hitRate);
        }
        
        /// <summary>
        /// Clears the script compilation cache.
        /// </summary>
        public static void ClearCache()
        {
            _scriptCache.Clear();
            System.Threading.Interlocked.Exchange(ref _cacheHits, 0);
            System.Threading.Interlocked.Exchange(ref _cacheMisses, 0);
        }
        
        /// <summary>
        /// Evicts least recently used entries when cache exceeds maximum size.
        /// </summary>
        private static void EvictStaleEntriesIfNeeded()
        {
            if (_scriptCache.Count <= MaxCacheSize)
                return;
                
            // Remove entries that haven't been accessed in the last hour first,
            // then remove least recently accessed entries
            var staleThreshold = DateTime.UtcNow.AddHours(-1);
            var entriesToRemove = _scriptCache
                .Where(kvp => kvp.Value.LastAccessedAt < staleThreshold)
                .OrderBy(kvp => kvp.Value.AccessCount)
                .ThenBy(kvp => kvp.Value.LastAccessedAt)
                .Take(_scriptCache.Count - MaxCacheSize + 10) // Remove 10 extra to avoid frequent eviction
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var key in entriesToRemove)
            {
                _scriptCache.TryRemove(key, out _);
            }
        }
        
        /// <summary>
        /// Computes a unique hash for the script and its compilation settings.
        /// </summary>
        private static string ComputeScriptHash(string cSharpScript, ScriptSettings settings, 
            OptimizationLevel optimizationLevel, Assembly[] plugins)
        {
            using var sha256 = SHA256.Create();
            var sb = new StringBuilder();
            
            // Include script content
            sb.Append(cSharpScript);
            sb.Append('|');
            
            // Include optimization level
            sb.Append((int)optimizationLevel);
            sb.Append('|');

            // Include the current runtime assembly build identity so stale compiled scripts
            // are not reused across local rebuilds with API-shape changes.
            sb.Append(_ruriLibAssembly.FullName);
            sb.Append('|');
            sb.Append(_ruriLibAssembly.ManifestModule.ModuleVersionId);
            sb.Append('|');
            
            // Include custom usings
            if (settings?.CustomUsings != null)
            {
                foreach (var u in settings.CustomUsings.OrderBy(x => x))
                {
                    sb.Append(u);
                    sb.Append(',');
                }
            }
            sb.Append('|');
            
            // Include plugin names (sorted for consistency)
            foreach (var plugin in plugins.OrderBy(p => p.FullName))
            {
                sb.Append(plugin.FullName);
                sb.Append('@');
                sb.Append(plugin.ManifestModule.ModuleVersionId);
                sb.Append(',');
            }
            
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hashBytes = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hashBytes);
        }
        
        #endregion Script Compilation Cache

        static ScriptBuilder()
        {
            _standardUsings = new List<string>
            {
                "RuriLib.Helpers",
                "RuriLib.Logging",
                "RuriLib.Extensions",
                "RuriLib.Models.Bots",
                "RuriLib.Models.Proxies",
                "RuriLib.Models.Conditions.Comparisons",
                "System.Collections.Generic",
                "System.Linq",
                "System.Net.Security",
                "RuriLib.Models.Blocks.Custom.HttpRequest.Multipart",
                "RuriLib.Functions.Http.Options",
                "Jering.Javascript.NodeJS",
                "Jint",
                "System.Threading",
                "System.Threading.Tasks",
                "System",
                "System.Text",
                "System.Text.RegularExpressions"
            };
            
            // Add block category namespaces
            if (Globals.DescriptorsRepository?.Descriptors != null)
            {
                _standardUsings.AddRange(Globals.DescriptorsRepository.Descriptors.Values
                    .Select(d => d.Category.Namespace)
                    .Distinct());
            }
        }

        /// <summary>
        /// Builds an executable C# <see cref="IScript"/> from a <paramref name="cSharpScript"/> string,
        /// some <paramref name="settings"/> and a <paramref name="pluginRepo"/> to reference the correct assemblies.
        /// Uses caching to avoid recompilation of identical scripts.
        /// </summary>
        public IScript Build(string cSharpScript, ScriptSettings settings, PluginRepository pluginRepo,
            OptimizationLevel optimizationLevel = OptimizationLevel.Debug)
        {
            var plugins = pluginRepo?.GetPlugins().ToArray() ?? Array.Empty<Assembly>();
            
            // Compute cache key
            var cacheKey = ComputeScriptHash(cSharpScript, settings, optimizationLevel, plugins);
            
            // Check memory cache first
            if (_scriptCache.TryGetValue(cacheKey, out var cachedScript))
            {
                // Update access metadata
                cachedScript.LastAccessedAt = DateTime.UtcNow;
                System.Threading.Interlocked.Increment(ref cachedScript.AccessCount);
                System.Threading.Interlocked.Increment(ref _cacheHits);
                return cachedScript.Script;
            }
            
            // Check disk cache
            // Use UserData folder if available, or just a local cache folder
            // Assuming current directory is the base
            var cacheDir = Path.Combine("UserData", "CompiledScripts");
            var cleanHash = cacheKey.Replace("/", "-").Replace("+", "_").Replace("=", "");
            var dllPath = Path.Combine(cacheDir, $"{cleanHash}.dll");

            if (File.Exists(dllPath))
            {
                try
                {
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
                    var loadedScript = new CompiledAssemblyScript(assembly);

                    // Add to memory cache
                    var newCachedEntry = new CachedScript
                    {
                        Script = loadedScript,
                        CreatedAt = DateTime.UtcNow,
                        LastAccessedAt = DateTime.UtcNow,
                        AccessCount = 1,
                        CompilationTimeMs = 0 // Loaded from disk
                    };
                    _scriptCache.TryAdd(cacheKey, newCachedEntry);
                    System.Threading.Interlocked.Increment(ref _cacheHits);
                    
                    EvictStaleEntriesIfNeeded();
                    return loadedScript;
                }
                catch 
                {
                    // Failed to load from disk, proceed to compile
                }
            }
            
            System.Threading.Interlocked.Increment(ref _cacheMisses);
            
            // Cache miss - compile the script
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Create options with standard references and imports
            var options = ScriptOptions.Default
                .WithOptimizationLevel(optimizationLevel)
                .WithReferences(new Assembly[] { _ruriLibAssembly }.Concat(plugins))
                .WithImports(GetImports(settings));

            // Add transient references (system assemblies) required by RuriLib
            options = options.AddReferences(GetRequiredAssemblies(plugins));

            var script = CSharpScript.Create(
                code: cSharpScript,
                options: options,
                globalsType: typeof(ScriptGlobals));
                
            // Try to emit to disk
            try 
            {
                var compilation = script.GetCompilation();
                // We only emit if there are no errors? 
                // Compilation.Emit will return success false if errors.
                // But we don't want to break the flow if user has invalid code (let RoslynScript handle it via Compile() diagnostics)
                // But we can only save valid DLLs.
                
                using (var ms = new MemoryStream())
                {
                    var emitResult = compilation.Emit(ms);
                    if (emitResult.Success)
                    {
                        Directory.CreateDirectory(cacheDir);
                        ms.Seek(0, SeekOrigin.Begin);
                        using (var fs = new FileStream(dllPath, FileMode.Create))
                        {
                            ms.CopyTo(fs);
                        }
                    }
                }
            }
            catch
            {
                // Ignore emit errors (e.g. IO exceptions, restricted access)
            }
                
            sw.Stop();
            
            var resultScript = new RoslynScript(script);

            // Store in cache
            var newCachedScript = new CachedScript
            {
                Script = resultScript,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                AccessCount = 1,
                CompilationTimeMs = sw.ElapsedMilliseconds
            };
            
            _scriptCache.TryAdd(cacheKey, newCachedScript);
            
            // Evict stale entries if needed
            EvictStaleEntriesIfNeeded();
            
            return resultScript;
        }
        
        /// <summary>
        /// Builds an executable C# <see cref="IScript"/> bypassing the cache.
        /// Use this when you need a fresh compilation (e.g., for debugging).
        /// </summary>
        public IScript BuildWithoutCache(string cSharpScript, ScriptSettings settings, PluginRepository pluginRepo,
            OptimizationLevel optimizationLevel = OptimizationLevel.Debug)
        {
            var plugins = pluginRepo?.GetPlugins().ToArray() ?? Array.Empty<Assembly>();
            
            // Create options with standard references and imports
            var options = ScriptOptions.Default
                .WithOptimizationLevel(optimizationLevel)
                .WithReferences(new Assembly[] { _ruriLibAssembly }.Concat(plugins))
                .WithImports(GetImports(settings));

            // Add transient references (system assemblies) required by RuriLib
            options = options.AddReferences(GetRequiredAssemblies(plugins));

            var script = CSharpScript.Create(
                code: cSharpScript,
                options: options,
                globalsType: typeof(ScriptGlobals));

            return new RoslynScript(script);
        }

        /// <summary>
        /// Gets the basic usings that the C# script requires in order to be successfully executed.
        /// </summary>
        public static IEnumerable<string> GetUsings() => _standardUsings;

        private static IEnumerable<string> GetImports(ScriptSettings settings)
        {
            if (settings.CustomUsings == null || settings.CustomUsings.Count == 0)
                return _standardUsings;

            // Combine standard usings with parsed custom usings
            return _standardUsings.Concat(settings.CustomUsings
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(ParseUsing))
                .Distinct();
        }

        private static string ParseUsing(string u)
        {
            // Optimize parsing: "using MyLib.Test;" -> "MyLib.Test"
            // Avoid Regex overhead for simple parsing
            var trimmed = u.Trim();
            
            if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';'))
            {
                // Length of "using " is 6. Length of ";" is 1.
                // We want the content between index 6 and (Length - 1).
                // Length of substring = Length - 6 - 1 = Length - 7.
                if (trimmed.Length > 7)
                {
                    return trimmed.Substring(6, trimmed.Length - 7).Trim();
                }
            }

            return trimmed;
        }

        private static IEnumerable<Assembly> GetRequiredAssemblies(Assembly[] plugins)
        {
            var pluginReferenceNames = GetPluginReferenceNames(plugins);
            var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var requiredAssemblies = new List<Assembly>();

            foreach (var asm in domainAssemblies)
            {
                var fullName = asm.FullName;
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    continue;
                }

                if (_ruriLibReferenceNames.Contains(fullName) || pluginReferenceNames.Contains(fullName))
                {
                    requiredAssemblies.Add(asm);
                }
            }

            return requiredAssemblies;
        }

        private static HashSet<string> GetPluginReferenceNames(Assembly[] plugins)
        {
            if (plugins.Length == 0)
            {
                return new HashSet<string>();
            }

            var key = string.Join("|", plugins
                .Select(p => p.FullName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.Ordinal));

            return _pluginReferenceNamesCache.GetOrAdd(key, _ =>
            {
                var names = new HashSet<string>(StringComparer.Ordinal);

                foreach (var plugin in plugins)
                {
                    if (!string.IsNullOrWhiteSpace(plugin.FullName))
                    {
                        names.Add(plugin.FullName);
                    }

                    foreach (var referenced in plugin.GetReferencedAssemblies())
                    {
                        if (!string.IsNullOrWhiteSpace(referenced.FullName))
                        {
                            names.Add(referenced.FullName);
                        }
                    }
                }

                return names;
            });
        }
    }
}
