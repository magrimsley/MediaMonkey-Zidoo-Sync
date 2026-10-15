using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Read-only native synchronization planning contract.</summary>
public interface INativeSyncPlanProvider
{
    Task<MediaMonkeyExportTransactionPlan> PrepareAsync(AppConfig config, CancellationToken cancellationToken = default);
}

/// <summary>Adapter around the existing validated planner; no duplicate planning logic.</summary>
public sealed class NativeSyncPlanProvider : INativeSyncPlanProvider
{
    private readonly MediaMonkeyExportTransactionPlanner _planner;

    public NativeSyncPlanProvider(MediaMonkeyExportTransactionPlanner planner)
        => _planner = planner ?? throw new System.ArgumentNullException(nameof(planner));

    public Task<MediaMonkeyExportTransactionPlan> PrepareAsync(AppConfig config, CancellationToken cancellationToken = default)
        => _planner.PrepareAsync(config, cancellationToken);
}
