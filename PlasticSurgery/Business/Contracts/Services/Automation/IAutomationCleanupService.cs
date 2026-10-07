using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Clinics;

namespace PlasticSurgery.Business.Contracts.Services.Automation;

/// <summary>
/// Cleanup for the SculptFlowAutomation tester: each run registers a throwaway clinic through the normal sign-up page,
/// exercises the system as that clinic, then deletes it so nothing it created is left behind.
/// </summary>
public interface IAutomationCleanupService
{
    /// <summary>Automation clinics that still exist (e.g. a run that crashed before cleaning up), oldest first.</summary>
    Task<IReadOnlyList<ClinicSummaryRow>> ListClinicsAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes an automation clinic and its identity users in one transaction. The clinic row's ON DELETE CASCADE
    /// foreign keys remove all its data. Any clinic that isn't an automation clinic is left untouched.
    /// </summary>
    Task<AutomationDeleteOutcome> DeleteClinicAsync(Guid clinicId, CancellationToken ct = default);
}
