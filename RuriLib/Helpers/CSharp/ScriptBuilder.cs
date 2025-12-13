using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs.Settings;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RuriLib.Helpers.CSharp
{
    /// <summary>
    /// In charge of building the final executable C# script from a string of C# code.
    /// </summary>
    public class ScriptBuilder
    {
        private static readonly Assembly _ruriLibAssembly = Assembly.GetAssembly(typeof(ScriptBuilder));
        private static readonly HashSet<string> _ruriLibReferenceNames = new(_ruriLibAssembly.GetReferencedAssemblies().Select(a => a.FullName));
        
        // Cache the standard usings to avoid recreating the list on every build
        private static readonly List<string> _standardUsings;

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
        /// Builds an executable C# <see cref="Script"/> from a <paramref name="cSharpScript"/> string,
        /// some <paramref name="settings"/> and a <paramref name="pluginRepo"/> to reference the correct assemblies.
        /// </summary>
        public Script Build(string cSharpScript, ScriptSettings settings, PluginRepository pluginRepo,
            OptimizationLevel optimizationLevel = OptimizationLevel.Debug)
        {
            var plugins = pluginRepo?.GetPlugins().ToArray() ?? Array.Empty<Assembly>();
            
            // Create options with standard references and imports
            var options = ScriptOptions.Default
                .WithOptimizationLevel(optimizationLevel)
                .WithReferences(new Assembly[] { _ruriLibAssembly }.Concat(plugins))
                .WithImports(GetImports(settings));

            // Add transient references (system assemblies) required by RuriLib
            // Optimization: Filter current domain assemblies using the pre-hashed set
            var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var requiredAssemblies = new List<Assembly>();

            foreach (var asm in domainAssemblies)
            {
                // Verify if this assembly is referenced by RuriLib
                if (_ruriLibReferenceNames.Contains(asm.FullName))
                {
                    requiredAssemblies.Add(asm);
                    continue;
                }

                // Check if referenced by any plugin
                // We do this loop here to avoid LINQ overhead for the filtered set
                if (plugins.Length > 0)
                {
                    foreach (var plugin in plugins)
                    {
                        if (plugin.GetReferencedAssemblies().Any(r => r.FullName == asm.FullName))
                        {
                            requiredAssemblies.Add(asm);
                            break;
                        }
                    }
                }
            }
            
            options = options.AddReferences(requiredAssemblies);

            return CSharpScript.Create(
                code: cSharpScript,
                options: options,
                globalsType: typeof(ScriptGlobals));
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
    }
}
