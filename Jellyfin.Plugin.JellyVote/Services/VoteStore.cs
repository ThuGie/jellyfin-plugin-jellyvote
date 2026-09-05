using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.JellyVote.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class VoteStore
    {
        private readonly ILogger<VoteStore> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public VoteStore(ILogger<VoteStore> logger)
        {
            _logger = logger;
        }

        private string DataPath
        {
            get
            {
                var folder = Plugin.Instance?.DataFolderPath
                    ?? Path.Combine(Path.GetTempPath(), "jellyvote");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "votes.json");
            }
        }

        public async Task<VoteStoreDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<T> UpdateAsync<T>(Func<VoteStoreDocument, T> mutator, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var doc = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                var result = mutator(doc);
                await WriteUnlockedAsync(doc, cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task UpdateAsync(Action<VoteStoreDocument> mutator, CancellationToken cancellationToken = default)
        {
            await UpdateAsync(doc =>
            {
                mutator(doc);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<VoteStoreDocument> ReadUnlockedAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(DataPath))
                {
                    return new VoteStoreDocument();
                }

                await using var stream = File.OpenRead(DataPath);
                var doc = await JsonSerializer.DeserializeAsync<VoteStoreDocument>(stream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return doc ?? new VoteStoreDocument();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read JellyVote store; starting empty");
                return new VoteStoreDocument();
            }
        }

        private async Task WriteUnlockedAsync(VoteStoreDocument doc, CancellationToken cancellationToken)
        {
            var tmp = DataPath + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, doc, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Copy(tmp, DataPath, true);
            File.Delete(tmp);
        }
    }
}
