using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Localization;

namespace TopSpeed.Server.Updates
{
    internal sealed class ServerUpdateService
    {
        private readonly ServerUpdateConfig _config;
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _jsonOptions;

        public ServerUpdateService(ServerUpdateConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TopSpeedServerUpdater/1.0");
            _http.Timeout = TimeSpan.FromSeconds(25);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
        }

        public async Task<ServerUpdateCheckResult> CheckAsync(ServerVersion currentVersion, CancellationToken cancellationToken)
        {
            try
            {
                var info = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);
                if (info == null)
                    return Fail(LocalizationService.Translate("The update info file could not be read."));

                var versionText = SelectManifestVersion(info);
                if (!ServerVersion.TryParse(versionText, out var remoteVersion))
                    return Fail(LocalizationService.Translate("The update info file has an invalid server version format."));

                if (remoteVersion.CompareTo(currentVersion) <= 0)
                {
                    return new ServerUpdateCheckResult
                    {
                        Outcome = ServerUpdateCheckOutcome.UpToDate
                    };
                }

                var release = await ReadLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                var expectedAsset = _config.BuildExpectedAssetName(versionText);
                var asset = FindAsset(release, expectedAsset);
                if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
                {
                    // The manifest is published from the repository the moment a version is
                    // bumped, but the release assets only appear once the build finishes.
                    // That gap is expected, so it is a retryable state rather than a failure.
                    return new ServerUpdateCheckResult
                    {
                        Outcome = ServerUpdateCheckOutcome.NotPublished,
                        VersionText = versionText
                    };
                }

                return new ServerUpdateCheckResult
                {
                    Outcome = ServerUpdateCheckOutcome.UpdateAvailable,
                    VersionText = versionText,
                    Update = new ServerUpdateInfo
                    {
                        VersionText = versionText,
                        Version = remoteVersion,
                        Changes = SelectManifestChanges(info),
                        DownloadUrl = asset.DownloadUrl ?? string.Empty,
                        AssetSizeBytes = asset.Size ?? 0
                    }
                };
            }
            catch (TaskCanceledException)
            {
                return Fail(LocalizationService.Translate("Update check timed out."));
            }
            catch (Exception ex)
            {
                return Fail(LocalizationService.Format("Update check failed: {0}", ex.Message));
            }
        }

        public async Task<ServerDownloadResult> DownloadAsync(
            ServerUpdateInfo update,
            string targetDirectory,
            Action<ServerDownloadProgress>? onProgress,
            CancellationToken cancellationToken)
        {
            if (update == null)
                throw new ArgumentNullException(nameof(update));
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("Target directory is required.", nameof(targetDirectory));

            var zipPath = Path.Combine(targetDirectory, _config.BuildExpectedAssetName(update.VersionText));

            try
            {
                using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new ServerDownloadResult
                    {
                        IsSuccess = false,
                        ErrorMessage = LocalizationService.Format("Download failed with status code {0}.", (int)response.StatusCode),
                        ZipPath = zipPath
                    };
                }

                var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
                var downloadedBytes = 0L;
                var lastPercent = -1;
                var buffer = new byte[81920];

                // Scoped so both handles are closed before the size check, which may
                // need to delete the file it just wrote.
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var file = new FileStream(
                        zipPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: buffer.Length,
                        useAsync: true);

                    while (true)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (bytesRead <= 0)
                            break;

                        await file.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        downloadedBytes += bytesRead;

                        var percent = 0;
                        if (totalBytes > 0)
                            percent = (int)Math.Floor((downloadedBytes * 100d) / totalBytes);
                        if (percent > 100)
                            percent = 100;

                        if (percent != lastPercent || downloadedBytes == totalBytes)
                        {
                            lastPercent = percent;
                            onProgress?.Invoke(new ServerDownloadProgress
                            {
                                DownloadedBytes = downloadedBytes,
                                TotalBytes = totalBytes,
                                Percent = percent
                            });
                        }
                    }
                }

                // A connection that drops mid-transfer ends the read loop without an
                // exception, so a short file is the signature of a truncated download.
                if (totalBytes > 0 && downloadedBytes != totalBytes)
                {
                    TryDeleteIncompleteDownload(zipPath);
                    return new ServerDownloadResult
                    {
                        IsSuccess = false,
                        ErrorMessage = LocalizationService.Format(
                            "The download is incomplete. Expected {0} bytes but received {1}.",
                            totalBytes,
                            downloadedBytes),
                        ZipPath = zipPath
                    };
                }

                onProgress?.Invoke(new ServerDownloadProgress
                {
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    Percent = 100
                });

                return new ServerDownloadResult
                {
                    IsSuccess = true,
                    ZipPath = zipPath,
                    TotalBytes = totalBytes
                };
            }
            catch (TaskCanceledException)
            {
                TryDeleteIncompleteDownload(zipPath);
                return new ServerDownloadResult
                {
                    IsSuccess = false,
                    ErrorMessage = LocalizationService.Translate("Download timed out or was canceled."),
                    ZipPath = zipPath
                };
            }
            catch (Exception ex)
            {
                TryDeleteIncompleteDownload(zipPath);
                return new ServerDownloadResult
                {
                    IsSuccess = false,
                    ErrorMessage = LocalizationService.Format("Download failed: {0}", ex.Message),
                    ZipPath = zipPath
                };
            }
        }

        private static void TryDeleteIncompleteDownload(string zipPath)
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch
            {
                // Leaving a stray zip behind is harmless; the next attempt overwrites it.
            }
        }

        private async Task<UpdateManifestDoc?> ReadManifestAsync(CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(_config.InfoUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<UpdateManifestDoc>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ReleaseDoc?> ReadLatestReleaseAsync(CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(_config.LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<ReleaseDoc>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        private static string SelectManifestVersion(UpdateManifestDoc info)
        {
            var serverVersion = (info.ServerVersion ?? string.Empty).Trim();
            if (serverVersion.Length > 0)
                return serverVersion;

            return (info.Version ?? string.Empty).Trim();
        }

        private static IReadOnlyList<string> SelectManifestChanges(UpdateManifestDoc info)
        {
            if (info.ServerChanges != null && info.ServerChanges.Count > 0)
                return info.ServerChanges;
            if (info.Changes != null && info.Changes.Count > 0)
                return info.Changes;
            return Array.Empty<string>();
        }

        private static ReleaseAssetDoc? FindAsset(ReleaseDoc? release, string expectedName)
        {
            if (release?.Assets == null || release.Assets.Count == 0)
                return null;

            for (var i = 0; i < release.Assets.Count; i++)
            {
                var asset = release.Assets[i];
                if (asset == null || string.IsNullOrWhiteSpace(asset.Name))
                    continue;

                var assetName = asset.Name.Trim();
                if (!string.Equals(assetName, expectedName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return asset;
            }

            return null;
        }

        private static ServerUpdateCheckResult Fail(string message)
        {
            return new ServerUpdateCheckResult
            {
                Outcome = ServerUpdateCheckOutcome.Failed,
                ErrorMessage = string.IsNullOrWhiteSpace(message)
                    ? LocalizationService.Translate("Unknown update error.")
                    : message
            };
        }
    }
}
