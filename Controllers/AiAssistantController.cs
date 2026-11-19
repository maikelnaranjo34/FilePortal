using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FilePortal.Models;
using FilePortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FilePortal.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiAssistantController(AiSettingsService settingsStore, IHttpClientFactory clients) : ControllerBase
{
    public sealed class Message
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    public sealed class ChatRequest
    {
        public string? Message { get; set; }
        public List<Message>? History { get; set; }
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var settings = settingsStore.Get();
        return Ok(new
        {
            available = settings.Enabled && !string.IsNullOrWhiteSpace(settings.ProtectedOpenAiApiKey),
            voiceAvailable = settings.Enabled && !string.IsNullOrWhiteSpace(settings.ProtectedAssemblyAiApiKey),
            settings.OpenAiModel,
            settings.Language
        });
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 4_000)
            return BadRequest(new { error = "Enter a message between 1 and 4,000 characters." });

        var settings = settingsStore.Get();
        var key = settingsStore.GetOpenAiApiKey();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(key))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The FileNest assistant is not configured." });

        var input = (request.History ?? []).TakeLast(12)
            .Where(item => (item.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(item.Content))
            .Select(item => new { role = item.Role, content = item.Content!.Length > 4_000 ? item.Content[..4_000] : item.Content })
            .Cast<object>()
            .Append(new { role = "user", content = message })
            .ToArray();
        var currentUser = (UserRecord)HttpContext.Items[typeof(UserRecord)]!;
        var body = new
        {
            model = settings.OpenAiModel,
            instructions = "You are Nesti, the concise and friendly FilePortal assistant. Help the signed-in user manage and understand their files. Never claim to have read, moved, changed, or deleted a file unless a server tool explicitly reports that action. Never request API keys or passwords. Destructive actions always require a separate visual confirmation in FilePortal. The current user is " + currentUser.Username + ".",
            input,
            max_output_tokens = 1200
        };

        using var providerRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        providerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        providerRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await clients.CreateClient("AiProviders").SendAsync(providerRequest, cancellationToken);
            using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ProviderError(payload) });

            var reply = ExtractText(payload.RootElement);
            return string.IsNullOrWhiteSpace(reply)
                ? StatusCode(StatusCodes.Status502BadGateway, new { error = "OpenAI returned no assistant message." })
                : Ok(new { reply });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "OpenAI could not be reached." });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "OpenAI did not respond in time." });
        }
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output)) return "";
        return string.Join("\n", output.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message" &&
                item.TryGetProperty("content", out _))
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(content => content.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                content.TryGetProperty("text", out _))
            .Select(content => content.GetProperty("text").GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string ProviderError(JsonDocument payload) =>
        payload.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
            ? "OpenAI rejected the request: " + message.GetString()
            : "OpenAI rejected the request.";
}
