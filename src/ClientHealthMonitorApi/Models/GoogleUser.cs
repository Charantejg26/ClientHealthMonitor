namespace ClientHealthMonitor.Models
{
    public class GoogleUser
    {
        public Guid Id { get; set; }
        public string Sub { get; set; }
        public string Name { get; set; }
        public string Given_Name { get; set; }
        public string Family_Name { get; set; }
        public string Picture { get; set; }
        public string Email { get; set; }
        public string UserRole { get; set; }
        public bool Email_Verified { get; set; }
    }

    public class GoogleAuthRequest { public required string Code { get; set; } }

    public class SetUserRoleRequest
    {
        public required string UserRole { get; set; }
    }

    public class EmailRequest
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; } // Can be HTML
    }

    // Internal model for the Google API call
    public class GmailPayload
    {
        public string raw { get; set; }
    }
}
