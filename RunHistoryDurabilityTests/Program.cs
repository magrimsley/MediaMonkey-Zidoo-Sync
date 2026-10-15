using MediaMonkeyZidooSync.Services;
using MediaMonkeyZidooSync.Models;
using System.Text.Json;

int passed = 0;
string root = Path.Combine(Path.GetTempPath(), "MMZS-HistoryTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
var events = new[] { new LogEvent(DateTime.Now, LogLevel.Blue, "test") };
try {
    var svc = new RunHistoryService();
    var baseline = svc.Save(root, DateTime.Now, "baseline", "ok", TimeSpan.FromSeconds(1), events);
    Test("Committed record has log", () => Check(File.Exists(baseline.LogPath) && svc.List(root).Count == 1, "Missing baseline"));
    foreach (string stage in new[] { "log-staged", "log-published", "record-staged" }) {
        Test("Failure after " + stage + " preserves history", () => {
            var failure = new RunHistoryService(checkpoint: s => { if (s == stage) throw new IOException("injected " + stage); },
                diagnostic: (_, _) => { });
            try { failure.Save(root, DateTime.Now, "failed", "error", TimeSpan.Zero, events); throw new Exception("No exception"); }
            catch (IOException ex) { Check(ex.Message.Contains(stage), "Original failure was replaced"); }
            Check(svc.List(root).Count == 1, "Partial history published");
            Check(File.Exists(baseline.LogPath), "Prior history damaged");
            Check(!Directory.GetFiles(Path.Combine(root, "RunHistory"), "*.tmp").Any(), "Temp file left");
        });
    }
    Test("Malformed JSON is ignored", () => {
        File.WriteAllText(Path.Combine(root, "RunHistory", "bad.json"), "{invalid");
        Check(new RunHistoryService(diagnostic: (_, _) => { }).List(root).Count == 1, "Malformed record listed");
    });
    Test("Missing log is ignored", () => {
        var other = svc.Save(root, DateTime.Now, "missing", "ok", TimeSpan.Zero, events);
        File.Delete(other.LogPath);
        Check(svc.List(root).Count == 1, "Record with missing log listed");
    });
    Test("No existing history returns empty", () => Check(svc.List(Path.Combine(root, "absent")).Count == 0, "Unexpected entries"));
    Console.WriteLine($"Passed: {passed}");
} finally { try { Directory.Delete(root, true); } catch (IOException) { } }
