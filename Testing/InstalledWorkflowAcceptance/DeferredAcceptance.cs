using System.IO;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

internal static partial class Program
{
    private static HashSet<string> DeferredManagedNames()
    {
        string path=Path.Combine(fixture.Root,"deferred-managed-playlists.json");
        var names=File.Exists(path)?JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(path))!:Array.Empty<string>();
        if(names.Any(name=>name!="Hootie & Smash Mouth")||names.Length>1)throw new InvalidDataException("Unreviewed deferred playlist exclusion.");
        return new HashSet<string>(names,StringComparer.Ordinal);
    }
    private static void ValidatePreparedFixture(string expectedHash)
    {
        if(fixture.CandidateSHA256!=expectedHash||Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash||
            fixture.Name!="MMZS Acceptance "+fixture.Nonce||!OwnedFolderName(fixture.Config.NAS.PlaylistFolderUNC)||
            !File.ReadAllText(Path.Combine(fixture.Config.NAS.PlaylistFolderUNC,".mmzs-acceptance-owner.json")).Contains(fixture.Nonce,StringComparison.Ordinal)||
            !Path.GetFullPath(fixture.Config.MediaMonkey.DatabasePath).StartsWith(Path.GetFullPath(fixture.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
            string.Equals(fixture.SourceFolder,fixture.Config.NAS.PlaylistFolderUNC,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prepared fixture ownership or payload differs.");
        _=DeferredManagedNames();
    }
    private static async Task PrepareDeferredAcceptance(string settingsPath,string results,string expectedHash)
    {
        fixture=Prepare(settingsPath,results,expectedHash,true);
        ValidatePreparedFixture(expectedHash);
        using var lease=new FileStream(Path.Combine(fixture.SourceFolder,".mmzs-production.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(!await Reachable(fixture.Config))throw new IOException("Prepare-only review requires the currently on device; no wake sent.");
        fixture=fixture with{Baseline=await Inventory(fixture.Config)};
        if(fixture.Baseline.Any(row=>row.Name=="Hootie & Smash Mouth"||row.Name==fixture.Name))throw new InvalidDataException("Device differs from explicit deferred state.");
        SetTracks(2);
        var runner=new SyncRunner(new NativeProductionSyncService());
        var review=await runner.PrepareReviewAsync(fixture.Config);
        if(review.Count!=1||review[0].FileName!=fixture.Name+".m3u"||review[0].ExpectedCount!=2||review[0].OriginalSHA256 is not null)
            throw new InvalidDataException("Prepared review is not confined to owned creation.");
        var validation=await new MediaMonkeyPlaylistExporter().ValidateAsync(fixture.Config);
        if(validation.PlaylistMap.Any(row=>row.M3UFile=="Hootie & Smash Mouth.m3u")||
            validation.PlaylistMap.Where(row=>row.M3UFile!=fixture.Name+".m3u").Any(row=>row.TrackCount>0&&fixture.Baseline.SingleOrDefault(item=>item.Name==Path.GetFileNameWithoutExtension(row.M3UFile))?.Count!=row.TrackCount))
            throw new InvalidDataException("Prepared managed inventory requires unrelated repair.");
        WriteEvidence(Path.Combine(fixture.Root,"fixture.json"),fixture);
        WriteEvidence(Path.Combine(fixture.Root,"prepared-review.json"),new{Status="PREPARED_ONLY",Review=review,ExistingDeviceRecords=fixture.Baseline.Length,
            DeferredRestoration="Hootie & Smash Mouth, twelve tracks; source database/M3U unchanged",PlaylistMutationCommands=0,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
        Console.WriteLine("Prepared isolated native acceptance; no device mutation. Recipe: "+Path.Combine(fixture.Root,"fixture.json"));
    }
}
