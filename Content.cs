using Newtonsoft.Json;

namespace AzureBlobUploader
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Content
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("contenturl")]
        public string ContentUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        public string ContentType { get; set; }
        public string Cookie { get; set; }
    }
}
