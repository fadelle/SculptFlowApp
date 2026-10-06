using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Entities.Dtos.Clinics;
using PlasticSurgery.Entities.Requests.Clinics;

namespace PlasticSurgery.Business.Contracts.Services.Clinics;

/// <summary>
/// Normal registration ALWAYS means NEW USER -> NEW CLINIC. It never attaches an account to an existing
/// clinic (joining one will be a separate invitation/staff flow), so a new signup starts with an empty,
/// fully isolated tenant: no leads, conversations, messages, appointments, procedures, Knowledge Base,
/// campaigns or channel connections — every one of those is scoped by clinic_id, and nothing here copies
/// or shares data from another clinic.
///
/// Everything happens in ONE database transaction on the shared DbContext (Identity's UserManager uses
/// the same context), so a failure at any step — weak password, duplicate email, a slug collision,
/// anything — rolls the whole thing back: never a clinic without its user, a membership without its
/// clinic, or a user with no clinic.
/// </summary>
public interface IClinicRegistrationService
{
    Task<ClinicRegistrationResult> RegisterAsync(RegisterClinicRequest request, CancellationToken ct = default);

    /// <summary>Same NEW USER -> NEW CLINIC rule and the same single transaction as <see cref="RegisterAsync"/>,
    /// but the user has no password and is created with a provider-verified email.</summary>
    Task<ClinicRegistrationResult> RegisterExternalAsync(RegisterExternalClinicRequest request, CancellationToken ct = default);
}
