using System.IO;
using System.Net.Http;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Plain-language presentation; exception details are saved separately.</summary>
public static class UserMessages
{
    public static LogEvent Cancelled(Exception? error = null) => new(DateTime.Now, LogLevel.Yellow,
        "User cancelled the synchronization." +
        (error?.InnerException is NativeTransactionRolledBackException
            ? " The current playlist was restored to its previous state." : "") +
        " Previously completed playlist changes remain applied.");

    public static LogEvent Completed(int changes) => new(DateTime.Now, LogLevel.Green,
        changes == 0 ? "Synchronization completed successfully. No playlist changes were needed." :
        $"Synchronization completed successfully. {changes} playlist {(changes == 1 ? "change was" : "changes were")} applied and verified.");

    public static string Explain(Exception error) => error switch
    {
        NativeTransactionRolledBackException => "Synchronization stopped. The current playlist was restored; earlier completed changes may remain. See the diagnostic log for details.",
        UnauthorizedAccessException => "Access to a required file or folder was denied. Check its permissions and try again. See the diagnostic log for details.",
        HttpRequestException or TaskCanceledException => "The Zidoo did not respond. Check its power and network connection, then try again. See the diagnostic log for details.",
        InvalidDataException => ForDisplay(error.Message) + " Open the diagnostic log for details.",
        IOException => "Synchronization could not finish safely. Keep the recovery files and review the diagnostic log before running again.",
        _ => "The operation could not be completed. See the diagnostic log for details."
    };

    public static LogEvent Failure(Exception error) => new(DateTime.Now, LogLevel.Red, Explain(error))
        { DiagnosticDetails = error.ToString() };

    // Defense in depth for output from other components; copying visible output uses this too.
    public static string ForDisplay(string message) =>
        message.Contains("Exception:", StringComparison.Ordinal) || message.Contains(" --->", StringComparison.Ordinal) ||
        message.Contains("\n   at ", StringComparison.Ordinal) || message.Contains("\n at ", StringComparison.Ordinal)
            ? "The operation could not be completed. See the diagnostic log for details." : message;
}
