namespace FilePortal.Models;

public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public string OpenAiModel { get; set; } = "gpt-5-mini";
    public string AssemblyAiSpeechModel { get; set; } = "assemblyai-stt-3-7-realtime-agent";
    public string AssemblyAiTtsModel { get; set; } = "assemblyai-tts-1";
    public string AssemblyAiVoiceId { get; set; } = "49c443a4-8a1d-5f7e-b70b-98bdbd9823c3";
    public string Language { get; set; } = "english";
    public string? ProtectedOpenAiApiKey { get; set; }
    public string? ProtectedAssemblyAiApiKey { get; set; }
}
