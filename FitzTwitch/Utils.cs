using System.Text.RegularExpressions;
using TwitchLib;
using TwitchLib.Models.Client;

namespace FitzTwitch
{
    public static class Utils
    {
        private static readonly Regex _verifyNum = new Regex("^(-1|[0-9]+)$", RegexOptions.Compiled);

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
    }
}
