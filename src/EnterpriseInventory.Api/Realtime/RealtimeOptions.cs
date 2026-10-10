using System.ComponentModel.DataAnnotations;

namespace EnterpriseInventory.Api.Realtime;

/// <summary>Settings of the live connections, from the <c>Realtime</c> configuration section.</summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>
    /// How often the sessions behind open live connections are checked. A connection whose session timed out or lost
    /// its access in the directory is closed at the next check.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.1", "00:05:00")]
    public TimeSpan SessionCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}
