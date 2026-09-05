using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVote.Helpers;
using Jellyfin.Plugin.JellyVote.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using JUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class VoteWorkflowService
    {
        private readonly VoteStore _store;
        private readonly QuorumService _quorum;
        private readonly DeletionService _deletion;
        private readonly NotificationService _notifications;
        private readonly AuditLogService _audit;
        private readonly IUserManager _userManager;
        private readonly ILogger<VoteWorkflowService> _logger;

        public VoteWorkflowService(
            VoteStore store,
            QuorumService quorum,
            DeletionService deletion,
            NotificationService notifications,
            AuditLogService audit,
            IUserManager userManager,
            ILogger<VoteWorkflowService> logger)
        {
            _store = store;
            _quorum = quorum;
            _deletion = deletion;
            _notifications = notifications;
            _audit = audit;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<(bool Ok, string Message, DeletionVote? Vote)> ProposeAsync(
            JUser user,
            ProposeVoteRequest request,
            CancellationToken cancellationToken = default)
        {
            var cfg = _quorum.Config;
            if (!cfg.Enabled)
            {
                return (false, "JellyVote is disabled.", null);
            }

            if (!_quorum.CanPropose(user))
            {
                return (false, "You are not allowed to propose deletions.", null);
            }

            var item = _quorum.GetItem(request.ItemId);
            if (item == null)
            {
                return (false, "Item not found.", null);
            }

            if (!_quorum.IsItemTypeAllowed(item))
            {
                return (false, "This media type is not enabled for voting.", null);
            }

            if (!item.IsVisible(user))
            {
                return (false, "You cannot access this item.", null);
            }

            var reason = (request.DeleteReason ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(reason))
            {
                return (false, "A delete reason is required.", null);
            }

            var reasons = cfg.GetDeleteReasonList();
            if (reasons.Count > 0 && !reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            {
                return (false, "Invalid delete reason.", null);
            }

            if (cfg.RequireNoteForOther
                && reason.Equals("Other", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(request.DeleteNote))
            {
                return (false, "A note is required when choosing Other.", null);
            }

            var eligible = _quorum.GetEligibleVoters(item, forNotify: false);
            // Proposer counts as a Yes vote automatically
            var eligibleIds = eligible.Select(u => u.Id).Where(id => id != user.Id).ToList();
            // Include proposer in pool for required calc? Plan: eligible = users who can vote.
            // Proposer already voted Yes, so required uses full eligible including proposer.
            var poolForRequired = _quorum.GetEligibleVoters(item, forNotify: false);
            if (!poolForRequired.Any(u => u.Id == user.Id) && _quorum.CanVote(user))
            {
                poolForRequired.Add(user);
            }

            var required = _quorum.ComputeRequiredYes(poolForRequired.Count);
            if (poolForRequired.Count == 0)
            {
                return (false, "No eligible voters found for this item.", null);
            }

            DeletionVote? created = null;
            string? error = null;

            await _store.UpdateAsync(doc =>
            {
                if (cfg.OneOpenVotePerItem
                    && doc.Votes.Any(v => v.ItemId == item.Id && v.Status == VoteStatus.Open))
                {
                    error = "There is already an open vote for this item.";
                    return;
                }

                if (cfg.CooldownHoursAfterReject > 0)
                {
                    var cooldown = DateTime.UtcNow.AddHours(-cfg.CooldownHoursAfterReject);
                    var recentReject = doc.Votes
                        .Where(v => v.ItemId == item.Id && v.Status == VoteStatus.Rejected)
                        .OrderByDescending(v => v.ResolvedAtUtc)
                        .FirstOrDefault();
                    if (recentReject?.ResolvedAtUtc != null && recentReject.ResolvedAtUtc > cooldown)
                    {
                        error = $"This item is in cooldown until {recentReject.ResolvedAtUtc.Value.AddHours(cfg.CooldownHoursAfterReject):u} UTC.";
                        return;
                    }
                }

                var vote = new DeletionVote
                {
                    ItemId = item.Id,
                    ItemName = item.Name ?? item.Id.ToString(),
                    ItemType = item.GetType().Name,
                    ProposerUserId = user.Id,
                    ProposerName = user.Username,
                    DeleteReason = reason,
                    DeleteNote = request.DeleteNote,
                    Status = VoteStatus.Open,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(Math.Max(1, cfg.VotingWindowHours)),
                    EligibleUserIds = poolForRequired.Select(u => u.Id).ToList(),
                    RequiredYesVotes = required,
                    DeleteFilesFromDisk = cfg.DeleteFilesFromDisk,
                    DryRun = cfg.DryRunMode,
                    Ballots =
                    {
                        new BallotEntry
                        {
                            UserId = user.Id,
                            UserName = user.Username,
                            Choice = BallotChoice.Yes,
                            Note = request.DeleteNote,
                            VotedAtUtc = DateTime.UtcNow
                        }
                    }
                };

                doc.Votes.Add(vote);
                created = vote;
            }, cancellationToken).ConfigureAwait(false);

            if (error != null)
            {
                return (false, error, null);
            }

            if (created == null)
            {
                return (false, "Failed to create vote.", null);
            }

            _audit.Write("propose", $"{created.Id} item={item.Id} by={user.Username} reason={reason}");

            // Early resolve if single-user server meets quorum
            var early = await TryResolveVoteAsync(created.Id, forceWindowEnd: false, cancellationToken).ConfigureAwait(false);
            var finalVote = early ?? created;

            if (cfg.NotifyOnPropose && finalVote.Status == VoteStatus.Open)
            {
                var notifyUsers = _quorum.GetEligibleVoters(item, forNotify: true)
                    .Where(u => u.Id != user.Id)
                    .Select(u => u.Id)
                    .ToList();

                if (cfg.AlwaysNotifyAdmins)
                {
                    foreach (var admin in UserManagerCompat.GetUsers(_userManager).Where(_quorum.IsAdmin))
                    {
                        if (!notifyUsers.Contains(admin.Id) && admin.Id != user.Id)
                        {
                            notifyUsers.Add(admin.Id);
                        }
                    }
                }

                await _notifications.NotifyUsersAsync(
                    notifyUsers,
                    finalVote.Id,
                    "JellyVote: deletion proposed",
                    $"{user.Username} proposed deleting \"{item.Name}\" — reason: {reason}. Vote now.")
                    .ConfigureAwait(false);
            }

            return (true, "Vote created.", finalVote);
        }

        public async Task<(bool Ok, string Message, DeletionVote? Vote)> CastBallotAsync(
            JUser user,
            Guid voteId,
            CastBallotRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_quorum.CanVote(user))
            {
                return (false, "You are not allowed to vote.", null);
            }

            var cfg = _quorum.Config;
            DeletionVote? updated = null;
            string? error = null;

            await _store.UpdateAsync(doc =>
            {
                var vote = doc.Votes.FirstOrDefault(v => v.Id == voteId);
                if (vote == null)
                {
                    error = "Vote not found.";
                    return;
                }

                if (vote.Status != VoteStatus.Open)
                {
                    error = "This vote is no longer open.";
                    return;
                }

                if (DateTime.UtcNow > vote.ExpiresAtUtc)
                {
                    error = "This vote has expired.";
                    return;
                }

                if (!vote.EligibleUserIds.Contains(user.Id) && !_quorum.IsAdmin(user))
                {
                    error = "You are not eligible to vote on this item.";
                    return;
                }

                if (vote.Ballots.Any(b => b.UserId == user.Id))
                {
                    error = "You have already voted.";
                    return;
                }

                if (request.Choice == BallotChoice.No)
                {
                    var keep = (request.KeepReason ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(keep))
                    {
                        error = "A keep reason is required when voting No.";
                        return;
                    }

                    var keepReasons = cfg.GetKeepReasonList();
                    if (keepReasons.Count > 0 && !keepReasons.Contains(keep, StringComparer.OrdinalIgnoreCase))
                    {
                        error = "Invalid keep reason.";
                        return;
                    }

                    if (cfg.RequireNoteForOther
                        && keep.Equals("Other", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(request.Note))
                    {
                        error = "A note is required when choosing Other.";
                        return;
                    }
                }

                vote.Ballots.Add(new BallotEntry
                {
                    UserId = user.Id,
                    UserName = user.Username,
                    Choice = request.Choice,
                    KeepReason = request.Choice == BallotChoice.No ? request.KeepReason : null,
                    Note = request.Note,
                    VotedAtUtc = DateTime.UtcNow
                });

                updated = vote;
            }, cancellationToken).ConfigureAwait(false);

            if (error != null)
            {
                return (false, error, null);
            }

            if (updated == null)
            {
                return (false, "Failed to cast ballot.", null);
            }

            _audit.Write("ballot", $"{voteId} by={user.Username} choice={request.Choice}");

            if (cfg.NotifyOnEachVote)
            {
                var targets = new[] { updated.ProposerUserId }.Concat(
                    cfg.AlwaysNotifyAdmins
                        ? UserManagerCompat.GetUsers(_userManager).Where(_quorum.IsAdmin).Select(a => a.Id)
                        : Array.Empty<Guid>()).Distinct().Where(id => id != user.Id);

                await _notifications.NotifyUsersAsync(
                    targets,
                    updated.Id,
                    "JellyVote: new ballot",
                    $"{user.Username} voted {request.Choice} on \"{updated.ItemName}\".")
                    .ConfigureAwait(false);
            }

            var resolved = await TryResolveVoteAsync(voteId, forceWindowEnd: false, cancellationToken).ConfigureAwait(false);
            return (true, "Vote recorded.", resolved ?? updated);
        }

        public async Task<(bool Ok, string Message, DeletionVote? Vote)> AdminResolveAsync(
            JUser admin,
            Guid voteId,
            AdminResolveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_quorum.IsAdmin(admin))
            {
                return (false, "Admin only.", null);
            }

            var cfg = _quorum.Config;
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();

            if (action == "pass" && !cfg.AdminsCanForcePass)
            {
                return (false, "Force-pass is disabled.", null);
            }

            if (action == "reject" && !cfg.AdminsCanForceReject)
            {
                return (false, "Force-reject is disabled.", null);
            }

            if (action == "cancel" && !cfg.AdminsCanCancel)
            {
                return (false, "Cancel is disabled.", null);
            }

            if (action == "approve-disk")
            {
                return await FinalizePassedVoteAsync(voteId, admin, request.ApproveDiskDelete, request.Note, cancellationToken)
                    .ConfigureAwait(false);
            }

            VoteStatus? target = action switch
            {
                "pass" => VoteStatus.Passed,
                "reject" => VoteStatus.Rejected,
                "cancel" => VoteStatus.Cancelled,
                _ => null
            };

            if (target == null)
            {
                return (false, "Unknown action. Use pass, reject, cancel, or approve-disk.", null);
            }

            DeletionVote? vote = null;
            await _store.UpdateAsync(doc =>
            {
                vote = doc.Votes.FirstOrDefault(v => v.Id == voteId);
                if (vote == null)
                {
                    return;
                }

                if (vote.Status is not (VoteStatus.Open or VoteStatus.PendingAdmin or VoteStatus.PendingDiskApproval))
                {
                    return;
                }

                if (target == VoteStatus.Passed)
                {
                    // Handled via Finalize
                }
                else
                {
                    vote.Status = target.Value;
                    vote.ResolvedAtUtc = DateTime.UtcNow;
                    vote.ResolveNote = request.Note ?? $"Admin {action} by {admin.Username}";
                }
            }, cancellationToken).ConfigureAwait(false);

            if (vote == null)
            {
                return (false, "Vote not found.", null);
            }

            if (target == VoteStatus.Passed)
            {
                return await FinalizePassedVoteAsync(voteId, admin, approveDisk: true, request.Note, cancellationToken)
                    .ConfigureAwait(false);
            }

            _audit.Write("admin_" + action, $"{voteId} by={admin.Username}");

            if (cfg.NotifyOnResolve)
            {
                await _notifications.NotifyUsersAsync(
                    vote.EligibleUserIds,
                    vote.Id,
                    "JellyVote: vote closed",
                    $"\"{vote.ItemName}\" vote was {action}ed by admin {admin.Username}.")
                    .ConfigureAwait(false);
            }

            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            return (true, $"Vote {action}ed.", doc.Votes.FirstOrDefault(v => v.Id == voteId));
        }

        public async Task ProcessDueVotesAsync(CancellationToken cancellationToken = default)
        {
            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var openIds = doc.Votes
                .Where(v => v.Status == VoteStatus.Open)
                .Select(v => v.Id)
                .ToList();

            foreach (var id in openIds)
            {
                var vote = doc.Votes.First(v => v.Id == id);
                var windowEnded = DateTime.UtcNow >= vote.ExpiresAtUtc;
                await TryResolveVoteAsync(id, windowEnded, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<DeletionVote?> TryResolveVoteAsync(
            Guid voteId,
            bool forceWindowEnd,
            CancellationToken cancellationToken = default)
        {
            DeletionVote? snapshot = null;
            await _store.UpdateAsync(doc =>
            {
                snapshot = doc.Votes.FirstOrDefault(v => v.Id == voteId);
            }, cancellationToken).ConfigureAwait(false);

            if (snapshot == null || snapshot.Status != VoteStatus.Open)
            {
                return snapshot;
            }

            var windowEnded = forceWindowEnd || DateTime.UtcNow >= snapshot.ExpiresAtUtc;
            if (!_quorum.Config.EarlyResolveOnQuorum && !windowEnded)
            {
                return snapshot;
            }

            var decision = _quorum.Evaluate(snapshot, windowEnded);
            if (decision == null)
            {
                return snapshot;
            }

            if (decision == VoteStatus.Passed)
            {
                var result = await FinalizePassedVoteAsync(voteId, admin: null, approveDisk: false, note: null, cancellationToken)
                    .ConfigureAwait(false);
                return result.Vote;
            }

            await _store.UpdateAsync(doc =>
            {
                var vote = doc.Votes.FirstOrDefault(v => v.Id == voteId);
                if (vote == null || vote.Status != VoteStatus.Open)
                {
                    return;
                }

                vote.Status = decision.Value;
                vote.ResolvedAtUtc = DateTime.UtcNow;
                vote.ResolveNote = decision == VoteStatus.PendingAdmin
                    ? "No eligible voters; awaiting admin."
                    : windowEnded
                        ? "Voting window ended."
                        : "Resolved early by quorum rules.";
                snapshot = vote;
            }, cancellationToken).ConfigureAwait(false);

            _audit.Write("resolve", $"{voteId} status={decision}");

            if (_quorum.Config.NotifyOnResolve && snapshot != null)
            {
                await _notifications.NotifyUsersAsync(
                    snapshot.EligibleUserIds,
                    snapshot.Id,
                    "JellyVote: vote resolved",
                    $"\"{snapshot.ItemName}\" vote result: {decision}.")
                    .ConfigureAwait(false);
            }

            return snapshot;
        }

        private async Task<(bool Ok, string Message, DeletionVote? Vote)> FinalizePassedVoteAsync(
            Guid voteId,
            JUser? admin,
            bool approveDisk,
            string? note,
            CancellationToken cancellationToken)
        {
            var cfg = _quorum.Config;
            DeletionVote? vote = null;

            await _store.UpdateAsync(doc =>
            {
                vote = doc.Votes.FirstOrDefault(v => v.Id == voteId);
            }, cancellationToken).ConfigureAwait(false);

            if (vote == null)
            {
                return (false, "Vote not found.", null);
            }

            if (vote.Status is not (VoteStatus.Open or VoteStatus.PendingAdmin or VoteStatus.PendingDiskApproval or VoteStatus.Passed))
            {
                // allow PendingDiskApproval
            }

            var deleteDisk = vote.DeleteFilesFromDisk || cfg.DeleteFilesFromDisk;
            if (deleteDisk && cfg.RequireAdminApprovalForDiskDelete && !approveDisk)
            {
                await _store.UpdateAsync(doc =>
                {
                    var v = doc.Votes.First(x => x.Id == voteId);
                    v.Status = VoteStatus.PendingDiskApproval;
                    v.ResolveNote = note ?? "Passed; awaiting admin approval for disk delete.";
                    vote = v;
                }, cancellationToken).ConfigureAwait(false);

                _audit.Write("pending_disk", $"{voteId}");

                if (cfg.AlwaysNotifyAdmins)
                {
                    var admins = UserManagerCompat.GetUsers(_userManager).Where(_quorum.IsAdmin).Select(a => a.Id);
                    await _notifications.NotifyUsersAsync(
                        admins,
                        voteId,
                        "JellyVote: disk delete approval needed",
                        $"\"{vote!.ItemName}\" passed voting. Approve disk deletion in the Votes tab.")
                        .ConfigureAwait(false);
                }

                return (true, "Passed; awaiting admin disk-delete approval.", vote);
            }

            var item = _quorum.GetItem(vote.ItemId);
            if (item == null)
            {
                await _store.UpdateAsync(doc =>
                {
                    var v = doc.Votes.First(x => x.Id == voteId);
                    v.Status = VoteStatus.Passed;
                    v.ResolvedAtUtc = DateTime.UtcNow;
                    v.DeletionResult = "Item already missing from library.";
                    v.ResolveNote = note ?? v.ResolveNote;
                    vote = v;
                }, cancellationToken).ConfigureAwait(false);

                return (true, "Vote passed; item already gone.", vote);
            }

            var (success, message) = _deletion.TryDelete(vote, item, deleteDisk, vote.DryRun || cfg.DryRunMode);

            await _store.UpdateAsync(doc =>
            {
                var v = doc.Votes.First(x => x.Id == voteId);
                v.Status = success ? VoteStatus.Passed : VoteStatus.PendingAdmin;
                v.ResolvedAtUtc = DateTime.UtcNow;
                v.DeletionResult = message;
                v.ResolveNote = note ?? (admin != null ? $"Finalized by {admin.Username}" : "Quorum passed");
                vote = v;
            }, cancellationToken).ConfigureAwait(false);

            if (cfg.NotifyOnResolve)
            {
                await _notifications.NotifyUsersAsync(
                    vote!.EligibleUserIds,
                    vote.Id,
                    "JellyVote: deletion result",
                    $"\"{vote.ItemName}\": {message}")
                    .ConfigureAwait(false);
            }

            return (success, message, vote);
        }
    }
}
