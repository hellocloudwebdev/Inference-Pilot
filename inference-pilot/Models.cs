using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ProviderPilot;

public sealed class AppState
{
    public string ActiveProfileId { get; set; } = "";
    public ObservableCollection<RouteProfile> Profiles { get; set; } = new();
    public ClaudeCodeSettings ClaudeCode { get; set; } = new();
    public CodexSettings Codex { get; set; } = new();
    public bool SetUserEnvironmentKey { get; set; } = true;
}

public sealed class ClaudeCodeSettings
{
    public bool Enabled { get; set; } = true;
    public bool UseGateway { get; set; } = true;
    public ClaudeModelSlot Default { get; set; } = new("sonnet");
    public ClaudeModelSlot Opus { get; set; } = new("claude-opus-4-1");
    public ClaudeModelSlot Sonnet { get; set; } = new("claude-sonnet-4");
    public ClaudeModelSlot Haiku { get; set; } = new("claude-3-5-haiku");
    public ClaudeModelSlot Subagent { get; set; } = new("claude-sonnet-4");
}

public sealed class ClaudeModelSlot
{
    public ClaudeModelSlot()
    {
    }

    public ClaudeModelSlot(string model)
    {
        Model = model;
    }

    public string InferenceId { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class CodexSettings
{
    public bool Enabled { get; set; } = true;
    public string InferenceId { get; set; } = "";
    public string Model { get; set; } = "gpt-5-codex";
}

public sealed class RouteProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New inference";
    public string ProviderKind { get; set; } = "Custom";
    public string BaseUrl { get; set; } = "https://api.example.com/v1";
    public string AuthEnvKey { get; set; } = "OPENAI_API_KEY";
    [JsonIgnore]
    public string ApiKey { get; set; } = "";
    public string WireApi { get; set; } = "responses";
    public string ExtraHeaders { get; set; } = "";
    public string QueryParams { get; set; } = "";
    public List<string> LoadedModels { get; set; } = new();
    public List<string> StarredModels { get; set; } = new();
    public DateTime? ModelsLoadedAt { get; set; }
    public bool AutoloadModels { get; set; } = true;

    // Legacy fields kept so older profiles.json files migrate without data loss.
    public bool ApplyClaude { get; set; } = true;
    public bool ApplyCodex { get; set; } = true;
    public bool SetUserEnvironmentKey { get; set; } = true;
    public bool ClaudeUseGateway { get; set; } = true;
    public string ClaudeDefaultProvider { get; set; } = "";
    public string ClaudeDefaultModel { get; set; } = "";
    public string ClaudeOpusProvider { get; set; } = "";
    public string ClaudeOpusModel { get; set; } = "";
    public string ClaudeSonnetProvider { get; set; } = "";
    public string ClaudeSonnetModel { get; set; } = "";
    public string ClaudeHaikuProvider { get; set; } = "";
    public string ClaudeHaikuModel { get; set; } = "";
    public string ClaudeSubagentProvider { get; set; } = "";
    public string ClaudeSubagentModel { get; set; } = "";
    public string CodexProviderLabel { get; set; } = "";
    public string CodexModel { get; set; } = "";
    public bool CodexLetCliChooseReasoning { get; set; } = true;
    public string CodexReasoningEffort { get; set; } = "";
    public string Notes { get; set; } = "";

    public RouteProfile Clone()
    {
        return new RouteProfile
        {
            Id = Id,
            Name = Name,
            ProviderKind = ProviderKind,
            BaseUrl = BaseUrl,
            AuthEnvKey = AuthEnvKey,
            ApiKey = ApiKey,
            WireApi = WireApi,
            ExtraHeaders = ExtraHeaders,
            QueryParams = QueryParams,
            LoadedModels = LoadedModels.ToList(),
            StarredModels = StarredModels.ToList(),
            ModelsLoadedAt = ModelsLoadedAt,
            AutoloadModels = AutoloadModels
        };
    }
}

public sealed class ApplyResult
{
    public List<string> ChangedFiles { get; } = new();
    public List<string> Backups { get; } = new();
    public List<string> EnvironmentKeys { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class ValidationIssue
{
    public string Level { get; init; } = "Info";
    public string Message { get; init; } = "";
}

public static class ProviderPresets
{
    public static RouteProfile Create(string kind)
    {
        var profile = kind switch
        {
            "OpenAI" => new RouteProfile
            {
                Name = "OpenAI",
                ProviderKind = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                AuthEnvKey = "OPENAI_API_KEY",
                WireApi = "responses"
            },
            "Anthropic" => new RouteProfile
            {
                Name = "Anthropic",
                ProviderKind = "Anthropic",
                BaseUrl = "https://api.anthropic.com",
                AuthEnvKey = "ANTHROPIC_API_KEY",
                WireApi = "chat"
            },
            "Azure" => new RouteProfile
            {
                Name = "Azure OpenAI",
                ProviderKind = "Azure",
                BaseUrl = "https://YOUR_RESOURCE.openai.azure.com/openai/v1",
                AuthEnvKey = "AZURE_OPENAI_API_KEY",
                WireApi = "responses"
            },
            "NVIDIA NIM" => new RouteProfile
            {
                Name = "NVIDIA NIM",
                ProviderKind = "NVIDIA NIM",
                BaseUrl = "https://integrate.api.nvidia.com/v1",
                AuthEnvKey = "NVIDIA_API_KEY",
                WireApi = "chat"
            },
            "OpenRouter" => new RouteProfile
            {
                Name = "OpenRouter",
                ProviderKind = "OpenRouter",
                BaseUrl = "https://openrouter.ai/api/v1",
                AuthEnvKey = "OPENROUTER_API_KEY",
                WireApi = "chat",
                ExtraHeaders = "HTTP-Referer=https://providerpilot.local\nX-Title=ProviderPilot"
            },
            "Google" => new RouteProfile
            {
                Name = "Google Gemini",
                ProviderKind = "Google",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                AuthEnvKey = "GEMINI_API_KEY",
                WireApi = "chat"
            },
            _ => new RouteProfile()
        };

        profile.Id = Guid.NewGuid().ToString("N");
        return profile;
    }

    public static string[] Kinds { get; } =
    [
        "OpenAI",
        "Anthropic",
        "Azure",
        "NVIDIA NIM",
        "OpenRouter",
        "Google",
        "Custom"
    ];
}
