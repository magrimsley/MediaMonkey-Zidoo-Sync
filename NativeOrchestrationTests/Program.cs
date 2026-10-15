using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { Console.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
static MediaMonkeyExportTransactionPlan Plan(params PlaylistTransactionPlanItem[] items) => new(
    Array.AsReadOnly(items),
    items.Count(x => x.Action == PlaylistTransactionAction.Create),
    items.Count(x => x.Action == PlaylistTransactionAction.Update),
    items.Count(x => x.Action == PlaylistTransactionAction.Unchanged),
    items.Count(x => x.Action == PlaylistTransactionAction.PendingZidooDelete));
static PlaylistTransactionPlanItem Item(string name, PlaylistTransactionAction action) => new(name, action);
static NativeSyncReadOnlyOrchestrator Orchestrator(INativeSyncPlanProvider provider) =>
    new(provider, new NativePlaylistFolderReconciler());
static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T ex) { return ex; }
    throw new Exception($"Expected {typeof(T).Name}");
}
var config = new AppConfig();
Test("Assessment classifies proposed operations and remains non-executable", () =>
{
    var fake = new FakeProvider(Plan(Item("a.m3u", PlaylistTransactionAction.Create),
        Item("b.m3u", PlaylistTransactionAction.Update), Item("c.m3u", PlaylistTransactionAction.Unchanged),
        Item("d.m3u", PlaylistTransactionAction.PendingZidooDelete)));
    var result = Orchestrator(fake).AssessAsync(config).GetAwaiter().GetResult();
    if (result.CreateCount != 1 || result.UpdateCount != 1 || result.UnchangedCount != 1 ||
        result.PendingZidooDeleteCount != 1 || result.ProposedWork.Count != 3 || result.MayExecute || fake.Calls != 1)
        throw new Exception("Incorrect read-only assessment");
});
Test("Injected provider is used and repeated assessments are deterministic", () =>
{
    var fake = new FakeProvider(Plan(Item("z.m3u", PlaylistTransactionAction.Create), Item("a.m3u", PlaylistTransactionAction.Update)));
    var runner = new SyncRunner(Orchestrator(fake));
    var first = runner.AssessAsync(config).GetAwaiter().GetResult();
    var second = runner.AssessAsync(config).GetAwaiter().GetResult();
    if (fake.Calls != 2 || !first.ProposedWork.SequenceEqual(second.ProposedWork) ||
        first.ProposedWork[0].FileName != "a.m3u") throw new Exception("Unstable assessment");
});
Test("Cancellation before planning refuses assessment", () =>
{
    var fake = new FakeProvider(Plan());
    using var cts = new CancellationTokenSource(); cts.Cancel();
    Throws<OperationCanceledException>(() => Orchestrator(fake).AssessAsync(config, cts.Token).GetAwaiter().GetResult());
    if (fake.Calls != 0) throw new Exception("Planner invoked after cancellation");
});
Test("Cancellation during planning refuses assessment", () =>
{
    var fake = new FakeProvider(Plan()) { CancelOnCall = true };
    using var cts = new CancellationTokenSource();
    Throws<OperationCanceledException>(() => Orchestrator(fake).AssessAsync(config, cts.Token).GetAwaiter().GetResult());
});
Test("Planner exceptions propagate without executing", () =>
{
    var fake = new FakeProvider(Plan()) { Failure = new IOException("read failed") };
    Throws<IOException>(() => Orchestrator(fake).AssessAsync(config).GetAwaiter().GetResult());
});
Test("Null plan fails closed", () =>
{
    Throws<InvalidOperationException>(() => Orchestrator(new FakeProvider(null)).AssessAsync(config).GetAwaiter().GetResult());
});
Test("Inconsistent counts fail closed", () =>
{
    var fake = new FakeProvider(new MediaMonkeyExportTransactionPlan(
        new[] { Item("a.m3u", PlaylistTransactionAction.Create) }, 0, 0, 0, 0));
    Throws<InvalidOperationException>(() => Orchestrator(fake).AssessAsync(config).GetAwaiter().GetResult());
});
Test("Invalid filename fails closed", () =>
{
    var fake = new FakeProvider(Plan(Item("../unsafe.m3u", PlaylistTransactionAction.Create)));
    Throws<InvalidOperationException>(() => Orchestrator(fake).AssessAsync(config).GetAwaiter().GetResult());
});
Test("Duplicate filenames fail closed", () =>
{
    var fake = new FakeProvider(Plan(Item("a.m3u", PlaylistTransactionAction.Create), Item("A.m3u", PlaylistTransactionAction.Update)));
    Throws<InvalidOperationException>(() => Orchestrator(fake).AssessAsync(config).GetAwaiter().GetResult());
});
Test("Unconfigured runner refuses assessment", () =>
{
    Throws<InvalidOperationException>(() => new SyncRunner().AssessAsync(config).GetAwaiter().GetResult());
});
Test("Production RunAsync remains disabled", () =>
{
    var runner = new SyncRunner(Orchestrator(new FakeProvider(Plan())));
    Throws<NotSupportedException>(() => runner.RunAsync(config).GetAwaiter().GetResult());
});
Test("Plan inspection exposes validated proposed operations without execution", () =>
{
    var result = Orchestrator(new FakeProvider(Plan(
        Item("create.m3u", PlaylistTransactionAction.Create),
        Item("update.m3u", PlaylistTransactionAction.Update),
        Item("same.m3u", PlaylistTransactionAction.Unchanged),
        Item("delete.m3u", PlaylistTransactionAction.PendingZidooDelete))))
        .AssessAsync(config).GetAwaiter().GetResult();
    var rows = NativePlanInspection.FromAssessment(result);
    if (rows.Count != 3 || rows.Any(x => x.Action == PlaylistTransactionAction.Unchanged) ||
        rows.Single(x => x.Action == PlaylistTransactionAction.PendingZidooDelete).RequiresZidooDeletionVerification != true ||
        result.MayExecute)
        throw new Exception("Incorrect inspection projection");
});
Test("Plan inspection fails closed on inconsistent proposed counts", () =>
{
    var invalid = new NativeSyncReadOnlyResult(2, 0, 0, 0,
        Array.AsReadOnly(new[] { new NativePlaylistFolderReconciler.WorkItem("one.m3u", PlaylistTransactionAction.Create, false) }));
    Throws<InvalidOperationException>(() => NativePlanInspection.FromAssessment(invalid));
});
Test("Plan inspection cannot authorize execution", () =>
{
    var result = Orchestrator(new FakeProvider(Plan(Item("one.m3u", PlaylistTransactionAction.Create))))
        .AssessAsync(config).GetAwaiter().GetResult();
    var rows = NativePlanInspection.FromAssessment(result);
    if (rows.Count != 1 || result.MayExecute) throw new Exception("Unsafe inspection");
    Throws<NotSupportedException>(() => new SyncRunner(Orchestrator(new FakeProvider(Plan())))
        .RunAsync(config).GetAwaiter().GetResult());
});
Console.WriteLine($"Results: {passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeProvider : INativeSyncPlanProvider
{
    private readonly MediaMonkeyExportTransactionPlan? _plan;
    public int Calls { get; private set; }
    public bool CancelOnCall { get; init; }
    public Exception? Failure { get; init; }
    public FakeProvider(MediaMonkeyExportTransactionPlan? plan) => _plan = plan;
    public Task<MediaMonkeyExportTransactionPlan> PrepareAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (CancelOnCall) throw new OperationCanceledException(cancellationToken);
        if (Failure is not null) throw Failure;
        return Task.FromResult(_plan!);
    }
}
