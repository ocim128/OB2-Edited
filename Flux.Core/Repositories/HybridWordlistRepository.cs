using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Flux.Core.Entities;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Core.Repositories;

/// <summary>
/// Stores wordlists to the disk and the database. Files are stored on disk while
/// metadata is stored in a database.
/// </summary>
public class HybridWordlistRepository : IWordlistRepository
{
    private readonly string baseFolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private IServiceScope _getAllScope;

    public HybridWordlistRepository(IServiceScopeFactory scopeFactory, string baseFolder)
    {
        _scopeFactory = scopeFactory;
        this.baseFolder = baseFolder;
        Directory.CreateDirectory(baseFolder);
    }

    private ApplicationDbContext CreateDbContext()
    {
        var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private (ApplicationDbContext context, IServiceScope scope) CreateDbContextWithScope()
    {
        var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (context, scope);
    }

    /// <inheritdoc/>
    public async Task AddAsync(WordlistEntity entity, CancellationToken cancellationToken = default)
    {
        var (context, scope) = CreateDbContextWithScope();
        try
        {
            // Save it to the DB
            context.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task AddAsync(WordlistEntity entity, MemoryStream stream,
        CancellationToken cancellationToken = default)
    {
        // Generate a unique filename
        var path = Path.Combine(baseFolder, $"{Guid.NewGuid()}.txt");
        entity.FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? path.Replace('/', '\\')
            : path.Replace('\\', '/');

        // Count the amount of lines from the in-memory buffer before writing to disk
        entity.Total = CountLines(stream);

        // Create the file on disk
        await File.WriteAllBytesAsync(entity.FileName, stream.ToArray(),
            cancellationToken);

        await AddAsync(entity);
    }

    /// <inheritdoc/>
    public IQueryable<WordlistEntity> GetAll()
    {
        // Dispose any existing scope to prevent memory leaks
        _getAllScope?.Dispose();

        var (context, scope) = CreateDbContextWithScope();
        _getAllScope = scope;
        return context.Wordlists;
    }

    /// <inheritdoc/>
    public async Task<WordlistEntity> GetAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var (context, scope) = CreateDbContextWithScope();
        try
        {
            return await context.Wordlists.Include(w => w.Owner)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(WordlistEntity entity, CancellationToken cancellationToken = default)
    {
        var (context, scope) = CreateDbContextWithScope();
        try
        {
            context.Entry(entity).State = EntityState.Modified;
            context.Update(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(WordlistEntity entity, bool deleteFile = false,
        CancellationToken cancellationToken = default)
    {
        if (deleteFile && File.Exists(entity.FileName))
            File.Delete(entity.FileName);

        var (context, scope) = CreateDbContextWithScope();
        try
        {
            context.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Purge()
    {
        var (context, scope) = CreateDbContextWithScope();
        try
        {
            _ = context.Database.ExecuteSqlRaw($"DELETE FROM {nameof(ApplicationDbContext.Wordlists)}");
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _getAllScope?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int CountLines(MemoryStream ms)
    {
        var position = ms.Position;
        ms.Position = 0;
        int count = 0;
        using var reader = new StreamReader(ms, leaveOpen: true);
        while (reader.ReadLine() != null)
            count++;
        ms.Position = position;
        return count;
    }
}
