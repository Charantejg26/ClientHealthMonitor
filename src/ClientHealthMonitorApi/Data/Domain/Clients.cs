using System.ComponentModel.DataAnnotations;

namespace ClientHealthMonitor.Data.Domain
{
    public class Clients
    {
        [Key]
        public required Guid Id { get; set; }        
        [Required]
        public Guid UserId { get; set; }
        public Users? User { get; set; }
        public required string Client_Email { get; set; }
        public required string Client_Name { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }        
        public string? Notes { get; set; } 
        public required DateTimeOffset Last_Contact_At { get; set; }        
        //public required string EngagementStage { get; set; }  // Lead, Onboarding, ActiveDeal, SteadyState, RenewalWindow, Churned.
        public ClientScore? Score { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public required DateTimeOffset UpdatedAt { get; set; }

        public ICollection<ClientInteraction> Interactions { get; set; }
        public ICollection<Feedback> Feedbacks { get; set; }
    }
}
