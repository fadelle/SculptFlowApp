-- =====================================================================
-- Configuration seed: one config.settings row per setting declared in
-- PlasticSurgery/Common/Statics/ConfigDefaults.cs, with its default value.
-- GENERATED from ConfigDefaults (56 settings); regenerate it when a setting is added.
--
-- Safe to re-run: a row that already exists is left alone, so values
-- changed from the admin portal's Configuration page are never reset.
-- Run after Database/schema.sql (needs the config.settings table).
--
-- Note: a seeded row pins the value. If a default later changes in
-- ConfigDefaults, databases seeded earlier keep the old value until it is
-- changed or reset on the Configuration page.
-- =====================================================================
insert into config.settings (section, key, value, updated_by) values
  ('Availability', 'DefaultDurationMinutes', '30', 'seed'),
  ('Availability', 'DefaultBufferMinutes', '0', 'seed'),
  ('Availability', 'DefaultNoticeMinutes', '240', 'seed'),
  ('Availability', 'DefaultHorizonDays', '60', 'seed'),
  ('Availability', 'MaxRangeDays', '14', 'seed'),
  ('Billing', 'Enabled', 'true', 'seed'),
  ('Billing', 'GracePeriodDays', '7', 'seed'),
  ('Billing', 'ReservationTimeoutHours', '72', 'seed'),
  ('Billing', 'MaintenanceIntervalMinutes', '5', 'seed'),
  ('Billing', 'SignupPlanCode', '', 'seed'),
  ('Billing', 'Currency', 'USD', 'seed'),
  ('Billing', 'RateBackdateToleranceMinutes', '5', 'seed'),
  ('Billing', 'RecentTransactions', '15', 'seed'),
  ('Knowledge', 'MinScore', '0.30', 'seed'),
  ('Knowledge', 'ChunkMaxChars', '1000', 'seed'),
  ('Knowledge', 'ChunkOverlapChars', '150', 'seed'),
  ('Knowledge', 'ChunkMinChars', '200', 'seed'),
  ('Knowledge', 'MaxUploadBytes', '5242880', 'seed'),
  ('Knowledge', 'MaxExtractedChars', '250000', 'seed'),
  ('Knowledge', 'MinChunkSizeTokens', '50', 'seed'),
  ('Knowledge', 'MaxChunkSizeTokens', '1000', 'seed'),
  ('Knowledge', 'MinTopK', '1', 'seed'),
  ('Knowledge', 'MaxTopK', '10', 'seed'),
  ('WebScraping', 'MaxPages', '100', 'seed'),
  ('WebScraping', 'MaxDepth', '3', 'seed'),
  ('WebScraping', 'MaxConcurrency', '3', 'seed'),
  ('WebScraping', 'RequestTimeoutSeconds', '15', 'seed'),
  ('WebScraping', 'MaxResponseBytes', '2000000', 'seed'),
  ('WebScraping', 'MaxRedirects', '5', 'seed'),
  ('WebScraping', 'PolitenessDelayMs', '300', 'seed'),
  ('WebScraping', 'MaxRunMinutes', '20', 'seed'),
  ('WebScraping', 'MinTextChars', '80', 'seed'),
  ('WebScraping', 'MaxTextChars', '200000', 'seed'),
  ('WebScraping', 'MaxQueryVariantsPerPath', '5', 'seed'),
  ('WebScraping', 'MaxLinksPerPage', '300', 'seed'),
  ('WebScraping', 'MaxSourcesPerClinic', '25', 'seed'),
  ('WebScraping', 'DevAllowedHosts', '', 'seed'),
  ('WebScraping', 'UserAgent', 'SculptFlowBot/1.0 (+https://sculptflowapp.onrender.com; clinic knowledge-base crawler)', 'seed'),
  ('Benchmark', 'GenerationSampleSize', '20', 'seed'),
  ('Benchmark', 'SamplePoolSize', '60', 'seed'),
  ('Benchmark', 'MinSourceChunkChars', '80', 'seed'),
  ('Benchmark', 'MaxConsecutiveErrors', '5', 'seed'),
  ('Benchmark', 'StaleRunHours', '2', 'seed'),
  ('Benchmark', 'GenerationTimeoutMinutes', '30', 'seed'),
  ('Benchmark', 'MaxQuestionsPerChunk', '3', 'seed'),
  ('Campaigns', 'DefaultInactiveDays', '60', 'seed'),
  ('Embeddings', 'BaseUrl', 'https://api.openai.com/v1', 'seed'),
  ('Embeddings', 'Model', 'text-embedding-3-small', 'seed'),
  ('Embeddings', 'Dimensions', '1536', 'seed'),
  ('Embeddings', 'ApiKey', '', 'seed'),
  ('Embeddings', 'MaxInputsPerRequest', '64', 'seed'),
  ('Integrations', 'OAuthStateLifetimeMinutes', '15', 'seed'),
  ('Integrations', 'TokenRefreshMarginMinutes', '5', 'seed'),
  ('Infobip', 'TimeoutSeconds', '20', 'seed'),
  ('Infobip', 'MaxSendAttempts', '3', 'seed'),
  ('Dashboard', 'LeadsPageSize', '25', 'seed')
on conflict (section, key) do nothing;
