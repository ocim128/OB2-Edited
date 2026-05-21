using Flux.Core;
using Flux.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Flux.Shared.Tests.Hits;

public class RecentHitsQueryTests
{
    [Fact]
    public async Task DailyHitAggregation_TranslatesOnSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Hits.AddRange(
            new HitEntity { Type = "SUCCESS", ConfigName = "config", Date = new DateTime(2026, 5, 21, 1, 0, 0, DateTimeKind.Utc) },
            new HitEntity { Type = "SUCCESS", ConfigName = "config", Date = new DateTime(2026, 5, 21, 2, 0, 0, DateTimeKind.Utc) },
            new HitEntity { Type = "SUCCESS", ConfigName = "config", Date = new DateTime(2026, 5, 20, 1, 0, 0, DateTimeKind.Utc) });
        await context.SaveChangesAsync();

        var dailyHits = await context.Hits
            .AsNoTracking()
            .Where(h => h.Type == "SUCCESS")
            .GroupBy(h => new { h.ConfigName, Date = h.Date.Date })
            .Select(g => new { g.Key.ConfigName, g.Key.Date, Count = g.Count() })
            .ToListAsync();

        Assert.Contains(dailyHits, h => h.ConfigName == "config" && h.Date == new DateTime(2026, 5, 21) && h.Count == 2);
        Assert.Contains(dailyHits, h => h.ConfigName == "config" && h.Date == new DateTime(2026, 5, 20) && h.Count == 1);
    }
}
