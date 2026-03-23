using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Flux.Native.ViewModels.Configs;

internal sealed class ConfigStackerSearchDebouncer : IDisposable
{
    private CancellationTokenSource? _cts;

    public void Schedule(int delayMilliseconds, Action action)
    {
        Cancel();

        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMilliseconds, cts.Token).ConfigureAwait(false);
                if (!cts.IsCancellationRequested)
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Cancel();
}
