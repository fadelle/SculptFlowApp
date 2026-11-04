using Omni.App.Entities.Models;

namespace Omni.App.Entities.Dtos.Clients;

/// <summary>A client the admin portal opened without a user yet (IClientRegistrationService.OpenForOwnerAsync): the client, and
/// the plan it was put on (PlanStarted false when that failed; the client still exists).</summary>
public record OpenedClient(Client Client, string? PlanCode, bool PlanStarted);
