namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>One order in a contact export.</summary>
public record ContactExportOrder(Guid Id, string? ServiceName, string Status, decimal? QuotedAmount, decimal? DepositAmount, decimal? FinalAmount,
    string? CurrencyCode, DateTimeOffset? ServiceDate, string? Notes, DateTimeOffset CreatedAt);
