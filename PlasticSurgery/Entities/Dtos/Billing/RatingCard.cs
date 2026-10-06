namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>One rate card a clinic's events are rated against, and why it applies (client, plan or default).</summary>
public record RatingCard(Guid CardId, string Source);
