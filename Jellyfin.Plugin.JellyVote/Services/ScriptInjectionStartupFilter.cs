using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVote.Services
{
    public class ScriptInjectionStartupFilter : IStartupFilter
    {
        private readonly ILogger<ScriptInjectionStartupFilter> _logger;
        private int _loggedOnce;

        public ScriptInjectionStartupFilter(ILogger<ScriptInjectionStartupFilter> logger)
        {
            _logger = logger;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(InvokeAsync);
                next(app);
            };
        }

        private async Task InvokeAsync(HttpContext context, Func<Task> nextMw)
        {
            if (!IsIndexRequest(context.Request.Path.Value))
            {
                await nextMw().ConfigureAwait(false);
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method))
            {
                await nextMw().ConfigureAwait(false);
                return;
            }

            if (Plugin.Instance?.Configuration?.Enabled != true)
            {
                await nextMw().ConfigureAwait(false);
                return;
            }

            context.Request.Headers.Remove("Accept-Encoding");
            context.Request.Headers.Remove("Range");
            context.Request.Headers.Remove("If-Range");

            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await nextMw().ConfigureAwait(false);
            }
            catch
            {
                context.Response.Body = originalBody;
                throw;
            }

            context.Response.Body = originalBody;
            buffer.Seek(0, SeekOrigin.Begin);

            var isHtml = context.Response.StatusCode == 200
                && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

            if (!isHtml)
            {
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            string html;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            try
            {
                var plugin = Plugin.Instance;
                if (plugin != null)
                {
                    var alreadyInjected = html.IndexOf("/JellyVote/script", StringComparison.OrdinalIgnoreCase) >= 0;
                    var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (!alreadyInjected && bodyClose >= 0)
                    {
                        var tag = plugin.BuildScriptTag();
                        html = html.Substring(0, bodyClose) + tag + "\n" + html.Substring(bodyClose);
                        if (System.Threading.Interlocked.Exchange(ref _loggedOnce, 1) == 0)
                        {
                            _logger.LogInformation("JellyVote: injected client script via request-time middleware");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JellyVote script injection error; serving original HTML");
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html;charset=utf-8";
            context.Response.ContentLength = bytes.Length;
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers.Remove("Accept-Ranges");
            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }

        private static bool IsIndexRequest(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/web", StringComparison.OrdinalIgnoreCase);
        }
    }
}
