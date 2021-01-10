using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using RestSharp;
using Microsoft.Extensions.Primitives;
using System.Linq;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json.Linq;
using System.Net;

namespace AzureBlobUploader
{
    public static class AzureBlobUploaderFunction
    {
        [FunctionName("AzureBlobUploaderFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest request,
            [DurableClient] IDurableEntityClient client,
            ILogger logger,
            ExecutionContext context)
        {
            logger.LogInformation($"[AzureBlobUploaderFunction.Run][C# HttpTrigger] function executed at: {DateTime.Now}, Authorization header: {request.Headers["Authorization"]}");

            if ("get" == request.Method.ToLower())
                return new OkObjectResult("Listening... AzureBlobUploaderFunction expecting POST verb requests");

            IConfiguration config = GetConfuguration(context);

            await CreateContainerIfNotExists(config);
            CloudStorageAccount storageAccount = GetCloudStorageAccount(config);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(config["BlobStorageContainerName"]);

            string body = await new StreamReader(request.Body).ReadToEndAsync();
            logger.LogInformation(body);
            Content content = JsonConvert.DeserializeObject<Content>(body);
            if (request.Headers.TryGetValue("Cookie", out StringValues values))
                content.Cookie = values.ToArray().FirstOrDefault();

            logger.LogInformation($"[AzureBlobUploaderFunction.Run] Trying to download+upload content: id: {content.Id}, url: {content.ContentUrl}");
            Stream stream = await ReadContent(content, client, config, logger);
            CloudBlockBlob blob = await UploadContent(content, container, stream, logger);

            if (null == blob)
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);

            logger.LogInformation($"[AzureBlobUploaderFunction.Run] Blob uploaded - type: {blob.Properties.BlobType} is uploaded to container: {container.Name}, Url: {blob.Uri}");

            return new OkObjectResult(blob.Uri);
        }

        public static async Task<Stream> ReadContent(Content content, IDurableEntityClient client, IConfiguration config, ILogger logger)
        {
            Stream stream = new MemoryStream();
            Action<Stream> responseWriter = requestStream =>
            {
                requestStream.CopyToAsync(stream);
                stream.Position = 0;
            };

            Token token = await GetToken(client, content, config, logger);
            RestClient restClient = new RestClient(token.InstanceUrl + content.ContentUrl) { Timeout = -1 };
            RestRequest request = await BuildRequest(responseWriter, content, token);


            logger.LogInformation($"[ReadContent] First Try: id={content.Id}, type={content.Type}, contenturl{content.ContentUrl}");
            IRestResponse response = await restClient.ExecuteAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                token = await GetToken(client, content, config, logger, false);
                restClient.BaseUrl = new Uri(token.InstanceUrl + content.ContentUrl);
                request = await BuildRequest(responseWriter, content, token);
                logger.LogWarning($"[ReadContent] Second try - after reseting token: id={content.Id}, type={content.Type}, contenturl{content.ContentUrl}");
                response = await restClient.ExecuteAsync(request);
                request.ResponseWriter = requestStream =>
                {
                    requestStream.CopyToAsync(stream);
                    stream.Position = 0;
                };
            }

            content.ContentType = response.ContentType;
            logger.LogInformation($"[ReadContent] Response Content ({response.RawBytes}): id={content.Id}, type={content.Type}, content type: {response.ContentType } contenturl{content.ContentUrl}, Content: {response.Content}");
            return stream;
        }

        private static async Task<RestRequest> BuildRequest(Action<Stream> responseWriter, Content content, Token token)
        {
            RestRequest request = new RestRequest(Method.GET);
            request.AddHeader("Authorization", $"Bearer {token.AccessToken}");
            if (!string.IsNullOrWhiteSpace(content.Cookie))
                request.AddHeader("Cookie", content.Cookie);

            request.ResponseWriter = responseWriter;
            return request;
        }

        private static async Task<CloudBlockBlob> UploadContent(Content content, CloudBlobContainer container, Stream stream, ILogger logger)
        {
            CloudBlockBlob blob = container.GetBlockBlobReference($"{content.Name} ({content.Id})");
            blob.Properties.ContentType = content.ContentType;
            logger.LogInformation($"[UploadContent] Uploading content: id={content.Id}, type={content.Type}, content type: {content.ContentType }.");
            await blob.UploadFromStreamAsync(stream);
            await blob.SetPropertiesAsync();
            return blob;
        }

        private static async Task<bool> CreateContainerIfNotExists(IConfiguration config)
        {
            CloudStorageAccount storageAccount = GetCloudStorageAccount(config);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(config["BlobStorageContainerName"]);
            return await blobContainer.CreateIfNotExistsAsync();
        }

        private static CloudStorageAccount GetCloudStorageAccount(IConfiguration config)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["AzureWebJobsStorage"]);
            return storageAccount;
        }

        public static async Task<Token> GetToken(IDurableEntityClient client, Content content, IConfiguration config, ILogger logger, bool useCache = true)
        {
            Token token;
            TokenConfiguration configuration = new TokenConfiguration(config);
            EntityId entityId = new EntityId(nameof(TokenEntity), configuration.TokenEntityKey);

            if (useCache)
            {
                EntityStateResponse<JObject> stateResponse = await client.ReadEntityStateAsync<JObject>(entityId);
                if (stateResponse.EntityExists)
                {
                    token = stateResponse.EntityState["value"].ToObject<Token>();
                    if (null != token)
                    {
                        logger.LogInformation($"[GetToken] Found cached token with id: {token.Id}, IssuedAt:{token.IssuedAt}, InstanceUrl:{token.InstanceUrl}");
                        return token;
                    }
                }
            }
            else
            {
                logger.LogInformation($"[GetToken] Reseting token at {configuration.Url}");
                await client.SignalEntityAsync(entityId, "Reset");
            }

            logger.LogInformation($"[GetToken] Fetching token from {configuration.Url}");

            RestClient restClient = new RestClient(configuration.Url) { Timeout = -1 };
            RestRequest request = new RestRequest(Method.POST) { AlwaysMultipartFormData = true };

            if (!string.IsNullOrWhiteSpace(content.Cookie))
                request.AddHeader("Cookie", content.Cookie);

            request.AddParameter("username", configuration.Username);
            request.AddParameter("password", configuration.Password);
            request.AddParameter("grant_type", configuration.GrantType);
            request.AddParameter("client_id", configuration.ClientId);
            request.AddParameter("client_secret", configuration.Secret);
            IRestResponse response = await restClient.ExecuteAsync(request);

            token = JsonConvert.DeserializeObject<Token>(response.Content);

            JObject jToken = JObject.FromObject(token);
            logger.LogInformation($"[GetToken] Caching new token with id: {token.Id}, IssuedAt:{token.IssuedAt}, InstanceUrl:{token.InstanceUrl}");
            await client.SignalEntityAsync(entityId, "Set", jToken);

            return token;
        }

        private static IConfiguration GetConfuguration(ExecutionContext context)
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                            .SetBasePath(context.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables().Build();
            return config;
        }
    }
}
