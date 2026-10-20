using System.IO;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Durable device and power ownership for a run, including crashes before its first playlist transaction.</summary>
public sealed record NativeProductionRunState(int Schema,string PlaylistRoot,string Evidence,
    string IPAddress,int ApiPort,string ExpectedModel,string MacAddress,bool InitiallyReachable)
{
    public static string Marker(string root)=>Path.Combine(root,".mmzs-production-run.json");
    public static NativeProductionRunState Read(AppConfig config,string root)
    {
        string path=Marker(root);NativeInterruptedRecovery.NoLink(path);
        var state=JsonSerializer.Deserialize<NativeProductionRunState>(File.ReadAllBytes(path))??throw new InvalidDataException("Interrupted run state missing.");
        string reports=Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.Application.ReportRoot)).TrimEnd(Path.DirectorySeparatorChar);
        if(state.Schema!=1 || !string.Equals(Path.GetFullPath(state.PlaylistRoot),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(state.Evidence)),reports,StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(state.Evidence).StartsWith("NativeSync-",StringComparison.Ordinal) ||
            state.IPAddress!=config.Zidoo.IPAddress || state.ApiPort!=config.Zidoo.ApiPort ||
            state.ExpectedModel!=config.Zidoo.ExpectedModel || state.MacAddress!=config.Zidoo.MacAddress)
            throw new InvalidDataException("Interrupted run belongs to different device settings or storage.");
        NativeInterruptedRecovery.NoLink(state.Evidence);return state;
    }
    public void Save(string root)
    {
        if(File.Exists(Marker(root)))throw new IOException("A previous run still requires recovery.");
        NativeInterruptedRecovery.Save(Marker(root),JsonSerializer.SerializeToUtf8Bytes(this));
    }
}
