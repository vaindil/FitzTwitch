using System.Threading.Tasks;
using TwitchLib;
using TwitchLib.Events.Client;
using TwitchLib.Models.Client;

namespace FitzTwitch
{
    public static class Program
    {
        private static TwitchClient _client;

        public static async Task Main()
        {
            var credentials = new ConnectionCredentials("vaindil", "u7t3qqo9wo7lvmsmqtvzjuo5fltc5n");

            _client = new TwitchClient(credentials, "fitzyhere");
            _client.OnNewSubscriber += NewSub;
            _client.OnReSubscriber += Resub;
            _client.OnGiftedSubscription += Gift;
            _client.Connect();

            await Task.Delay(-1);
        }

        private static void NewSub(object sender, OnNewSubscriberArgs e)
        {
            _client.SendMessage($"@{e.Subscriber.DisplayName} Thanks for the new sub! " +
                "fitzHey fitzHey Welcome to the Super Secret Sombra Squad! fitzHYPE fitzHYPE fitzBEANHERE fitzBEANHERE");
        }

        private static void Resub(object sender, OnReSubscriberArgs e)
        {
            _client.SendMessage($"fitzDab fitzBEANHERE fitzHYPE Thank you @{e.ReSubscriber.DisplayName} " +
                $"for your {e.ReSubscriber.Months} months of support! fitzHYPE fitzBEANHERE fitzDab");
        }

        private static void Gift(object sender, OnGiftedSubscriptionArgs e)
        {
            _client.SendMessage($"Thank you @{e.GiftedSubscription.DisplayName} for the gift! " +
                $"@{e.GiftedSubscription.MsgParamRecipientDisplayName} fitzHey fitzHey " +
                "Welcome to the Super Secret Sombra Squad! fitzHYPE fitzHYPE fitzBEANHERE fitzBEANHERE");
        }
    }
}
