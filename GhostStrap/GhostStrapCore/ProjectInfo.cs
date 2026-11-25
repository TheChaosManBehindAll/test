// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap
{
    internal static class ProjectInfo
    {
        public const string Name = "GhostStrap";

        public static string Repository { get; } =
            Environment.GetEnvironmentVariable("GHOSTSTRAP_REPOSITORY") is { Length: > 0 } repository
                ? repository.Trim().TrimEnd('/').Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
                : "GhostStrap/GhostStrap";

        public const string RemoteDataUrl = "https://raw.githubusercontent.com/GhostStrap/config/refs/heads/main/Data.json";

        public const string BundleId = "com.ghoststrap.launcher";

        public const string RegistryKey = $@"Software\{Name}";
    }
}
