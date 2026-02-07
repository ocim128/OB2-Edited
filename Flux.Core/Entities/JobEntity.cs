using Flux.Core.Models.Jobs;
using System;

namespace Flux.Core.Entities;

/// <summary>
/// This entity stores a job of the Flux instance.
/// </summary>
public class JobEntity : Entity
{
    /// <summary>
    /// The creation date and time of the job.
    /// </summary>
    public DateTime CreationDate { get; set; }

    /// <summary>
    /// The type of job, used to know how to deserialize the options.
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// The job options as a json string.
    /// </summary>
    public string JobOptions { get; set; } = string.Empty;

    /// <summary>
    /// The owner of this job. Null if admin.
    /// </summary>
    public GuestEntity? Owner { get; set; }
}
