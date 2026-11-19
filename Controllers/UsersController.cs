using FilePortal.Models;
using FilePortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FilePortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(UserStoreService store, FileBrowserService browser) : ControllerBase
{
    private UserRecord CurrentUser => (UserRecord)HttpContext.Items[typeof(UserRecord)]!;

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Get() =>
        Ok(store.GetUsers().Select(user => new { user.Username, user.FirstName, user.LastName, user.IsAdmin }));

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult Post([FromForm] string username, [FromForm] string password, [FromForm] bool isAdmin = false,
        [FromForm] string? firstName = null, [FromForm] string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return BadRequest(new { error = "Username, password, first name, and last name are required." });

        return store.AddUser(username, password, isAdmin, firstName, lastName) == UserChange.Updated
            ? CreatedAtAction(nameof(Get), null)
            : Conflict(new { error = "Username already exists." });
    }

    public sealed class UpdateUserRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{username}")]
    public IActionResult Put(string username, [FromBody] UpdateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { error = "First name and last name are required." });

        return store.UpdateProfile(username, request.FirstName, request.LastName) == UserChange.Updated
            ? Ok(new { username, firstName = request.FirstName.Trim(), lastName = request.LastName.Trim() })
            : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{username}")]
    public IActionResult Delete(string username)
    {
        if (IsCurrentUser(username))
            return BadRequest(new { error = "Cannot delete yourself." });

        return store.RemoveUser(username) switch
        {
            UserChange.Updated => NoContent(),
            UserChange.LastAdmin => BadRequest(new { error = "Cannot remove the last admin." }),
            _ => NotFound()
        };
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{username}/promote")]
    public IActionResult Promote(string username) => ChangeRole(username, isAdmin: true);

    [Authorize(Roles = "Admin")]
    [HttpPost("{username}/demote")]
    public IActionResult Demote(string username)
    {
        if (IsCurrentUser(username))
            return BadRequest(new { error = "Cannot demote yourself." });

        return ChangeRole(username, isAdmin: false);
    }

    private IActionResult ChangeRole(string username, bool isAdmin) => store.SetAdmin(username, isAdmin) switch
    {
        UserChange.Updated => Ok(),
        UserChange.Conflict => Conflict(new { error = "User already has that role." }),
        UserChange.LastAdmin => BadRequest(new { error = "Cannot demote the last admin." }),
        _ => NotFound()
    };

    [HttpPost("{username}/password")]
    public IActionResult ChangePassword(string username, [FromForm] string? currentPassword, [FromForm] string newPassword)
    {
        var isSelf = IsCurrentUser(username);
        if (!isSelf && !CurrentUser.IsAdmin)
            return Forbid();

        if (string.IsNullOrEmpty(newPassword) || (isSelf && string.IsNullOrEmpty(currentPassword)))
            return BadRequest(new { error = isSelf ? "Current and new passwords are required." : "New password is required." });

        return store.UpdatePassword(username, newPassword, isSelf ? currentPassword : null) switch
        {
            UserChange.Updated => Ok(),
            UserChange.InvalidPassword => BadRequest(new { error = "Current password is incorrect." }),
            _ => NotFound()
        };
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{username}/folders")]
    public IActionResult SetFolders(string username, [FromBody] List<string> folderPaths)
    {
        var roots = browser.GetRoots(CurrentUser);
        var folders = new HashSet<string>(FileBrowserService.PathComparer);
        foreach (var folder in folderPaths)
        {
            if (folder is null || !browser.TryResolve(folder, roots, out var canonicalPath) || !Directory.Exists(canonicalPath))
                return BadRequest(new { error = "Choose existing folders from the configured roots." });

            folders.Add(canonicalPath);
        }

        var assignedFolders = folders.ToList();
        return store.SetAllowedFolders(username, assignedFolders) == UserChange.Updated
            ? Ok(new { folders = assignedFolders })
            : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{username}/folders")]
    public IActionResult GetFolders(string username)
    {
        var target = store.GetUser(username);
        return target is null ? NotFound() : Ok(new { username = target.Username, folders = target.AllowedFolders });
    }

    private bool IsCurrentUser(string username) =>
        string.Equals(CurrentUser.Username, username, StringComparison.OrdinalIgnoreCase);
}
