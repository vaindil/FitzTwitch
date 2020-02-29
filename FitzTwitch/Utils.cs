using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Models;

namespace FitzTwitch
{
    public static class Utils
    {
        private static readonly Regex _verifyNum = new Regex("^(-1|[0-9]+)$", RegexOptions.Compiled);
        private static readonly HttpClient _httpClient = new HttpClient();

        public static bool VerifyNumber(string num)
        {
            return _verifyNum.IsMatch(num) && int.TryParse(num, out _);
        }

        public static void SendMessageAt(this TwitchClient client, string displayName, string message)
        {
            client.SendMessage("fitzyhere", $"@{displayName}: {message}");
        }

        public static string GetWLDArgument(this ChatCommand cmd)
        {
            if (cmd.ArgumentsAsList.Count >= 1)
                return cmd.ArgumentsAsList[0];

            return "-1";
        }

        public static void LogToConsole(string message)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}: {message}");
        }

        public static Task<HttpResponseMessage> SendDiscordErrorWebhookAsync(string message, string webhookUrl)
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
            {
                Content = new StringContent("{\"content\":\"" + message + "\"}")
            };
            requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return _httpClient.SendAsync(requestMessage);
        }
    }
}
