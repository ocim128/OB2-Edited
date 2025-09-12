using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Parallelization.Models
{
    // Simplified PauseTokenSource implementation
    public class PauseTokenSource
    {
        private volatile bool isPaused = false;
        private TaskCompletionSource<bool> pauseTcs;

        public PauseToken Token => new(this);

        public Task<bool> IsPausedAsync(CancellationToken token = default)
        {
            return Task.FromResult(isPaused);
        }

        public Task ResumeAsync(CancellationToken token = default)
        {
            if (!isPaused)
                return Task.CompletedTask;

            lock (this)
            {
                if (!isPaused)
                    return Task.CompletedTask;

                isPaused = false;
                pauseTcs?.TrySetResult(true);
                pauseTcs = null;
            }

            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken token = default)
        {
            if (isPaused)
                return Task.CompletedTask;

            lock (this)
            {
                if (isPaused)
                    return Task.CompletedTask;

                isPaused = true;
                pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return Task.CompletedTask;
        }

        public async Task PauseIfRequestedAsync(CancellationToken token = default)
        {
            if (!isPaused)
                return;

            var tcs = pauseTcs;
            if (tcs != null)
            {
                await tcs.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }

    // PauseToken - consumer side
    public readonly struct PauseToken
    {
        private readonly PauseTokenSource source;

        public PauseToken(PauseTokenSource source)
        {
            this.source = source;
        }

        public Task<bool> IsPaused() => source.IsPausedAsync();

        public Task PauseIfRequestedAsync(CancellationToken token = default)
            => source.PauseIfRequestedAsync(token);

        public Task WaitWhilePausedAsync(CancellationToken token = default)
            => source.PauseIfRequestedAsync(token);
    }
}