// Alpha.53: diagnostic-only browser; never creates or modifies reports.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MediaMonkeyZidooSync.Services;

public sealed record RecoveryReportRow(DateTimeOffset CreatedUtc, Guid TransactionId,
    string Status, string Finding, string ReportPath, bool MayExecute = false);

public sealed class IsolatedRecoveryReportBrowserService
{
    public IReadOnlyList<RecoveryReportRow> List(string reportRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportRoot);
        string root = Path.GetFullPath(reportRoot);
        if (!Directory.Exists(root)) return Array.Empty<RecoveryReportRow>();
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected recovery report directory rejected.");
        var store = new IsolatedRecoveryPlanReportStore(root);
        var rows = new List<RecoveryReportRow>();
        // Enumerate only immediate files; malformed files are surfaced as review items.
        foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).Take(501))
        {
            if (rows.Count == 500) throw new InvalidDataException("More than 500 reports; narrow the report directory.");
            try
            {
                // A malformed filename is diagnostic evidence, not a reason to abort the whole list.
                // Do not relax the report store's strict sandbox/name validation.
                if (!Regex.IsMatch(Path.GetFileName(path), @"\A[0-9a-f]{32}-[0-9a-f]{32}\.json\z"))
                {
                    rows.Add(new RecoveryReportRow(default, Guid.Empty, "Manual review required",
                        "Invalid recovery report filename", path));
                    continue;
                }
                var report = store.Read(path); // Verifies SHA-256, identity and non-executable status.
                rows.Add(new RecoveryReportRow(report.CreatedUtc, report.TransactionId,
                    report.Disposition.ToString(), report.Finding.ToString(), path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
                rows.Add(new RecoveryReportRow(default, Guid.Empty, "Manual review required",
                    "Unreadable or invalid recovery report", path));
            }
        }
        return rows.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.ReportPath, StringComparer.Ordinal).ToArray();
    }
}
