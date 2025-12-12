using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net;
using System.Net.Mail;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly string _validUser;
        private readonly string _validPass;
        private readonly UserStoreService _userStore;
        private readonly IConfiguration _configuration;

        public AuthController(Microsoft.Extensions.Configuration.IConfiguration configuration, UserStoreService userStore)
        {
            _userStore = userStore;
            _configuration = configuration;

            // Prefer environment variables (typical ASP.NET Core mapping uses __ for nested keys),
            // fall back to configuration values, then to hard-coded defaults.
            var envUser = Environment.GetEnvironmentVariable("Auth__Username")
                          ?? Environment.GetEnvironmentVariable("AUTH__USERNAME");
            var envPass = Environment.GetEnvironmentVariable("Auth__Password")
                          ?? Environment.GetEnvironmentVariable("AUTH__PASSWORD");

            _validUser = !string.IsNullOrEmpty(envUser)
                ? envUser
                : configuration["Auth:Username"] ?? "maikel";

            _validPass = !string.IsNullOrEmpty(envPass)
                ? envPass
                : configuration["Auth:Password"] ?? "Cafeta0823$";
        }

        // POST /api/auth/login (no auth required)
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
        {
            // First-run: if no users exist, create the initial admin from the first login attempt
                if (_userStore.IsEmpty())
                {
                    var created = _userStore.AddUser(username, password, isAdmin: true);
                    if (!created) return StatusCode(500, new { message = "Failed to create initial admin." });
                }

                // First, try the user store
                var user = _userStore.Authenticate(username, password);
            bool isAdmin = false;

            if (user != null)
            {
                isAdmin = user.IsAdmin;
            }
            else
            {
                // Fallback to env/config credentials (treated as admin)
                if (username != _validUser || password != _validPass)
                {
                    return Unauthorized(new { message = "Invalid username or password" });
                }
                isAdmin = true;
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username)
            };
            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });

            // Send email notification
            _ = Task.Run(() => SendLoginNotificationEmail(username, HttpContext.Connection.RemoteIpAddress?.ToString()));

            return Ok(new { message = "Logged in" });
        }

        // POST /api/auth/logout
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

        // GET /api/auth/me  (optional helper)
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new { user = User.Identity?.Name });
        }

        private void SendLoginNotificationEmail(string username, string? ipAddress)
        {
            try
            {
                var smtpHost = _configuration["Email:SmtpHost"];
                var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
                var smtpUsername = _configuration["Email:SmtpUsername"];
                var smtpPassword = _configuration["Email:SmtpPassword"];
                var fromEmail = _configuration["Email:FromEmail"];
                var toEmail = _configuration["Email:ToEmail"];
                var enableSsl = bool.Parse(_configuration["Email:EnableSsl"] ?? "true");

                if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUsername) || 
                    string.IsNullOrEmpty(smtpPassword) || string.IsNullOrEmpty(toEmail))
                {
                    Console.WriteLine("Email configuration incomplete. Skipping notification.");
                    return;
                }

                // Get user details
                var userRecord = _userStore.GetUser(username);
                var fullName = "";
                if (userRecord != null && (!string.IsNullOrWhiteSpace(userRecord.FirstName) || !string.IsNullOrWhiteSpace(userRecord.LastName)))
                {
                    fullName = $"{userRecord.FirstName} {userRecord.LastName}".Trim();
                }

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail ?? smtpUsername),
                    Subject = $"FilePortal Login: {username}",
                    Body = $@"New login to FilePortal

Username: {username}
{(string.IsNullOrEmpty(fullName) ? "" : $"Name: {fullName}\n")}IP Address: {ipAddress ?? "Unknown"}
Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

This is an automated notification from your FilePortal application.",
                    IsBodyHtml = false
                };

                mailMessage.To.Add(toEmail);

                using var smtpClient = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                    EnableSsl = enableSsl
                };

                smtpClient.Send(mailMessage);
                Console.WriteLine($"Login notification email sent for user: {username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send login notification email: {ex.Message}");
                // Don't throw - email failure shouldn't prevent login
            }
        }
    }
}
