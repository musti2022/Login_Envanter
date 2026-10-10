using System.ComponentModel.DataAnnotations;
using EnterpriseInventory.Domain.Users;

namespace EnterpriseInventory.Application.Authentication;

/// <summary>Time limits of signed-in sessions, from the <c>Session</c> configuration section.</summary>
public sealed class UserSessionOptions : IValidatableObject
{
    public const string SectionName = "Session";

    /// <summary>A session ends after this many minutes without a request.</summary>
    [Range(1, 240)]
    public int IdleTimeoutMinutes { get; set; } = 20;

    /// <summary>A session ends this many hours after sign-in, however active it is.</summary>
    [Range(1, 24)]
    public int AbsoluteTimeoutHours { get; set; } = 8;

    /// <summary>Group membership and account state of signed-in users are checked in the directory this often.</summary>
    [Range(1, 60)]
    public int AccessRecheckMinutes { get; set; } = 5;

    /// <summary>
    /// How many minutes past <see cref="AccessRecheckMinutes"/> a session may continue while the directory cannot be
    /// reached. After that the session ends; 0 ends it at the first failed check.
    /// </summary>
    [Range(0, 240)]
    public int DirectoryOutageGraceMinutes { get; set; } = 15;

    public SessionTimeouts Timeouts => new(
        TimeSpan.FromMinutes(IdleTimeoutMinutes),
        TimeSpan.FromHours(AbsoluteTimeoutHours),
        TimeSpan.FromMinutes(AccessRecheckMinutes),
        TimeSpan.FromMinutes(DirectoryOutageGraceMinutes));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TimeSpan.FromMinutes(IdleTimeoutMinutes) > TimeSpan.FromHours(AbsoluteTimeoutHours))
        {
            yield return new ValidationResult(
                "Session:IdleTimeoutMinutes must not be longer than Session:AbsoluteTimeoutHours.",
                [nameof(IdleTimeoutMinutes), nameof(AbsoluteTimeoutHours)]);
        }
    }
}
