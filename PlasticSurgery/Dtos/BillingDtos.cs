namespace PlasticSurgery.Dtos;

// ---------------------------------------------------------------------------------------------------------------
// Clinic-facing (GET /api/billing/*, Settings → Billing). Never carries provider names, provider costs or margins.
// ---------------------------------------------------------------------------------------------------------------

public record BillingEntitlementRow(string Key, string Label, string Kind, string Value);

public record BillingUsageBreakdownRow(string EventType, string Label, int Count, decimal Quantity, decimal Amount);

/// <summary>Usage SculptFlow didn't charge for (the clinic pays the provider directly, or there's no per-message fee).
/// Shown apart from SculptFlow charges so it never looks like it came out of the wallet.</summary>
public record ProviderDirectUsageRow(string EventType, string Label, string PaidBy, int Count, decimal Quantity);

/// <summary>A connected messaging account and who pays for its messages, in clinic-facing words.</summary>
public record ClinicChannelBillingRow(string Channel, string ChannelLabel, string? DisplayName, string PaidBy, bool ChargedBySculptFlow);

public record BillingTransactionRow(Guid Id, DateTimeOffset CreatedAt, string EntryType, string Label, string BalanceType,
    decimal Amount, decimal BalanceAfter, string? Reason);

public record ClinicBillingSummary(
    bool BillingEnabled,
    string Currency,
    string? PlanCode,
    string? PlanName,
    decimal? PlanPrice,
    string? BillingPeriod,
    string? SubscriptionStatus,
    bool HasAccess,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? GraceEndsAt,
    decimal PlanIncludedCredit,
    decimal IncludedCreditRemaining,
    decimal WalletBalance,
    decimal ReservedAmount,
    decimal Spendable,
    IReadOnlyList<BillingEntitlementRow> Entitlements,
    DateTimeOffset UsageSince,
    IReadOnlyList<BillingUsageBreakdownRow> Usage,
    decimal UsageTotal,
    IReadOnlyList<BillingTransactionRow> RecentTransactions,
    IReadOnlyList<ClinicChannelBillingRow> ChannelAccounts,
    IReadOnlyList<ProviderDirectUsageRow> ProviderDirectUsage);

public record ClinicUsageRow(Guid Id, DateTimeOffset OccurredAt, string EventType, string Label, string Channel, decimal Quantity,
    string? Unit, string? CountryCode, decimal? UnitPrice, decimal? Amount, decimal CreditAmount, decimal WalletAmount,
    decimal RefundedAmount, string ChargeStatus, string? FailureReason, bool ChargedBySculptFlow, string PaidBy);

public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ---------------------------------------------------------------------------------------------------------------
// Platform admin (/api/platform-admin/billing/*, X-Platform-Admin-Key), called by the SculptFlowAdmin portal, which
// keeps a copy of these shapes (SculptFlowAdmin/Billing/BillingApiContracts.cs). Internal: may show providers, costs
// and margins. Change a shape here → update that copy.
// ---------------------------------------------------------------------------------------------------------------

public record PlanRequest(
    string Code,
    string Name,
    string? Description,
    decimal Price,
    string BillingPeriod,
    decimal IncludedUsageCredit,
    string? RateCardCode,
    bool IsActive = true,
    int SortOrder = 0,
    Dictionary<string, string>? Entitlements = null);

public record PlanResponse(Guid Id, string Code, string Name, string? Description, decimal Price, string Currency, string BillingPeriod,
    decimal IncludedUsageCredit, string? RateCardCode, bool IsActive, int SortOrder, IReadOnlyDictionary<string, string> Entitlements,
    int Subscribers);

public record RateCardRequest(string Code, string Name, string? Description, Guid? ClinicId, bool IsDefault = false);

public record RateCardUpdateRequest(string Name, string? Description, bool IsActive, bool IsDefault);

public record RateCardResponse(Guid Id, string Code, string Name, string? Description, Guid? ClinicId, bool IsDefault, bool IsActive,
    int CurrentRates);

/// <summary>A new rate version. EffectiveFrom defaults to now and can't be in the past; an existing version of the
/// same rate (card, event, country, operator, provider, provider billing) is closed at that moment. ProviderBilling
/// null = SculptFlow-funded pricing (and cost reporting for any account); a responsibility (e.g. customer_direct) =
/// a SculptFlow usage fee for accounts where the customer pays the provider.</summary>
public record AddRateRequest(
    string EventType,
    string? CountryCode,
    string? Operator,
    string? Provider,
    string? Unit,
    decimal ProviderCost,
    decimal ClientRate,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null,
    string? Notes = null,
    string? ProviderBilling = null);

public record CloseRateRequest(DateTimeOffset? EffectiveTo);

public record RateResponse(Guid Id, string RateCardCode, string EventType, string? CountryCode, string? Operator, string? Provider,
    string? ProviderBilling, string Unit, decimal ProviderCost, decimal ClientRate, decimal Margin, string Currency,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string? Notes, string? CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Set who pays the provider for one connected channel account, and/or whether SculptFlow charges usage on
/// it. Null = keep the default for that part. Reason is required (audit).</summary>
public record ProviderBillingRequest(string? ProviderBilling, bool? OmniUsageBilling, string Reason);

public record ResetProviderBillingRequest(string Reason);

public record StartSubscriptionApiRequest(string PlanCode, bool ChargeFirstPeriod = true, string? Reason = null);

public record CancelSubscriptionRequest(bool Immediately = false, string? Reason = null);

public record TopUpRequest(decimal Amount, string? Reference, string? Reason);

public record AdjustmentRequest(decimal Amount, string BalanceType, string Reason);

public record RefundRequest(string Reason);

public record SubscriptionResponse(Guid Id, Guid ClinicId, string PlanCode, string PlanName, string Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd, bool CancelAtPeriodEnd, DateTimeOffset? PastDueSince,
    DateTimeOffset? EndedAt);

/// <param name="Amount">What SculptFlow charged (0 for usage it doesn't bill).</param>
/// <param name="ProviderCost">The provider's cost where known, whoever paid it (see ProviderBilling).</param>
/// <param name="Margin">Amount minus the provider cost SculptFlow itself paid.</param>
public record AdminUsageBreakdownRow(string EventType, string Channel, string ProviderBilling, int Count, decimal Quantity, decimal Amount,
    decimal ProviderCost, decimal Margin);

public record AdminBillingOverview(
    Guid ClinicId,
    string ClinicName,
    ClinicBillingSummary Summary,
    SubscriptionResponse? Subscription,
    string? CustomRateCardCode,
    IReadOnlyList<AdminUsageBreakdownRow> UsageWithCosts,
    int OpenReservations,
    ReconciliationResponse Reconciliation,
    IReadOnlyList<PlasticSurgery.Billing.ChannelAccountBilling> ChannelAccounts);

public record AdminUsageRow(Guid Id, string IdempotencyKey, DateTimeOffset OccurredAt, string EventType, string Channel,
    Guid? ChannelIntegrationId, string ProviderBilling, decimal Quantity,
    string? Unit, string? CountryCode, string? Operator, string? Provider, string? RateSource, Guid? RateId, decimal? UnitProviderCost,
    decimal? UnitPrice, decimal? ProviderCost, decimal? Amount, decimal ReservedAmount, decimal CreditAmount, decimal WalletAmount,
    decimal RefundedAmount, string Currency, string ChargeStatus, string ProviderOutcome, string? FailureReason, string? ReleaseReason, Guid? MessageId,
    Guid? ConversationId, Guid? CampaignId, string Source, string? Actor, DateTimeOffset? ReservedAt, DateTimeOffset? SettledAt,
    DateTimeOffset? ReleasedAt, DateTimeOffset? RefundedAt);

public record AdminLedgerRow(Guid Id, DateTimeOffset CreatedAt, string EntryType, string BalanceType, decimal Amount, decimal BalanceAfter,
    string Currency, string IdempotencyKey, Guid? UsageRecordId, Guid? SubscriptionId, Guid? PlanId, string Source, string? Actor,
    string? Reason, string? Reference, string? CorrelationId);

public record ReconciliationResponse(bool IsBalanced, decimal WalletBalance, decimal WalletLedgerTotal, decimal IncludedCreditBalance,
    decimal IncludedCreditLedgerTotal, decimal ReservedAmount, decimal OpenReservationsTotal);

public record BillingAccountRow(Guid ClinicId, string ClinicName, string? PlanCode, string? SubscriptionStatus, DateTimeOffset? CurrentPeriodEnd,
    decimal WalletBalance, decimal IncludedCreditBalance, decimal ReservedAmount, string Currency);

public record QuoteResponse(string EventType, string? CountryCode, string? Operator, string? Provider, Guid RateId, string RateCardCode,
    string RateSource, decimal UnitPrice, decimal UnitProviderCost, string Currency, string Unit);

/// <param name="Count">Usage that counted at the provider (delivered/billable), charged by SculptFlow or not.</param>
/// <param name="Revenue">SculptFlow usage revenue (charged, minus refunds).</param>
/// <param name="ProviderCost">Provider cost where known, whoever paid it (ProviderBilling says who).</param>
/// <param name="Margin">Revenue minus the provider cost SculptFlow paid itself.</param>
public record BillingReportRow(Guid ClinicId, string ClinicName, string Channel, string EventType, string ProviderBilling, int Count,
    decimal Quantity, decimal Revenue, decimal ProviderCost, decimal Margin);

public record BillingReport(DateTimeOffset From, DateTimeOffset To, string Currency, IReadOnlyList<BillingReportRow> Usage,
    decimal UsageRevenue, decimal ProviderCostPaidBySculptFlow, decimal ProviderCostPaidExternally, decimal Margin,
    decimal SubscriptionRevenue, decimal TopUps);
