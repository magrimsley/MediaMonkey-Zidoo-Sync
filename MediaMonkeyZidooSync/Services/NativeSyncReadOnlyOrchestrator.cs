using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Read-only orchestration result. It does not grant permission to mutate state.</summary>
public sealed record NativeSyncReadOnlyResult(
    int CreateCount,
    int UpdateCount,
    int UnchangedCount,
    int PendingZidooDeleteCount,
    IReadOnlyList<NativePlaylistFolderReconciler.WorkItem> ProposedWork)
{
    public bool MayExecute => false;
}

/// <summary>
/// Alpha.56 safe composition boundary. Deliberately contains no mutation/device APIs.
/// Production execution requires a separate, independently reviewed authorization path.
/// </summary>
public sealed class NativeSyncReadOnlyOrchestrator
{
    private readonly INativeSyncPlanProvider _plans;
    private readonly NativePlaylistFolderReconciler _reconciler;

    public NativeSyncReadOnlyOrchestrator(
        INativeSyncPlanProvider plans,
        NativePlaylistFolderReconciler reconciler)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    }

    public async Task<NativeSyncReadOnlyResult> AssessAsync(
        AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = await _plans.PrepareAsync(config, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Native planner returned no plan.");
        if (plan.Items is null || plan.Items.Any(x => x is null || string.IsNullOrWhiteSpace(x.FileName)))
            throw new InvalidOperationException("Native planner returned invalid playlist items.");
        if (plan.CreateCount < 0 || plan.UpdateCount < 0 || plan.UnchangedCount < 0 || plan.PendingZidooDeleteCount < 0 ||
            plan.CreateCount + plan.UpdateCount + plan.UnchangedCount + plan.PendingZidooDeleteCount != plan.Items.Count)
            throw new InvalidOperationException("Native planner returned inconsistent counts.");
        var work = _reconciler.Reconcile(plan);
        cancellationToken.ThrowIfCancellationRequested();
        return new NativeSyncReadOnlyResult(plan.CreateCount, plan.UpdateCount,
            plan.UnchangedCount, plan.PendingZidooDeleteCount, Array.AsReadOnly(work.ToArray()));
    }
}
