using System;
using System.Reflection;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Reads the product identity from build-generated assembly metadata.</summary>
public static class ApplicationVersionProvider
{
    public static string ProductVersion => ReadProductVersion(typeof(ApplicationVersionProvider).Assembly);

    public static string ReadProductVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Assembly informational version is missing.");
        // SDK may append source revision metadata; this is not part of the user-facing product version.
        return version.Split('+', 2)[0];
    }
}
