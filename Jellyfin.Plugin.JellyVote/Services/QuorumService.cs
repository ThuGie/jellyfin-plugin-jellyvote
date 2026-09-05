using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyVote.Configuration;
using Jellyfin.Plugin.JellyVote.Helpers;
using Jellyfin.Plugin.JellyVote.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;
using JUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class QuorumService
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<QuorumService> _logger;

        public QuorumService(
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<QuorumService> logger)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public bool IsItemTypeAllowed(BaseItem item)
        {
            var cfg = Config;
            return item switch
            {
                Movie => cfg.AllowMovies,
                Series => cfg.AllowSeries,
                Season => cfg.AllowSeasons,
                Episode => cfg.AllowEpisodes,
                MusicAlbum => cfg.AllowMusicAlbums,
                Audio => cfg.AllowMusicTracks,
                _ => false
            };
        }

        public bool IsUserExcluded(Guid userId)
        {
            return Config.GetExcludedUserIdSet().Contains(userId);
        }

        public bool IsAdmin(JUser user)
        {
            return user.HasPermission(PermissionKind.IsAdministrator);
        }

        public bool CanPropose(JUser user)
        {
            var cfg = Config;
            if (!cfg.Enabled || IsUserExcluded(user.Id))
            {
                return false;
            }

            if (cfg.RequireAdminToPropose && !IsAdmin(user))
            {
                return false;
            }

            return cfg.AnyoneCanPropose || IsAdmin(user);
        }

        public bool CanVote(JUser user)
        {
            var cfg = Config;
            if (!cfg.Enabled || IsUserExcluded(user.Id))
            {
                return false;
            }

            return cfg.AnyoneCanVote || IsAdmin(user);
        }

        public bool IsUserActive(JUser user)
        {
            var days = Math.Max(1, Config.ActiveWithinDays);
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var last = user.LastActivityDate ?? user.LastLoginDate;
            return last.HasValue && last.Value.ToUniversalTime() >= cutoff;
        }

        public List<JUser> GetEligibleVoters(BaseItem item, bool forNotify)
        {
            var cfg = Config;
            var includeInactive = forNotify ? cfg.IncludeInactiveInNotify : cfg.IncludeInactiveInPool;
            var excluded = cfg.GetExcludedUserIdSet();

            return UserManagerCompat.GetUsers(_userManager)
                .Where(u => !excluded.Contains(u.Id))
                .Where(u => CanVote(u))
                .Where(u => includeInactive || IsUserActive(u))
                .Where(u =>
                {
                    try
                    {
                        return item.IsVisible(u);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Visibility check failed for user {UserId}", u.Id);
                        return false;
                    }
                })
                .ToList();
        }

        public int ComputeRequiredYes(int eligibleCount)
        {
            var configured = Math.Max(1, Config.MinimumYesVotes);
            if (eligibleCount <= 0)
            {
                return configured;
            }

            return Math.Min(configured, eligibleCount);
        }

        public (int Yes, int No) Tally(DeletionVote vote)
        {
            var yes = vote.Ballots.Count(b => b.Choice == BallotChoice.Yes);
            var no = vote.Ballots.Count(b => b.Choice == BallotChoice.No);
            return (yes, no);
        }

        /// <summary>
        /// Returns Pass, Reject, PendingAdmin, or null if still open.
        /// </summary>
        public VoteStatus? Evaluate(DeletionVote vote, bool windowEnded)
        {
            var cfg = Config;
            var (yes, no) = Tally(vote);
            var eligibleCount = vote.EligibleUserIds.Count;
            var required = vote.RequiredYesVotes > 0
                ? vote.RequiredYesVotes
                : ComputeRequiredYes(eligibleCount);

            if (eligibleCount == 0)
            {
                return windowEnded ? VoteStatus.PendingAdmin : null;
            }

            if (yes >= required && yes > no)
            {
                return VoteStatus.Passed;
            }

            // Early reject if remaining voters cannot catch up to required yes with majority
            if (cfg.EarlyResolveOnQuorum && !windowEnded)
            {
                var remaining = eligibleCount - vote.Ballots.Count;
                if (yes + remaining < required || (yes + remaining <= no && remaining == 0))
                {
                    if (no > yes && vote.Ballots.Count >= required)
                    {
                        return VoteStatus.Rejected;
                    }
                }
            }

            if (!windowEnded)
            {
                return null;
            }

            // Window ended
            if (cfg.EnableUnanimousPassAtWindowEnd && yes > 0 && no == 0)
            {
                var participation = eligibleCount == 0
                    ? 0
                    : (int)Math.Round(100.0 * vote.Ballots.Count / eligibleCount);
                if (participation >= cfg.UnanimousPassMinParticipationPercent)
                {
                    return VoteStatus.Passed;
                }
            }

            return VoteStatus.Rejected;
        }

        public BaseItem? GetItem(Guid itemId)
        {
            return _libraryManager.GetItemById(itemId);
        }
    }
}
