using System.ComponentModel.DataAnnotations;

namespace ClientHealthMonitor.Data.Domain
{
    public class Alerts
    {
        [Key]
        public required Guid Id { get; set; }
        [Required]
        public Guid ClientId { get; set; }
        public required Clients Client { get; set; }
        [Required]
        public Guid UserId { get; set; }
        public required Users User { get; set; }

        public required AlertType AlertType { get; set; } 
        public required string Message { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset DissmissedAt { get; set; }
    }
}
