using Flux.Core.Repositories;
using RuriLib.Models.Proxies;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Flux.Core.Models.Proxies;

/// <summary>
/// A proxy check output that writes proxies to an <see cref="IProxyRepository"/>.
/// </summary>
public class DatabaseProxyCheckOutput : IProxyCheckOutput, IDisposable
{
    private readonly IServiceScope _scope;
    private readonly IProxyRepository _proxyRepo;
    private readonly SemaphoreSlim _semaphore;

    public DatabaseProxyCheckOutput(IServiceScopeFactory scopeFactory)
    {
        _scope = scopeFactory.CreateScope();
        _proxyRepo = _scope.ServiceProvider.GetRequiredService<IProxyRepository>();
        _semaphore = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc/>
    public async Task StoreAsync(Proxy proxy)
    {
        // Only allow updating one proxy at a time (multiple threads should
        // not use the same DbContext at the same time).
        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            // Single UPDATE statement: the previous implementation issued a SELECT to materialize
            // the entity (and NRE'd if the proxy had been deleted meanwhile) and then an UPDATE,
            // serialized behind this semaphore, once per checked proxy.
            await _proxyRepo.UpdateCheckResultAsync(
                proxy.Id,
                proxy.Country,
                proxy.LastChecked,
                proxy.Ping,
                proxy.WorkingStatus).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            /* 
             * If we are here it means a few possible things
             * - we deleted the job but the parallelizer was still running
             * - the original proxy was deleted (e.g. from the proxy tab)
             * - the scope was disposed for some reason
             * 
             * In any case we don't want to save anything to the database.
             */
            
            // TODO: Turn this into a log message using a logger
            Console.WriteLine($"Error while saving proxy {proxy.Id} to the database: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _scope?.Dispose();
        GC.SuppressFinalize(this);
    }
}
