using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVote.Models;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class NotificationService
    {
        private readonly VoteStore _store;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            VoteStore store,
            ISessionManager sessionManager,
            ILogger<NotificationService> logger)
        {
            _store = store;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        public async Task NotifyUsersAsync(
            IEnumerable<Guid> userIds,
            Guid voteId,
            string title,
            string message)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            await _store.UpdateAsync(doc =>
            {
                foreach (var userId in ids)
                {
                    doc.Alerts.Add(new UserAlert
                    {
                        UserId = userId,
                        VoteId = voteId,
                        Title = title,
                        Message = message
                    });
                }

                // Cap alert history
                if (doc.Alerts.Count > 2000)
                {
                    doc.Alerts = doc.Alerts
                        .OrderByDescending(a => a.CreatedAtUtc)
                        .Take(1500)
                        .ToList();
                }
            }).ConfigureAwait(false);

            try
            {
                foreach (var session in _sessionManager.Sessions)
                {
                    if (session.UserId == Guid.Empty || !ids.Contains(session.UserId))
                    {
                        continue;
                    }

                    await _sessionManager.SendMessageCommand(
                        session.Id,
                        session.Id,
                        new MessageCommand
                        {
                            Header = title,
                            Text = message,
                            TimeoutMs = 8000
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Session message broadcast failed (non-fatal)");
            }
        }
    }
}
