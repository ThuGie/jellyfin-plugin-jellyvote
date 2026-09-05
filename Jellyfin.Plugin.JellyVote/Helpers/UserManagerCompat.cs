using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Library;
using JUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.JellyVote.Helpers
{
    /// <summary>
    /// Jellyfin 10.11.9 removed IUserManager.Users and replaced it with GetUsers().
    /// This shim supports both ABIs so one plugin build works across 10.11.x.
    /// </summary>
    public static class UserManagerCompat
    {
        private static readonly object Sync = new();
        private static Func<IUserManager, IEnumerable<JUser>>? _resolver;

        public static IEnumerable<JUser> GetUsers(IUserManager userManager)
        {
            ArgumentNullException.ThrowIfNull(userManager);
            EnsureResolver(userManager);
            return _resolver!(userManager) ?? Array.Empty<JUser>();
        }

        private static void EnsureResolver(IUserManager sample)
        {
            if (_resolver != null)
            {
                return;
            }

            lock (Sync)
            {
                if (_resolver != null)
                {
                    return;
                }

                var type = sample.GetType();
                var interfaces = type.GetInterfaces().Concat(new[] { type }).ToArray();

                // Prefer GetUsers() (10.11.9+)
                foreach (var t in interfaces)
                {
                    var method = t.GetMethod("GetUsers", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                    if (method == null)
                    {
                        continue;
                    }

                    _resolver = um =>
                    {
                        var result = method.Invoke(um, null);
                        return result switch
                        {
                            IEnumerable<JUser> enumerable => enumerable,
                            Array array => array.Cast<JUser>(),
                            _ => Array.Empty<JUser>()
                        };
                    };
                    return;
                }

                // Fallback: Users property (pre-10.11.9)
                foreach (var t in interfaces)
                {
                    var prop = t.GetProperty("Users", BindingFlags.Instance | BindingFlags.Public);
                    if (prop?.GetMethod == null)
                    {
                        continue;
                    }

                    _resolver = um =>
                    {
                        var result = prop.GetValue(um);
                        return result as IEnumerable<JUser> ?? Array.Empty<JUser>();
                    };
                    return;
                }

                _resolver = _ => Array.Empty<JUser>();
            }
        }
    }
}
