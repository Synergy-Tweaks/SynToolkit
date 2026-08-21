using DiscordRPC;
using NLog;
using System;

namespace SynToolkit.Services
{
    /// <summary>
    /// Owns the optional Discord Rich Presence connection for the app process.
    /// A missing application ID or a closed Discord client is treated as a normal state.
    /// </summary>
    public sealed class DiscordPresenceService : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private DiscordRpcClient _client;
        private RichPresence _presence;

        public bool TryStart(string applicationId, string largeImageKey)
        {
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                Logger.Info("Discord Rich Presence is disabled because no application ID is configured.");
                return false;
            }

            if (!ulong.TryParse(applicationId, out _))
            {
                Logger.Warn("Discord Rich Presence application ID is not a valid numeric ID.");
                return false;
            }

            try
            {
                _client = new DiscordRpcClient(applicationId);
                if (!_client.Initialize())
                {
                    _client.Dispose();
                    _client = null;
                    return false;
                }

                Assets assets = null;
                if (!string.IsNullOrWhiteSpace(largeImageKey))
                {
                    assets = new Assets
                    {
                        LargeImageKey = largeImageKey,
                        LargeImageText = "SynToolkit"
                    };
                }

                _presence = new RichPresence
                {
                    Details = "Using the best Toolkit",
                    State = "Configuring Windows",
                    Timestamps = Timestamps.Now,
                    Assets = assets,
                    Buttons = new[]
                    {
                        new Button
                        {
                            Label = "SynToolkit",
                            Url = "https://github.com/kwanteks/synergyos"
                        }
                    }
                };
                _client.SetPresence(_presence);

                Logger.Info("Discord Rich Presence initialized.");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Discord Rich Presence is unavailable.");
                Dispose();
                return false;
            }
        }

        public void UpdateState(string state)
        {
            if (_client is null || _presence is null || string.IsNullOrWhiteSpace(state))
            {
                return;
            }
            try
            {
                _presence.State = state;
                _client.SetPresence(_presence);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Discord Rich Presence state update failed.");
            }
        }

        public void Dispose()
        {
            try
            {
                _client?.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Discord Rich Presence cleanup failed.");
            }
            finally
            {
                _client = null;
                _presence = null;
            }
        }
    }
}
