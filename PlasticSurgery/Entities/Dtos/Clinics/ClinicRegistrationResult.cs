using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Dtos.Clinics;

/// <summary>Success carries the new user + clinic (so the caller can sign the user in); failure carries
/// messages that are safe to show on the Register page.</summary>
public record ClinicRegistrationResult(bool Succeeded, IdentityUser? User, Clinic? Clinic, IReadOnlyList<string> Errors)
{
    public static ClinicRegistrationResult Fail(params string[] errors) => new(false, null, null, errors);
    public static ClinicRegistrationResult Fail(IEnumerable<string> errors) => new(false, null, null, errors.ToList());
}
