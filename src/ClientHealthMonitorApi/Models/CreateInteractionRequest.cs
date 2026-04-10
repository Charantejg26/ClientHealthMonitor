using System.ComponentModel.DataAnnotations;

namespace ClientHealthMonitor.Models
{
    public class CreateInteractionRequest
    {
        [Required]
        public string InteractionType { get; set; } = null!; // e.g. Call, Email, Meeting

        public string? Content { get; set; }

        public DateTimeOffset? InteractionOccurredAt { get; set; }

        public IFormFile? File { get; set; } // optional upload (PDF, image, etc.)
    }

    public class UpdateInteractionRequest
    {
        public string? InteractionType { get; set; }
        public string? Content { get; set; }
        public DateTimeOffset? InteractionOccurredAt { get; set; }
    }
}
