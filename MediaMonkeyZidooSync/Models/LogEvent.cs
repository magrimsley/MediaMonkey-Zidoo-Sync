// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.0 (Major/Minor controlled by the product owner)
// Development channel: alpha.6
// Purpose: Live UI log-event model and semantic color categories.
// Remarks:
// - Version metadata is defined by MediaMonkeyZidooSync.csproj.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Synchronization is entirely native C#; PowerShell is used only for build, test and installation tooling.
// ============================================================================
namespace MediaMonkeyZidooSync.Models;

/// <summary>
/// Semantic categories used by the synchronization UI. These names correspond
/// to the validated production logging semantics.
/// </summary>
public enum LogLevel
{
    /// <summary>Routine/default operational information.</summary>
    Blue,
    /// <summary>Warnings and recovery activity.</summary>
    Yellow,
    /// <summary>Successful operations, commits, and verified recovery.</summary>
    Green,
    /// <summary>Errors and failed/recovery-required operations.</summary>
    Red
}

/// <summary>
/// Represents one live synchronization message retained by the UI.
/// </summary>
/// <param name="Timestamp">Local time at which the UI received the message.</param>
/// <param name="Level">Semantic display category.</param>
/// <param name="Message">Message text emitted by the synchronization process.</param>
public sealed record LogEvent(DateTime Timestamp, LogLevel Level, string Message)
{
    /// <summary>Technical details retained only in the saved diagnostic log.</summary>
    public string? DiagnosticDetails { get; init; }
}
