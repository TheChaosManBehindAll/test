// SPDX-FileCopyrightText: 2026 GhostStrap contributors
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;

namespace GhostStrap;

/// <summary>
/// Downloads large macOS client archives in parallel when the server supports
/// byte ranges, then assembles them in order. Adapted from GhostStrap's existing
/// parallel downloader and integrated into the Froststrap deployment pipeline.
/// </summary>
internal static class ParallelFileDownloader
{
    private const long ParallelThreshold = 8 * 1024 * 1024;
    private const int BufferSize = 128 * 1024;

    public static async Task DownloadAsync(
        HttpClient client,
        string url,
        string destinationPath,
        int chunkCount,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        Directory.CreateDirectory(parent);
        var temporaryPath = destinationPath + ".download";
        chunkCount = Math.Clamp(chunkCount, 2, 8);

        try
        {
            long totalSize = await TryGetLengthAsync(client, url, cancellationToken);
            bool canUseRanges = totalSize >= ParallelThreshold &&
                await SupportsRangesAsync(client, url, cancellationToken);

            if (!canUseRanges)
            {
                await DownloadSingleAsync(client, url, temporaryPath, totalSize, progress, cancellationToken);
            }
            else
            {
                try
                {
                    await DownloadRangesAsync(client, url, temporaryPath, totalSize, chunkCount, progress, cancellationToken);
                }
                catch (RangeNotSupportedException)
                {
                    DeleteParts(temporaryPath, chunkCount);
                    await DownloadSingleAsync(client, url, temporaryPath, totalSize, progress, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (totalSize > 0 && new FileInfo(temporaryPath).Length != totalSize)
                throw new InvalidDataException("Downloaded file size does not match the server's Content-Length.");

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            DeleteParts(temporaryPath, chunkCount);
        }
    }

    private static async Task<long> TryGetLengthAsync(HttpClient client, string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength ?? -1 : -1;
        }
        catch (HttpRequestException) when (!token.IsCancellationRequested)
        {
            return -1;
        }
    }

    private static async Task<bool> SupportsRangesAsync(HttpClient client, string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode && response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase);
        }
        catch (HttpRequestException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task DownloadRangesAsync(
        HttpClient client, string url, string temporaryPath, long totalSize,
        int chunkCount, Action<long, long>? progress, CancellationToken token)
    {
        long chunkSize = (totalSize + chunkCount - 1) / chunkCount;
        var ranges = Enumerable.Range(0, chunkCount)
            .Select(index => (Start: index * chunkSize, End: Math.Min(totalSize - 1, (index + 1) * chunkSize - 1)))
            .Where(range => range.Start < totalSize)
            .ToArray();
        long done = 0;

        var tasks = ranges.Select(async (range, index) =>
        {
            var partPath = $"{temporaryPath}.part{index}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(range.Start, range.End);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || contentRange is null ||
                !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                contentRange.From != range.Start || contentRange.To != range.End || contentRange.Length != totalSize)
                throw new RangeNotSupportedException();

            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            long chunkBytes = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                chunkBytes += read;
                if (chunkBytes > range.End - range.Start + 1)
                    throw new RangeNotSupportedException();
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                Interlocked.Add(ref done, read);
                progress?.Invoke(read, totalSize);
            }

            if (chunkBytes != range.End - range.Start + 1)
                throw new RangeNotSupportedException();
        });

        await Task.WhenAll(tasks);

        await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        for (var index = 0; index < ranges.Length; index++)
        {
            var partPath = $"{temporaryPath}.part{index}";
            await using var part = File.OpenRead(partPath);
            await part.CopyToAsync(output, token);
        }
        await output.FlushAsync(token);
    }

    private static async Task DownloadSingleAsync(
        HttpClient client, string url, string temporaryPath, long expectedSize,
        Action<long, long>? progress, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? expectedSize;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        var buffer = new byte[BufferSize];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            progress?.Invoke(read, total);
        }
        await target.FlushAsync(token);
    }

    private static void DeleteParts(string path, int count)
    {
        for (var index = 0; index < count; index++)
            TryDelete($"{path}.part{index}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    private sealed class RangeNotSupportedException : Exception { }
}
