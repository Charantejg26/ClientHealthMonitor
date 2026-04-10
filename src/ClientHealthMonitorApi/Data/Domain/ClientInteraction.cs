using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClientHealthMonitor.Data.Domain
{
    public class ClientInteraction
    {
        [Key]
        public required Guid Id { get; set; }
        [Required]
        public Guid ClientId { get; set; }
        public required Clients Client { get; set; }
        [Required]
        public Guid UserId { get; set; }
        public required Users User { get; set; }

        [Required, MaxLength(50)]
        public required string InteractionType { get; set; } // e.g., Call, Email, Meeting

        public required bool IsOutgoing { get; set; } // e.g., Incoming, Outgoing

        public string? gmail_message_id { get; set; }
        public string? gmail_thread_id { get; set; }

        public string? snippet { get; set; }
        public string? subject { get; set; }

        //public string? emailBody { get; set; }
        //public string? emailType { get; set; }
        public required DateTimeOffset message_date { get; set; }
        public string? raw_headers { get; set; }
        public string? parsed_headers { get; set; }


        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }
    }
}
