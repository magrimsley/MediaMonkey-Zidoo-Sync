using System.IO;
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
Test("Read-only runner cannot execute production synchronization", () =>
{
    var runner = new SyncRunner(Orchestrator(new FakeProvider(Plan())));
    Throws<InvalidOperationException>(() => runner.RunAsync(config).GetAwaiter().GetResult());
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
    Throws<InvalidOperationException>(() => new SyncRunner(Orchestrator(new FakeProvider(Plan())))
        .RunAsync(config).GetAwaiter().GetResult());
});
Test("Cancellation is yellow and diagnostics stay out of visible output", () =>
{
    var entry=UserMessages.Cancelled(new TaskCanceledException("private technical detail"));
    if(entry.Level!=LogLevel.Yellow || !entry.Message.Contains("cancelled") || entry.Message.Contains("Exception") ||
       entry.Message.Contains("private technical detail") || !string.IsNullOrEmpty(entry.DiagnosticDetails))throw new Exception("Cancellation presentation leaked diagnostics");
});
Test("Successful changed and no-change runs have green completion messages", () =>
{
    foreach(int count in new[]{0,1,3}){var entry=UserMessages.Completed(count);if(entry.Level!=LogLevel.Green || !entry.Message.Contains("completed successfully"))throw new Exception("Success is not green");}
});
Test("Timeout is a failure rather than a user cancellation", () =>
{
    var entry=UserMessages.Failure(new TaskCanceledException("HTTP stack detail"));
    if(entry.Level!=LogLevel.Red || !entry.Message.Contains("did not respond") || entry.Message.Contains("HTTP stack"))throw new Exception("Timeout misclassified");
});
Test("Unresolved recovery remains a failure even with nested cancellation", () =>
{
    var entry=UserMessages.Failure(new System.IO.IOException("pending journal technical detail",new OperationCanceledException()));
    if(entry.Level!=LogLevel.Red || !entry.Message.Contains("recovery files") || entry.Message.Contains("technical detail"))throw new Exception("Unsafe cancellation result");
});
Test("Diagnostic log preserves details while display hides stack traces", () =>
{
    Exception failure;try{throw new InvalidOperationException("test diagnostic marker");}catch(Exception e){failure=e;}
    var entry=UserMessages.Failure(failure);
    string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"MMZS-MessageTests-"+Guid.NewGuid().ToString("N"));
    var record=new RunHistoryService().Save(root,DateTime.Now,"test","Failed",TimeSpan.Zero,new[]{entry});
    if(!System.IO.File.ReadAllText(record.LogPath).Contains(failure.ToString()) || UserMessages.ForDisplay(failure.ToString()).Contains("InvalidOperationException"))throw new Exception("Diagnostic separation failed");
});
Test("Synchronization requires validation, exporter verification and review in order", () =>
{
    var flow=new SynchronizationWorkflow();
    if(!flow.CanValidate || flow.CanVerify || flow.CanReview || flow.CanRun)throw new Exception("Unsafe initial state");
    Throws<InvalidOperationException>(()=>flow.BeginRun());
    Throws<InvalidOperationException>(()=>flow.BeginVerification());
    Throws<InvalidOperationException>(()=>flow.AcceptReview());
    flow.BeginValidation();
    if(flow.CanValidate || flow.CanVerify || flow.CanReview || flow.CanRun)throw new Exception("Busy controls enabled");
    flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(true);
    if(!flow.CanReview || flow.CanRun)throw new Exception("Review skipped");
    flow.AcceptReview();flow.BeginRun();flow.EndRun();
    if(flow.CanVerify || flow.CanReview || flow.CanRun)throw new Exception("Run did not reset sequence");
});
Test("Failed checks and changed configuration cannot authorize synchronization", () =>
{
    var flow=new SynchronizationWorkflow();flow.BeginValidation();flow.EndValidation(false);
    if(flow.CanVerify)throw new Exception("Failed validation accepted");
    flow.BeginValidation();flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(false);
    if(flow.CanReview)throw new Exception("Failed verification accepted");
    flow.BeginVerification();flow.Invalidate();flow.EndVerification(true);
    if(flow.CanReview || flow.CanVerify)throw new Exception("Stale verification accepted");
    flow.BeginValidation();flow.Invalidate();flow.EndValidation(true);
    if(flow.CanVerify)throw new Exception("Stale validation accepted");
});
Test("No-change verification keeps Review and Run disabled", () =>
{
    var flow=new SynchronizationWorkflow();flow.BeginValidation();flow.EndValidation(true);
    flow.BeginVerification();flow.EndVerification(true, false);
    if(!flow.Verified || flow.CanReview || flow.CanRun || !flow.NextStep.Contains("Playlist files match MediaMonkey"))throw new Exception("No-change verification authorized work");
    bool refused=false;try{flow.AcceptReview();}catch(InvalidOperationException){refused=true;}
    if(!refused)throw new Exception("No-change review accepted");
    flow.BeginVerification();flow.EndVerification(true, true);
    if(!flow.CanReview)throw new Exception("Changed plan cannot be reviewed");
});
Test("Optional diagnostics cannot authorize synchronization", () =>
{
    var flow=new SynchronizationWorkflow();flow.SetDiagnosticBusy(true);flow.SetDiagnosticBusy(false);
    if(flow.CanRun || flow.CanReview || flow.CanVerify)throw new Exception("Diagnostics bypassed prerequisites");
});
Test("Missing managed repair excludes empty, existing and already planned playlists", () =>
{
    var missing=NativeProductionSyncService.MissingManagedPlaylists(new[]{("Missing.m3u",2),("Empty.m3u",0),("Existing.m3u",3),("Planned.m3u",1)},
        new[]{new ZidooPlaylistRecord("existing","Existing",3,0)},new[]{"Planned.m3u"});
    if(!missing.SequenceEqual(new[]{"Missing.m3u"}))throw new Exception("Unsafe repair selection");
});
Test("Verified rollback continues remaining transactions and records partial failure", () =>
{
    var changes=new[]{new NativeProductionTransaction.Change("one.m3u",null,0,null),new NativeProductionTransaction.Change("two.m3u",null,0,null)};
    int calls=0,restored=0;
    var failures=NativeProductionSyncService.ApplyTransactionsAsync(changes,(item,ct)=>
        {calls++;if(calls==1)throw new NativeTransactionRolledBackException("Verified rollback",new IOException());return Task.CompletedTask;},_=>{},_=>restored++,default).GetAwaiter().GetResult();
    if(calls!=2 || restored!=1 || failures.Count!=1)throw new Exception("Compensated batch semantics differ");
});
Test("Unverified recovery stops the batch before the next playlist", () =>
{
    var changes=new[]{new NativeProductionTransaction.Change("one.m3u",null,0,null),new NativeProductionTransaction.Change("two.m3u",null,0,null)};int calls=0;
    Throws<IOException>(()=>NativeProductionSyncService.ApplyTransactionsAsync(changes,(item,ct)=>{calls++;throw new IOException("Unresolved recovery");},_=>{},_=>{},default).GetAwaiter().GetResult());
    if(calls!=1)throw new Exception("Batch continued after unresolved recovery");
});
Test("User cancellation stops after compensation without attempting the next playlist", () =>
{
    using var cancellation=new CancellationTokenSource();int calls=0;
    var changes=new[]{new NativeProductionTransaction.Change("one.m3u",null,0,null),new NativeProductionTransaction.Change("two.m3u",null,0,null)};
    Throws<NativeTransactionRolledBackException>(()=>NativeProductionSyncService.ApplyTransactionsAsync(changes,(item,ct)=>
        {calls++;cancellation.Cancel();throw new NativeTransactionRolledBackException("Cancelled after compensation",new OperationCanceledException(ct));},_=>{},_=>{},cancellation.Token).GetAwaiter().GetResult());
    if(calls!=1)throw new Exception("Cancellation did not stop batch");
});
Test("Interrupted run power ownership is bound to device and configured storage", () =>
{
    string root=Path.Combine(Path.GetTempPath(),"MMZS-RunState-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    string reports=Path.Combine(root,"Reports");string evidence=Path.Combine(reports,"NativeSync-fixture");Directory.CreateDirectory(evidence);
    var settings=new AppConfig();settings.Application.ReportRoot=reports;settings.Zidoo.IPAddress="192.0.2.1";settings.Zidoo.ApiPort=9529;settings.Zidoo.ExpectedModel="TEST";settings.Zidoo.MacAddress="001122334455";
    new NativeProductionRunState(1,root,evidence,settings.Zidoo.IPAddress,settings.Zidoo.ApiPort,settings.Zidoo.ExpectedModel,settings.Zidoo.MacAddress,false).Save(root);
    if(NativeProductionRunState.Read(settings,root).InitiallyReachable)throw new Exception("Original off state lost");
    settings.Zidoo.IPAddress="192.0.2.2";Throws<InvalidDataException>(()=>NativeProductionRunState.Read(settings,root));
    settings.Zidoo.IPAddress="192.0.2.1";settings.Application.ReportRoot=Path.Combine(root,"OtherReports");Throws<InvalidDataException>(()=>NativeProductionRunState.Read(settings,root));
    if(!File.Exists(NativeProductionRunState.Marker(root)))throw new Exception("Conflicting state was discarded");
});
Test("User cancellation history has plain wording and no exception diagnostics", () =>
{
    var wrapped=new OperationCanceledException("Internal cancellation",new NativeTransactionRolledBackException("Restored",new TaskCanceledException("private detail")));
    var entry=UserMessages.Cancelled(wrapped);
    string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"MMZS-CancelMessages-"+Guid.NewGuid().ToString("N"));
    var record=new RunHistoryService().Save(root,DateTime.Now,"Synchronization","Cancelled",TimeSpan.Zero,new[]{entry});
    string log=System.IO.File.ReadAllText(record.LogPath);
    if(!log.Contains("User cancelled the synchronization.") || !log.Contains("current playlist was restored") ||
       log.Contains("Exception") || log.Contains("Diagnostic details") || log.Contains("private detail"))throw new Exception("Expected cancellation was written as an error");
});
Test("Only committed playlists emit completion when the next playlist is cancelled", () =>
{
    using var cancellation=new CancellationTokenSource();
    var changes=new[]{"First.m3u","Cancelled.m3u","NotStarted.m3u"}.Select(name=>new NativeProductionTransaction.Change(name,null,0,"hash")).ToArray();
    var started=new List<string>();var completed=new List<string>();
    Throws<NativeTransactionRolledBackException>(()=>NativeProductionSyncService.ApplyTransactionsAsync(changes,(item,ct)=>
    {
        if(item.FileName=="Cancelled.m3u"){cancellation.Cancel();throw new NativeTransactionRolledBackException("Restored",new OperationCanceledException(ct));}
        return Task.CompletedTask;
    },item=>started.Add(item.FileName),_=>{},cancellation.Token,item=>completed.Add(item.FileName)).GetAwaiter().GetResult());
    if(!started.SequenceEqual(new[]{"First.m3u","Cancelled.m3u"}) || !completed.SequenceEqual(new[]{"First.m3u"}))throw new Exception("Cancelled or unstarted playlist reported as completed");
});
Test("A compensated failure never emits completion for the failed playlist", () =>
{
    var changes=new[]{"Failed.m3u","Completed.m3u"}.Select(name=>new NativeProductionTransaction.Change(name,null,0,"hash")).ToArray();
    var completed=new List<string>();
    var failures=NativeProductionSyncService.ApplyTransactionsAsync(changes,(item,ct)=>
    {if(item.FileName=="Failed.m3u")throw new NativeTransactionRolledBackException("Restored",new IOException("true failure"));return Task.CompletedTask;},_=>{},_=>{},default,item=>completed.Add(item.FileName)).GetAwaiter().GetResult();
    if(failures.Count!=1 || !completed.SequenceEqual(new[]{"Completed.m3u"}))throw new Exception("Rollback was reported as completion");
});
Test("A cancellation request does not disguise an unrelated true failure", () =>
{
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();
    if(!SyncRunner.IsUserCancellation(new OperationCanceledException(),cancellation.Token) ||
       !SyncRunner.IsUserCancellation(new NativeTransactionRolledBackException("Restored",new TaskCanceledException()),cancellation.Token) ||
       SyncRunner.IsUserCancellation(new NativeTransactionRolledBackException("Restored",new IOException("real failure")),cancellation.Token) ||
       SyncRunner.IsUserCancellation(new IOException("Recovery failed",new OperationCanceledException()),cancellation.Token) ||
       SyncRunner.IsUserCancellation(new TaskCanceledException("timeout"),default))throw new Exception("Cancellation classification hid a true failure");
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
