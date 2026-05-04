using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProviderPilot;

public partial class MainWindow : Window
{
    private enum PageKind
    {
        Providers,
        Claude,
        Codex
    }

    private readonly AppStateService _stateService = new();
    private readonly SwitcherService _switcher = new();
    private readonly ModelCatalogService _modelCatalog = new();
    private AppState _state = new();
    private RouteProfile? _selectedProvider;
    private bool _loading;
    private bool _showingApiKey;

    public MainWindow()
    {
        InitializeComponent();
        ProviderKindBox.ItemsSource = ProviderPresets.Kinds;
        PathsTextSeed();
        _state = _stateService.Load();
        ProviderList.ItemsSource = _state.Profiles;
        RefreshProviderCombos();
        SelectInitialProvider();
        LoadAppConfigToControls();
        ShowPage(PageKind.Providers);
    }

    private void PathsTextSeed()
    {
        ToolTip = $"Claude: {ConfigPaths.ClaudeSettings}\nCodex: {ConfigPaths.CodexConfig}\nProfiles: {ConfigPaths.StateFile}";
    }

    private void SelectInitialProvider()
    {
        var active = _state.Profiles.FirstOrDefault(profile => profile.Id == _state.ActiveProfileId) ?? _state.Profiles.FirstOrDefault();
        if (active is null)
        {
            active = ProviderPresets.Create("OpenRouter");
            _state.Profiles.Add(active);
        }

        ProviderList.SelectedItem = active;
    }

    private void ProvidersNav_Click(object sender, RoutedEventArgs e) => ShowPage(PageKind.Providers);

    private void ClaudeNav_Click(object sender, RoutedEventArgs e) => ShowPage(PageKind.Claude);

    private void CodexNav_Click(object sender, RoutedEventArgs e) => ShowPage(PageKind.Codex);

    private void ShowPage(PageKind page)
    {
        ProviderPage.Visibility = page == PageKind.Providers ? Visibility.Visible : Visibility.Collapsed;
        ClaudePage.Visibility = page == PageKind.Claude ? Visibility.Visible : Visibility.Collapsed;
        CodexPage.Visibility = page == PageKind.Codex ? Visibility.Visible : Visibility.Collapsed;
        SetActiveNav(page);

        HeaderTitle.Text = page switch
        {
            PageKind.Claude => "Claude Code Configuration",
            PageKind.Codex => "Codex Configuration",
            _ => "Provider Settings"
        };

        HeaderSubtitle.Text = page switch
        {
            PageKind.Claude => "Choose a different inference provider and model for Default, Opus, Sonnet, Haiku, and Subagent.",
            PageKind.Codex => "Choose Codex inference and model. Reasoning remains controlled by Codex.",
            _ => "Add endpoints, keys, headers, and provider model catalogs. No app model choices live here."
        };

        RefreshDiagnostics();
    }

    private void SetActiveNav(PageKind page)
    {
        ResetNavButton(ClaudeNavButton);
        ResetNavButton(CodexNavButton);
        ResetNavButton(ProvidersNavButton);

        var active = page switch
        {
            PageKind.Codex => CodexNavButton,
            PageKind.Providers => ProvidersNavButton,
            _ => ClaudeNavButton
        };

        active.Background = (Brush)FindResource("Brand");
        active.BorderBrush = (Brush)FindResource("Brand");
        active.Foreground = Brushes.White;
    }

    private void ResetNavButton(Button button)
    {
        button.ClearValue(Button.BackgroundProperty);
        button.ClearValue(Button.BorderBrushProperty);
        button.ClearValue(Button.ForegroundProperty);
    }
    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SaveProviderFromControls();
        _selectedProvider = ProviderList.SelectedItem as RouteProfile;
        LoadProviderToControls();
    }

    private void LoadProviderToControls()
    {
        if (_selectedProvider is null)
        {
            return;
        }

        _loading = true;
        NameBox.Text = _selectedProvider.Name;
        ProviderKindBox.SelectedItem = _selectedProvider.ProviderKind;
        BaseUrlBox.Text = _selectedProvider.BaseUrl;
        AuthEnvBox.Text = _selectedProvider.AuthEnvKey;
        SetCombo(WireApiBox, _selectedProvider.WireApi);
        ApiKeyBox.Password = _selectedProvider.ApiKey;
        ApiKeyRevealBox.Text = _selectedProvider.ApiKey;
        SetApiKeyVisibility(show: false);
        AutoloadModelsBox.IsChecked = _selectedProvider.AutoloadModels;
        HeadersBox.Text = _selectedProvider.ExtraHeaders;
        QueryBox.Text = _selectedProvider.QueryParams;
        RefreshModelList();
        RefreshModelCatalogText();
        _loading = false;
        RefreshDiagnostics();
    }

    private void SaveProviderFromControls()
    {
        if (_selectedProvider is null || _loading)
        {
            return;
        }

        var oldName = _selectedProvider.Name;
        _selectedProvider.Name = NameBox.Text.Trim();
        _selectedProvider.ProviderKind = ProviderKindBox.SelectedItem?.ToString() ?? "Custom";
        _selectedProvider.BaseUrl = BaseUrlBox.Text.Trim();
        _selectedProvider.AuthEnvKey = AuthEnvBox.Text.Trim();
        _selectedProvider.WireApi = ComboText(WireApiBox, "responses");
        _selectedProvider.ApiKey = _showingApiKey ? ApiKeyRevealBox.Text : ApiKeyBox.Password;
        _selectedProvider.AutoloadModels = AutoloadModelsBox.IsChecked == true;
        _selectedProvider.ExtraHeaders = HeadersBox.Text;
        _selectedProvider.QueryParams = QueryBox.Text;
        _state.ActiveProfileId = _selectedProvider.Id;

        RewriteNameReferences(oldName, _selectedProvider);
        _stateService.Save(_state);
        ProviderList.Items.Refresh();
        RefreshProviderCombos();
    }

    private void RewriteNameReferences(string oldName, RouteProfile provider)
    {
        if (string.IsNullOrWhiteSpace(oldName))
        {
            return;
        }

        foreach (var slot in ClaudeSlots())
        {
            if (string.Equals(slot.InferenceId, oldName, StringComparison.OrdinalIgnoreCase))
            {
                slot.InferenceId = provider.Id;
            }
        }

        if (string.Equals(_state.Codex.InferenceId, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _state.Codex.InferenceId = provider.Id;
        }
    }

    private void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFromControls();
        var provider = ProviderPresets.Create("Custom");
        provider.Name = $"Custom Provider {_state.Profiles.Count + 1}";
        _state.Profiles.Add(provider);
        RefreshProviderCombos();
        ProviderList.SelectedItem = provider;
        _stateService.Save(_state);
        ShowPage(PageKind.Providers);
    }

    private void DuplicateProvider_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFromControls();
        if (_selectedProvider is null)
        {
            return;
        }

        var copy = _selectedProvider.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " Copy";
        copy.ApiKey = "";
        _state.Profiles.Add(copy);
        RefreshProviderCombos();
        ProviderList.SelectedItem = copy;
        _stateService.Save(_state);
    }

    private void UpdateProvider_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFromControls();
        RefreshModelCatalogText();
        StatusText.Text = _selectedProvider is null
            ? "No provider selected."
            : $"Updated provider '{_selectedProvider.Name}'.";
    }

    private void DeleteProvider_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider is null || _state.Profiles.Count <= 1)
        {
            StatusText.Text = "Keep at least one provider so Claude Code and Codex have an inference option.";
            return;
        }

        var answer = MessageBox.Show($"Delete provider '{_selectedProvider.Name}'?", "Delete provider", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var fallback = _state.Profiles.First(profile => profile.Id != _selectedProvider.Id);
        foreach (var slot in ClaudeSlots().Where(slot => slot.InferenceId == _selectedProvider.Id))
        {
            slot.InferenceId = fallback.Id;
        }

        if (_state.Codex.InferenceId == _selectedProvider.Id)
        {
            _state.Codex.InferenceId = fallback.Id;
        }

        var index = Math.Max(0, ProviderList.SelectedIndex - 1);
        _state.Profiles.Remove(_selectedProvider);
        RefreshProviderCombos();
        ProviderList.SelectedIndex = index;
        _stateService.Save(_state);
        LoadAppConfigToControls();
    }

    private void ProviderKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProvider is null)
        {
            return;
        }

        var kind = ProviderKindBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(kind) || kind == _selectedProvider.ProviderKind)
        {
            ProviderEditor_Changed(sender, e);
            return;
        }

        var preset = ProviderPresets.Create(kind);
        NameBox.Text = preset.Name;
        BaseUrlBox.Text = preset.BaseUrl;
        AuthEnvBox.Text = preset.AuthEnvKey;
        SetCombo(WireApiBox, preset.WireApi);
        HeadersBox.Text = preset.ExtraHeaders;
        QueryBox.Text = preset.QueryParams;
        ProviderEditor_Changed(sender, e);
        _ = MaybeAutoLoadModelsAsync();
    }

    private async void LoadModels_Click(object sender, RoutedEventArgs e)
    {
        await LoadModelsAsync(showErrors: true);
    }

    private async void MaybeAutoLoadModels_Event(object sender, RoutedEventArgs e)
    {
        await MaybeAutoLoadModelsAsync();
    }

    private async Task MaybeAutoLoadModelsAsync()
    {
        SaveProviderFromControls();
        if (_selectedProvider is null || !_selectedProvider.AutoloadModels)
        {
            return;
        }

        var key = !string.IsNullOrWhiteSpace(_selectedProvider.ApiKey)
            ? _selectedProvider.ApiKey
            : Environment.GetEnvironmentVariable(_selectedProvider.AuthEnvKey, EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable(_selectedProvider.AuthEnvKey)
              ?? "";

        if (string.IsNullOrWhiteSpace(key) || !Uri.TryCreate(_selectedProvider.BaseUrl, UriKind.Absolute, out _))
        {
            return;
        }

        await LoadModelsAsync(showErrors: false);
    }

    private async Task LoadModelsAsync(bool showErrors)
    {
        SaveProviderFromControls();
        if (_selectedProvider is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"Loading models from {_selectedProvider.Name}...";
            ModelCatalogText.Text = "Loading model catalog...";
            var models = await _modelCatalog.LoadModelsAsync(_selectedProvider);
            _selectedProvider.LoadedModels = models;
            _selectedProvider.ModelsLoadedAt = DateTime.Now;
            _stateService.Save(_state);
            RefreshModelList();
            RefreshModelCatalogText();
            RefreshModelCombos();
            StatusText.Text = $"Loaded {models.Count} models from {_selectedProvider.Name}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ModelCatalogText.Text = "Could not load models. Check URL, key, headers, and /models support.";
            if (showErrors)
            {
                MessageBox.Show(ex.Message, "Model loading failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void LoadAppConfigToControls()
    {
        _loading = true;
        ClaudeEnabledBox.IsChecked = _state.ClaudeCode.Enabled;
        ClaudeGatewayBox.IsChecked = _state.ClaudeCode.UseGateway;
        SetEnvBox.IsChecked = _state.SetUserEnvironmentKey;
        CodexEnabledBox.IsChecked = _state.Codex.Enabled;

        RefreshProviderCombos();
        SetProviderCombo(ClaudeDefaultProviderBox, _state.ClaudeCode.Default.InferenceId);
        SetProviderCombo(ClaudeOpusProviderBox, _state.ClaudeCode.Opus.InferenceId);
        SetProviderCombo(ClaudeSonnetProviderBox, _state.ClaudeCode.Sonnet.InferenceId);
        SetProviderCombo(ClaudeHaikuProviderBox, _state.ClaudeCode.Haiku.InferenceId);
        SetProviderCombo(ClaudeSubagentProviderBox, _state.ClaudeCode.Subagent.InferenceId);
        SetProviderCombo(CodexProviderBox, _state.Codex.InferenceId);

        RefreshModelCombos();
        ClaudeDefaultBox.Text = _state.ClaudeCode.Default.Model;
        ClaudeOpusBox.Text = _state.ClaudeCode.Opus.Model;
        ClaudeSonnetBox.Text = _state.ClaudeCode.Sonnet.Model;
        ClaudeHaikuBox.Text = _state.ClaudeCode.Haiku.Model;
        ClaudeSubagentBox.Text = _state.ClaudeCode.Subagent.Model;
        CodexModelBox.Text = _state.Codex.Model;
        _loading = false;
    }

    private void SaveAppConfigFromControls()
    {
        if (_loading)
        {
            return;
        }

        _state.ClaudeCode.Enabled = ClaudeEnabledBox.IsChecked == true;
        _state.ClaudeCode.UseGateway = ClaudeGatewayBox.IsChecked == true;
        _state.SetUserEnvironmentKey = SetEnvBox.IsChecked == true;
        _state.Codex.Enabled = CodexEnabledBox.IsChecked == true;

        _state.ClaudeCode.Default.InferenceId = ProviderComboId(ClaudeDefaultProviderBox);
        _state.ClaudeCode.Default.Model = ComboText(ClaudeDefaultBox, "");
        _state.ClaudeCode.Opus.InferenceId = ProviderComboId(ClaudeOpusProviderBox);
        _state.ClaudeCode.Opus.Model = ComboText(ClaudeOpusBox, "");
        _state.ClaudeCode.Sonnet.InferenceId = ProviderComboId(ClaudeSonnetProviderBox);
        _state.ClaudeCode.Sonnet.Model = ComboText(ClaudeSonnetBox, "");
        _state.ClaudeCode.Haiku.InferenceId = ProviderComboId(ClaudeHaikuProviderBox);
        _state.ClaudeCode.Haiku.Model = ComboText(ClaudeHaikuBox, "");
        _state.ClaudeCode.Subagent.InferenceId = ProviderComboId(ClaudeSubagentProviderBox);
        _state.ClaudeCode.Subagent.Model = ComboText(ClaudeSubagentBox, "");
        _state.Codex.InferenceId = ProviderComboId(CodexProviderBox);
        _state.Codex.Model = ComboText(CodexModelBox, "");
        _stateService.Save(_state);
    }

    private void AppConfig_Changed(object sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SaveAppConfigFromControls();
        RefreshDiagnostics();
    }

    private void ClaudeSlotProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SaveAppConfigFromControls();
        RefreshModelCombos();
        RefreshDiagnostics();
    }

    private void CodexProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SaveAppConfigFromControls();
        RefreshModelCombos();
        RefreshDiagnostics();
    }

    private void ProviderEditor_Changed(object sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SaveProviderFromControls();
        RefreshDiagnostics();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_showingApiKey)
        {
            ApiKeyRevealBox.Text = ApiKeyBox.Password;
        }

        ProviderEditor_Changed(sender, e);
    }

    private void ApiKeyRevealBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_showingApiKey)
        {
            ApiKeyBox.Password = ApiKeyRevealBox.Text;
            ProviderEditor_Changed(sender, e);
        }
    }

    private void ToggleApiKey_Click(object sender, RoutedEventArgs e)
    {
        SetApiKeyVisibility(!_showingApiKey);
    }

    private void SetApiKeyVisibility(bool show)
    {
        _showingApiKey = show;
        if (show)
        {
            ApiKeyRevealBox.Text = ApiKeyBox.Password;
            ApiKeyRevealBox.Visibility = Visibility.Visible;
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ToggleApiKeyButton.Content = "Hide";
        }
        else
        {
            ApiKeyBox.Password = ApiKeyRevealBox.Text;
            ApiKeyBox.Visibility = Visibility.Visible;
            ApiKeyRevealBox.Visibility = Visibility.Collapsed;
            ToggleApiKeyButton.Content = "See";
        }
    }

    private void CopyApiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = _showingApiKey ? ApiKeyRevealBox.Text : ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            StatusText.Text = "No API key to copy.";
            return;
        }

        Clipboard.SetText(key);
        StatusText.Text = "API key copied to clipboard.";
    }

    private void CopyBaseUrl_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BaseUrlBox.Text))
        {
            StatusText.Text = "No base URL to copy.";
            return;
        }

        Clipboard.SetText(BaseUrlBox.Text);
        StatusText.Text = "Base URL copied to clipboard.";
    }

    private void StarModel_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider is null || ProviderModelsList.SelectedValue is not string model)
        {
            StatusText.Text = "Select a model first.";
            return;
        }

        if (_selectedProvider.StarredModels.Contains(model))
        {
            _selectedProvider.StarredModels.Remove(model);
            StatusText.Text = $"Removed star from model: {model}";
        }
        else
        {
            _selectedProvider.StarredModels.Add(model);
            StatusText.Text = $"Starred model: {model}";
        }
        
        _stateService.Save(_state);
        RefreshModelList();
        RefreshModelCombos();
    }

    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider is null || ProviderModelsList.SelectedValue is not string model)
        {
            StatusText.Text = "Select a model first.";
            return;
        }

        _selectedProvider.LoadedModels.Remove(model);
        _selectedProvider.StarredModels.Remove(model);
        _stateService.Save(_state);
        RefreshModelList();
        RefreshModelCombos();
        RefreshModelCatalogText();
        StatusText.Text = $"Removed model from catalog: {model}";
    }

    private void RefreshModelList()
    {
        if (_selectedProvider is null) return;
        var selected = ProviderModelsList.SelectedValue as string;
        ProviderModelsList.ItemsSource = _selectedProvider.LoadedModels.Select(m => new ModelDisplayItem
        {
            ModelName = m, 
            DisplayText = _selectedProvider.StarredModels.Contains(m) ? $"★ {m}" : $"   {m}"
        }).ToList();
        ProviderModelsList.DisplayMemberPath = "DisplayText";
        ProviderModelsList.SelectedValuePath = "ModelName";
        ProviderModelsList.SelectedValue = selected;
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFromControls();
        SaveAppConfigFromControls();
        MessageBox.Show(_switcher.BuildPreview(_state), "Switch preview", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SaveProviderFromControls();
        SaveAppConfigFromControls();
        var preview = _switcher.BuildPreview(_state);
        var answer = MessageBox.Show(preview + "\nApply this switch now?", "Confirm provider switch", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = _switcher.Apply(_state);
            var files = result.ChangedFiles.Count == 0 ? "No files changed" : string.Join("\n", result.ChangedFiles);
            var env = result.EnvironmentKeys.Count == 0 ? "No environment variables changed" : string.Join(", ", result.EnvironmentKeys.Distinct());
            var backups = result.Backups.Count == 0 ? "No previous config files existed, so no backups were needed" : string.Join("\n", result.Backups);
            StatusText.Text = "Applied switch. Restart Claude Code/Codex terminals to pick up new environment variables.";
            MessageBox.Show($"Switch applied.\n\nFiles:\n{files}\n\nEnvironment:\n{env}\n\nBackups:\n{backups}", "ProviderPilot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Could not apply switch", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshDiagnostics();
    }

    private void RefreshProviderCombos()
    {
        if (_state.Profiles.Count == 0)
        {
            return;
        }

        var providers = _state.Profiles.ToList();
        SetProviderItems(ClaudeDefaultProviderBox, providers);
        SetProviderItems(ClaudeOpusProviderBox, providers);
        SetProviderItems(ClaudeSonnetProviderBox, providers);
        SetProviderItems(ClaudeHaikuProviderBox, providers);
        SetProviderItems(ClaudeSubagentProviderBox, providers);
        SetProviderItems(CodexProviderBox, providers);
    }

    private void RefreshModelCombos()
    {
        SetModelItems(ClaudeDefaultBox, ModelsFor(ProviderComboId(ClaudeDefaultProviderBox)));
        SetModelItems(ClaudeOpusBox, ModelsFor(ProviderComboId(ClaudeOpusProviderBox)));
        SetModelItems(ClaudeSonnetBox, ModelsFor(ProviderComboId(ClaudeSonnetProviderBox)));
        SetModelItems(ClaudeHaikuBox, ModelsFor(ProviderComboId(ClaudeHaikuProviderBox)));
        SetModelItems(ClaudeSubagentBox, ModelsFor(ProviderComboId(ClaudeSubagentProviderBox)));
        SetModelItems(CodexModelBox, ModelsFor(ProviderComboId(CodexProviderBox)));
    }

    private List<string> ModelsFor(string providerId)
    {
        return _state.Profiles.FirstOrDefault(provider => provider.Id == providerId)?.StarredModels
            ?? _selectedProvider?.StarredModels
            ?? [];
    }

    private void RefreshModelCatalogText()
    {
        if (_selectedProvider is null)
        {
            return;
        }

        ModelCatalogText.Text = _selectedProvider.LoadedModels.Count == 0
            ? "No models loaded yet. Add the provider key, then load models."
            : $"{_selectedProvider.LoadedModels.Count} models loaded" +
              (_selectedProvider.ModelsLoadedAt is null ? "." : $" at {_selectedProvider.ModelsLoadedAt:t}.");
    }

    private void RefreshDiagnostics()
    {
        var issues = _switcher.Validate(_state);
        var hasError = issues.Any(issue => issue.Level == "Error");
        var hasWarning = issues.Any(issue => issue.Level == "Warning");

        if (hasError)
        {
            HealthText.Text = "Needs setup";
            HealthBadge.Background = new SolidColorBrush(Color.FromRgb(255, 241, 242));
            HealthText.Foreground = new SolidColorBrush(Color.FromRgb(190, 18, 60));
        }
        else if (hasWarning)
        {
            HealthText.Text = "Review";
            HealthBadge.Background = (Brush)FindResource("WarnSoft");
            HealthText.Foreground = (Brush)FindResource("Warn");
        }
        else
        {
            HealthText.Text = "Ready";
            HealthBadge.Background = (Brush)FindResource("BrandSoft");
            HealthText.Foreground = (Brush)FindResource("Brand");
        }
    }

    private IEnumerable<ClaudeModelSlot> ClaudeSlots()
    {
        yield return _state.ClaudeCode.Default;
        yield return _state.ClaudeCode.Opus;
        yield return _state.ClaudeCode.Sonnet;
        yield return _state.ClaudeCode.Haiku;
        yield return _state.ClaudeCode.Subagent;
    }

    private static void SetProviderItems(ComboBox combo, List<RouteProfile> providers)
    {
        var selected = (combo.SelectedItem as RouteProfile)?.Id;
        combo.ItemsSource = null;
        combo.DisplayMemberPath = nameof(RouteProfile.Name);
        combo.SelectedValuePath = nameof(RouteProfile.Id);
        combo.ItemsSource = providers;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            combo.SelectedValue = selected;
        }
    }

    private void SetProviderCombo(ComboBox combo, string providerId)
    {
        var provider = SwitcherService.FindProvider(_state, providerId);
        combo.SelectedValue = provider.Id;
    }

    private string ProviderComboId(ComboBox combo)
    {
        return combo.SelectedValue?.ToString()
            ?? (combo.SelectedItem as RouteProfile)?.Id
            ?? _state.Profiles.FirstOrDefault()?.Id
            ?? "";
    }

    private static void SetModelItems(ComboBox combo, List<string> models)
    {
        var text = combo.Text;
        combo.ItemsSource = null;
        combo.ItemsSource = models;
        combo.Text = text;
    }

    private static void SetCombo(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string ComboText(ComboBox combo, string fallback)
    {
        var typed = combo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            return typed;
        }

        return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? combo.SelectedItem?.ToString()
            ?? fallback;
    }
}

public class ModelDisplayItem
{
    public string ModelName { get; set; } = "";
    public string DisplayText { get; set; } = "";
}
