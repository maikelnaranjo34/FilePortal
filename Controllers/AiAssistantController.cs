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
public sealed class AiAssistantController(AiSettingsService settingsStore, IHttpClientFactory clients,
    AuthorizedFileSearchService fileSearch, DuplicateFinderService duplicateFinder, FileBrowserService fileBrowser) : ControllerBase
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
        var availableRoots = string.Join(", ", fileBrowser.GetRoots(currentUser).Select(root => root.Name));
        var body = new
        {
            model = settings.OpenAiModel,
            instructions = "You are Nesti, the concise and friendly FilePortal assistant. Help the signed-in user manage and understand their files. Never claim to have read, moved, changed, or deleted a file unless a server tool explicitly reports that action. Never request API keys or passwords. Destructive actions always require a separate visual confirmation in FilePortal. Duplicate scans are read-only. The current user is " + currentUser.Username + ". Available root display names: " + availableRoots + ".",
            input,
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "search_files",
                    description = "Search file and folder names across only the folders the signed-in FilePortal user is authorized to access.",
                    parameters = new
                    {
                        type = "object",
                        properties = new { query = new { type = "string", description = "A file or folder name, or part of one." } },
                        required = new[] { "query" },
                        additionalProperties = false
                    },
                    strict = true
                },
                new
                {
                    type = "function",
                    name = "find_duplicates",
                    description = "Find exact content duplicates and similar filenames inside one FilePortal root available to the signed-in user. This is read-only and never deletes files.",
                    parameters = new
                    {
                        type = "object",
                        properties = new { rootName = new { type = "string", description = "The exact display name of an available FilePortal root." } },
                        required = new[] { "rootName" },
                        additionalProperties = false
                    },
                    strict = true
                }
            },
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

            var toolCalls = ExtractToolCalls(payload.RootElement).ToList();
            if (toolCalls.Count > 0)
            {
                var outputs = new List<object>();
                foreach (var call in toolCalls)
                {
                    outputs.Add(new
                    {
                        type = "function_call_output",
                        call_id = call.CallId,
                        output = await ExecuteTool(call, currentUser, cancellationToken)
                    });
                }
                var continuation = new
                {
                    model = settings.OpenAiModel,
                    previous_response_id = payload.RootElement.GetProperty("id").GetString(),
                    input = outputs,
                    max_output_tokens = 1200
                };
                using var continuationRequest = CreateOpenAiRequest(key, continuation);
                using var continuationResponse = await clients.CreateClient("AiProviders").SendAsync(continuationRequest, cancellationToken);
                using var continuationPayload = await JsonDocument.ParseAsync(
                    await continuationResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (!continuationResponse.IsSuccessStatusCode)
                    return StatusCode(StatusCodes.Status502BadGateway, new { error = ProviderError(continuationPayload) });
                var continuedReply = ExtractText(continuationPayload.RootElement);
                return string.IsNullOrWhiteSpace(continuedReply)
                    ? StatusCode(StatusCodes.Status502BadGateway, new { error = "OpenAI returned no assistant message after searching." })
                    : Ok(new { reply = continuedReply });
            }

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

    [HttpGet("voice/tokens")]
    public async Task<IActionResult> VoiceTokens(CancellationToken cancellationToken)
    {
        var settings = settingsStore.Get();
        var key = settingsStore.GetAssemblyAiApiKey();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(key))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "AssemblyAI voice is not configured." });

        try
        {
            var sttTask = MintAssemblyToken(
                "https://streaming.assemblyai.com/v3/token?expires_in_seconds=60&max_session_duration_seconds=300",
                key, cancellationToken);
            var ttsTask = MintAssemblyToken(
                "https://token.us.assemblyai.com/v1/token?product=tts&expires_in_seconds=60",
                key, cancellationToken);
            await Task.WhenAll(sttTask, ttsTask);
            return Ok(new
            {
                sttToken = sttTask.Result,
                ttsToken = ttsTask.Result,
                speechModel = settings.AssemblyAiSpeechModel,
                ttsModel = settings.AssemblyAiTtsModel,
                voiceId = settings.AssemblyAiVoiceId,
                language = settings.Language
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "AssemblyAI voice tokens could not be created." });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "AssemblyAI did not respond in time." });
        }
    }

    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates([FromQuery] string? rootName, CancellationToken cancellationToken)
    {
        rootName = rootName?.Trim();
        if (string.IsNullOrEmpty(rootName) || rootName.Length > 200)
            return BadRequest(new { error = "Choose an available root name." });
        var user = (UserRecord)HttpContext.Items[typeof(UserRecord)]!;
        try
        {
            return Ok(await duplicateFinder.FindAsync(user, rootName, 5_000, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (DirectoryNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    private async Task<string> MintAssemblyToken(string url, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", key);
        using var response = await clients.CreateClient("AiProviders").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("AssemblyAI rejected the token request.");
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return payload.RootElement.GetProperty("token").GetString()
            ?? throw new HttpRequestException("AssemblyAI returned an invalid token response.");
    }

    private sealed record ToolCall(string Name, string CallId, string Arguments);

    private static IEnumerable<ToolCall> ExtractToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output)) yield break;
        foreach (var item in output.EnumerateArray())
            if (item.TryGetProperty("type", out var type) && type.GetString() == "function_call" &&
                item.TryGetProperty("name", out var name) && item.TryGetProperty("call_id", out var callId) &&
                item.TryGetProperty("arguments", out var arguments))
                yield return new ToolCall(name.GetString() ?? "", callId.GetString() ?? "", arguments.GetString() ?? "{}");
    }

    private async Task<string> ExecuteTool(ToolCall call, UserRecord user, CancellationToken cancellationToken)
    {
        try
        {
            using var arguments = JsonDocument.Parse(call.Arguments);
            if (call.Name == "search_files")
            {
                var query = arguments.RootElement.GetProperty("query").GetString()?.Trim();
                if (string.IsNullOrEmpty(query) || query.Length > 200)
                    return JsonSerializer.Serialize(new { error = "Search terms must contain 1 to 200 characters." });
                var results = fileSearch.Search(user, query, 25, cancellationToken)
                    .Select(item => new { item.Name, item.Path, item.IsDirectory, item.Size, item.LastModified })
                    .ToList();
                return JsonSerializer.Serialize(new { query, count = results.Count, results });
            }
            if (call.Name == "find_duplicates")
            {
                var rootName = arguments.RootElement.GetProperty("rootName").GetString()?.Trim();
                if (string.IsNullOrEmpty(rootName) || rootName.Length > 200)
                    return JsonSerializer.Serialize(new { error = "Choose a valid root name." });
                var report = await duplicateFinder.FindAsync(user, rootName, 5_000, cancellationToken);
                return JsonSerializer.Serialize(report);
            }
            return JsonSerializer.Serialize(new { error = "Unsupported tool." });
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "The tool request was invalid." });
        }
        catch (Exception exception) when (exception is KeyNotFoundException or DirectoryNotFoundException)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    private static HttpRequestMessage CreateOpenAiRequest(string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
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
