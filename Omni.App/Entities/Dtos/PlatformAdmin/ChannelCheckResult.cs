namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A WhatsApp connection checked with its stored token (QA t8-20): what Meta says about the number, or why it refused.
/// The admin portal copies it.</summary>
public sealed record ChannelCheckResult(bool Ok, string? DisplayPhoneNumber, string? VerifiedName, string? Error, DateTimeOffset CheckedAt)
{
    public Guid? ClientId { get; init; }
}
