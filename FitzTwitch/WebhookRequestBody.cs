using Newtonsoft.Json;

namespace FitzTwitch
{
    public class WebhookRequestBody
    {
        [JsonProperty(PropertyName = "hub.callback")]
        public string Callback { get; set; }

        [JsonProperty(PropertyName = "hub.mode")]
        public string Mode { get; set; }

        [JsonProperty(PropertyName = "hub.topic")]
        public string Topic { get; set; }

        [JsonProperty(PropertyName = "hub.lease_seconds")]
        public int LeaseSeconds { get; set; }

        [JsonProperty(PropertyName = "hub.secret")]
        public string Secret { get; set; }
    }
}
