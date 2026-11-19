using System.Text.Json;
using System.Text;
using FilePortal.Models;
using FilePortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

const string ProvisionCommand = "--provision-admin";
var provisioning = args.Length > 0 && args[0] == ProvisionCommand;
var builder = WebApplication.CreateBuilder(provisioning ? [] : args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton<FileBrowserService>();
builder.Services.AddSingleton<UserStoreService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<PhotoLibraryService>();
builder.Services.AddSingleton<AlbumStoreService>();
builder.Services.AddSingleton<FavoriteStoreService>();
builder.Services.AddSingleton<ShareStoreService>();
builder.Services.AddSingleton<FileArchiveService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var dataDirectory = UserStoreService.GetDirectoryPath(builder.Configuration);
var webRoot = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var dataRelativeToWebRoot = Path.GetRelativePath(webRoot, dataDirectory);
if (!Path.IsPathRooted(dataRelativeToWebRoot) &&
    dataRelativeToWebRoot != ".." &&
    !dataRelativeToWebRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
{
    throw new InvalidOperationException("The FilePortal data directory must be outside wwwroot.");
}
var protection = builder.Services.AddDataProtection()
    .SetApplicationName("FilePortal")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows())
    protection.ProtectKeysWithDpapi(protectToLocalMachine: true);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnValidatePrincipal = async context =>
        {
            var username = context.Principal?.Identity?.Name;
            var stamp = context.Principal?.FindFirst(UserStoreService.SessionStampClaim)?.Value;
            var users = context.HttpContext.RequestServices.GetRequiredService<UserStoreService>();
            var user = username is null || stamp is null ? null : users.GetUser(username);
            if (user is null || !Guid.TryParse(stamp, out var sessionStamp) || sessionStamp != user.SecurityStamp ||
                context.Principal!.IsInRole("Admin") != user.IsAdmin)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            context.HttpContext.Items[typeof(UserRecord)] = user;
        };

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { error = "Forbidden" });
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (provisioning)
{
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: dotnet FilePortal.dll --provision-admin <username>");
        return 1;
    }

    Console.Write("Password: ");
    var password = ReadPassword();
    Console.Write("Confirm password: ");
    var confirmation = ReadPassword();
    if (string.IsNullOrWhiteSpace(password) || password != confirmation)
    {
        Console.Error.WriteLine("Enter matching, nonempty passwords.");
        return 1;
    }

    var users = app.Services.GetRequiredService<UserStoreService>();
    if (users.ProvisionAdmin(args[1], password) != UserChange.Updated)
    {
        Console.Error.WriteLine("An administrator already exists or the username is in use.");
        return 1;
    }

    Console.WriteLine("Administrator provisioned.");
    return 0;
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler(exceptionApp => exceptionApp.Run(context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return context.Response.WriteAsJsonAsync(new { error = "FilePortal could not complete the request." });
    }));
}

var clientFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (Path.GetExtension(context.File.Name) is ".html" or ".js" or ".css")
            context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
};
app.UseStaticFiles(clientFiles);
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/portal.html", () => Results.Redirect("/"));
app.MapGet("/FileManager", () => Results.Redirect("/files.html"));
app.MapFallback("/api/{**path}", () => Results.NotFound(new { error = "API route not found" }));
app.MapFallbackToFile("index.html", clientFiles);

app.Run();
return 0;

static string ReadPassword()
{
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? "";

    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return password.ToString();
        }

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            password.Length--;
        else if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }
}
