using FilePortal.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add controllers and force camelCase JSON (so JS sees isDirectory, fullPath, etc.)
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// File browser service
builder.Services.AddSingleton<FileBrowserService>();
// User store for managing users
builder.Services.AddSingleton<UserStoreService>();

// Swagger (optional)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        
        // Return JSON 401 for API calls instead of redirecting
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            }
            context.Response.Redirect("/login.html");
            return Task.CompletedTask;
        };
        
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new { error = "Forbidden" });
            }
            context.Response.Redirect("/login.html");
            return Task.CompletedTask;
        };
    });

// Authorization
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve wwwroot (index.html, js, css)
app.UseStaticFiles();

app.UseRouting();

// Authentication & Authorization middleware (must come after routing, before endpoints)
app.UseAuthentication();
app.UseAuthorization();

// Map controllers (FilesController)
app.MapControllers();

// Fallback to index.html for unknown routes
app.MapFallbackToFile("/index.html");

// Let IIS handle URL binding - don't specify URLs in code
app.Run();

//app.Run("http://0.0.0.0:5251");

