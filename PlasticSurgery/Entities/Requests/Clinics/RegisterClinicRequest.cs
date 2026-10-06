using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace PlasticSurgery.Entities.Requests.Clinics;

public record RegisterClinicRequest(string? FullName, string? ClinicName, string? Email, string? Password, string? ConfirmPassword);
