// Alpha.51: isolated, diagnostic-only immutable recovery-plan reports.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaMonkeyZidooSync.Services;

public sealed record RecoveryPlanReport(Guid TransactionId, RecoveryPlanDisposition Disposition,
    RecoveryAuthorizationDisposition Authorization, JournalSequenceFinding Finding,
    DateTimeOffset CreatedUtc, bool MayExecute);

public sealed class IsolatedRecoveryPlanReportStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false };

    public IsolatedRecoveryPlanReportStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root) || (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Report root must be an existing non-redirected sandbox directory.");
    }

    public string Append(RecoveryExecutionPlan plan, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var record = new RecoveryPlanReport(plan.TransactionId, plan.Disposition,
            plan.Authorization, plan.Finding, createdUtc, false);
        Validate(record);
        string payload = JsonSerializer.Serialize(record, JsonOptions);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string path = Path.Combine(_root, record.TransactionId.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".json");
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ReportEnvelope(payload, digest)));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return path;
    }

    public RecoveryPlanReport Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), _root, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(full), @"\A[0-9a-f]{32}-[0-9a-f]{32}\.json\z"))
            throw new InvalidDataException("Report path is outside the sandbox or malformed.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected report file rejected.");
        var envelope = JsonSerializer.Deserialize<ReportEnvelope>(RecoveryFileReader.ReadAllText(full), JsonOptions)
            ?? throw new InvalidDataException("Missing report envelope.");
        if (string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Sha256))
            throw new InvalidDataException("Missing report payload or digest.");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload)));
        if (!string.Equals(digest, envelope.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Report integrity mismatch.");
        var report = JsonSerializer.Deserialize<RecoveryPlanReport>(envelope.Payload, JsonOptions)
            ?? throw new InvalidDataException("Missing report record.");
        Validate(report);
        if (!Path.GetFileName(full).StartsWith(report.TransactionId.ToString("N") + "-", StringComparison.Ordinal))
            throw new InvalidDataException("Report filename transaction mismatch.");
        return report;
    }

    private static void Validate(RecoveryPlanReport report)
    {
        if (report.TransactionId == Guid.Empty || report.MayExecute || report.CreatedUtc == default ||
            !Enum.IsDefined(report.Disposition) || !Enum.IsDefined(report.Authorization) ||
            !Enum.IsDefined(report.Finding))
            throw new InvalidDataException("Invalid or executable recovery plan report.");
    }

    private sealed record ReportEnvelope(string Payload, string Sha256);
}
