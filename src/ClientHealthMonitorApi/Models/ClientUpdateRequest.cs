namespace ClientHealthMonitor.Models
{
    public class ClientUpdateRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Status { get; set; }
        public string? Notes { get; set; }
    }
}
