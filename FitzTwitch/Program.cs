using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;

namespace FitzTwitch
{
    public static class Program
    {
        private static bool _isDev;
        private static IConfiguration _config;

        private static readonly TwitchClient _client = new TwitchClient();

        private static readonly HttpClient _httpClient = new HttpClient();

        private static Timer _winLossTimer;
        private static bool _winLossAllowed = true;

        private static int _numPollAnswers;
        private static readonly ConcurrentBag<int> _pollResults = new ConcurrentBag<int>();

        public static async Task Main()
        {
            _isDev = Environment.GetEnvironmentVariable("FT_DEV") != null;
            _config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();

            var credentials = new ConnectionCredentials(_config["Username"], _config["AccessToken"]);

            _client.Initialize(credentials, "fitzyhere", '!');
            _client.ChatThrottler = null;

            _client.OnChatCommandReceived += CommandReceived;
            _client.OnMessageReceived += SpamCatcher;

            _client.OnConnectionError += ConnectionError;
            _client.OnDisconnected += Disconnected;

            _client.Connect();

            await Task.Delay(-1);
        }

        private static void SpamCatcher(object sender, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Message.IndexOf("n i g g e r", StringComparison.InvariantCultureIgnoreCase) >= 0)
                _client.BanUser(e.ChatMessage.Channel, e.ChatMessage.Username, "Racist spam");
        }

        private async static void CommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            var displayName = e.Command.ChatMessage.DisplayName;

            if (_numPollAnswers != 0 && int.TryParse(e.Command.ChatMessage.Message, out var num) && num >= 1 && num <= _numPollAnswers)
            {
                _pollResults.Add(num);
                return;
            }

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
                    await UpdateSingleAsync(NumberToUpdate.Wins, wldArg, displayName);
                    break;

                case "l":
                case "loss":
                case "losses":
                case "lose":
                case "k":
                case "kill":
                case "kills":
                    await UpdateSingleAsync(NumberToUpdate.Losses, wldArg, displayName);
                    break;

                case "d":
                case "draw":
                case "draws":
                case "death":
                case "deaths":
                    await UpdateSingleAsync(NumberToUpdate.Draws, wldArg, displayName);
                    break;

                case "clear":
                case "c":
                case "reset":
                case "r":
                    await UpdateSingleAsync(NumberToUpdate.Clear, "0", displayName);
                    break;

                case "all":
                case "wld":
                    await CheckCommandAndUpdateAllAsync(e.Command);
                    break;

                case "startpoll":
                case "pollstart":
                    StartPoll(e.Command);
                    break;

                case "endpoll":
                case "pollend":
                    EndPoll();
                    break;

                case "poll":
                    TogglePoll(e.Command);
                    break;
            }
        }

        private async static Task UpdateSingleAsync(NumberToUpdate type, string num, string displayName)
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

        private static async Task UpdateAllAsync(string wins, string losses, string draws, string displayName)
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

        private static async Task CheckCommandAndUpdateAllAsync(ChatCommand cmd)
        {
            if (cmd.ArgumentsAsList.Count != 3)
            {
                _client.SendMessageAt(cmd.ChatMessage.DisplayName, "Must provide three numbers. No more, no fewer. DansGame");
                return;
            }

            await UpdateAllAsync(cmd.ArgumentsAsList[0], cmd.ArgumentsAsList[1], cmd.ArgumentsAsList[2], cmd.ChatMessage.DisplayName);
        }

        private static async Task<bool> SendRecordApiCallAsync(NumberToUpdate type, string num)
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

            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        private static async Task SendRefreshCallAsync(string displayName)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _config["WinLossApiBaseUrl"] + "refresh");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                _client.SendMessageAt(displayName, "Refreshed successfully.");
            else
                _client.SendMessageAt(displayName, "Something went wrong. Blame that one guy.");
        }

        private static void TogglePoll(ChatCommand cmd)
        {
            if (_numPollAnswers != 0)
                EndPoll();
            else
                StartPoll(cmd);
        }

        private static void StartPoll(ChatCommand cmd)
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

            if (!int.TryParse(cmd.ChatMessage.Message, out var numOfAnswers))
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

        private static void EndPoll()
        {
            var numAnswers = _numPollAnswers;
            _numPollAnswers = 0;

            var sb = new StringBuilder("Results: ");

            for (var i = 1; i <= numAnswers; i++)
            {
                if (i != 1)
                    sb.Append(" | ");

                sb.Append(i);
                sb.Append(": ");
                sb.Append(_pollResults.Count(x => x == i));
            }

            _client.SendMessage("fitzyhere", sb.ToString());

            _pollResults.Clear();
        }

        private static void ConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Console.Error.WriteLine("Connection error: " + e.Error.Message);
            Environment.Exit(1);
        }

        private static void Disconnected(object sender, OnDisconnectedArgs e)
        {
            Console.Error.WriteLine("Disconnected");
            Environment.Exit(1);
        }

        private static void ResetWinLossAllowed(object _)
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
    }
}
