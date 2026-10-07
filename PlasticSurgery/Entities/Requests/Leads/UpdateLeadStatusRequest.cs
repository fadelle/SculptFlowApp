namespace PlasticSurgery.Entities.Requests.Leads;

/// <summary>QualificationStatus and Source are both optional — null means "leave unchanged".
/// Source has no fixed enum (Lead.Source is free text, no CHECK constraint — see Entities/Models/Lead.cs),
/// so it isn't validated against a status list the way Status/QualificationStatus are.</summary>
public record UpdateLeadStatusRequest(
    string Status,
    string? QualificationStatus,
    string? Source = null
);
