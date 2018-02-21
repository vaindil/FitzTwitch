using System;
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
        private static TwitchClient _client;
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Regex _verifyNum = new Regex("^[0-9]+$", RegexOptions.Compiled);
        private const string AUTHHEADER = "ThereOnceWasAManFromPeruWhoDreamtHeWasEatingHisShoeHeWokeWithAFrightInTheMiddleOfTheNightToFindThatHisDreamHadComeTrue";

        private static Timer _winLossTimer;
        private static bool _winLossAllowed = true;

        public static async Task Main()
        {
            var credentials = new ConnectionCredentials("vaindil", "4y5k9yo2wgzl4bit09daeqvmw16tnq");

            _client = new TwitchClient(credentials, "fitzyhere");
            _client.AddChatCommandIdentifier('!');
            _client.ChatThrottler = null;

            _client.OnChatCommandReceived += CommandReceived;
            _client.OnNewSubscriber += NewSub;
            _client.OnReSubscriber += Resub;
            _client.OnGiftedSubscription += Gift;

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

            var url = "http://localhost:5052/fitzy/";
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
            request.Headers.Authorization = new AuthenticationHeaderValue(AUTHHEADER);

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
