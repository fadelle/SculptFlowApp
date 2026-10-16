namespace Omni.App.Entities.Requests.Billing;

public record RateCardRequest(string Code, string Name, string? Description, Guid? ClientId, bool IsDefault = false);
