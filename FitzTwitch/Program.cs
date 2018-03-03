using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib;
using TwitchLib.Events.Client;
using TwitchLib.Models.Client;

namespace FitzTwitch
{
    public static class Program
    {
        private static IConfiguration _config;

        private static TwitchClient _client;

        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Regex _verifyNum = new Regex("^[0-9]+$", RegexOptions.Compiled);

        private static Timer _winLossTimer;
        private static bool _winLossAllowed = true;

        public static async Task Main()
        {
            _config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();

            var credentials = new ConnectionCredentials(_config["Username"], _config["AccessToken"]);

            _client = new TwitchClient(credentials, "fitzyhere");
            _client.AddChatCommandIdentifier('!');
            _client.ChatThrottler = null;

            _client.OnChatCommandReceived += CommandReceived;
            _client.OnNewSubscriber += NewSub;
            _client.OnReSubscriber += Resub;
            _client.OnGiftedSubscription += Gift;

            _client.OnConnectionError += ConnectionError;
            _client.OnDisconnected += Disconnected;

            _client.Connect();

            await Task.Delay(-1);
        }

        private async static void CommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            if (e.Command.ChatMessage.DisplayName != "vaindil")
            {
                if (!e.Command.ChatMessage.IsBroadcaster && !e.Command.ChatMessage.IsModerator)
                    return;

                if (!_winLossAllowed)
                    return;
            }

            switch (e.Command.CommandText.ToLowerInvariant())
            {
                case "w":
                case "win":
                    await UpdateText(NumberToUpdate.Wins, e.Command);
                    break;

                case "l":
                case "loss":
                case "lose":
                    await UpdateText(NumberToUpdate.Losses, e.Command);
                    break;

                case "d":
                case "draw":
                case "t":
                case "tie":
                    await UpdateText(NumberToUpdate.Draws, e.Command);
                    break;

                case "clear":
                case "c":
                case "reset":
                case "r":
                    await ResetAll(e.Command.ChatMessage.DisplayName);
                    break;
            }
        }

        private async static Task UpdateText(NumberToUpdate type, ChatCommand cmd)
        {
            var num = "-1";
            if (cmd.ArgumentsAsList.Count > 0)
            {
                if (_verifyNum.IsMatch(cmd.ArgumentsAsList[0]) && int.TryParse(cmd.ArgumentsAsList[0], out _))
                {
                    num = cmd.ArgumentsAsList[0];
                }
                else
                {
                    _client.SendMessage("fitzyhere", $"@{cmd.ChatMessage.DisplayName}: Specified number is invalid");
                    return;
                }
            }

            _winLossAllowed = false;

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
            }

            url += num;

            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _client.SendMessage("fitzyhere", $"@{cmd.ChatMessage.DisplayName}: {type.ToString()} updated successfully");
                _winLossTimer = new Timer(ResetWinLossAllowed, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
            }
            else
            {
                _client.SendMessage("fitzyhere", $"@{cmd.ChatMessage.DisplayName}: {type.ToString()} not updated, something went wrong. You know who to bug.");
                _winLossAllowed = true;
            }
        }

        private static async Task ResetAll(string displayName)
        {
            _winLossAllowed = false;

            var request = new HttpRequestMessage(HttpMethod.Put, "http://localhost:5052/fitzy/wins/0");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);
            await _httpClient.SendAsync(request);

            request = new HttpRequestMessage(HttpMethod.Put, "http://localhost:5052/fitzy/losses/0");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);
            await _httpClient.SendAsync(request);

            request = new HttpRequestMessage(HttpMethod.Put, "http://localhost:5052/fitzy/draws/0");
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);
            await _httpClient.SendAsync(request);

            _client.SendMessage("fitzyhere", $"@{displayName}: Reset successfully");
            _winLossTimer = new Timer(ResetWinLossAllowed, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
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

        private static void NewSub(object sender, OnNewSubscriberArgs e)
        {
            _client.SendMessage("fitzyhere", $"@{e.Subscriber.DisplayName} Thanks for the new sub! " +
                "fitzHey fitzHey Welcome to the Super Secret Sombra Squad! fitzHYPE fitzHYPE fitzBEANHERE fitzBEANHERE");
        }

        private static void Resub(object sender, OnReSubscriberArgs e)
        {
            _client.SendMessage("fitzyhere", $"fitzDab fitzBEANHERE fitzHYPE Thank you @{e.ReSubscriber.DisplayName} " +
                $"for your {e.ReSubscriber.Months} months of support! fitzHYPE fitzBEANHERE fitzDab");
        }

        private static void Gift(object sender, OnGiftedSubscriptionArgs e)
        {
            _client.SendMessage("fitzyhere", $"Thank you @{e.GiftedSubscription.DisplayName} for the gift! " +
                $"@{e.GiftedSubscription.MsgParamRecipientDisplayName} fitzHey fitzHey " +
                "Welcome to the Super Secret Sombra Squad! fitzHYPE fitzHYPE fitzBEANHERE fitzBEANHERE");
        }

        private enum NumberToUpdate
        {
            Wins,
            Losses,
            Draws
        }
    }
}
