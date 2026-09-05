using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class AuditLogService
    {
        private readonly ILogger<AuditLogService> _logger;
        private readonly object _sync = new();

        public AuditLogService(ILogger<AuditLogService> logger)
        {
            _logger = logger;
        }

        private string LogPath
        {
            get
            {
                var folder = Plugin.Instance?.DataFolderPath
                    ?? Path.Combine(Path.GetTempPath(), "jellyvote");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "audit.log");
            }
        }

        public void Write(string action, string details)
        {
            var line = $"{DateTime.UtcNow:O}\t{action}\t{details}";
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write JellyVote audit log");
            }

            _logger.LogInformation("JellyVote audit: {Action} {Details}", action, details);
        }
    }
}
