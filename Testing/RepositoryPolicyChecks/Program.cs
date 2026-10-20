using System.Diagnostics;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
string git = args.Length > 1 ? args[1] : "git";
var start = new ProcessStartInfo(git) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add("ls-files"); start.ArgumentList.Add("-z");
using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not start.");
string tracked = process.StandardOutput.ReadToEnd();
string error = process.StandardError.ReadToEnd();
process.WaitForExit();
if (process.ExitCode != 0) throw new InvalidOperationException("Git inventory failed: " + error);
string[] paths = tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries);
var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ ".ps1", ".psm1", ".psd1", ".ps1xml", ".pssc", ".psrc", ".zip", ".7z", ".tar", ".tgz", ".gz", ".msi" };
var failures = paths.Where(p => forbidden.Contains(Path.GetExtension(p)) || p.StartsWith("LocalOnly/", StringComparison.OrdinalIgnoreCase)).ToList();
foreach (string path in paths.Where(p => p.StartsWith(".github/workflows/") && (p.EndsWith(".yml") || p.EndsWith(".yaml"))))
{
    string yaml = File.ReadAllText(Path.Combine(root, path));
    if (Regex.IsMatch(yaml, @"shell:\s*(powershell|pwsh)\b|\.ps(m|d)?1\b", RegexOptions.IgnoreCase))
        failures.Add(path + ": workflow depends on PowerShell");
}
if (failures.Count != 0)
{
    Console.Error.WriteLine("FAIL: local-only scripts/archive content found in Git source:");
    foreach (string path in failures) Console.Error.WriteLine(path);
    return 1;
}
Console.WriteLine($"PASS: {paths.Length} tracked paths; no PowerShell files, package archives or PowerShell workflow dependencies.");
return 0;

