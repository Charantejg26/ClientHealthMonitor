using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CommonLib
{
    public class GoogleApiTokenService
    {
        
        private readonly ITokenEncryptionService _cryptoService;
        private readonly HttpClient _httpClient;
        public GoogleApiTokenService(HttpClient httpClient ,ITokenEncryptionService cryptoService) 
        {
            _httpClient = httpClient;
            _cryptoService = cryptoService;
        }
        public async Task<string> EnsureAccessToken(Guid userId, string clientid, string clientSecret, string refreshToken, string accessTokenDb, DateTimeOffset tokenExpiry)
        {
            var decryptedToken = _cryptoService.Decrypt(refreshToken);
            if (string.IsNullOrEmpty(decryptedToken))
            {               
                throw new InvalidOperationException($"Decrypted refresh token is empty for user {userId}");
            }
            // If you already have access_token in DB and it isn't expired, return it. Otherwise call token endpoint.            
            var nowUtc = DateTimeOffset.UtcNow;
            var safetyBuffer = TimeSpan.FromMinutes(1);

            if (nowUtc < tokenExpiry - safetyBuffer)
            {
                // Token still valid
                var decryptedAccessToken = _cryptoService.Decrypt(accessTokenDb);
                return decryptedAccessToken;
            }
            // For brevity, call the token endpoint directly

            var pairs = new Dictionary<string, string>() {
               {"client_id", clientid},
               {"client_secret", clientSecret},
               {"refresh_token", decryptedToken},
               {"grant_type", "refresh_token"}
            };
            var resp = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(pairs));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return "";
            }
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("access_token").GetString() ?? "";
        }
    }
}
