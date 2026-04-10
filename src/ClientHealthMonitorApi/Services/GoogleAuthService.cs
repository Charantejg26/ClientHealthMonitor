using ClientHealthMonitor.Data;
using ClientHealthMonitor.Data.Domain;
using ClientHealthMonitor.Models;
using ClientHealthMonitor.Services.Interfaces;
using CommonLib;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClientHealthMonitor.Services
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<GoogleAuthService> _logger;
        private readonly GoogleSettings _googlesettings;
        private readonly AppDbContext _dbContext;
        private readonly ITokenEncryptionService _cryptoService;
        private readonly GoogleApiTokenService _tokenService;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public GoogleAuthService(
            IHttpClientFactory httpFactory,
            ILogger<GoogleAuthService> logger,
            IConfiguration config,
            ITokenEncryptionService cryptoService,
            AppDbContext dbContext,
            GoogleApiTokenService tokenService)
        {
            _httpFactory = httpFactory;
            _logger = logger;
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
            _googlesettings = config?.GetSection("Authentication:Google").Get<GoogleSettings>()
                ?? throw new ArgumentNullException(nameof(config), "Google settings configuration section is missing or invalid.");
            _tokenService = tokenService;
            _clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
            _clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "";
        }


        public async Task<TokenResponse> ExchangeCodeAsync(string code)
        {
            var client = _httpFactory.CreateClient();
            var tokenUrl = "https://oauth2.googleapis.com/token";
            var dict = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["redirect_uri"] = _googlesettings.redirect_uri ?? "postmessage",
                ["grant_type"] = "authorization_code"
            };

            var resp = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(dict));
            var body = await resp.Content.ReadAsStringAsync();

            TokenResponse tokenResponse;

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google token exchange failed: {Status} {Body}", resp.StatusCode, body);
                return tokenResponse = new TokenResponse
                {
                    Success = false,
                    error = $"HTTP {resp.StatusCode}: {body}"
                };
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString();
            var accessToken_expiresIn = root.TryGetProperty("expires_in", out var acei) ? acei.GetInt32() : (int?)null;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var refreshToken_expiresIn = root.TryGetProperty("refresh_token_expires_in", out var rtei) ? rtei.GetInt32() : (int?)null;
            var idToken = root.TryGetProperty("id_token", out var idt) ? idt.GetString() : null;
            var scope = root.TryGetProperty("scope", out var scp) ? scp.GetString() : null;
            var tokenType = root.TryGetProperty("token_type", out var tkt) ? tkt.GetString() : null;

            return tokenResponse = new TokenResponse
            {
                Success = true,
                access_token = accessToken,
                expires_in = accessToken_expiresIn ?? 0,
                refresh_token = refreshToken,
                refresh_token_expires_in = refreshToken_expiresIn ?? 0,
                scope = scope,
                token_type = tokenType,
                id_token = idToken,
                error = null
            };
        }


        public async Task<GoogleUser?> GetUserInfoAsync(TokenResponse tokenResponse)
        {
            var client = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.access_token);

            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch userinfo: {Status}", resp.StatusCode);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            var googleUser = JsonSerializer.Deserialize<GoogleUser>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (googleUser == null)
                return null;

            // Check if the user already exists in the database
            var existingUser = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.GoogleId == googleUser.Sub);

            if (existingUser == null)
            {
                var user = new Data.Domain.Users
                {
                    Id = Guid.NewGuid(),
                    GoogleId = googleUser.Sub,
                    Email = googleUser.Email,
                    Given_Name = googleUser.Given_Name,
                    Family_Name = googleUser.Family_Name,
                    TimeZone = "America/New_York",
                    UserRole = "",
                    LastSeenAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync();

                // Reflect DB id back to returned GoogleUser
                googleUser.Id = user.Id;
                googleUser.UserRole = user.UserRole;
            }
            else
            {
                // Update last seen timestamp
                googleUser.Id = existingUser.Id;
                googleUser.UserRole = existingUser.UserRole;
                existingUser.LastSeenAt = DateTime.UtcNow;

                _dbContext.Users.Update(existingUser);
                await _dbContext.SaveChangesAsync();
            }

            var existingToken = await _dbContext.AuthTokens
            .FirstOrDefaultAsync(t => t.UserId == googleUser.Id && t.Provider == "Google");

            if (existingToken == null)
            {
                // Insert token
                var newToken = new Data.Domain.AuthTokens
                {
                    Id = Guid.NewGuid(),
                    UserId = googleUser.Id,
                    AccessToken = !string.IsNullOrWhiteSpace(tokenResponse.access_token) ? _cryptoService.Encrypt(tokenResponse.access_token) : "",
                    RefreshToken = !string.IsNullOrWhiteSpace(tokenResponse.refresh_token) ? _cryptoService.Encrypt(tokenResponse.refresh_token) : "",
                    Provider = "Google",
                    Scope = tokenResponse.scope ?? "",
                    AccessToken_ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.AuthTokens.Add(newToken);
            }
            else
            {
                // Update existing token
                existingToken.AccessToken = !string.IsNullOrWhiteSpace(tokenResponse.access_token) ? _cryptoService.Encrypt(tokenResponse.access_token) : existingToken.AccessToken;

                // RefreshToken may be null sometimes if Google doesn't send new one.
                existingToken.RefreshToken = !string.IsNullOrWhiteSpace(tokenResponse.refresh_token) ? _cryptoService.Encrypt(tokenResponse.refresh_token) : "";

                existingToken.Scope = tokenResponse.scope ?? existingToken.Scope;
                existingToken.AccessToken_ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                existingToken.UpdatedAt = DateTime.UtcNow;

                _dbContext.AuthTokens.Update(existingToken);
            }

            await _dbContext.SaveChangesAsync();

            return googleUser;
        }

        public async Task<bool> SendEmailAsync(Guid userId, string refreshToken, string accessTokenDb, DateTimeOffset tokenExpiry, EmailRequest request)
        {
            // Decrypt the token
            var accessToken = await _tokenService.EnsureAccessToken(userId, _clientId, _clientSecret, refreshToken, accessTokenDb, tokenExpiry);

            // Construct the RFC 2822 email message
            var messageLines = new List<string>
            {
                $"To: {request.To}",
                $"Subject: {request.Subject}",
                "MIME-Version: 1.0",
                "Content-Type: text/html; charset=utf-8",
                "", // Empty line separates headers from body
                request.Body
            };

            string message = string.Join("\r\n", messageLines);

            // Base64Url encode (replace characters for Google's API)
            var base64Raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(message))
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            // Call Google API using your consistent HttpClient pattern
            using var client = new HttpClient();
            var url = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var payload = new GmailPayload { raw = base64Raw };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

            var resp = await client.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Gmail API failed: {Error}", error);
                return false;
            }

            return true;
        }

    }
}
