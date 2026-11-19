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
        var clientActions = new List<object>();
        var body = new
        {
            model = settings.OpenAiModel,
            instructions = "You are Nesti, the concise FilePortal action assistant. When the user asks to open, navigate, search, download, or prepare an operation, call the appropriate tool instead of describing steps. Do not narrate what you are thinking or what you intend to do. Reply in at most two short sentences after tools finish. Never claim an action happened unless its tool reports it was prepared or completed. Never request API keys or passwords. Destructive actions require separate visual confirmation. Duplicate scans are read-only. The current user is " + currentUser.Username + ". Available root display names: " + availableRoots + ".",
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
                },
                new
                {
                    type = "function", name = "list_roots",
                    description = "List the FilePortal roots currently available to the signed-in user.",
                    parameters = new { type = "object", properties = new { }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "list_folder",
                    description = "List the immediate contents of an authorized folder path.",
                    parameters = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "open_page",
                    description = "Navigate the user's browser to a FilePortal page.",
                    parameters = new { type = "object", properties = new { page = new { type = "string", @enum = new[] { "home", "files", "gallery", "shared", "favorites", "trash", "settings" } } }, required = new[] { "page" }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "open_category",
                    description = "Open a specific file category inside a specific available FilePortal root. Use this for requests such as open Videos on My_Apps F.",
                    parameters = new { type = "object", properties = new {
                        rootName = new { type = "string", description = "Exact available root display name." },
                        category = new { type = "string", @enum = new[] { "pictures", "videos", "pdfs", "notes", "documents" } }
                    }, required = new[] { "rootName", "category" }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "open_file",
                    description = "Ask the browser to open one authorized file in FilePortal.",
                    parameters = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "prepare_download",
                    description = "Prepare a browser download for authorized files or folders. The browser presents the action to the user.",
                    parameters = new { type = "object", properties = new { paths = new { type = "array", items = new { type = "string" }, minItems = 1, maxItems = 50 } }, required = new[] { "paths" }, additionalProperties = false }, strict = true
                },
                new
                {
                    type = "function", name = "prepare_delete",
                    description = "Prepare deletion of authorized files or folders. This never deletes directly; FilePortal must show a visual confirmation first.",
                    parameters = new { type = "object", properties = new { paths = new { type = "array", items = new { type = "string" }, minItems = 1, maxItems = 50 } }, required = new[] { "paths" }, additionalProperties = false }, strict = true
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
                        output = await ExecuteTool(call, currentUser, clientActions, cancellationToken)
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
                    : Ok(new { reply = continuedReply, actions = clientActions });
            }

            var reply = ExtractText(payload.RootElement);
            return string.IsNullOrWhiteSpace(reply)
                ? StatusCode(StatusCodes.Status502BadGateway, new { error = "OpenAI returned no assistant message." })
                : Ok(new { reply, actions = clientActions });
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

    private async Task<string> ExecuteTool(ToolCall call, UserRecord user, List<object> clientActions,
        CancellationToken cancellationToken)
    {
        try
        {
            using var arguments = JsonDocument.Parse(call.Arguments);
            var roots = fileBrowser.GetRoots(user);
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
            if (call.Name == "list_roots")
                return JsonSerializer.Serialize(new { roots = roots.Select(root => new { root.Name, root.Path }) });
            if (call.Name == "list_folder")
            {
                var requestedPath = arguments.RootElement.GetProperty("path").GetString() ?? "";
                if (!fileBrowser.TryResolve(requestedPath, roots, out var folder, allowMissing: false) || !Directory.Exists(folder))
                    return JsonSerializer.Serialize(new { error = "That folder is unavailable or outside your access." });
                var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos()
                    .Where(fileBrowser.CanList).OrderByDescending(entry => entry is DirectoryInfo)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Take(100)
                    .Select(entry => new { entry.Name, entry.FullName, isDirectory = entry is DirectoryInfo,
                        size = entry is FileInfo file ? file.Length : (long?)null, entry.LastWriteTime });
                return JsonSerializer.Serialize(new { path = folder, entries });
            }
            if (call.Name == "open_page")
            {
                var page = arguments.RootElement.GetProperty("page").GetString() ?? "home";
                var route = page switch { "files" => "/files.html", "gallery" => "/gallery.html", "shared" => "/#shared",
                    "favorites" => "/gallery.html#favorites", "trash" => "/#trash", "settings" => "/users.html", _ => "/" };
                clientActions.Add(new { type = "navigate", url = route });
                return JsonSerializer.Serialize(new { prepared = true, page, route });
            }
            if (call.Name == "open_category")
            {
                var rootName = arguments.RootElement.GetProperty("rootName").GetString() ?? "";
                var category = arguments.RootElement.GetProperty("category").GetString() ?? "";
                var root = roots.FirstOrDefault(item => string.Equals(item.Name, rootName, StringComparison.OrdinalIgnoreCase));
                if (root is null || category is not ("pictures" or "videos" or "pdfs" or "notes" or "documents"))
                    return JsonSerializer.Serialize(new { error = "Choose an available root and supported category." });
                var route = "/files.html" + QueryString.Create(new Dictionary<string, string?>
                {
                    ["path"] = root.Path,
                    ["category"] = category
                }).Value;
                clientActions.Add(new { type = "navigate", url = route });
                return JsonSerializer.Serialize(new { prepared = true, root = root.Name, category, route });
            }
            if (call.Name == "open_file")
            {
                var requestedPath = arguments.RootElement.GetProperty("path").GetString() ?? "";
                if (!fileBrowser.TryResolve(requestedPath, roots, out var file, allowMissing: false) || !System.IO.File.Exists(file))
                    return JsonSerializer.Serialize(new { error = "That file is unavailable or outside your access." });
                clientActions.Add(new { type = "open_file", path = file });
                return JsonSerializer.Serialize(new { prepared = true, path = file });
            }
            if (call.Name is "prepare_download" or "prepare_delete")
            {
                var requestedPaths = arguments.RootElement.GetProperty("paths").EnumerateArray()
                    .Select(item => item.GetString()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().Distinct(FileBrowserService.PathComparer).ToList();
                if (requestedPaths is { Count: 0 } or { Count: > 50 })
                    return JsonSerializer.Serialize(new { error = "Choose between 1 and 50 items." });
                var safePaths = new List<string>();
                foreach (var requestedPath in requestedPaths)
                {
                    if (!fileBrowser.TryResolve(requestedPath, roots, out var safePath, allowMissing: false) ||
                        (!System.IO.File.Exists(safePath) && !Directory.Exists(safePath)))
                        return JsonSerializer.Serialize(new { error = "At least one item is unavailable or outside your access." });
                    safePaths.Add(safePath);
                }
                var actionType = call.Name == "prepare_delete" ? "confirm_delete" : "confirm_download";
                clientActions.Add(new { type = actionType, paths = safePaths });
                return JsonSerializer.Serialize(new { prepared = true, requiresUserConfirmation = true, count = safePaths.Count });
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
