using FilePortal.Models;
using FilePortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FilePortal.Controllers;

[ApiController]
[Route("api/duplicates")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DuplicatesController(DuplicateFinderService duplicates) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? rootName, CancellationToken cancellationToken)
    {
        rootName = rootName?.Trim();
        if (string.IsNullOrEmpty(rootName) || rootName.Length > 200)
            return BadRequest(new { error = "Choose an available root name." });
        var user = (UserRecord)HttpContext.Items[typeof(UserRecord)]!;
        try
        {
            return Ok(await duplicates.FindAsync(user, rootName, 5_000, cancellationToken));
        }
        catch (KeyNotFoundException exception) { return BadRequest(new { error = exception.Message }); }
        catch (DirectoryNotFoundException exception) { return NotFound(new { error = exception.Message }); }
    }
}
