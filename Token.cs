using Newtonsoft.Json;

namespace AzureBlobUploader
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Token
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("instance_url")]
        public string InstanceUrl { get; set; }
        [JsonProperty("token_type")]
        public string Type { get; set; }
        [JsonProperty("issued_at")]
        public long IssuedAt { get; set; }
        [JsonProperty("signature")]
        public string Signature { get; set; }
    }
}
