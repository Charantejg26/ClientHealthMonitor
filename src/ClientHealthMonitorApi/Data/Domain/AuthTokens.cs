using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClientHealthMonitor.Data.Domain
{
    public class AuthTokens
    {
        [Key]
        public required Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        public required string Provider { get; set; }  
        public string? AccessToken { get; set; } = null;
        public string? RefreshToken { get; set; } = null;
        public string? Scope { get; set; } = null;
        public string? gmail_last_history_id { get; set; } = null;
        public DateTimeOffset? last_initial_sync_started_at { get; set; } = null;

        public DateTimeOffset AccessToken_ExpiresAt { get; set; }

        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }

        // One-to-one: navigation to the single User that owns this token
        [ForeignKey(nameof(UserId))]
        public Users? User { get; set; }
    }
}
