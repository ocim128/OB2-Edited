using System;
using System.IO;

namespace RuriLib.Tests.Infrastructure;

internal sealed class TestWorkspace : IDisposable
{
    public string RootPath { get; }

    public TestWorkspace()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "Flux-RuriLib-Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Test workspaces are best-effort cleanup only.
        }
    }
}
