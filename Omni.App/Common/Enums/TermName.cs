namespace Omni.App.Common.Enums;

/// <summary>The words a business type renames (core.business_types.terminology). Each has a singular ("one") and a
/// plural ("other") form per language. In UI strings they are placeholders: {contact} {contacts} {Contact} {Contacts}
/// (lower-case singular, plural, then the same with a capital first letter), and the same for every other term.</summary>
public static class TermName
{
    public const string Contact = "contact";
    public const string Member = "member";
    public const string Service = "service";
    public const string Booking = "booking";
    public const string Order = "order";
    public const string Client = "client";

    public static readonly IReadOnlyList<string> All = [Contact, Member, Service, Booking, Order, Client];
}
