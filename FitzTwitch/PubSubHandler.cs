using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

namespace FitzTwitch
{
    public class PubSubHandler
    {
        private readonly IConfiguration _config;

        private readonly TwitchAPI _api;
        private readonly TwitchPubSub _pubSub;

        private readonly HttpClient _httpClient;

        public PubSubHandler(IConfiguration config, TwitchAPI api, HttpClient httpClient)
        {
            _config = config;

            _api = api;
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

            _pubSub.Connect();
        }

        private void PubSubConnected(object sender, EventArgs e)
        {
            _pubSub.ListenToChatModeratorActions(_config["BotUserId"], Program._channelId);
            _pubSub.SendTopics(_config["AccessToken"]);
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: PubSub topics sent");
        }

        private void PubSubClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: PubSub closed");
            _pubSub.Connect();
        }

        private void PubSubError(object sender, OnPubSubServiceErrorArgs e)
        {
            Console.Error.WriteLine($"PubSub error: {e.Exception.Message}");

            _pubSub.Connect();
        }

        private void OnLog(object sender, OnLogArgs e)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: PubSub message received: {e.Data}");
        }

        private async void OnBan(object sender, OnBanArgs e)
        {
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
            var unbanned = await _api.Helix.Users.GetUsersAsync(ids: new List<string> { e.UnbannedUserId });
            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.UnbannedBy,
                UserUsername = unbanned.Users[0].Login,
                Action = "unban",
                Duration = 0,
                Reason = "(none)"
            });
        }

        private async void OnUntimeout(object sender, OnUntimeoutArgs e)
        {
            var untimedout = await _api.Helix.Users.GetUsersAsync(ids: new List<string> { e.UntimeoutedUserId });
            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.UntimeoutedBy,
                UserUsername = untimedout.Users[0].Login,
                Action = "untimeout",
                Duration = 0,
                Reason = "(none)"
            });
        }

        private async void OnMessageDeleted(object sender, OnMessageDeletedArgs e)
        {
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

            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: Action sent to API");
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
