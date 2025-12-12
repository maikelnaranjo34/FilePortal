using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly UserStoreService _store;

        public UsersController(UserStoreService store)
        {
            _store = store;
        }

        [AllowAnonymous]
        [HttpGet("init")]
        public IActionResult GetInit()
        {
            var isEmpty = _store.IsEmpty();
            return Ok(new { isEmpty });
        }

        [Authorize]
        [HttpGet("debug")]
        public IActionResult GetDebug()
        {
            var username = User.Identity?.Name ?? "unknown";
            var isAdmin = User.IsInRole("Admin");
            var allUsers = _store.GetUsers().Select(u => new { u.Username, u.IsAdmin }).ToList();
            return Ok(new { currentUser = username, currentUserIsAdmin = isAdmin, allUsers });
        }

        [AllowAnonymous]
        [HttpPost("init")]
        public IActionResult PostInit([FromForm] string username, [FromForm] string password)
        {
            if (!_store.IsEmpty())
                return BadRequest(new { error = "Users already exist." });

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return BadRequest(new { error = "username and password are required" });

            var ok = _store.AddUser(username, password, isAdmin: true);
            if (!ok) return StatusCode(500, new { error = "Failed to create admin." });
            return CreatedAtAction(null, null);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Get()
        {
            var users = _store.GetUsers().Select(u => new { u.Username, u.IsAdmin });
            return Ok(users);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Post([FromForm] string username, [FromForm] string password, [FromForm] bool isAdmin = false)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return BadRequest(new { error = "username and password are required" });

            var ok = _store.AddUser(username, password, isAdmin);
            if (!ok) return Conflict(new { error = "user exists or invalid input" });
            return CreatedAtAction(nameof(Get), null);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{username}")]
        public IActionResult Delete(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest();

            var current = User.Identity?.Name ?? string.Empty;
            if (string.Equals(current, username, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Cannot delete yourself." });
            }

            var target = _store.GetUser(username);
            if (target == null) return NotFound();

            if (target.IsAdmin)
            {
                var adminCount = _store.CountAdmins();
                if (adminCount <= 1)
                {
                    return BadRequest(new { error = "Cannot remove the last admin." });
                }
            }

            var ok = _store.RemoveUser(username);
            if (!ok) return NotFound();
            return NoContent();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{username}/promote")]
        public IActionResult Promote(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest();
            var target = _store.GetUser(username);
            if (target == null) return NotFound();
            if (target.IsAdmin) return BadRequest(new { error = "User is already an admin." });
            var ok = _store.SetAdmin(username, true);
            if (!ok) return StatusCode(500);
            return Ok();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{username}/demote")]
        public IActionResult Demote(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest();
            var current = User.Identity?.Name ?? string.Empty;
            if (string.Equals(current, username, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Cannot demote yourself." });
            }

            var target = _store.GetUser(username);
            if (target == null) return NotFound();
            if (!target.IsAdmin) return BadRequest(new { error = "User is not an admin." });

            var adminCount = _store.CountAdmins();
            if (adminCount <= 1)
            {
                return BadRequest(new { error = "Cannot demote the last admin." });
            }

            var ok = _store.SetAdmin(username, false);
            if (!ok) return StatusCode(500);
            return Ok();
        }

        // POST /api/users/{username}/password
        // Allows an admin to set any user's password, or a user to change their own password
        [Authorize]
        [HttpPost("{username}/password")]
        public IActionResult ChangePassword(string username, [FromForm] string currentPassword, [FromForm] string newPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(newPassword)) return BadRequest(new { error = "username and newPassword are required" });

            var current = User.Identity?.Name ?? string.Empty;
            var isAdmin = User.IsInRole("Admin");

            // If not admin, only allow changing own password and require currentPassword
            if (!isAdmin)
            {
                if (!string.Equals(current, username, StringComparison.OrdinalIgnoreCase))
                    return Forbid();

                if (string.IsNullOrEmpty(currentPassword) || !_store.ValidateCredentials(username, currentPassword))
                    return BadRequest(new { error = "current password invalid" });
            }

            var ok = _store.UpdatePassword(username, newPassword);
            if (!ok) return NotFound(new { error = "user not found or update failed" });
            return Ok();
        }
    }
}
