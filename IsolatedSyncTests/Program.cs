using MediaMonkeyZidooSync.Services;
using System.Threading;

var root = Path.Combine(Path.GetTempPath(), "MMZS-IsolatedSyncTests-" + Guid.NewGuid().ToString("N"));
int passed = 0;
void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
void MustThrow<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
var a = new IsolatedPlaylistSyncService.PlaylistChange("A.m3u", "#EXTM3U\nA");
var b = new IsolatedPlaylistSyncService.PlaylistChange("B.m3u", "#EXTM3U\nB");
try
{
    Test("Uninitialized sandbox rejects writes", () => MustThrow<InvalidOperationException>(() => new IsolatedPlaylistSyncService().Apply(root, new[] { a })));
    IsolatedPlaylistSyncService.InitializeSandbox(root);
    var service = new IsolatedPlaylistSyncService();
    Test("Create isolated playlist", () => { var r = service.Apply(root, new[] { a }); Check(r.Created == 1 && File.ReadAllText(Path.Combine(root, "A.m3u")) == a.Content, "Create failed"); });
    Test("Unchanged playlist is not rewritten", () => { var r = service.Apply(root, new[] { a }); Check(r.Unchanged == 1 && r.Updated == 0, "Unchanged failed"); });
    Test("Update isolated playlist", () => { var r = service.Apply(root, new[] { a with { Content = "updated" } }); Check(r.Updated == 1 && File.ReadAllText(Path.Combine(root, "A.m3u")) == "updated", "Update failed"); });
    Test("Reject traversal and absolute paths", () => { foreach (var n in new[] { "../escape.m3u", "..\\escape.m3u", "C:\\escape.m3u", "folder/a.m3u" }) MustThrow<ArgumentException>(() => service.Apply(root, new[] { a with { FileName = n } })); });
    Test("Reject duplicate filenames", () => MustThrow<ArgumentException>(() => service.Apply(root, new[] { a, a with { FileName = "a.M3U" } })));
    Test("Reject non-playlist extensions", () => MustThrow<ArgumentException>(() => service.Apply(root, new[] { a with { FileName = "bad.txt" } })));
    Test("Reject outside-temp targets", () => MustThrow<InvalidOperationException>(() => IsolatedPlaylistSyncService.InitializeSandbox(Path.GetPathRoot(root)!)));
    Test("Failure after publish rolls back update and create", () => {
        var before = File.ReadAllText(Path.Combine(root, "A.m3u"));
        int count = 0;
        var broken = new IsolatedPlaylistSyncService(s => { if (s == "published" && ++count == 2) throw new IOException("injected"); });
        MustThrow<IOException>(() => broken.Apply(root, new[] { a with { Content = "broken" }, b }));
        Check(File.ReadAllText(Path.Combine(root, "A.m3u")) == before && !File.Exists(Path.Combine(root, "B.m3u")), "Rollback failed");
    });
    Test("Rollback conflict preserves unrelated edit and transaction evidence", () => {
        var conflictRoot = Path.Combine(Path.GetTempPath(), "MMZS-RollbackConflict-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(conflictRoot);
            File.WriteAllText(Path.Combine(conflictRoot, "A.m3u"), "original");
            var broken = new IsolatedPlaylistSyncService(stage => {
                if (stage == "published") {
                    File.WriteAllText(Path.Combine(conflictRoot, "A.m3u"), "independent edit");
                    throw new IOException("injected failure after external edit");
                }
            });
            MustThrow<AggregateException>(() => broken.Apply(conflictRoot,
                new[] { a with { Content = "published content" } }));
            Check(File.ReadAllText(Path.Combine(conflictRoot, "A.m3u")) == "independent edit",
                "Rollback overwrote independent edit");
            Check(Directory.EnumerateDirectories(conflictRoot, ".mmzs-txn-*").Any(),
                "Rollback conflict discarded recovery evidence");
            MustThrow<InvalidDataException>(() => service.Apply(conflictRoot,
                Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>()));
        }
        finally { if (Directory.Exists(conflictRoot)) Directory.Delete(conflictRoot, true); }
    });
    Test("Cancellation before commit preserves files", () => {
        var before = File.ReadAllText(Path.Combine(root, "A.m3u"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        MustThrow<OperationCanceledException>(() => service.Apply(root, new[] { a with { Content = "canceled" } }, cts.Token));
        Check(File.ReadAllText(Path.Combine(root, "A.m3u")) == before, "Canceled write changed target");
    });
    Test("Reject oversized playlist content", () => MustThrow<ArgumentException>(() => service.Apply(root, new[] { a with { Content = new string('X', 8 * 1024 * 1024 + 1) } })));
    Test("Empty change set does not modify playlists", () => {
        var before = File.ReadAllText(Path.Combine(root, "A.m3u"));
        var outcome = service.Apply(root, Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>());
        Check(outcome.Created == 0 && outcome.Updated == 0 && outcome.Unchanged == 0, "Unexpected changes");
        Check(File.ReadAllText(Path.Combine(root, "A.m3u")) == before, "Empty batch modified playlist");
    });
    Test("Failure during staging leaves existing playlists unchanged", () => {
        var before = File.ReadAllText(Path.Combine(root, "A.m3u"));
        var broken = new IsolatedPlaylistSyncService(s => { if (s == "staged") throw new IOException("injected staging failure"); });
        MustThrow<IOException>(() => broken.Apply(root, new[] { a with { Content = "different" } }));
        Check(File.ReadAllText(Path.Combine(root, "A.m3u")) == before, "Staging failure modified playlist");
    });
    Test("Concurrent sandbox transaction is rejected without changes", () => {
        var before = File.ReadAllText(Path.Combine(root, "A.m3u"));
        using (var heldLock = new FileStream(Path.Combine(root, ".mmzs-transaction.lock"),
                   FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            MustThrow<IOException>(() => service.Apply(root, new[] { a with { Content = "should not write" } }));
        }
        Check(File.ReadAllText(Path.Combine(root, "A.m3u")) == before, "Concurrent attempt modified playlist");
    });
    Test("Restart recovery restores interrupted update and removes interrupted create", () => {
        var recoveryRoot = Path.Combine(Path.GetTempPath(), "MMZS-Restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(recoveryRoot);
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "original");
            var txn = Path.Combine(recoveryRoot, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(txn);
            File.Copy(Path.Combine(recoveryRoot, "A.m3u"), Path.Combine(txn, "0.bak"));
            File.WriteAllText(Path.Combine(txn, "journal.json"),
                "[{\"FileName\":\"A.m3u\",\"Existed\":true,\"PublishedSha256\":\"49F12235C6272D01D017477038084F0FE5C37B8069A0EC381D23AD3E5F47DB03\"},{\"FileName\":\"B.m3u\",\"Existed\":false,\"PublishedSha256\":\"4F5E24CEE8CD3976B3A34CB7F72A47948B6A4F2966EC4329F3F908667D151DC8\"}]");
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "partial update");
            File.WriteAllText(Path.Combine(recoveryRoot, "B.m3u"), "partial create");
            var outcome = service.Apply(recoveryRoot, Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>());
            Check(outcome.Created == 0 && File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "original",
                "Interrupted update not restored");
            Check(!File.Exists(Path.Combine(recoveryRoot, "B.m3u")) && !Directory.Exists(txn),
                "Interrupted create not removed");
        }
        finally { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); }
    });
    Test("Invalid restart journal fails closed without modifying playlists", () => {
        var recoveryRoot = Path.Combine(Path.GetTempPath(), "MMZS-InvalidJournal-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(recoveryRoot);
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "safe");
            var txn = Path.Combine(recoveryRoot, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(txn);
            File.WriteAllText(Path.Combine(txn, "journal.json"),
                "[{\"FileName\":\"../escape.m3u\",\"Existed\":false}]");
            MustThrow<InvalidDataException>(() => service.Apply(recoveryRoot, new[] { a with { Content = "unsafe" } }));
            Check(File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "safe" && Directory.Exists(txn),
                "Invalid journal should remain for inspection");
        }
        finally { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); }
    });
    Test("Recovery refuses unrelated edits and preserves evidence", () => {
        var recoveryRoot = Path.Combine(Path.GetTempPath(), "MMZS-ExternalEdit-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(recoveryRoot);
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "original");
            var txn = Path.Combine(recoveryRoot, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(txn);
            File.Copy(Path.Combine(recoveryRoot, "A.m3u"), Path.Combine(txn, "0.bak"));
            var publishedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("published")));
            File.WriteAllText(Path.Combine(txn, "journal.json"),
                "[{\"FileName\":\"A.m3u\",\"Existed\":true,\"PublishedSha256\":\"" + publishedHash + "\"}]");
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "external change");
            MustThrow<InvalidDataException>(() => service.Apply(recoveryRoot,
                Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>()));
            Check(File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "external change" &&
                  Directory.Exists(txn) && File.Exists(Path.Combine(txn, "0.bak")),
                "Recovery overwrote external change or discarded evidence");
        }
        finally { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); }
    });
    Test("Recovery rejects journal without published digest", () => {
        var recoveryRoot = Path.Combine(Path.GetTempPath(), "MMZS-NoDigest-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(recoveryRoot);
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "unchanged");
            var txn = Path.Combine(recoveryRoot, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(txn);
            File.Copy(Path.Combine(recoveryRoot, "A.m3u"), Path.Combine(txn, "0.bak"));
            File.WriteAllText(Path.Combine(txn, "journal.json"),
                "[{\"FileName\":\"A.m3u\",\"Existed\":true}]");
            MustThrow<InvalidDataException>(() => service.Apply(recoveryRoot,
                Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>()));
            Check(File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "unchanged" &&
                  Directory.Exists(txn), "Unverifiable journal was applied");
        }
        finally { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); }
    });
    Test("Recovery interruption preserves evidence and safely retries", () => {
        var recoveryRoot = Path.Combine(Path.GetTempPath(), "MMZS-Retry-" + Guid.NewGuid().ToString("N"));
        try
        {
            IsolatedPlaylistSyncService.InitializeSandbox(recoveryRoot);
            var txn = Path.Combine(recoveryRoot, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(txn);
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), "original");
            File.Copy(Path.Combine(recoveryRoot, "A.m3u"), Path.Combine(txn, "0.bak"));
            var published = "published";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(published)));
            File.WriteAllText(Path.Combine(txn, "journal.json"),
                "[{\"FileName\":\"A.m3u\",\"Existed\":true,\"PublishedSha256\":\"" + digest + "\"}]");
            File.WriteAllText(Path.Combine(recoveryRoot, "A.m3u"), published);
            var interrupted = new IsolatedPlaylistSyncService(stage => {
                if (stage == "recovery-after-restore") throw new IOException("injected recovery interruption");
            });
            MustThrow<IOException>(() => interrupted.Apply(recoveryRoot,
                Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>()));
            Check(File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "original" &&
                  Directory.Exists(txn) && File.Exists(Path.Combine(txn, "0.bak")),
                  "Interrupted recovery discarded backup or altered restored file");
            var result = service.Apply(recoveryRoot,
                Array.Empty<IsolatedPlaylistSyncService.PlaylistChange>());
            Check(result.Created == 0 && !Directory.Exists(txn) &&
                  File.ReadAllText(Path.Combine(recoveryRoot, "A.m3u")) == "original",
                  "Retry did not complete recovery");
        }
        finally { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); }
    });
    Test("Reject missing sandbox marker", () => {
        var unmarked = Path.Combine(Path.GetTempPath(), "MMZS-Unmarked-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(unmarked); MustThrow<InvalidOperationException>(() => service.Apply(unmarked, new[] { a })); }
        finally { Directory.Delete(unmarked, true); }
    });
    Console.WriteLine($"Passed: {passed}");
}
finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } }
