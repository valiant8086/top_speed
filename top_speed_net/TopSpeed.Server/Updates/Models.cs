using System;
using System.Collections.Generic;

namespace TopSpeed.Server.Updates
{
    internal sealed class ServerUpdateInfo
    {
        public string VersionText { get; set; } = string.Empty;
        public ServerVersion Version { get; set; }
        public IReadOnlyList<string> Changes { get; set; } = Array.Empty<string>();
        public string DownloadUrl { get; set; } = string.Empty;
        public long AssetSizeBytes { get; set; }
    }

    internal enum ServerUpdateCheckOutcome
    {
        /// <summary>The check itself did not complete: network, manifest or parsing trouble.</summary>
        Failed,
        UpToDate,
        UpdateAvailable,

        /// <summary>
        /// The manifest advertises a newer version but its download is not in the release yet.
        /// Normal for a short while after a release is tagged, and also what a build that never
        /// finished looks like. Retryable, and deliberately not reported as an error.
        /// </summary>
        NotPublished
    }

    internal sealed class ServerUpdateCheckResult
    {
        public ServerUpdateCheckOutcome Outcome { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ServerUpdateInfo? Update { get; set; }

        /// <summary>Version named by the manifest. Set for both UpdateAvailable and NotPublished.</summary>
        public string VersionText { get; set; } = string.Empty;

        public bool IsSuccess => Outcome != ServerUpdateCheckOutcome.Failed;
    }

    internal sealed class ServerDownloadProgress
    {
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public int Percent { get; set; }
    }

    internal sealed class ServerDownloadResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ZipPath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
    }
}
