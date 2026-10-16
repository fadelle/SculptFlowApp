using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Clients;

namespace Omni.App.Business.Contracts.Services.Automation;

/// <summary>
/// Cleanup for the Omni.Automation tester: each run registers a throwaway client through the normal sign-up page,
/// exercises the system as that client, then deletes it so nothing it created is left behind.
/// </summary>
public interface IAutomationCleanupService
{
    /// <summary>Automation clients that still exist (e.g. a run that crashed before cleaning up), oldest first.</summary>
    Task<IReadOnlyList<ClientSummaryRow>> ListClientsAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes an automation client and its identity users in one transaction. The client row's ON DELETE CASCADE
    /// foreign keys remove all its data. Any client that isn't an automation client is left untouched.
    /// </summary>
    Task<AutomationDeleteOutcome> DeleteClientAsync(Guid clientId, CancellationToken ct = default);
}
