using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using RuriLib.Helpers.CSharp;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Services;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Scripting;

internal readonly record struct PreparedScriptSources(string MainScript, string StartupScript);

internal sealed class ScriptPreparationResult
{
    public required string TranspiledScript { get; init; }
    public required string TranspiledStartupScript { get; init; }
    public IScript? Script { get; init; }
    public IScript? StartupScript { get; init; }
    public MethodInfo? DllMethod { get; init; }
}

internal sealed class ScriptPreparationService
{
    public PreparedScriptSources TranspileSources(Config config, bool stepByStep = false)
    {
        if (config.Mode is not (ConfigMode.Stack or ConfigMode.LoliCode))
        {
            return new PreparedScriptSources(
                config.CSharpScript ?? string.Empty,
                config.StartupCSharpScript ?? string.Empty);
        }

        var mainScript = config.Mode == ConfigMode.Stack
            ? Stack2CSharpTranspiler.Transpile(config.Stack, config.Settings, stepByStep)
            : Loli2CSharpTranspiler.Transpile(config.LoliCodeScript, config.Settings, stepByStep);

        var startupScript = Loli2CSharpTranspiler.Transpile(config.StartupLoliCodeScript, config.Settings, stepByStep);
        return new PreparedScriptSources(mainScript, startupScript);
    }

    public async Task<ScriptPreparationResult> PrepareAsync(
        Config config,
        PluginRepository pluginRepo,
        CancellationToken cancellationToken = default,
        bool stepByStep = false,
        PreparedScriptSources? preparedSources = null)
    {
        var sources = preparedSources ?? TranspileSources(config, stepByStep);
        config.CSharpScript = sources.MainScript;
        config.StartupCSharpScript = sources.StartupScript;

        MethodInfo? dllMethod = null;
        IScript? script = null;

        if (config.Mode == ConfigMode.DLL)
        {
            await using var ms = new MemoryStream(config.DLLBytes);
            var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
            var type = assembly.GetType("RuriLib.CompiledConfig");
            dllMethod = type?.GetMember("Execute").FirstOrDefault() as MethodInfo;
        }
        else
        {
            script = BuildAndValidateScript(
                config.CSharpScript,
                config,
                pluginRepo,
                cancellationToken,
                "The C# script has compilation errors:");
        }

        IScript? startupScript = null;
        if (!string.IsNullOrWhiteSpace(config.StartupCSharpScript))
        {
            startupScript = BuildAndValidateScript(
                config.StartupCSharpScript,
                config,
                pluginRepo,
                cancellationToken,
                "The Startup C# script has compilation errors:");
        }

        return new ScriptPreparationResult
        {
            TranspiledScript = config.CSharpScript ?? string.Empty,
            TranspiledStartupScript = config.StartupCSharpScript ?? string.Empty,
            Script = script,
            StartupScript = startupScript,
            DllMethod = dllMethod
        };
    }

    public async Task ExecuteStartupScriptAsync(
        IScript startupScript,
        BotData startupData,
        dynamic globalVariables,
        CancellationToken cancellationToken)
    {
        _ = await BotRuntimeContextBuilder
            .ExecuteStartupScriptAsync(startupScript, startupData, globalVariables, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IScript BuildAndValidateScript(
        string source,
        Config config,
        PluginRepository pluginRepo,
        CancellationToken cancellationToken,
        string errorPrefix)
    {
        var script = new ScriptBuilder().Build(
            source,
            config.Settings.ScriptSettings,
            pluginRepo,
            OptimizationLevel.Release);

        var diagnostics = script.Compile(cancellationToken);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var errors = string.Join(
                global::System.Environment.NewLine,
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));

            throw new CompilationErrorException(
                errorPrefix + global::System.Environment.NewLine + errors,
                diagnostics);
        }

        return script;
    }
}
