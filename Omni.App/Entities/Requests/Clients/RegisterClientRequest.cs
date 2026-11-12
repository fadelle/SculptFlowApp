using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace Omni.App.Entities.Requests.Clients;

/// <summary>Self sign-up. BusinessTypeCode must name an active business type (the Register page's picker).</summary>
public record RegisterClientRequest(string? FullName, string? ClientName, string? Email, string? Password, string? ConfirmPassword,
    string? BusinessTypeCode = null,
    // The browser's IANA time zone at signup (QA t5-ops-21: every client started on UTC); unknown or blank = UTC.
    string? Timezone = null);
