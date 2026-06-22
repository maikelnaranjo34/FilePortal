using System;

namespace FilePortal.Models
{
    public class UserRecord
    {
        public string Username { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty; // base64
        public string Hash { get; set; } = string.Empty; // base64
        public DateTime CreatedUtc { get; set; }
        public bool IsAdmin { get; set; }
        
        // Folder access control: list of folder paths user can access
        // If empty, admin users can access all, regular users can access none
        // If populated, only these paths are accessible
        public List<string> AllowedFolders { get; set; } = new();
    }
}
