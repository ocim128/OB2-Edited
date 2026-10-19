using RuriLib.Models.Jobs.Statistics;
using RuriLib.Models.Jobs.Status;

namespace RuriLib.Tests.Models.Jobs;

public class JobStatisticsErrorCountTests
{
    [Theory]
    [InlineData(BotStatus.Error)]
    [InlineData(BotStatus.Success)]
    [InlineData(BotStatus.Fail)]
    public void UpdateForStatus_CountsExactlyOneResultPerCheck(string status)
    {
        var stats = new JobStatistics();

        stats.UpdateForStatus(status);

        // Every processed check must contribute exactly one to Tested regardless of status,
        // otherwise the progress bar and CPM disagree with the data pool cursor.
        Assert.Equal(1, stats.Tested);
    }

    [Fact]
    public void UpdateForStatus_Error_IncrementsErrorsOnce()
    {
        var stats = new JobStatistics();

        stats.UpdateForStatus(BotStatus.Error);

        // Regression guard: the executor used to increment Errors when an attempt threw AND the
        // result processor counted the final ERROR status, so a single errored line reported 2.
        Assert.Equal(1, stats.Errors);
        Assert.Equal(1, stats.Tested);
    }

    [Fact]
    public void UpdateForStatus_Error_DoesNotAffectOtherStatusCounters()
    {
        var stats = new JobStatistics();

        stats.UpdateForStatus(BotStatus.Error);

        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Fails);
        Assert.Equal(0, stats.Retried);
        Assert.Equal(0, stats.Banned);
        Assert.Equal(0, stats.Invalid);
        Assert.Equal(0, stats.Custom);
        Assert.Equal(0, stats.ToCheck);
    }
}
