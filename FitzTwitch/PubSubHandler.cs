using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

namespace FitzTwitch
{
    public class PubSubHandler
    {
        private readonly IConfiguration _config;
        private readonly TwitchPubSub _pubSub;
        private readonly HttpClient _httpClient;

        private readonly Timer _reconnectTimer;

        public PubSubHandler(IConfiguration config, HttpClient httpClient)
        {
            _config = config;
            _pubSub = new TwitchPubSub();
            _httpClient = httpClient;

            _pubSub.OnPubSubServiceConnected += PubSubConnected;
            _pubSub.OnPubSubServiceClosed += PubSubClosed;
            _pubSub.OnPubSubServiceError += PubSubError;

            _pubSub.OnBan += OnBan;
            _pubSub.OnTimeout += OnTimeout;
            _pubSub.OnMessageDeleted += OnMessageDeleted;
            _pubSub.OnUnban += OnUnban;
            _pubSub.OnUntimeout += OnUntimeout;

            if (bool.Parse(_config["LogAllPubSubMessages"]))
            {
                _pubSub.OnLog += OnLog;
            }

            _reconnectTimer = new Timer(_ => PubSubConnect(), null, TimeSpan.Zero, TimeSpan.FromHours(18));
        }

        private void PubSubConnect()
        {
            // will error if it's not connected, don't care
            try
            {
                _pubSub.Disconnect();
                LogToConsole("PubSub intentionally disconnected, about to reconnect");
            }
            catch
            {
            }

            _pubSub.Connect();
            LogToConsole("PubSub reconnected");
        }

        private void PubSubConnected(object sender, EventArgs e)
        {
            _pubSub.ListenToChatModeratorActions(_config["BotUserId"], Program._channelId);
            _pubSub.SendTopics(_config["AccessToken"]);

            LogToConsole("PubSub topics sent");
        }

        private void PubSubClosed(object sender, EventArgs e)
        {
            LogToConsole("PubSub closed, reconnecting");
            _pubSub.Connect();
        }

        private void PubSubError(object sender, OnPubSubServiceErrorArgs e)
        {
            LogToConsole($"PubSub error, reconnecting: {e.Exception.Message}");

            _pubSub.Connect();
        }

        private void OnLog(object sender, OnLogArgs e)
        {
            LogToConsole($"PubSub message received: {e.Data}");
        }

        private async void OnBan(object sender, OnBanArgs e)
        {
            LogToConsole($"User {e.BannedUser} ({e.BannedUserId}) banned by {e.BannedBy} ({e.BannedByUserId})");

            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.BannedBy,
                UserUsername = e.BannedUser,
                Action = "ban",
                Duration = -1,
                Reason = e.BanReason
            });
        }

        private async void OnTimeout(object sender, OnTimeoutArgs e)
        {
            LogToConsole($"User {e.TimedoutUser} ({e.TimedoutUserId}) timed out by {e.TimedoutBy} ({e.TimedoutById})");

            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.TimedoutBy,
                UserUsername = e.TimedoutUser,
                Action = "timeout",
                Duration = (int)Math.Ceiling(e.TimeoutDuration.TotalSeconds),
                Reason = e.TimeoutReason
            });
        }

        private async void OnUnban(object sender, OnUnbanArgs e)
        {
            LogToConsole($"User {e.UnbannedUser} ({e.UnbannedUserId}) unbanned by {e.UnbannedBy} ({e.UnbannedByUserId})");

            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.UnbannedBy,
                UserUsername = e.UnbannedUser,
                Action = "unban",
                Duration = 0,
                Reason = "(none)"
            });
        }

        private async void OnUntimeout(object sender, OnUntimeoutArgs e)
        {
            LogToConsole($"User {e.UntimeoutedUser} ({e.UntimeoutedUserId}) untimed out by {e.UntimeoutedBy} " +
                $"({e.UntimeoutedByUserId})");

            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.UntimeoutedBy,
                UserUsername = e.UntimeoutedUser,
                Action = "untimeout",
                Duration = 0,
                Reason = "(none)"
            });
        }

        private async void OnMessageDeleted(object sender, OnMessageDeletedArgs e)
        {
            LogToConsole($"User {e.TargetUser} ({e.TargetUserId}) had message deleted by {e.DeletedBy} " +
                $"({e.DeletedByUserId}): {e.Message}");

            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.DeletedBy,
                UserUsername = e.TargetUser,
                Action = "messagedeleted",
                Duration = 0,
                Reason = "(none)"
            });
        }

        private async Task SendActionAsync(ActionTaken action)
        {
            if (string.IsNullOrWhiteSpace(action.Reason))
                action.Reason = "(none)";

            var request = new HttpRequestMessage(HttpMethod.Post, _config["ActionsApiBaseUrl"])
            {
                Content = new StringContent(JsonConvert.SerializeObject(action))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["FitzyApiKey"]);

            await _httpClient.SendAsync(request);

            LogToConsole($"Action sent to API: {action.Action} | {action.UserUsername} by {action.ModUsername} | " +
                $"duration: {action.Duration}");
        }

        private void LogToConsole(string message)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: {message}");
        }

        private class Moderator
        {
            public string Id { get; set; }

            public string Username { get; set; }
        }

        private class ActionTaken
        {
            public string ModUsername { get; set; }

            public string UserUsername { get; set; }

            public string Action { get; set; }

            public int Duration { get; set; }

            public string Reason { get; set; }
        }
    }
}
