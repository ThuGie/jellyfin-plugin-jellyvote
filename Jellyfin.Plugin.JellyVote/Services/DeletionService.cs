using System;
using System.Linq;
using Jellyfin.Plugin.JellyVote.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class DeletionService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;
        private readonly AuditLogService _audit;
        private readonly ILogger<DeletionService> _logger;

        public DeletionService(
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            AuditLogService audit,
            ILogger<DeletionService> logger)
        {
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
            _audit = audit;
            _logger = logger;
        }

        public bool IsItemPlaying(Guid itemId)
        {
            try
            {
                return _sessionManager.Sessions.Any(s =>
                    s.NowPlayingItem != null && s.NowPlayingItem.Id == itemId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check playing sessions");
                return false;
            }
        }

        public (bool Success, string Message) TryDelete(DeletionVote vote, BaseItem item, bool deleteFilesFromDisk, bool dryRun)
        {
            var cfg = Plugin.Instance?.Configuration;
            if (cfg?.BlockDeleteWhilePlaying == true && IsItemPlaying(item.Id))
            {
                var msg = "Item is currently playing; deletion blocked.";
                _audit.Write("delete_blocked_playing", $"{vote.Id} {item.Name}");
                return (false, msg);
            }

            if (dryRun || cfg?.DryRunMode == true)
            {
                var dryMsg = deleteFilesFromDisk
                    ? $"[DRY-RUN] Would delete from library AND disk: {item.Name} ({item.Id})"
                    : $"[DRY-RUN] Would remove from library only: {item.Name} ({item.Id})";
                _audit.Write("delete_dry_run", dryMsg);
                return (true, dryMsg);
            }

            try
            {
                _libraryManager.DeleteItem(
                    item,
                    new DeleteOptions
                    {
                        DeleteFileLocation = deleteFilesFromDisk,
                        DeleteFromExternalProvider = false
                    },
                    true);

                var ok = deleteFilesFromDisk
                    ? $"Deleted from library and disk: {item.Name}"
                    : $"Removed from library only: {item.Name}";
                _audit.Write("delete_ok", $"{vote.Id} {ok}");
                return (true, ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deletion failed for {ItemId}", item.Id);
                _audit.Write("delete_error", $"{vote.Id} {item.Id} {ex.Message}");
                return (false, $"Deletion failed: {ex.Message}");
            }
        }
    }
}
