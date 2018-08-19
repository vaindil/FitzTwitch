using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

namespace FitzTwitch
{
    public class PubSubHandler
    {
        private readonly IConfiguration _config;

        private readonly TwitchClient _client;
        private readonly TwitchAPI _api;
        private readonly TwitchPubSub _pubSub;

        private readonly HttpClient _httpClient;

        public PubSubHandler(IConfiguration config, TwitchClient client, TwitchAPI api, HttpClient httpClient)
        {
            _config = config;

            _client = client;
            _api = api;
            _pubSub = new TwitchPubSub();

            _httpClient = httpClient;

            _pubSub.OnPubSubServiceConnected += PubSubConnected;
            _pubSub.OnPubSubServiceClosed += PubSubClosed;
            _pubSub.OnPubSubServiceError += PubSubError;

            _pubSub.OnStreamUp += StreamUp;
            _pubSub.OnStreamDown += StreamDown;
            _pubSub.OnBan += OnBan;
            _pubSub.OnTimeout += OnTimeout;
            _pubSub.OnUnban += OnUnban;
            _pubSub.OnUntimeout += OnUntimeout;

            _pubSub.Connect();
        }

        private void PubSubConnected(object sender, EventArgs e)
        {
            _pubSub.ListenToVideoPlayback("fitzyhere");
            _pubSub.ListenToChatModeratorActions(_config["UserId"], Program._channelId);
            _pubSub.SendTopics(_config["AccessToken"]);
        }

        private void PubSubClosed(object sender, EventArgs e)
        {
            _pubSub.Connect();
        }

        private void PubSubError(object sender, OnPubSubServiceErrorArgs e)
        {
            Console.Error.WriteLine($"PubSub error: {e.Exception.Message}");

            _pubSub.Connect();
        }

        private async void StreamUp(object sender, OnStreamUpArgs e)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, _config["ActionsApiBaseUrl"] + "live");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["FitzyApiKey"]);

            await _httpClient.SendAsync(request);
        }

        private async void StreamDown(object sender, OnStreamDownArgs e)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, _config["ActionsApiBaseUrl"] + "offline");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["FitzyApiKey"]);

            await _httpClient.SendAsync(request);
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
            var unbanned = await _api.Users.helix.GetUsersAsync(ids: new List<string> { e.UnbannedUserId });
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
            var untimedout = await _api.Users.helix.GetUsersAsync(ids: new List<string> { e.UntimeoutedUserId });
            await SendActionAsync(new ActionTaken
            {
                ModUsername = e.UntimeoutedBy,
                UserUsername = untimedout.Users[0].Login,
                Action = "untimeout",
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
