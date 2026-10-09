using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Configuration;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Configuration;

namespace PlasticSurgery.Business.Managers;

/// <summary>
/// Singleton. Holds a copy of config.settings (section + key → value) and answers reads from it, falling back to the
/// constant default in ConfigDefaults. ConfigRefreshJob refreshes it at startup and every minute; SettingsService
/// refreshes it after a change. A stored value that doesn't parse as the setting's type or is out of its range
/// (someone edited the table by hand) is ignored and the default is used, with a warning in the log.
/// </summary>
public class ConfigManager : IConfigManager
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ConfigManager> _logger;
    private volatile IReadOnlyDictionary<(string Section, string Key), string> _values =
        new Dictionary<(string, string), string>();

    public ConfigManager(IServiceScopeFactory scopes, ILogger<ConfigManager> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    // ---- Availability
    public int AvailabilityDefaultDurationMinutes => GetInt(ConfigDefaults.AvailabilityDefaultDurationMinutes);
    public int AvailabilityDefaultBufferMinutes => GetInt(ConfigDefaults.AvailabilityDefaultBufferMinutes);
    public int AvailabilityDefaultNoticeMinutes => GetInt(ConfigDefaults.AvailabilityDefaultNoticeMinutes);
    public int AvailabilityDefaultHorizonDays => GetInt(ConfigDefaults.AvailabilityDefaultHorizonDays);
    public int AvailabilityMaxRangeDays => GetInt(ConfigDefaults.AvailabilityMaxRangeDays);

    // ---- Billing
    public bool BillingEnabled => GetBool(ConfigDefaults.BillingEnabled);
    public int BillingGracePeriodDays => GetInt(ConfigDefaults.BillingGracePeriodDays);
    public int BillingReservationTimeoutHours => GetInt(ConfigDefaults.BillingReservationTimeoutHours);
    public int BillingMaintenanceIntervalMinutes => GetInt(ConfigDefaults.BillingMaintenanceIntervalMinutes);
    public string BillingSignupPlanCode => GetString(ConfigDefaults.BillingSignupPlanCode);
    public string BillingCurrency => GetString(ConfigDefaults.BillingCurrency).Trim().ToUpperInvariant();
    public int BillingRateBackdateToleranceMinutes => GetInt(ConfigDefaults.BillingRateBackdateToleranceMinutes);
    public int BillingRecentTransactions => GetInt(ConfigDefaults.BillingRecentTransactions);

    // ---- Knowledge
    public decimal KnowledgeMinScore => GetDecimal(ConfigDefaults.KnowledgeMinScore);
    public int KnowledgeChunkMaxChars => GetInt(ConfigDefaults.KnowledgeChunkMaxChars);
    public int KnowledgeChunkOverlapChars => GetInt(ConfigDefaults.KnowledgeChunkOverlapChars);
    public int KnowledgeChunkMinChars => GetInt(ConfigDefaults.KnowledgeChunkMinChars);
    public int KnowledgeMaxUploadBytes => GetInt(ConfigDefaults.KnowledgeMaxUploadBytes);
    public int KnowledgeMaxExtractedChars => GetInt(ConfigDefaults.KnowledgeMaxExtractedChars);
    public int KnowledgeMinChunkSizeTokens => GetInt(ConfigDefaults.KnowledgeMinChunkSizeTokens);
    public int KnowledgeMaxChunkSizeTokens => GetInt(ConfigDefaults.KnowledgeMaxChunkSizeTokens);
    public int KnowledgeMinTopK => GetInt(ConfigDefaults.KnowledgeMinTopK);
    public int KnowledgeMaxTopK => GetInt(ConfigDefaults.KnowledgeMaxTopK);

    // ---- WebScraping
    public int WebScrapingMaxPages => GetInt(ConfigDefaults.WebScrapingMaxPages);
    public int WebScrapingMaxDepth => GetInt(ConfigDefaults.WebScrapingMaxDepth);
    public int WebScrapingMaxConcurrency => GetInt(ConfigDefaults.WebScrapingMaxConcurrency);
    public int WebScrapingRequestTimeoutSeconds => GetInt(ConfigDefaults.WebScrapingRequestTimeoutSeconds);
    public int WebScrapingMaxResponseBytes => GetInt(ConfigDefaults.WebScrapingMaxResponseBytes);
    public int WebScrapingMaxRedirects => GetInt(ConfigDefaults.WebScrapingMaxRedirects);
    public int WebScrapingPolitenessDelayMs => GetInt(ConfigDefaults.WebScrapingPolitenessDelayMs);
    public int WebScrapingMaxRunMinutes => GetInt(ConfigDefaults.WebScrapingMaxRunMinutes);
    public int WebScrapingMinTextChars => GetInt(ConfigDefaults.WebScrapingMinTextChars);
    public int WebScrapingMaxTextChars => GetInt(ConfigDefaults.WebScrapingMaxTextChars);
    public int WebScrapingMaxQueryVariantsPerPath => GetInt(ConfigDefaults.WebScrapingMaxQueryVariantsPerPath);
    public int WebScrapingMaxLinksPerPage => GetInt(ConfigDefaults.WebScrapingMaxLinksPerPage);
    public int WebScrapingMaxSourcesPerClinic => GetInt(ConfigDefaults.WebScrapingMaxSourcesPerClinic);
    public string WebScrapingDevAllowedHosts => GetString(ConfigDefaults.WebScrapingDevAllowedHosts);
    public string WebScrapingUserAgent => GetString(ConfigDefaults.WebScrapingUserAgent);

    // ---- Benchmark
    public int BenchmarkGenerationSampleSize => GetInt(ConfigDefaults.BenchmarkGenerationSampleSize);
    public int BenchmarkSamplePoolSize => GetInt(ConfigDefaults.BenchmarkSamplePoolSize);
    public int BenchmarkMinSourceChunkChars => GetInt(ConfigDefaults.BenchmarkMinSourceChunkChars);
    public int BenchmarkMaxConsecutiveErrors => GetInt(ConfigDefaults.BenchmarkMaxConsecutiveErrors);
    public int BenchmarkStaleRunHours => GetInt(ConfigDefaults.BenchmarkStaleRunHours);
    public int BenchmarkGenerationTimeoutMinutes => GetInt(ConfigDefaults.BenchmarkGenerationTimeoutMinutes);
    public int BenchmarkMaxQuestionsPerChunk => GetInt(ConfigDefaults.BenchmarkMaxQuestionsPerChunk);

    // ---- Campaigns
    public int CampaignsDefaultInactiveDays => GetInt(ConfigDefaults.CampaignsDefaultInactiveDays);

    // ---- Embeddings
    public string EmbeddingsBaseUrl => GetString(ConfigDefaults.EmbeddingsBaseUrl);
    public string EmbeddingsModel => GetString(ConfigDefaults.EmbeddingsModel);
    public int EmbeddingsDimensions => GetInt(ConfigDefaults.EmbeddingsDimensions);
    public string EmbeddingsApiKey => GetString(ConfigDefaults.EmbeddingsApiKey);
    public int EmbeddingsMaxInputsPerRequest => GetInt(ConfigDefaults.EmbeddingsMaxInputsPerRequest);

    // ---- Integrations
    public int IntegrationsOAuthStateLifetimeMinutes => GetInt(ConfigDefaults.IntegrationsOAuthStateLifetimeMinutes);
    public int IntegrationsTokenRefreshMarginMinutes => GetInt(ConfigDefaults.IntegrationsTokenRefreshMarginMinutes);

    // ---- Infobip
    public int InfobipTimeoutSeconds => GetInt(ConfigDefaults.InfobipTimeoutSeconds);
    public int InfobipMaxSendAttempts => GetInt(ConfigDefaults.InfobipMaxSendAttempts);

    // ---- Dashboard
    public int DashboardLeadsPageSize => GetInt(ConfigDefaults.DashboardLeadsPageSize);

    // ---- generic access

    public string GetString(string section, string key) => GetString(Definition(section, key));
    public int GetInt(string section, string key) => GetInt(Definition(section, key));
    public decimal GetDecimal(string section, string key) => GetDecimal(Definition(section, key));
    public bool GetBool(string section, string key) => GetBool(Definition(section, key));

    public string GetString(ConfigDefinition setting) => Value(setting);
    public int GetInt(ConfigDefinition setting) => ConfigValues.ToInt(Value(setting));
    public decimal GetDecimal(ConfigDefinition setting) => ConfigValues.ToDecimal(Value(setting));
    public bool GetBool(ConfigDefinition setting) => ConfigValues.ToBool(Value(setting));

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            Apply(await scope.ServiceProvider.GetRequiredService<ISettingRepository>().ListReadOnlyAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Couldn't read config.settings; keeping the last values (defaults until the first read succeeds).");
        }
    }

    /// <summary>Replaces the in-memory values with these rows (RefreshAsync uses it; tests call it directly).</summary>
    public void Apply(IEnumerable<ConfigSetting> rows) =>
        // Rows that differ only by letter case collapse to one setting (last one wins) instead of failing every refresh.
        _values = rows.GroupBy(r => (r.Section.ToLowerInvariant(), r.Key.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.Last().Value);

    // ------------------------------------------------------------------------------------------------------------

    private string Value(ConfigDefinition setting)
    {
        if (!_values.TryGetValue((setting.Section.ToLowerInvariant(), setting.Key.ToLowerInvariant()), out var stored)) return setting.DefaultValue;
        if (ConfigValues.Validate(setting, stored) is { } problem)
        {
            _logger.LogWarning("config.settings {Section}:{Key} = '{Value}' is invalid ({Problem}); using the default {Default}.",
                setting.Section, setting.Key, setting.IsSecret ? "(secret)" : stored, problem, setting.IsSecret ? "(secret)" : setting.DefaultValue);
            return setting.DefaultValue;
        }
        return stored;
    }

    private static ConfigDefinition Definition(string section, string key) =>
        ConfigDefaults.Find(section, key)
        ?? throw new InvalidOperationException($"Setting {section}:{key} isn't declared in Common/Statics/ConfigDefaults.");
}
