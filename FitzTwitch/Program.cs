using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
            }
        }

        private async static Task UpdateSingleAsync(NumberToUpdate type, string num, string displayName)
        {
            if (!Utils.VerifyNumber(num))
            {
                _client.SendMessageAt(displayName, "Specified number is invalid");
                return;
            }

            if (num == "-1")
                _winLossAllowed = false;

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
