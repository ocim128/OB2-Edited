using System;
using System.Threading.Tasks;

namespace Flux.Core.Extensions;

public static class TaskExtensions
{
    /// <summary>
    /// Forgets the task and executes an action if an exception is thrown.
    /// </summary>
    public static void Forget(this Task task, Action<Exception>? onError = null)
    {
        _ = ForgetAwaited(task, onError);

        static async Task ForgetAwaited(Task task, Action<Exception>? onError)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }
}
