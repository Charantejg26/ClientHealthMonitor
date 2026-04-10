namespace ClientHealthMonitor.Models
{
    public class TokenResponse
    {
        public string? access_token { get; set; }
        public int expires_in { get; set; }
        public string? refresh_token { get; set; }
        public string? scope { get; set; }
        public string? token_type { get; set; }
        public string? id_token { get; set; }
        public int refresh_token_expires_in { get; set; }
        public string? error { get; set; }
        public bool Success { get; set; } = false;
    }
}
