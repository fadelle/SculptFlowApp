namespace Omni.App.Entities.Responses.Billing;

public record RateCardResponse(Guid Id, string Code, string Name, string? Description, Guid? ClientId, bool IsDefault, bool IsActive,
    int CurrentRates);
