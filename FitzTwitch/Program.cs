using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;

namespace FitzTwitch
{
    public class Program
    {
        public static async Task Main() => await new Program().RealMainAsync();

        private bool _isDev;
        private IConfiguration _config;

#pragma warning disable IDE0052 // Remove unread private members
        private PubSubHandler _pubSubHandler;
        private Timer _webhookRefreshTimer;
#pragma warning restore IDE0052 // Remove unread private members

        private readonly TwitchClient _client = new TwitchClient();
        private readonly TwitchAPI _api = new TwitchAPI();

        private readonly HttpClient _httpClient = new HttpClient();

        private Timer _winLossTimer;
        private bool _winLossAllowed = true;

        private int _numPollAnswers;
        private readonly ConcurrentBag<PollAnswer> _pollResults = new ConcurrentBag<PollAnswer>();

        private readonly Random _random = new Random();

        public const string _channelId = "23155607";

        public async Task RealMainAsync()
        {
            _isDev = Environment.GetEnvironmentVariable("FT_DEV") != null;
            _config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();

            var credentials = new ConnectionCredentials(_config["Username"], _config["AccessToken"]);

            _pubSubHandler = new PubSubHandler(_config, _httpClient);

            _api.Settings.ClientId = _config["ClientId"];
            _api.Settings.AccessToken = _config["AccessToken"];

            _client.Initialize(credentials, "fitzyhere", '!');

            _client.OnChatCommandReceived += CommandReceived;
            _client.OnMessageReceived += PollCounter;

            _client.OnConnectionError += ConnectionError;
            _client.OnDisconnected += Disconnected;

            _client.Connect();

            _webhookRefreshTimer = new Timer(async _ => await SubscribeToWebhookAsync(), null, TimeSpan.Zero, TimeSpan.FromDays(5));

            await Task.Delay(-1);
        }

        private async Task SubscribeToWebhookAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/webhooks/hub");
            var requestBody = new WebhookRequestBody
            {
                Callback = _config["WebhookUrl"],
                Mode = "subscribe",
                Topic = $"https://api.twitch.tv/helix/streams?user_id={_channelId}",
                LeaseSeconds = 864000,
                Secret = _config["WebhookSigningSecret"]
            };

            request.Content = new StringContent(JsonConvert.SerializeObject(requestBody));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("Client-ID", _config["ClientId"]);

            await _httpClient.SendAsync(request);

            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: Webhook subscribed");
        }

        private void PollCounter(object sender, OnMessageReceivedArgs e)
        {
            if (_numPollAnswers != 0 && int.TryParse(e.ChatMessage.Message, out var ans) && ans >= 1 && ans <= _numPollAnswers)
            {
                _pollResults.Add(new PollAnswer(e.ChatMessage.UserId, ans));
            }
        }

        private async void CommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            var displayName = e.Command.ChatMessage.DisplayName;

            //if (string.Equals(e.Command.CommandText, "gin", StringComparison.OrdinalIgnoreCase))
            //{
            //    GintokiCommand(e.Command.ArgumentsAsString);
            //    return;
            //}

            if (!e.Command.ChatMessage.IsBroadcaster && !e.Command.ChatMessage.IsModerator)
                return;

            if (string.Equals(e.Command.CommandText, "refresh", StringComparison.InvariantCultureIgnoreCase))
            {
                await SendRefreshCallAsync(displayName);
                return;
            }

            if (!_winLossAllowed && !_isDev)
                return;

            var wldArg = e.Command.GetWLDArgument();

            switch (e.Command.CommandText.ToLowerInvariant())
            {
                case "w":
                case "win":
                case "wins":
                    Utils.LogToConsole($"Wins updated by {e.Command.ChatMessage.DisplayName}: {wldArg}");
                    await UpdateSingleAsync(NumberToUpdate.Wins, wldArg, displayName);
                    break;

                case "l":
                case "loss":
                case "losses":
                case "lose":
                case "k":
                case "kill":
                case "kills":
                    Utils.LogToConsole($"Losses updated by {e.Command.ChatMessage.DisplayName}: {wldArg}");
                    await UpdateSingleAsync(NumberToUpdate.Losses, wldArg, displayName);
                    break;

                case "d":
                case "draw":
                case "draws":
                case "death":
                case "deaths":
                    Utils.LogToConsole($"Draws updated by {e.Command.ChatMessage.DisplayName}: {wldArg}");
                    await UpdateSingleAsync(NumberToUpdate.Draws, wldArg, displayName);
                    break;

                case "clear":
                case "c":
                case "reset":
                case "r":
                    Utils.LogToConsole($"Record cleared by {e.Command.ChatMessage.DisplayName}: {wldArg}");
                    await UpdateSingleAsync(NumberToUpdate.Clear, "0", displayName);
                    break;

                case "all":
                case "wld":
                    Utils.LogToConsole($"All record args updated by {e.Command.ChatMessage.DisplayName}");
                    await CheckCommandAndUpdateAllAsync(e.Command);
                    break;

                case "startpoll":
                case "pollstart":
                    StartPoll(e.Command);
                    break;

                case "endpoll":
                case "pollend":
                    EndPoll(e.Command.ChatMessage.DisplayName);
                    break;
            }
        }

        private async Task UpdateSingleAsync(NumberToUpdate type, string num, string displayName)
        {
            if (!Utils.VerifyNumber(num))
            {
                _client.SendMessageAt(displayName, "Specified number is invalid");
                return;
            }

            _winLossAllowed &= num != "-1";

            if (await SendRecordApiCallAsync(type, num))
            {
                _client.SendMessageAt(displayName, "Updated successfully");
                _winLossTimer = new Timer(ResetWinLossAllowed, null, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(-1));
            }
            else
            {
                _client.SendMessageAt(displayName, "Not updated, something went wrong. You know who to bug.");
                _winLossAllowed = true;
            }
        }

        private async Task UpdateAllAsync(string wins, string losses, string draws, string displayName)
        {
            if (!Utils.VerifyNumber(wins))
            {
                _client.SendMessageAt(displayName, "Wins number invalid");
                return;
            }

            if (!Utils.VerifyNumber(losses))
            {
                _client.SendMessageAt(displayName, "Losses number invalid");
                return;
            }

            if (!Utils.VerifyNumber(draws))
            {
                _client.SendMessageAt(displayName, "Draws number invalid");
                return;
            }

            _winLossAllowed = false;

            await SendRecordApiCallAsync(NumberToUpdate.Wins, wins);
            await SendRecordApiCallAsync(NumberToUpdate.Losses, losses);
            await SendRecordApiCallAsync(NumberToUpdate.Draws, draws);

            _client.SendMessageAt(displayName, $"Set successfully: {wins}-{losses}-{draws}");
            _winLossTimer = new Timer(ResetWinLossAllowed, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
        }

        private async Task CheckCommandAndUpdateAllAsync(ChatCommand cmd)
        {
            if (cmd.ArgumentsAsList.Count != 3)
            {
                _client.SendMessageAt(cmd.ChatMessage.DisplayName, "Must provide three numbers. No more, no fewer. DansGame");
                return;
            }

            await UpdateAllAsync(cmd.ArgumentsAsList[0], cmd.ArgumentsAsList[1], cmd.ArgumentsAsList[2], cmd.ChatMessage.DisplayName);
        }

        private async Task<bool> SendRecordApiCallAsync(NumberToUpdate type, string num)
        {
            var url = _config["WinLossApiBaseUrl"];
            switch (type)
            {
                case NumberToUpdate.Wins:
                    url += "wins/";
                    break;

                case NumberToUpdate.Losses:
                    url += "losses/";
                    break;

                case NumberToUpdate.Draws:
                    url += "draws/";
                    break;

                case NumberToUpdate.Clear:
                    url += "clear";
                    break;
            }

            if (type != NumberToUpdate.Clear)
                url += num;

            Utils.LogToConsole($"Sending record API call to {url}");

            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["FitzyApiKey"]);

            var response = await _httpClient.SendAsync(request);

            // timer ensures there won't be multiple calls made at once, this log line is okay
            Utils.LogToConsole($"Previous API call succeeded: {response.IsSuccessStatusCode}");
            return response.IsSuccessStatusCode;
        }

        private void GintokiCommand(string message)
        {
            message = message.TrimStart('/', '!', '.', '+');

            var outMsg = new StringBuilder();
            foreach (var l in message)
            {
                if (!char.IsLetter(l))
                {
                    outMsg.Append(l);
                    continue;
                }

                var final = l;

                if (_random.Next(0, 10) >= 6)
                {
                    if (char.IsUpper(l))
                        final = char.ToLower(l);
                    else
                        final = char.ToUpper(l);
                }

                outMsg.Append(final);
            }

            _client.SendMessage("fitzyhere", outMsg.ToString());
        }

        private async Task SendRefreshCallAsync(string displayName)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _config["WinLossApiBaseUrl"] + "refresh");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["FitzyApiKey"]);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                _client.SendMessageAt(displayName, "Refreshed successfully.");
            else
                _client.SendMessageAt(displayName, "Something went wrong. Blame that one guy.");
        }

        private void StartPoll(ChatCommand cmd)
        {
            var displayName = cmd.ChatMessage.DisplayName;

            if (_numPollAnswers != 0)
            {
                _client.SendMessageAt(displayName, "You can't start a new poll when one is already open, you nerd.");
                return;
            }

            if (cmd.ArgumentsAsList.Count < 1)
            {
                _client.SendMessageAt(displayName, "You must provide the number of possible answers, you nerd.");
                return;
            }

            if (!int.TryParse(cmd.ArgumentsAsList[0], out var numOfAnswers))
            {
                _client.SendMessageAt(displayName, "You didn't provide a valid number of possible answers, you nerd.");
                return;
            }

            if (numOfAnswers < 2)
            {
                _client.SendMessageAt(displayName, "Poll must have two or more possible answers, you nerd.");
                return;
            }

            _numPollAnswers = numOfAnswers;
            _pollResults.Clear();

            _client.SendMessageAt(displayName, "Poll is now open, you nerd.");
        }

        private void EndPoll(string displayName)
        {
            if (_numPollAnswers == 0)
            {
                _client.SendMessageAt(displayName, "You can't end a nonexistent poll, you nerd.");
                return;
            }

            var numAnswers = _numPollAnswers;
            _numPollAnswers = 0;

            var userIds = new List<string>();
            var results = new List<int>();

            var sb = new StringBuilder("Results: ");

            while (_pollResults.TryTake(out var result))
            {
                if (userIds.Contains(result.UserId))
                    continue;

                userIds.Add(result.UserId);
                results.Add(result.Answer);
            }

            for (var i = 1; i <= numAnswers; i++)
            {
                if (i != 1)
                    sb.Append(" | ");

                sb.Append(i);
                sb.Append(": ");
                sb.Append(results.Count(x => x == i));
            }

            _client.SendMessage("fitzyhere", sb.ToString());

            _pollResults.Clear();
        }

        private void ConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Utils.LogToConsole("Connection error: " + e.Error.Message);
            Environment.Exit(1);
        }

        private void Disconnected(object sender, OnDisconnectedEventArgs e)
        {
            Utils.LogToConsole("Disconnected");
            Environment.Exit(1);
        }

        private void ResetWinLossAllowed(object _)
        {
            _winLossAllowed = true;

            _winLossTimer?.Dispose();
        }

        private enum NumberToUpdate
        {
            Wins,
            Losses,
            Draws,
            Clear
        }

        private class PollAnswer
        {
            public PollAnswer(string userId, int answer)
            {
                UserId = userId;
                Answer = answer;
            }

            public string UserId { get; set; }

            public int Answer { get; set; }
        }
    }
}
