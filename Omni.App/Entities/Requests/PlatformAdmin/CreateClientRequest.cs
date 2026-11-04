namespace Omni.App.Entities.Requests.PlatformAdmin;

/// <summary>The admin portal opens a client for someone the team sold to (POST /api/platform-admin/clients, QA t6-admin-20):
/// its name, business type and dashboard language, an optional plan (else the sign-up plan), and the owner who gets an emailed
/// invite. Null plan = the sign-up plan (Billing:SignupPlanCode); null language = English.</summary>
public record CreateClientRequest(
    string? Name,
    string? BusinessTypeCode,
    string? OwnerEmail,
    string? OwnerName,
    string? PlanCode,
    string? Language
);
