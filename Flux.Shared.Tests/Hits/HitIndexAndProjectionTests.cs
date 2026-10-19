using Flux.Core;
using Flux.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flux.Shared.Tests.Hits;

public class HitIndexAndProjectionTests
{
    private static async Task<(SqliteConnection Connection, ApplicationDbContext Context)> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    [Fact]
    public async Task ConfigIdIndex_IsCreatedByTheModel()
    {
        var (connection, context) = await CreateContextAsync();
        await using var _ = connection;
        await using var __ = context;

        // The per-job recent-hits query filters on ConfigId + WordlistId and orders by Date.
        // No other index covers ConfigId, so this one must exist in the created schema.
        var indexes = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Hits'")
            .ToListAsync();

        Assert.Contains("IX_Hits_ConfigId_WordlistId_Date", indexes);
    }

    [Fact]
    public async Task FilteringByConfigIdAndWordlistId_ReturnsOnlyThatJobsHits()
    {
        var (connection, context) = await CreateContextAsync();
        await using var _ = connection;
        await using var __ = context;

        context.Hits.AddRange(
            new HitEntity { ConfigId = "cfg-a", WordlistId = 1, Type = "SUCCESS", Data = "keep", Date = DateTime.UtcNow },
            new HitEntity { ConfigId = "cfg-a", WordlistId = 2, Type = "SUCCESS", Data = "other-wordlist", Date = DateTime.UtcNow },
            new HitEntity { ConfigId = "cfg-b", WordlistId = 1, Type = "SUCCESS", Data = "other-config", Date = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var filtered = await context.Hits
            .AsNoTracking()
            .Where(h => h.ConfigId == "cfg-a" && h.WordlistId == 1)
            .OrderByDescending(h => h.Date)
            .Take(200)
            .Select(h => h.Data)
            .ToListAsync();

        Assert.Equal(new[] { "keep" }, filtered);
    }

    [Fact]
    public async Task NewestWindow_KeepsTheMostRecentRowsInOriginalOrder()
    {
        var (connection, context) = await CreateContextAsync();
        await using var _ = connection;
        await using var __ = context;

        for (var i = 0; i < 20; i++)
        {
            context.Hits.Add(new HitEntity
            {
                ConfigId = "cfg",
                WordlistId = 1,
                Type = "SUCCESS",
                Data = i.ToString(),
                Date = new DateTime(2026, 1, 1).AddMinutes(i)
            });
        }

        await context.SaveChangesAsync();

        // Mirrors the projection's "newest N, oldest-to-newest" window.
        var window = (await context.Hits
                .AsNoTracking()
                .Where(h => h.ConfigId == "cfg")
                .OrderByDescending(h => h.Date)
                .Take(5)
                .ToListAsync())
            .OrderBy(h => h.Date)
            .Select(h => h.Data)
            .ToList();

        Assert.Equal(new[] { "15", "16", "17", "18", "19" }, window);
    }
}
