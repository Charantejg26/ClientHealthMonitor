
namespace ClientHealthMonitor.Models
{
    public class ClientCreateRequest
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? Phone { get; set; }
        public required string ClientStatus{ get; set; }  // Active/Prospect/Cold
        public string? Notes { get; set; }
    }
    
}
