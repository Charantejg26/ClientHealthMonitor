using ClientHealthMonitor.Models;

namespace ClientHealthMonitor.Services.Interfaces
{
    public interface IGoogleAuthService
    {
        /// <summary>
        /// Exchange authorization code for access token / refresh token and id_token.
        /// </summary>
        Task<TokenResponse> ExchangeCodeAsync(string code);

        /// <summary>
        /// Get user info from Google userinfo endpoint using access token.
        /// </summary>
        Task<GoogleUser?> GetUserInfoAsync(TokenResponse tokenResponse);

        Task<bool> SendEmailAsync(Guid userId, string refreshToken, string accessTokenDb, DateTimeOffset tokenExpiry, EmailRequest request);
    }
}
