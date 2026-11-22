using FilePortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FilePortal.Controllers;

[ApiController]
[Route("api/ai/settings")]
[Authorize(Roles = "Admin")]
public sealed class AiSettingsController(AiSettingsService store, IHttpClientFactory clients) : ControllerBase
{
    public sealed class UpdateRequest
    {
        public bool Enabled { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? AssemblyAiApiKey { get; set; }
        public string? OpenAiModel { get; set; }
        public string? AssemblyAiSpeechModel { get; set; }
        public string? AssemblyAiTtsModel { get; set; }
        public string? AssemblyAiVoiceId { get; set; }
        public string? Language { get; set; }
    }

    [HttpGet]
    public IActionResult Get() => Ok(Sanitize(store.Get()));

    [HttpPut]
    public IActionResult Put([FromBody] UpdateRequest request)
    {
        if (request.Enabled && string.IsNullOrWhiteSpace(request.OpenAiModel))
            return BadRequest(new { error = "Choose an OpenAI model before enabling the assistant." });

        var current = store.Get();
        if (request.Enabled && string.IsNullOrWhiteSpace(request.OpenAiApiKey) &&
            string.IsNullOrWhiteSpace(current.ProtectedOpenAiApiKey))
            return BadRequest(new { error = "Configure an OpenAI API key before enabling the assistant." });
        if (request.Enabled && string.IsNullOrWhiteSpace(request.AssemblyAiApiKey) &&
            string.IsNullOrWhiteSpace(current.ProtectedAssemblyAiApiKey))
            return BadRequest(new { error = "Configure an AssemblyAI API key before enabling the assistant." });

        store.Update(settings =>
        {
            settings.Enabled = request.Enabled;
            settings.OpenAiModel = Clean(request.OpenAiModel, settings.OpenAiModel, 120);
            settings.AssemblyAiSpeechModel = Clean(request.AssemblyAiSpeechModel, settings.AssemblyAiSpeechModel, 120);
            settings.AssemblyAiTtsModel = Clean(request.AssemblyAiTtsModel, settings.AssemblyAiTtsModel, 120);
            settings.AssemblyAiVoiceId = Clean(request.AssemblyAiVoiceId, settings.AssemblyAiVoiceId, 160);
            settings.Language = Clean(request.Language, settings.Language, 40);
            if (!string.IsNullOrWhiteSpace(request.OpenAiApiKey))
                settings.ProtectedOpenAiApiKey = store.Protect(request.OpenAiApiKey.Trim());
            if (!string.IsNullOrWhiteSpace(request.AssemblyAiApiKey))
                settings.ProtectedAssemblyAiApiKey = store.Protect(request.AssemblyAiApiKey.Trim());
        });

        return Ok(Sanitize(store.Get()));
    }

    [HttpDelete("keys/{provider}")]
    public IActionResult DeleteKey(string provider)
    {
        if (!provider.Equals("openai", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("assemblyai", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        store.Update(settings =>
        {
            if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
                settings.ProtectedOpenAiApiKey = null;
            else
                settings.ProtectedAssemblyAiApiKey = null;
            settings.Enabled = false;
        });
        return NoContent();
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var models = new List<string>();
        var key = store.GetOpenAiApiKey();
        if (!string.IsNullOrWhiteSpace(key))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await clients.CreateClient("AiProviders").SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                models = payload.RootElement.GetProperty("data").EnumerateArray()
                    .Select(item => item.GetProperty("id").GetString())
                    .Where(id => id is not null && IsReasoningModel(id))
                    .Cast<string>()
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return Ok(new
        {
            openAi = models,
            assemblyAiSpeech = new[] { "assemblyai-stt-3-7-realtime-agent", "universal-3-6-pro" },
            assemblyAiTts = new[] { "assemblyai-tts-1" }
        });
    }

    [HttpPost("test/{provider}")]
    public async Task<IActionResult> Test(string provider, CancellationToken cancellationToken)
    {
        try
        {
            using var request = provider.ToLowerInvariant() switch
            {
                "openai" => OpenAiTestRequest(),
                "assemblyai" => AssemblyAiTestRequest(),
                _ => null
            };
            if (request is null) return NotFound();
            using var response = await clients.CreateClient("AiProviders").SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? Ok(new { connected = true })
                : StatusCode(StatusCodes.Status502BadGateway, new { error = $"{ProviderName(provider)} rejected the saved credentials." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = $"{ProviderName(provider)} could not be reached." });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = $"{ProviderName(provider)} did not respond in time." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private HttpRequestMessage OpenAiTestRequest()
    {
        var key = store.GetOpenAiApiKey() ?? throw new InvalidOperationException("OpenAI is not configured.");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private HttpRequestMessage AssemblyAiTestRequest()
    {
        var key = store.GetAssemblyAiApiKey() ?? throw new InvalidOperationException("AssemblyAI is not configured.");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://streaming.assemblyai.com/v3/token?expires_in_seconds=10");
        request.Headers.TryAddWithoutValidation("Authorization", key);
        return request;
    }

    private static bool IsReasoningModel(string id) => id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("o1", StringComparison.OrdinalIgnoreCase) || id.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
    private static string ProviderName(string provider) => provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ? "OpenAI" : "AssemblyAI";

    private static object Sanitize(FilePortal.Models.AiSettings settings) => new
    {
        settings.Enabled,
        settings.OpenAiModel,
        settings.AssemblyAiSpeechModel,
        settings.AssemblyAiTtsModel,
        settings.AssemblyAiVoiceId,
        settings.Language,
        openAiConfigured = !string.IsNullOrWhiteSpace(settings.ProtectedOpenAiApiKey),
        assemblyAiConfigured = !string.IsNullOrWhiteSpace(settings.ProtectedAssemblyAiApiKey)
    };

    private static string Clean(string? value, string fallback, int maximumLength)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? fallback : value[..Math.Min(value.Length, maximumLength)];
    }
}
