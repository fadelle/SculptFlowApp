namespace Omni.App.Entities.Requests.Contacts;

/// <summary>Body for DELETE /api/contacts/{id}: the contact's name (else phone, else email) typed to confirm.</summary>
public record DeleteContactRequest(string? Confirmation);
