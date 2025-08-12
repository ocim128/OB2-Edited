using System;
using System.Linq;

namespace RuriLib.Models.Jobs.Status;

/// <summary>
/// Represents the status of a bot execution
/// </summary>
public static class BotStatus
{
    public const string Success = "SUCCESS";
    public const string Fail = "FAIL";
    public const string Retry = "RETRY";
    public const string Ban = "BAN";
    public const string Error = "ERROR";
    public const string Invalid = "INVALID";
    public const string None = "NONE";

    private static readonly string[] BadStatuses = [Fail, Retry, Ban, Error, Invalid];

    /// <summary>
    /// Determines if the given status is considered a hit
    /// </summary>
    public static bool IsHitStatus(string status) => !BadStatuses.Contains(status);

    /// <summary>
    /// Determines if the given status should trigger a retry
    /// </summary>
    public static bool ShouldRetry(string status) => status is Retry or Error;

    /// <summary>
    /// Determines if the given status should trigger a ban
    /// </summary>
    public static bool ShouldBan(string status) => status is Ban or Error;

    /// <summary>
    /// Determines if the given status is valid
    /// </summary>
    public static bool IsValidStatus(string status) =>
        status is Success or Fail or Retry or Ban or Error or Invalid or None;
}