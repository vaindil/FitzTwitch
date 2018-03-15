﻿using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace FitzTwitch
{
    public static class Program
    {
        private static bool _isDev;
        private static IConfiguration _config;

        private static TwitchClient _client = new TwitchClient();

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

            _client.OnConnectionError += ConnectionError;
            _client.OnDisconnected += Disconnected;

            _client.Connect();

            await Task.Delay(-1);
        }

        private async static void CommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            if (!e.Command.ChatMessage.IsBroadcaster && !e.Command.ChatMessage.IsModerator)
                return;

            if (!_winLossAllowed && !_isDev)
                return;

            var wldArg = e.Command.GetWLDArgument();
            var displayName = e.Command.ChatMessage.DisplayName;

            switch (e.Command.CommandText.ToLowerInvariant())
            {
                case "w":
                case "win":
                    await UpdateSingleAsync(NumberToUpdate.Wins, wldArg, displayName);
                    break;

                case "l":
                case "loss":
                case "lose":
                    await UpdateSingleAsync(NumberToUpdate.Losses, wldArg, displayName);
                    break;

                case "d":
                case "draw":
                case "t":
                case "tie":
                    await UpdateSingleAsync(NumberToUpdate.Draws, wldArg, displayName);
                    break;

                case "clear":
                case "c":
                case "reset":
                case "r":
                    await UpdateAllAsync("0", "0", "0", displayName);
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

            _winLossAllowed = false;

            if (await SendRecordApiCallAsync(type, num))
            {
                _client.SendMessageAt(displayName, $"{type.ToString()} updated successfully");
                _winLossTimer = new Timer(ResetWinLossAllowed, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
            }
            else
            {
                _client.SendMessageAt(displayName, $"{type.ToString()} not updated, something went wrong. You know who to bug.");
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
                _client.SendMessageAt(cmd.ChatMessage.DisplayName, "Must provide three numbers, no more, no fewer. DansGame");
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
            }

            url += num;

            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(_config["WinLossApiKey"]);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
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
            Draws
        }
    }
}
