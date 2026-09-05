using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVote.Models;
using Jellyfin.Plugin.JellyVote.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Controllers
{
    [ApiController]
    [Route("JellyVote")]
    public class JellyVoteController : ControllerBase
    {
        private readonly VoteStore _store;
        private readonly VoteWorkflowService _workflow;
        private readonly QuorumService _quorum;
        private readonly IUserManager _userManager;
        private readonly ILogger<JellyVoteController> _logger;

        public JellyVoteController(
            VoteStore store,
            VoteWorkflowService workflow,
            QuorumService quorum,
            IUserManager userManager,
            ILogger<JellyVoteController> logger)
        {
            _store = store;
            _workflow = workflow;
            _quorum = quorum;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("script")]
        [AllowAnonymous]
        public ActionResult GetScript()
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("jellyvote.js", StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                return NotFound();
            }

            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
            {
                return NotFound();
            }

            using var reader = new StreamReader(stream);
            var js = reader.ReadToEnd();
            return Content(js, "application/javascript");
        }

        [HttpGet("Configuration/configPage.css")]
        [AllowAnonymous]
        public ActionResult GetCss()
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("configPage.css", StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                return NotFound();
            }

            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
            {
                return NotFound();
            }

            using var reader = new StreamReader(stream);
            return Content(reader.ReadToEnd(), "text/css");
        }

        [HttpGet("public-config")]
        [Authorize]
        public ActionResult GetPublicConfig()
        {
            var cfg = Plugin.Instance?.Configuration;
            if (cfg == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                enabled = cfg.Enabled,
                allowMovies = cfg.AllowMovies,
                allowSeries = cfg.AllowSeries,
                allowSeasons = cfg.AllowSeasons,
                allowEpisodes = cfg.AllowEpisodes,
                allowMusicAlbums = cfg.AllowMusicAlbums,
                allowMusicTracks = cfg.AllowMusicTracks,
                requireNoteForOther = cfg.RequireNoteForOther,
                deleteReasons = cfg.GetDeleteReasonList(),
                keepReasons = cfg.GetKeepReasonList(),
                votingWindowHours = cfg.VotingWindowHours,
                minimumYesVotes = cfg.MinimumYesVotes,
                deleteFilesFromDisk = cfg.DeleteFilesFromDisk,
                pluginId = Plugin.PluginGuid
            });
        }

        [HttpGet("votes")]
        [Authorize]
        public async Task<ActionResult> ListVotes([FromQuery] string? status, CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var isAdmin = _quorum.IsAdmin(user);
            var votes = doc.Votes.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<VoteStatus>(status, true, out var parsed))
            {
                votes = votes.Where(v => v.Status == parsed);
            }

            if (!isAdmin)
            {
                votes = votes.Where(v =>
                    v.ProposerUserId == user.Id
                    || v.EligibleUserIds.Contains(user.Id)
                    || v.Ballots.Any(b => b.UserId == user.Id));
            }

            return Ok(votes.OrderByDescending(v => v.CreatedAtUtc).Select(Summarize));
        }

        [HttpGet("votes/{voteId:guid}")]
        [Authorize]
        public async Task<ActionResult> GetVote(Guid voteId, CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var vote = doc.Votes.FirstOrDefault(v => v.Id == voteId);
            if (vote == null)
            {
                return NotFound();
            }

            return Ok(Summarize(vote));
        }

        [HttpGet("item/{itemId:guid}")]
        [Authorize]
        public async Task<ActionResult> GetItemVoteState(Guid itemId, CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var item = _quorum.GetItem(itemId);
            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var open = doc.Votes.FirstOrDefault(v => v.ItemId == itemId && v.Status == VoteStatus.Open);

            return Ok(new
            {
                canPropose = item != null && _quorum.CanPropose(user) && _quorum.IsItemTypeAllowed(item) && item.IsVisible(user),
                canVote = _quorum.CanVote(user),
                openVote = open == null ? null : Summarize(open),
                hasVoted = open?.Ballots.Any(b => b.UserId == user.Id) == true
            });
        }

        [HttpPost("votes")]
        [Authorize]
        public async Task<ActionResult> Propose([FromBody] ProposeVoteRequest request, CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var (ok, message, vote) = await _workflow.ProposeAsync(user, request, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return BadRequest(new { message });
            }

            return Ok(new { message, vote = Summarize(vote!) });
        }

        [HttpPost("votes/{voteId:guid}/ballot")]
        [Authorize]
        public async Task<ActionResult> CastBallot(
            Guid voteId,
            [FromBody] CastBallotRequest request,
            CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var (ok, message, vote) = await _workflow.CastBallotAsync(user, voteId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!ok)
            {
                return BadRequest(new { message });
            }

            return Ok(new { message, vote = Summarize(vote!) });
        }

        [HttpPost("votes/{voteId:guid}/admin")]
        [Authorize]
        public async Task<ActionResult> AdminResolve(
            Guid voteId,
            [FromBody] AdminResolveRequest request,
            CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var (ok, message, vote) = await _workflow.AdminResolveAsync(user, voteId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!ok)
            {
                return BadRequest(new { message });
            }

            return Ok(new { message, vote = vote == null ? null : Summarize(vote) });
        }

        [HttpGet("alerts")]
        [Authorize]
        public async Task<ActionResult> GetAlerts(CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var doc = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var alerts = doc.Alerts
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(50)
                .ToList();

            return Ok(new
            {
                unread = alerts.Count(a => !a.Read),
                alerts
            });
        }

        [HttpPost("alerts/read")]
        [Authorize]
        public async Task<ActionResult> MarkAlertsRead([FromBody] MarkReadRequest? request, CancellationToken cancellationToken)
        {
            var user = GetCurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            await _store.UpdateAsync(doc =>
            {
                foreach (var alert in doc.Alerts.Where(a => a.UserId == user.Id))
                {
                    if (request?.AlertIds == null || request.AlertIds.Count == 0 || request.AlertIds.Contains(alert.Id))
                    {
                        alert.Read = true;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            return Ok(new { message = "ok" });
        }

        private Jellyfin.Database.Implementations.Entities.User? GetCurrentUser()
        {
            var id = GetUserId();
            if (id == Guid.Empty)
            {
                return null;
            }

            return _userManager.GetUserById(id);
        }

        private Guid GetUserId()
        {
            foreach (var claim in User.Claims)
            {
                if ((claim.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase)
                     || claim.Type.Equals(ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase)
                     || claim.Type.EndsWith("userId", StringComparison.OrdinalIgnoreCase))
                    && Guid.TryParse(claim.Value, out var id))
                {
                    return id;
                }
            }

            return Guid.Empty;
        }

        private static object Summarize(DeletionVote vote)
        {
            var yes = vote.Ballots.Count(b => b.Choice == BallotChoice.Yes);
            var no = vote.Ballots.Count(b => b.Choice == BallotChoice.No);
            return new
            {
                vote.Id,
                vote.ItemId,
                vote.ItemName,
                vote.ItemType,
                vote.ProposerUserId,
                vote.ProposerName,
                vote.DeleteReason,
                vote.DeleteNote,
                status = vote.Status.ToString(),
                vote.CreatedAtUtc,
                vote.ExpiresAtUtc,
                vote.ResolvedAtUtc,
                vote.ResolveNote,
                vote.RequiredYesVotes,
                vote.DeleteFilesFromDisk,
                vote.DryRun,
                vote.DeletionResult,
                yesVotes = yes,
                noVotes = no,
                ballotCount = vote.Ballots.Count,
                eligibleCount = vote.EligibleUserIds.Count,
                ballots = vote.Ballots.Select(b => new
                {
                    b.UserId,
                    b.UserName,
                    choice = b.Choice.ToString(),
                    b.KeepReason,
                    b.Note,
                    b.VotedAtUtc
                })
            };
        }

        public class MarkReadRequest
        {
            public List<Guid>? AlertIds { get; set; }
        }
    }
}
