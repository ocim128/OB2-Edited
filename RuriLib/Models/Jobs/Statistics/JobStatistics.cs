using System;
using System.Threading;
using RuriLib.Models.Jobs.Status;

namespace RuriLib.Models.Jobs.Statistics;

/// <summary>
/// Manages statistics for a MultiRunJob
/// </summary>
public class JobStatistics
{
    private int _dataTested;
    private int _dataHits;
    private int _dataCustom;
    private int _dataFails;
    private int _dataRetried;
    private int _dataBanned;
    private int _dataToCheck;
    private int _dataInvalid;
    private int _dataErrors;

    /// <summary>
    /// The number of tested data lines.
    /// </summary>
    public int Tested => _dataTested;

    /// <summary>
    /// The number of hits.
    /// </summary>
    public int Hits => _dataHits;

    /// <summary>
    /// The number of custom statuses.
    /// </summary>
    public int Custom => _dataCustom;

    /// <summary>
    /// The number of fails.
    /// </summary>
    public int Fails => _dataFails;

    /// <summary>
    /// The number of retried data lines.
    /// </summary>
    public int Retried => _dataRetried;

    /// <summary>
    /// The number of banned data lines.
    /// </summary>
    public int Banned => _dataBanned;

    /// <summary>
    /// The number of data lines to check.
    /// </summary>
    public int ToCheck => _dataToCheck;

    /// <summary>
    /// The number of invalid data lines.
    /// </summary>
    public int Invalid => _dataInvalid;

    /// <summary>
    /// The number of errors.
    /// </summary>
    public int Errors => _dataErrors;

    /// <summary>
    /// Resets all statistics to zero
    /// </summary>
    public void Reset()
    {
        _dataTested = 0;
        _dataHits = 0;
        _dataCustom = 0;
        _dataFails = 0;
        _dataRetried = 0;
        _dataBanned = 0;
        _dataToCheck = 0;
        _dataErrors = 0;
        _dataInvalid = 0;
    }

    /// <summary>
    /// Increments the tested counter
    /// </summary>
    public void IncrementTested() => Interlocked.Increment(ref _dataTested);
    
    /// <summary>
    /// Increments the hits counter
    /// </summary>
    public void IncrementHits() => Interlocked.Increment(ref _dataHits);
    
    /// <summary>
    /// Increments the custom counter
    /// </summary>
    public void IncrementCustom() => Interlocked.Increment(ref _dataCustom);
    
    /// <summary>
    /// Increments the fails counter
    /// </summary>
    public void IncrementFails() => Interlocked.Increment(ref _dataFails);
    
    /// <summary>
    /// Increments the retried counter
    /// </summary>
    public void IncrementRetried() => Interlocked.Increment(ref _dataRetried);
    
    /// <summary>
    /// Increments the banned counter
    /// </summary>
    public void IncrementBanned() => Interlocked.Increment(ref _dataBanned);
    
    /// <summary>
    /// Increments the to-check counter
    /// </summary>
    public void IncrementToCheck() => Interlocked.Increment(ref _dataToCheck);
    
    /// <summary>
    /// Increments the invalid counter
    /// </summary>
    public void IncrementInvalid() => Interlocked.Increment(ref _dataInvalid);
    
    /// <summary>
    /// Increments the errors counter
    /// </summary>
    public void IncrementErrors() => Interlocked.Increment(ref _dataErrors);

    /// <summary>
    /// Updates statistics based on bot status
    /// </summary>
    public void UpdateForStatus(string status)
    {
        switch (status)
        {
            case BotStatus.Success:
                Interlocked.Increment(ref _dataHits);
                break;
            case BotStatus.None:
                Interlocked.Increment(ref _dataToCheck);
                break;
            case BotStatus.Fail:
                Interlocked.Increment(ref _dataFails);
                break;
            case BotStatus.Invalid:
                Interlocked.Increment(ref _dataInvalid);
                break;
            case BotStatus.Error:
                Interlocked.Increment(ref _dataErrors);
                break;
            case BotStatus.Retry:
                Interlocked.Increment(ref _dataRetried);
                break;
            case BotStatus.Ban:
                Interlocked.Increment(ref _dataBanned);
                break;
            default:
                Interlocked.Increment(ref _dataCustom);
                break;
        }
        
        Interlocked.Increment(ref _dataTested);
    }
}