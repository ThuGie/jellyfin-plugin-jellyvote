using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyVote.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            Enabled = true;

            AllowMovies = true;
            AllowSeries = true;
            AllowSeasons = false;
            AllowEpisodes = false;
            AllowMusicAlbums = true;
            AllowMusicTracks = false;

            AnyoneCanPropose = true;
            AnyoneCanVote = true;
            RequireAdminToPropose = false;
            RequireNoteForOther = true;

            MinimumYesVotes = 2;
            VotingWindowHours = 72;
            ActiveWithinDays = 30;
            IncludeInactiveInPool = false;
            IncludeInactiveInNotify = false;
            EarlyResolveOnQuorum = true;
            EnableUnanimousPassAtWindowEnd = true;
            UnanimousPassMinParticipationPercent = 50;
            CooldownHoursAfterReject = 168;
            OneOpenVotePerItem = true;

            DeleteFilesFromDisk = false;
            RequireAdminApprovalForDiskDelete = true;
            DryRunMode = false;
            BlockDeleteWhilePlaying = true;

            NotifyOnPropose = true;
            NotifyOnEachVote = true;
            NotifyOnResolve = true;
            AlwaysNotifyAdmins = true;

            AdminsCanForcePass = true;
            AdminsCanForceReject = true;
            AdminsCanCancel = true;
            ExcludedUserIds = string.Empty;

            DeleteReasons = string.Join('\n', DefaultDeleteReasons);
            KeepReasons = string.Join('\n', DefaultKeepReasons);
        }

        public bool Enabled { get; set; }

        public bool AllowMovies { get; set; }
        public bool AllowSeries { get; set; }
        public bool AllowSeasons { get; set; }
        public bool AllowEpisodes { get; set; }
        public bool AllowMusicAlbums { get; set; }
        public bool AllowMusicTracks { get; set; }

        public bool AnyoneCanPropose { get; set; }
        public bool AnyoneCanVote { get; set; }
        public bool RequireAdminToPropose { get; set; }
        public bool RequireNoteForOther { get; set; }

        public int MinimumYesVotes { get; set; }
        public int VotingWindowHours { get; set; }
        public int ActiveWithinDays { get; set; }
        public bool IncludeInactiveInPool { get; set; }
        public bool IncludeInactiveInNotify { get; set; }
        public bool EarlyResolveOnQuorum { get; set; }
        public bool EnableUnanimousPassAtWindowEnd { get; set; }
        public int UnanimousPassMinParticipationPercent { get; set; }
        public int CooldownHoursAfterReject { get; set; }
        public bool OneOpenVotePerItem { get; set; }

        /// <summary>
        /// When true, delete media files from disk on a passed vote.
        /// When false, remove the item from the library only.
        /// </summary>
        public bool DeleteFilesFromDisk { get; set; }

        public bool RequireAdminApprovalForDiskDelete { get; set; }
        public bool DryRunMode { get; set; }
        public bool BlockDeleteWhilePlaying { get; set; }

        public bool NotifyOnPropose { get; set; }
        public bool NotifyOnEachVote { get; set; }
        public bool NotifyOnResolve { get; set; }
        public bool AlwaysNotifyAdmins { get; set; }

        public bool AdminsCanForcePass { get; set; }
        public bool AdminsCanForceReject { get; set; }
        public bool AdminsCanCancel { get; set; }

        /// <summary>Comma or newline separated Jellyfin user GUIDs excluded from proposing/voting.</summary>
        public string ExcludedUserIds { get; set; }

        public string DeleteReasons { get; set; }
        public string KeepReasons { get; set; }

        public static readonly string[] DefaultDeleteReasons =
        {
            "Bad quality / encoding issues",
            "Wrong file / wrong version",
            "Duplicate / superseded by better copy",
            "Old / unused",
            "Incomplete / corrupt",
            "Wrong metadata / mismatched title",
            "Personal preference / don't want it",
            "Other"
        };

        public static readonly string[] DefaultKeepReasons =
        {
            "Still want to watch",
            "Someone else wants it",
            "Waiting for better quality / remux",
            "Keep for collection completeness",
            "Currently watching / in progress",
            "Other"
        };

        public IReadOnlyList<string> GetDeleteReasonList()
        {
            return SplitLines(DeleteReasons);
        }

        public IReadOnlyList<string> GetKeepReasonList()
        {
            return SplitLines(KeepReasons);
        }

        public HashSet<Guid> GetExcludedUserIdSet()
        {
            var set = new HashSet<Guid>();
            if (string.IsNullOrWhiteSpace(ExcludedUserIds))
            {
                return set;
            }

            foreach (var part in ExcludedUserIds.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Guid.TryParse(part.Trim(), out var id))
                {
                    set.Add(id);
                }
            }

            return set;
        }

        private static List<string> SplitLines(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
