using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVote.Models
{
    public enum VoteStatus
    {
        Open = 0,
        Passed = 1,
        Rejected = 2,
        Cancelled = 3,
        PendingAdmin = 4,
        PendingDiskApproval = 5
    }

    public enum BallotChoice
    {
        Yes = 0,
        No = 1
    }

    public class DeletionVote
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public Guid ProposerUserId { get; set; }
        public string ProposerName { get; set; } = string.Empty;
        public string DeleteReason { get; set; } = string.Empty;
        public string? DeleteNote { get; set; }
        public VoteStatus Status { get; set; } = VoteStatus.Open;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
        public string? ResolveNote { get; set; }
        public List<BallotEntry> Ballots { get; set; } = new();
        public List<Guid> EligibleUserIds { get; set; } = new();
        public int RequiredYesVotes { get; set; }
        public bool DeleteFilesFromDisk { get; set; }
        public bool DryRun { get; set; }
        public string? DeletionResult { get; set; }
    }

    public class BallotEntry
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public BallotChoice Choice { get; set; }
        public string? KeepReason { get; set; }
        public string? Note { get; set; }
        public DateTime VotedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class VoteStoreDocument
    {
        public List<DeletionVote> Votes { get; set; } = new();
        public List<UserAlert> Alerts { get; set; } = new();
    }

    public class UserAlert
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid VoteId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public bool Read { get; set; }
    }

    public class ProposeVoteRequest
    {
        public Guid ItemId { get; set; }
        public string DeleteReason { get; set; } = string.Empty;
        public string? DeleteNote { get; set; }
    }

    public class CastBallotRequest
    {
        public BallotChoice Choice { get; set; }
        public string? KeepReason { get; set; }
        public string? Note { get; set; }
    }

    public class AdminResolveRequest
    {
        public string Action { get; set; } = string.Empty;
        public string? Note { get; set; }
        public bool ApproveDiskDelete { get; set; }
    }
}
