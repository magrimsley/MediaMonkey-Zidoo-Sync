using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MediaMonkeyZidooSync.Services;

// Infrastructure-only bounded retry for Windows sharing/lock violations.
// All other IO, integrity, and authorization failures propagate unchanged.
internal static class RecoveryFileReader
{
    internal static string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
                return reader.ReadToEnd();
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && attempt < 4)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }
}
