using System;
using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Native sync entry point. Production mutation remains disabled.</summary>
public sealed class SyncRunner
{
    private readonly NativeSyncReadOnlyOrchestrator? _assessment;

    // Retained for existing WPF composition; no hidden dependency construction.
    public SyncRunner() { }

    // Explicit composition for read-only planning and isolated tests.
    public SyncRunner(NativeSyncReadOnlyOrchestrator assessment)
        => _assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));

    public event Action<LogEvent>? Output;

    /// <summary>Read-only assessment; does not authorize or perform execution.</summary>
    public async Task<NativeSyncReadOnlyResult> AssessAsync(AppConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ct.ThrowIfCancellationRequested();
        if (_assessment is null)
            throw new InvalidOperationException("Read-only native assessment is not configured.");
        Output?.Invoke(new LogEvent(DateTime.Now, LogLevel.Blue, "Starting read-only native synchronization assessment."));
        var result = await _assessment.AssessAsync(config, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        Output?.Invoke(new LogEvent(DateTime.Now, LogLevel.Blue,
            $"Read-only assessment complete: create {result.CreateCount}, update {result.UpdateCount}, unchanged {result.UnchangedCount}, pending deletion {result.PendingZidooDeleteCount}. Execution remains disabled."));
        return result;
    }

    /// <summary>Production synchronization is deliberately not authorized.</summary>
    public Task<int> RunAsync(AppConfig config, CancellationToken ct = default)
    {
        const string message = "Synchronization is not yet enabled in this prerelease.";
        Output?.Invoke(new LogEvent(DateTime.Now, LogLevel.Yellow, message));
        throw new NotSupportedException(message);
    }
}
