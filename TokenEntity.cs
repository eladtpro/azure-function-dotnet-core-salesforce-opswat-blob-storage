using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using RestSharp;
using System.Threading.Tasks;

namespace AzureBlobUploader
{
    [JsonObject(MemberSerialization.OptIn)]
    public class TokenEntity
    {
        [JsonProperty("value")]
        public Token CurrentValue { get; set; }

        public void Set(Token token) => CurrentValue = token;

        public void Reset() => CurrentValue = null;

        public Token Get() => CurrentValue;

        [FunctionName(nameof(TokenEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<TokenEntity>();


    }
}
