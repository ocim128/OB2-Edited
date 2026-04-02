using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace RuriLib.Tests.Infrastructure;

internal static class TestAssemblyResolver
{
    private static readonly object Sync = new();
    private static bool _registered;
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving += ResolveFromPackageCache;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPackageCache;
            _registered = true;
        }
    }

    private static Assembly? ResolveFromPackageCache(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var path = FindAssemblyPath(assemblyName.Name);
        return path is null ? null : context.LoadFromAssemblyPath(path);
    }

    private static Assembly? ResolveFromPackageCache(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);
        var path = FindAssemblyPath(assemblyName.Name);
        return path is null ? null : Assembly.LoadFrom(path);
    }

    private static string? FindAssemblyPath(string? simpleName)
    {
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        return Cache.GetOrAdd(simpleName, static name =>
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            for (var i = 0; i < 6; i++)
            {
                var candidate = current;
                for (var j = 0; j < i && candidate.Parent is not null; j++)
                {
                    candidate = candidate.Parent;
                }

                var packagesPath = Path.Combine(candidate.FullName, "packages");
                if (!Directory.Exists(packagesPath))
                {
                    continue;
                }

                var match = Directory
                    .EnumerateFiles(packagesPath, $"{name}.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        });
    }
}
