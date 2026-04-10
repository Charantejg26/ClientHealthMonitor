using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClientHealthMonitor.Data.Domain
{
    public class ClientScore
    {
        [Key]
        public required Guid Id { get; set; }
        [Required]
        public Guid ClientId { get; set; }
        public required Clients Client { get; set; }


        // 0..100 score       
        public int Score { get; set; }

        public int RelationshipHealth { get; set; }

        public int ExecutionHealth { get; set; } // 0..100
        public int OpportunityBoost { get; set; } // 0..100 (informational)
        public double DataCoverage { get; set; } // 0..1 (coverage/volume)
        public string Tier { get; set; } = "Unknown";


        //public double Confidence { get; set; } // 0.0 to 1.0
        public string? Summary { get; set; } // short rationale or highlights



        // CalculatedAt can be different from CreatedAt if computed externally
        public required DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;

        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }
    }
}
