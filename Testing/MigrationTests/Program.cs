using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

public static class TestEnvironment {
    public static string NewLine => System.Environment.NewLine;
    public enum SpecialFolder { LocalApplicationData, CommonApplicationData }
    public static string Base = "";
    public static string GetFolderPath(SpecialFolder folder) => Path.Combine(Base, folder.ToString());
}
internal static class Program {
    static string Root => Path.Combine(TestEnvironment.GetFolderPath(TestEnvironment.SpecialFolder.LocalApplicationData), "MediaMonkey Zidoo Sync");
    static string Legacy => Path.Combine(TestEnvironment.GetFolderPath(TestEnvironment.SpecialFolder.CommonApplicationData), "MediaMonkey Zidoo Sync", "Configuration", "Current", "MediaMonkey-Zidoo-Config-v1.0.0.json");
    static string Current => Path.Combine(Root,"Configuration","Current",Path.GetFileName(Legacy));
    static string Valid() => JsonSerializer.Serialize(new AppConfig {
        Application=new(){ReportRoot=Path.Combine(TestEnvironment.Base,"reports")},
        Zidoo=new(){IPAddress="192.0.2.1",ApiPort=80,ExpectedModel="TEST",MacAddress="001122334455",BroadcastAddress="192.0.2.255",WolPort=9,PlaylistGridColumns=4},
        NAS=new(){Host="offline.invalid",ShareName="test",MusicFolderName="music",PlaylistFolderName="lists",MusicRootUNC=@"\\offline.invalid\test\music",PlaylistFolderUNC=@"\\offline.invalid\test\lists"},
        MediaMonkey=new(){DatabasePath=Path.Combine(TestEnvironment.Base,"nonexistent.db"),PlaylistParent="TEST"},
        Timing=new(){ZidooReadinessTimeoutSeconds=1,ColdBootUiStabilizationSeconds=0}});
    static void Write(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);}
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Deny(string path,bool directory,Action action){
        var sid=WindowsIdentity.GetCurrent().User!;
        if(directory){var info=new DirectoryInfo(path);var old=info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);var acl=info.GetAccessControl();acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.Write,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));info.SetAccessControl(acl);try{action();}finally{var restore=new DirectorySecurity();restore.SetSecurityDescriptorSddlForm(old,AccessControlSections.Access);info.SetAccessControl(restore);}}
        else{var info=new FileInfo(path);var old=info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);var acl=info.GetAccessControl();acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.ReadData,AccessControlType.Deny));info.SetAccessControl(acl);try{action();}finally{var restore=new FileSecurity();restore.SetSecurityDescriptorSddlForm(old,AccessControlSections.Access);info.SetAccessControl(restore);}}
    }
    static async Task<int> Main(string[] args){
        if(args[0]=="--worker") { TestEnvironment.Base=args[1];while(!File.Exists(args[2]))await Task.Delay(10);var workerService=new ConfigurationService();await workerService.LoadAsync(args[3]);return 0; }
        var results=new List<object>(); var baseRoot=args[0];Directory.CreateDirectory(baseRoot);
        async Task Test(string name,Func<Task> run){TestEnvironment.Base=Path.Combine(baseRoot,name);Directory.CreateDirectory(TestEnvironment.Base);DiagnosticReportingService.Messages.Clear();try{await run();results.Add(new{Name=name,Status="PASS"});Console.WriteLine("PASS "+name);}catch(Exception ex){results.Add(new{Name=name,Status="FAIL",Detail=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}}
        Task Done()=>Task.CompletedTask;
        await Test("existing-user-precedence",()=>{Write(Legacy,Valid());Write(Current,"existing-user-bytes");new ConfigurationService();Assert(File.ReadAllText(Current)=="existing-user-bytes","User config overwritten");return Done();});
        await Test("valid-legacy-migration",async()=>{var json=Valid();Write(Legacy,json);var service=new ConfigurationService();Assert(File.ReadAllText(Current)==json,"Migration not byte preserving");await service.LoadAsync("missing-seed");Assert(File.ReadAllText(Legacy)==json,"Legacy changed");});
        foreach(var invalid in new[]{"{broken", "null", "{}"}) await Test("invalid-legacy-"+results.Count,()=>{Write(Legacy,invalid);new ConfigurationService();Assert(!File.Exists(Current),"Invalid config imported");Assert(File.ReadAllText(Legacy)==invalid,"Legacy changed");Assert(DiagnosticReportingService.Messages.Count>0,"No diagnostic");return Done();});
        await Test("legacy-read-permission-denied",()=>{var json=Valid();Write(Legacy,json);Deny(Legacy,false,()=>{new ConfigurationService();Assert(!File.Exists(Current),"Unreadable legacy imported");Assert(DiagnosticReportingService.Messages.Any(x=>x is UnauthorizedAccessException),"No permission diagnostic");});Assert(File.ReadAllText(Legacy)==json,"Legacy changed");return Done();});
        await Test("destination-write-permission-denied",()=>{Write(Legacy,Valid());new ConfigurationService(Root);Deny(Path.GetDirectoryName(Current)!,true,()=>{new ConfigurationService();Assert(!File.Exists(Current),"Unexpected write");Assert(DiagnosticReportingService.Messages.Any(x=>x is UnauthorizedAccessException),"No permission diagnostic");});return Done();});
        await Test("destination-directory-unavailable",()=>{Write(Root,"blocked");try{new ConfigurationService();throw new Exception("Expected failure");}catch(IOException){}return Done();});
        await Test("explicit-root-skips-legacy",()=>{Write(Legacy,Valid());new ConfigurationService(Root);Assert(!File.Exists(Current),"Explicit root migrated legacy");return Done();});
        await Test("concurrent-first-run-and-load",async()=>{for(int round=0;round<40;round++){TestEnvironment.Base=Path.Combine(baseRoot,"concurrent-first-run-and-load",round.ToString());Write(Legacy,Valid());var expected=File.ReadAllText(Legacy);using var start=new ManualResetEventSlim(false);var tasks=Enumerable.Range(0,16).Select(_=>Task.Run(async()=>{start.Wait();var s=new ConfigurationService();await s.LoadAsync(Legacy);})).ToArray();start.Set();await Task.WhenAll(tasks);Assert(File.ReadAllText(Current)==expected,"Concurrent import changed bytes");Assert(File.ReadAllText(Legacy)==expected,"Concurrent import changed legacy");}});
        await Test("concurrent-seed-without-legacy",async()=>{var seed=Path.Combine(TestEnvironment.Base,"seed.json");Write(seed,Valid());using var start=new ManualResetEventSlim(false);var tasks=Enumerable.Range(0,16).Select(_=>Task.Run(async()=>{start.Wait();await new ConfigurationService().LoadAsync(seed);})).ToArray();start.Set();await Task.WhenAll(tasks);Assert(File.ReadAllText(Current)==File.ReadAllText(seed),"Concurrent seed changed bytes");});
        await Test("concurrent-separate-processes",async()=>{for(int round=0;round<8;round++){TestEnvironment.Base=Path.Combine(baseRoot,"concurrent-separate-processes",round.ToString());Write(Legacy,Valid());var barrier=Path.Combine(TestEnvironment.Base,"start");var workers=new List<System.Diagnostics.Process>();try{for(int i=0;i<8;i++){var psi=new System.Diagnostics.ProcessStartInfo(System.Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"--worker",TestEnvironment.Base,barrier,Legacy})psi.ArgumentList.Add(arg);workers.Add(System.Diagnostics.Process.Start(psi)!);}File.WriteAllText(barrier,"");foreach(var worker in workers){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await worker.WaitForExitAsync(timeout.Token);Assert(worker.ExitCode==0,"Child startup failed");}Assert(File.ReadAllText(Current)==File.ReadAllText(Legacy),"Cross-process import changed bytes");Assert(!Directory.GetFiles(Path.GetDirectoryName(Current)!,"*.initializing").Any(),"Staging debris remains");}finally{foreach(var worker in workers){if(!worker.HasExited)worker.Kill();worker.Dispose();}}}});
        await Test("transient-current-sharing-lock",async()=>{Write(Current,Valid());var expected=File.ReadAllText(Current);var svc=new ConfigurationService();var held=new FileStream(Current,FileMode.Open,FileAccess.Read,FileShare.None);var loading=svc.LoadAsync("missing-seed");Assert(!loading.IsCompleted,"No retry under lock");await Task.Delay(150);held.Dispose();await loading;Assert(File.ReadAllText(Current)==expected,"Lock retry modified bytes");});
        await Test("persistent-current-sharing-lock",async()=>{Write(Current,Valid());var svc=new ConfigurationService();using var held=new FileStream(Current,FileMode.Open,FileAccess.Read,FileShare.None);try{await svc.LoadAsync("missing-seed");throw new Exception("Persistent lock unexpectedly loaded");}catch(IOException ex)when((ex.HResult & 0xffff) is 32 or 33){}});
        var failed=results.Count(x=>JsonSerializer.Serialize(x).Contains("\"FAIL\""));
        File.WriteAllText(Path.Combine(baseRoot,"summary.json"),JsonSerializer.Serialize(new{Scope="Runtime execution of checkpoint ConfigurationService source; only OS folder resolution and diagnostics redirected to test fixtures. Not installed-binary/profile validation.",Failed=failed,Checks=results},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
    }
}
namespace MediaMonkeyZidooSync.Services {
 public sealed class DiagnosticReportingService {
  public static DiagnosticReportingService Shared {get;}=new();
  public static List<Exception> Messages {get;}=new();
  public void Report(string operation,Exception exception,string severity="Error"){lock(Messages)Messages.Add(exception);}
 }
}



