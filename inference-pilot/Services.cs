using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ProviderPilot;

public static class ConfigPaths
{
    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string AppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProviderPilot");

    public static string StateFile =>
        Path.Combine(AppData, "profiles.json");

    public static string ClaudeSettings =>
        Path.Combine(Home, ".claude", "settings.json");

    public static string CodexConfig =>
        Path.Combine(Home, ".codex", "config.toml");
}

public sealed class AppStateService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppState Load()
    {
        Directory.CreateDirectory(ConfigPaths.AppData);
        AppState state;
        if (!File.Exists(ConfigPaths.StateFile))
        {
            state = new AppState();
            state.Profiles.Add(ProviderPresets.Create("OpenRouter"));
            state.Profiles.Add(ProviderPresets.Create("Google"));
            state.Profiles.Add(ProviderPresets.Create("NVIDIA NIM"));
            state.ActiveProfileId = state.Profiles[0].Id;
        }
        else
        {
            var json = File.ReadAllText(ConfigPaths.StateFile);
            state = JsonSerializer.Deserialize<AppState>(json, Options) ?? new AppState();
        }

        Normalize(state);
        Save(state);
        return state;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(ConfigPaths.AppData);
        AtomicWrite(ConfigPaths.StateFile, JsonSerializer.Serialize(state, Options));
    }

    private static void Normalize(AppState state)
    {
        if (state.Profiles.Count == 0)
        {
            state.Profiles.Add(ProviderPresets.Create("OpenRouter"));
        }

        if (string.IsNullOrWhiteSpace(state.ActiveProfileId) ||
            state.Profiles.All(profile => profile.Id != state.ActiveProfileId))
        {
            state.ActiveProfileId = state.Profiles[0].Id;
        }

        var active = state.Profiles.First(profile => profile.Id == state.ActiveProfileId);
        MigrateSlot(state.ClaudeCode.Default, active.ClaudeDefaultProvider, active.ClaudeDefaultModel, active.Id, "sonnet");
        MigrateSlot(state.ClaudeCode.Opus, active.ClaudeOpusProvider, active.ClaudeOpusModel, active.Id, "claude-opus-4-1");
        MigrateSlot(state.ClaudeCode.Sonnet, active.ClaudeSonnetProvider, active.ClaudeSonnetModel, active.Id, "claude-sonnet-4");
        MigrateSlot(state.ClaudeCode.Haiku, active.ClaudeHaikuProvider, active.ClaudeHaikuModel, active.Id, "claude-3-5-haiku");
        MigrateSlot(state.ClaudeCode.Subagent, active.ClaudeSubagentProvider, active.ClaudeSubagentModel, active.Id, "claude-sonnet-4");
        NormalizeSlotProvider(state, state.ClaudeCode.Default, active.Id);
        NormalizeSlotProvider(state, state.ClaudeCode.Opus, active.Id);
        NormalizeSlotProvider(state, state.ClaudeCode.Sonnet, active.Id);
        NormalizeSlotProvider(state, state.ClaudeCode.Haiku, active.Id);
        NormalizeSlotProvider(state, state.ClaudeCode.Subagent, active.Id);

        if (string.IsNullOrWhiteSpace(state.Codex.InferenceId))
        {
            state.Codex.InferenceId = ResolveProviderId(state, active.CodexProviderLabel) ?? active.Id;
        }
        else
        {
            state.Codex.InferenceId = ResolveProviderId(state, state.Codex.InferenceId) ?? active.Id;
        }

        if (string.IsNullOrWhiteSpace(state.Codex.Model))
        {
            state.Codex.Model = string.IsNullOrWhiteSpace(active.CodexModel) ? "gpt-5-codex" : active.CodexModel;
        }
    }

    private static void MigrateSlot(ClaudeModelSlot slot, string legacyProvider, string legacyModel, string fallbackId, string fallbackModel)
    {
        if (string.IsNullOrWhiteSpace(slot.InferenceId))
        {
            slot.InferenceId = string.IsNullOrWhiteSpace(legacyProvider) ? fallbackId : legacyProvider;
        }

        if (string.IsNullOrWhiteSpace(slot.Model))
        {
            slot.Model = string.IsNullOrWhiteSpace(legacyModel) ? fallbackModel : legacyModel;
        }
    }

    private static void NormalizeSlotProvider(AppState state, ClaudeModelSlot slot, string fallbackId)
    {
        slot.InferenceId = ResolveProviderId(state, slot.InferenceId) ?? fallbackId;
    }

    private static string? ResolveProviderId(AppState state, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return state.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Name, value, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    public static string BackupFile(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        var backupDir = Path.Combine(ConfigPaths.AppData, "backups");
        Directory.CreateDirectory(backupDir);
        var safeName = Path.GetFileName(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(backupDir, $"{safeName}.{stamp}.bak");
        File.Copy(path, target, overwrite: false);
        return target;
    }

    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}

public sealed class SwitcherService
{
    public string BuildPreview(AppState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ProviderPilot switch preview");
        builder.AppendLine();

        if (state.ClaudeCode.Enabled)
        {
            var defaultProvider = FindProvider(state, state.ClaudeCode.Default.InferenceId);
            builder.AppendLine("Claude Code Configuration");
            builder.AppendLine($"  file: {ConfigPaths.ClaudeSettings}");
            builder.AppendLine($"  gateway inference: {defaultProvider.Name}");
            builder.AppendLine($"  ANTHROPIC_BASE_URL = {defaultProvider.BaseUrl}");
            AppendClaudeSlot(builder, "Default", state.ClaudeCode.Default, state);
            AppendClaudeSlot(builder, "Opus", state.ClaudeCode.Opus, state);
            AppendClaudeSlot(builder, "Sonnet", state.ClaudeCode.Sonnet, state);
            AppendClaudeSlot(builder, "Haiku/background", state.ClaudeCode.Haiku, state);
            AppendClaudeSlot(builder, "Subagent", state.ClaudeCode.Subagent, state);
            builder.AppendLine();
        }

        if (state.Codex.Enabled)
        {
            var codexProvider = FindProvider(state, state.Codex.InferenceId);
            var providerId = CodexProviderId(codexProvider);
            builder.AppendLine("Codex Configuration");
            builder.AppendLine($"  file: {ConfigPaths.CodexConfig}");
            builder.AppendLine($"  inference: {codexProvider.Name}");
            builder.AppendLine($"  model = \"{state.Codex.Model}\"");
            builder.AppendLine($"  model_provider = \"{providerId}\"");
            builder.AppendLine("  reasoning = left unchanged for Codex to control");
            builder.AppendLine();
        }

        if (state.SetUserEnvironmentKey)
        {
            builder.AppendLine("Windows user environment");
            foreach (var provider in SelectedProviders(state))
            {
                builder.AppendLine(string.IsNullOrWhiteSpace(provider.ApiKey)
                    ? $"  {provider.AuthEnvKey}: unchanged"
                    : $"  {provider.AuthEnvKey}: <hidden>");
            }
        }

        return builder.ToString();
    }

    public ApplyResult Apply(AppState state)
    {
        var issues = Validate(state);
        var blocking = issues.FirstOrDefault(issue => issue.Level == "Error");
        if (blocking is not null)
        {
            throw new InvalidOperationException(blocking.Message);
        }

        var result = new ApplyResult();

        if (state.ClaudeCode.Enabled)
        {
            var backup = AppStateService.BackupFile(ConfigPaths.ClaudeSettings);
            if (!string.IsNullOrWhiteSpace(backup))
            {
                result.Backups.Add(backup);
            }

            WriteClaudeSettings(state);
            result.ChangedFiles.Add(ConfigPaths.ClaudeSettings);
        }

        if (state.Codex.Enabled)
        {
            var backup = AppStateService.BackupFile(ConfigPaths.CodexConfig);
            if (!string.IsNullOrWhiteSpace(backup))
            {
                result.Backups.Add(backup);
            }

            WriteCodexConfig(state);
            result.ChangedFiles.Add(ConfigPaths.CodexConfig);
        }

        if (state.SetUserEnvironmentKey)
        {
            foreach (var provider in SelectedProviders(state))
            {
                if (string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(provider.AuthEnvKey.Trim(), provider.ApiKey.Trim(), EnvironmentVariableTarget.User);
                result.EnvironmentKeys.Add(provider.AuthEnvKey.Trim());
            }

            var claudeDefaultProvider = FindProvider(state, state.ClaudeCode.Default.InferenceId);
            if (state.ClaudeCode.Enabled && !string.IsNullOrWhiteSpace(claudeDefaultProvider.ApiKey))
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", claudeDefaultProvider.ApiKey.Trim(), EnvironmentVariableTarget.User);
                result.EnvironmentKeys.Add("ANTHROPIC_AUTH_TOKEN");
            }
        }

        foreach (var warning in issues.Where(issue => issue.Level == "Warning"))
        {
            result.Warnings.Add(warning.Message);
        }

        return result;
    }

    public List<ValidationIssue> Validate(AppState state)
    {
        var issues = new List<ValidationIssue>();
        foreach (var provider in state.Profiles)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = "Every inference setting needs a name." });
            }

            if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = $"{provider.Name}: base URL must be a valid http or https URL." });
            }

            if (!Regex.IsMatch(provider.AuthEnvKey.Trim(), "^[A-Z_][A-Z0-9_]*$"))
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = $"{provider.Name}: auth env var must look like OPENAI_API_KEY." });
            }

            if (provider.WireApi is not ("responses" or "chat"))
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = $"{provider.Name}: wire API must be responses or chat." });
            }
        }

        if (state.ClaudeCode.Enabled)
        {
            ValidateSlot(issues, "Claude default", state.ClaudeCode.Default, state);
            ValidateSlot(issues, "Claude opus", state.ClaudeCode.Opus, state);
            ValidateSlot(issues, "Claude sonnet", state.ClaudeCode.Sonnet, state);
            ValidateSlot(issues, "Claude haiku", state.ClaudeCode.Haiku, state);
            ValidateSlot(issues, "Claude subagent", state.ClaudeCode.Subagent, state);
        }

        if (state.Codex.Enabled)
        {
            if (FindProviderOrNull(state, state.Codex.InferenceId) is null)
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = "Codex inference is missing." });
            }

            if (string.IsNullOrWhiteSpace(state.Codex.Model))
            {
                issues.Add(new ValidationIssue { Level = "Error", Message = "Codex model is required." });
            }
        }

        if (SelectedProviders(state).Any(provider => string.IsNullOrWhiteSpace(provider.ApiKey)))
        {
            issues.Add(new ValidationIssue
            {
                Level = "Warning",
                Message = "One or more selected inference settings have no API key in this session. Existing environment variables will be left unchanged."
            });
        }

        return issues;
    }

    private static void ValidateSlot(List<ValidationIssue> issues, string label, ClaudeModelSlot slot, AppState state)
    {
        if (FindProviderOrNull(state, slot.InferenceId) is null)
        {
            issues.Add(new ValidationIssue { Level = "Error", Message = $"{label}: inference is missing." });
        }

        if (string.IsNullOrWhiteSpace(slot.Model))
        {
            issues.Add(new ValidationIssue { Level = "Error", Message = $"{label}: model is required." });
        }
    }

    private static void WriteClaudeSettings(AppState state)
    {
        JsonObject root;
        if (File.Exists(ConfigPaths.ClaudeSettings))
        {
            var existing = File.ReadAllText(ConfigPaths.ClaudeSettings);
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(existing) ? "{}" : existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var env = root["env"] as JsonObject ?? new JsonObject();
        root["env"] = env;

        var defaultProvider = FindProvider(state, state.ClaudeCode.Default.InferenceId);
        if (state.ClaudeCode.UseGateway)
        {
            env["ANTHROPIC_BASE_URL"] = defaultProvider.BaseUrl.TrimEnd('/');
        }

        env["ANTHROPIC_MODEL"] = state.ClaudeCode.Default.Model.Trim();
        env["ANTHROPIC_DEFAULT_OPUS_MODEL"] = state.ClaudeCode.Opus.Model.Trim();
        env["ANTHROPIC_DEFAULT_SONNET_MODEL"] = state.ClaudeCode.Sonnet.Model.Trim();
        env["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = state.ClaudeCode.Haiku.Model.Trim();
        env["CLAUDE_CODE_SUBAGENT_MODEL"] = state.ClaudeCode.Subagent.Model.Trim();
        env["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
        env["PROVIDERPILOT_CLAUDE_ROUTE_MAP"] = BuildClaudeRouteMap(state).ToJsonString();

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        AppStateService.AtomicWrite(ConfigPaths.ClaudeSettings, json + Environment.NewLine);
    }

    private static JsonObject BuildClaudeRouteMap(AppState state)
    {
        return new JsonObject
        {
            ["default"] = BuildSlotMap(state.ClaudeCode.Default, state),
            ["opus"] = BuildSlotMap(state.ClaudeCode.Opus, state),
            ["sonnet"] = BuildSlotMap(state.ClaudeCode.Sonnet, state),
            ["haiku"] = BuildSlotMap(state.ClaudeCode.Haiku, state),
            ["subagent"] = BuildSlotMap(state.ClaudeCode.Subagent, state)
        };
    }

    private static JsonObject BuildSlotMap(ClaudeModelSlot slot, AppState state)
    {
        var provider = FindProvider(state, slot.InferenceId);
        return new JsonObject
        {
            ["inference_id"] = provider.Id,
            ["inference"] = provider.Name,
            ["model"] = slot.Model
        };
    }

    private static void WriteCodexConfig(AppState state)
    {
        var provider = FindProvider(state, state.Codex.InferenceId);
        var providerId = CodexProviderId(provider);
        var path = ConfigPaths.CodexConfig;
        var text = File.Exists(path) ? File.ReadAllText(path) : "";

        text = RemoveSection(text, TomlSection("model_providers", providerId));
        text = RemoveSection(text, TomlSection("profiles", providerId));
        text = UpsertTopLevel(text, "model", TomlString(state.Codex.Model.Trim()));
        text = UpsertTopLevel(text, "model_provider", TomlString(providerId));
        text = RemoveTopLevelKey(text, "model_reasoning_effort");

        var section = new StringBuilder();
        section.AppendLine();
        section.AppendLine($"[{TomlSection("model_providers", providerId)}]");
        section.AppendLine($"name = {TomlString(provider.Name)}");
        section.AppendLine($"base_url = {TomlString(provider.BaseUrl.TrimEnd('/'))}");
        section.AppendLine($"env_key = {TomlString(provider.AuthEnvKey.Trim())}");
        section.AppendLine($"wire_api = {TomlString(provider.WireApi.Trim())}");

        var headers = ParsePairs(provider.ExtraHeaders);
        if (headers.Count > 0)
        {
            section.AppendLine($"http_headers = {{ {string.Join(", ", headers.Select(pair => $"{TomlBareOrQuotedKey(pair.Key)} = {TomlString(pair.Value)}"))} }}");
        }

        var query = ParsePairs(provider.QueryParams);
        if (query.Count > 0)
        {
            section.AppendLine($"query_params = {{ {string.Join(", ", query.Select(pair => $"{TomlBareOrQuotedKey(pair.Key)} = {TomlString(pair.Value)}"))} }}");
        }

        section.AppendLine("request_max_retries = 4");
        section.AppendLine("stream_max_retries = 5");
        section.AppendLine("stream_idle_timeout_ms = 300000");
        section.AppendLine();
        section.AppendLine($"[profiles.{providerId}]");
        section.AppendLine($"model = {TomlString(state.Codex.Model.Trim())}");
        section.AppendLine($"model_provider = {TomlString(providerId)}");

        AppStateService.AtomicWrite(path, text.TrimEnd() + Environment.NewLine + section);
    }

    private static void AppendClaudeSlot(StringBuilder builder, string label, ClaudeModelSlot slot, AppState state)
    {
        var provider = FindProvider(state, slot.InferenceId);
        builder.AppendLine($"  {label}: {provider.Name} -> {slot.Model}");
    }

    private static IEnumerable<RouteProfile> SelectedProviders(AppState state)
    {
        var ids = new List<string>();
        if (state.ClaudeCode.Enabled)
        {
            ids.AddRange([
                state.ClaudeCode.Default.InferenceId,
                state.ClaudeCode.Opus.InferenceId,
                state.ClaudeCode.Sonnet.InferenceId,
                state.ClaudeCode.Haiku.InferenceId,
                state.ClaudeCode.Subagent.InferenceId
            ]);
        }

        if (state.Codex.Enabled)
        {
            ids.Add(state.Codex.InferenceId);
        }

        return ids
            .Select(id => FindProviderOrNull(state, id))
            .Where(provider => provider is not null)
            .Cast<RouteProfile>()
            .DistinctBy(provider => provider.Id);
    }

    public static RouteProfile FindProvider(AppState state, string idOrName)
    {
        return FindProviderOrNull(state, idOrName) ?? state.Profiles.First();
    }

    public static RouteProfile? FindProviderOrNull(AppState state, string idOrName)
    {
        return state.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> ParsePairs(string value)
    {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#", StringComparison.Ordinal) || !line.Contains('='))
            {
                continue;
            }

            var index = line.IndexOf('=');
            var key = line[..index].Trim();
            var val = line[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = val;
            }
        }

        return result;
    }

    private static string RemoveSection(string text, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var lines = SplitLines(text);
        var output = new List<string>();
        var skipping = false;
        foreach (var line in lines)
        {
            var section = MatchSection(line);
            if (section is not null)
            {
                skipping = section == sectionName;
            }

            if (!skipping)
            {
                output.Add(line);
            }
        }

        return string.Join(Environment.NewLine, output).TrimEnd() + Environment.NewLine;
    }

    private static string UpsertTopLevel(string text, string key, string tomlValue)
    {
        var lines = SplitLines(text).ToList();
        var firstSection = lines.FindIndex(line => MatchSection(line) is not null);
        if (firstSection < 0)
        {
            firstSection = lines.Count;
        }

        var keyRegex = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        for (var i = 0; i < firstSection; i++)
        {
            if (keyRegex.IsMatch(lines[i]))
            {
                lines[i] = $"{key} = {tomlValue}";
                return string.Join(Environment.NewLine, lines);
            }
        }

        lines.Insert(firstSection, $"{key} = {tomlValue}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string RemoveTopLevelKey(string text, string key)
    {
        var lines = SplitLines(text).ToList();
        var firstSection = lines.FindIndex(line => MatchSection(line) is not null);
        if (firstSection < 0)
        {
            firstSection = lines.Count;
        }

        var keyRegex = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        for (var i = firstSection - 1; i >= 0; i--)
        {
            if (keyRegex.IsMatch(lines[i]))
            {
                lines.RemoveAt(i);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string? MatchSection(string line)
    {
        var match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string CodexProviderId(RouteProfile profile)
    {
        var slug = Regex.Replace(profile.Name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(slug) ? $"providerpilot_{profile.Id[..8]}" : $"providerpilot_{slug}";
    }

    private static string TomlSection(string parent, string child) =>
        $"{parent}.{TomlBareOrQuotedKey(child)}";

    private static string TomlBareOrQuotedKey(string key) =>
        Regex.IsMatch(key, "^[A-Za-z0-9_-]+$") ? key : TomlString(key);

    private static string TomlString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

public sealed class ModelCatalogService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<List<string>> LoadModelsAsync(RouteProfile profile, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(profile));
        ApplyAuthHeaders(request, profile);
        ApplyExtraHeaders(request, profile.ExtraHeaders);

        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = body.Length > 240 ? body[..240] + "..." : body;
            throw new InvalidOperationException($"Model load failed: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
        }

        var models = ExtractModelIds(body)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (models.Count == 0)
        {
            throw new InvalidOperationException("Provider responded, but no model IDs were found in the /models response.");
        }

        return models;
    }

    private static Uri BuildModelsUri(RouteProfile profile)
    {
        var baseUrl = profile.BaseUrl.Trim().TrimEnd('/');
        if (profile.ProviderKind == "Anthropic" && !baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl += "/v1";
        }
        else if (profile.ProviderKind == "Google")
        {
            if (baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^7];
            }
        }

        var uri = baseUrl + "/models";
        
        if (profile.ProviderKind == "Google")
        {
            var key = !string.IsNullOrWhiteSpace(profile.ApiKey)
                ? profile.ApiKey.Trim()
                : Environment.GetEnvironmentVariable(profile.AuthEnvKey.Trim(), EnvironmentVariableTarget.User)
                  ?? Environment.GetEnvironmentVariable(profile.AuthEnvKey.Trim())
                  ?? "";
                  
            if (!string.IsNullOrWhiteSpace(key))
            {
                uri += $"?key={Uri.EscapeDataString(key)}";
            }
        }

        return new Uri(uri);
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, RouteProfile profile)
    {
        var key = !string.IsNullOrWhiteSpace(profile.ApiKey)
            ? profile.ApiKey.Trim()
            : Environment.GetEnvironmentVariable(profile.AuthEnvKey.Trim(), EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable(profile.AuthEnvKey.Trim())
              ?? "";

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        switch (profile.ProviderKind)
        {
            case "Anthropic":
                request.Headers.TryAddWithoutValidation("x-api-key", key);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                break;
            case "Azure":
                request.Headers.TryAddWithoutValidation("api-key", key);
                break;
            case "Google":
                request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                break;
        }
    }

    private static void ApplyExtraHeaders(HttpRequestMessage request, string value)
    {
        foreach (var pair in ParsePairs(value))
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
    }

    private static IEnumerable<string> ExtractModelIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in ExtractFromArray(data))
            {
                yield return model;
            }

            yield break;
        }

        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in ExtractFromArray(models))
            {
                yield return model;
            }
        }
    }

    private static IEnumerable<string> ExtractFromArray(JsonElement models)
    {
        foreach (var item in models.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString() ?? "";
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                yield return id.GetString() ?? "";
            }
            else if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                yield return name.GetString() ?? "";
            }
        }
    }

    private static Dictionary<string, string> ParsePairs(string value)
    {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#", StringComparison.Ordinal) || !line.Contains('='))
            {
                continue;
            }

            var index = line.IndexOf('=');
            var key = line[..index].Trim();
            var val = line[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = val;
            }
        }

        return result;
    }
}
