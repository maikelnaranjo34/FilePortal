using System;

namespace FilePortal.Models
{
    public class UserRecord
    {
        public string Username { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty; // base64
        public string Hash { get; set; } = string.Empty; // base64
        public DateTime CreatedUtc { get; set; }
        public bool IsAdmin { get; set; }
    }
}
