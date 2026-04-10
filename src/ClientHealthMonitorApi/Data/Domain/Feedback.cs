using System.ComponentModel.DataAnnotations;

namespace ClientHealthMonitor.Data.Domain
{
    public class Feedback
    {
        [Key]
        public required Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }
        public required Users User { get; set; }


        [Required]
        public Guid ClientId { get; set; }
        public required Clients Client { get; set; }


        public Guid? InteractionId { get; set; }
        public ClientInteraction Interaction { get; set; }


        // Free-text feedback
        public required string FeedbackText { get; set; }


        // Optional rating 1..5
        public int? Rating { get; set; }

        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }
    }
}
