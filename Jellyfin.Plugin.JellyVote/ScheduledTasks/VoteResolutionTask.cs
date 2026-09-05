using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVote.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.ScheduledTasks
{
    public class VoteResolutionTask : IScheduledTask
    {
        private readonly VoteWorkflowService _workflow;
        private readonly ILogger<VoteResolutionTask> _logger;

        public VoteResolutionTask(VoteWorkflowService workflow, ILogger<VoteResolutionTask> logger)
        {
            _workflow = workflow;
            _logger = logger;
        }

        public string Name => "JellyVote resolution";

        public string Key => "JellyVoteResolution";

        public string Description => "Resolves expired or quorum-ready JellyVote deletion votes.";

        public string Category => "Maintenance";

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            progress.Report(0);
            try
            {
                await _workflow.ProcessDueVotesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyVote resolution task failed");
            }

            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromMinutes(15).Ticks
                }
            };
        }
    }
}
