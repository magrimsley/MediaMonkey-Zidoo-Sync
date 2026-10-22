using System;
using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Native synchronization entry point with separate read-only composition.</summary>
public sealed class SyncRunner
{
    private readonly NativeSyncReadOnlyOrchestrator? _assessment;
    private readonly NativeProductionSyncService? _production;

    public SyncRunner() : this(new NativeProductionSyncService()) { }
    public SyncRunner(NativeProductionSyncService production)
    {
        _production=production??throw new ArgumentNullException(nameof(production));
        _production.Output+=message=>Output?.Invoke(new LogEvent(DateTime.Now,
            message.StartsWith("Completed and verified: ",StringComparison.Ordinal)?LogLevel.Green:LogLevel.Blue,message));
    }

    // Explicit composition for read-only planning and isolated tests.
    public SyncRunner(NativeSyncReadOnlyOrchestrator assessment)
        => _assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));

    public event Action<LogEvent>? Output;
    public bool LastRunVerifiedDevice=>_production?.LastRunVerifiedDevice==true;
    public int LastRunCompletedChanges=>_production?.LastRunCompletedChanges??0;
    public bool LastRunRecovered=>_production?.LastRunRecovered==true;

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

    public Task<IReadOnlyList<NativeProductionSyncService.ReviewedChange>> PrepareReviewAsync(AppConfig config,CancellationToken ct=default)
        => (_production??throw new InvalidOperationException("This runner is read-only.")).PrepareReviewAsync(config,ct);
    public Task<IReadOnlyList<NativeProductionSyncService.ReviewedChange>> PrepareRepairCandidatesAsync(AppConfig config,CancellationToken ct=default)
        => (_production??throw new InvalidOperationException("This runner is read-only.")).PrepareRepairCandidatesAsync(config,ct);
    public Task<bool> RecoverPendingAsync(AppConfig config,Func<string,bool> confirm,CancellationToken ct=default)
        => (_production??throw new InvalidOperationException("This runner is read-only.")).RecoverPendingAsync(config,confirm,ct);

    public static bool IsUserCancellation(Exception error, CancellationToken ct) =>
        ct.IsCancellationRequested && (error is OperationCanceledException ||
            error is NativeTransactionRolledBackException { InnerException: OperationCanceledException });
    /// <summary>Executes native synchronization; read-only runners cannot authorize it.</summary>
    public async Task<int> RunAsync(AppConfig config, CancellationToken ct = default,
        IReadOnlyList<NativeProductionSyncService.ReviewedChange>? reviewed=null)
    {
        if(_production is null)throw new InvalidOperationException("This runner is read-only; synchronization is not configured.");
        try{return await Task.Run(()=>_production.RunAsync(config,ct,reviewed),ct).ConfigureAwait(false);}
        catch(NativeTransactionRolledBackException error) when(IsUserCancellation(error,ct))
        {
            // The engine has finished compensation and power restoration before this boundary.
            throw new OperationCanceledException("Synchronization cancelled after the current playlist was restored.", error, ct);
        }
    }
}
