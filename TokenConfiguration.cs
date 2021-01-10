using Microsoft.Extensions.Configuration;

namespace AzureBlobUploader
{
    public class TokenConfiguration
    {
        public TokenConfiguration(IConfiguration config)
        {
            Url = config["Token.Url"];
            Username = config["Token.Username"];
            Password = config["Token.Password"];
            GrantType = config["Token.GrantType"];
            ClientId = config["Token.ClientId"];
            Secret = config["Token.Secret"];
            TokenEntityKey = config["Token.TokenEntityKey"];
        }


        public string Url { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string GrantType { get; set; }
        public string ClientId { get; set; }
        public string Secret { get; set; }
        public string TokenEntityKey { get; set; }
    }



}
