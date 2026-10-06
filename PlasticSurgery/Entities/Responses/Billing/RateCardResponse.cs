namespace PlasticSurgery.Entities.Responses.Billing;

public record RateCardResponse(Guid Id, string Code, string Name, string? Description, Guid? ClinicId, bool IsDefault, bool IsActive,
    int CurrentRates);
