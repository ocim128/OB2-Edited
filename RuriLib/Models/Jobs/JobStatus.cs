namespace RuriLib.Models.Jobs;

/// <summary>
/// Has all the underlying TaskManager statuses plus some extra ones like Waiting for additional job-specific features
/// </summary>
public enum JobStatus
{
    Idle = 0,
    Waiting = 1,
    Starting = 2,
    Running = 3,
    Pausing = 4,
    Paused = 5,
    Stopping = 6,
    Resuming = 7
}
