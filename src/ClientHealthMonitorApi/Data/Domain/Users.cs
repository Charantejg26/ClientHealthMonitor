using System.ComponentModel.DataAnnotations;

namespace ClientHealthMonitor.Data.Domain
{
    public class Users
    {
        [Key]
        public required Guid Id { get; set; }
        public required string Email { get; set; }
        public required string GoogleId { get; set; }
        public required string Given_Name { get; set; }
        public required string Family_Name { get; set; }

        public required string TimeZone { get; set; }

        public required string UserRole { get; set; }

        public DateTimeOffset? LastSeenAt { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }

        public ICollection<Clients> Clients { get; set; }
        public ICollection<ClientInteraction> ClientInteractions { get; set; }
        public ICollection<Feedback> Feedbacks { get; set; }

        // One-to-one inverse navigation to AuthTokens
        public AuthTokens? AuthToken { get; set; }

    }
}
