using System;

namespace Flux.Native.ViewModels.Jobs;

public partial class MultiRunJobViewerViewModel
{
    public void ClearSparklineData()
    {
        cpmHistory.Clear();
        hitsPerMinuteHistory.Clear();
        lastRecordedHits = 0;
        lastHitsRecordTime = DateTime.Now;
        SparklineDataUpdated?.Invoke();
    }

    private void RecordSparklineData()
    {
        cpmHistory.Add(Job.CPM);
        while (cpmHistory.Count > MaxHistoryPoints)
        {
            cpmHistory.RemoveAt(0);
        }

        var now = DateTime.Now;
        var elapsedMinutes = (now - lastHitsRecordTime).TotalMinutes;
        if (elapsedMinutes > 0)
        {
            var currentHits = HitsCount;
            var hitsDelta = currentHits - lastRecordedHits;
            hitsPerMinuteHistory.Add(Math.Max(0, hitsDelta / elapsedMinutes));
            while (hitsPerMinuteHistory.Count > MaxHistoryPoints)
            {
                hitsPerMinuteHistory.RemoveAt(0);
            }

            lastRecordedHits = currentHits;
            lastHitsRecordTime = now;
        }

        SparklineDataUpdated?.Invoke();
    }

    private void TryPlayHitSound()
    {
        try
        {
            soundPlayer.Play();
        }
        catch
        {
        }
    }
}
