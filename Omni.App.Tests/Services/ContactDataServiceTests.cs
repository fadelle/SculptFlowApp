using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Services.Contacts;

namespace Omni.App.Tests.Services;

/// <summary>Milestone C S4: privacy requests. Only the account owner exports or deletes; a delete needs the typed name
/// (else phone, else email) and runs in one transaction with an unlinked event.</summary>
public class ContactDataServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IContactDataRepository> _data = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Contact _contact;

    public ContactDataServiceTests()
    {
        _contact = new Contact { Id = Guid.NewGuid(), ClientId = _clientId, FullName = "Ann Lee", Phone = "+96170123456" };
        _contacts.Setup(c => c.GetAsync(_clientId, _contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(c => c.GetByIdAsync(_contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow" });
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
    }

    private ContactDataService Sut() =>
        new(_contacts.Object, _data.Object, _clients.Object, _events.Object, _uow.Object, TimeProvider.System, NullLogger<ContactDataService>.Instance);

    private UserAccess Owner => new("owner", _clientId, UserRole.Client, null, null, null);
    private UserAccess Member => new("m1", _clientId, UserRole.Member, null, null, null);

    [Fact]
    public async Task Members_can_neither_export_nor_delete()
    {
        Assert.Equal("contactDetail.data.errors.clientOnly", (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().ExportAsync(Member, _contact.Id))).Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().DeleteAsync(Member, _contact.Id, "Ann Lee"));
        _data.Verify(d => d.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_export_gathers_every_part_for_the_clients_contact_only()
    {
        var message = new ContactExportMessage(Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "inbound", "contact", "text", "hi", null, DateTimeOffset.UtcNow);
        _data.Setup(d => d.ListMessagesAsync(_clientId, _contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync([message]);

        var export = await Sut().ExportAsync(Owner, _contact.Id);

        Assert.Equal(("Glow", _contact.Id, "Ann Lee"), (export!.ClientName, export.Contact.Id, export.Contact.FullName));
        Assert.Equal(message, Assert.Single(export.Messages));
        Assert.Null(await Sut().ExportAsync(Owner, Guid.NewGuid()));
        Assert.Equal(_contact.Id, (await Sut().ExportAnyAsync(_contact.Id))!.Contact.Id);
    }

    [Theory]
    [InlineData("Ann Lee", true)]
    [InlineData("  ann lee ", true)]
    [InlineData("Ann", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task A_delete_needs_the_name_typed_exactly(string? typed, bool deletes)
    {
        if (deletes)
        {
            Assert.True(await Sut().DeleteAsync(Owner, _contact.Id, typed));
            _data.Verify(d => d.DeleteAsync(_clientId, _contact.Id, It.IsAny<CancellationToken>()), Times.Once);
            _events.Verify(e => e.Log(_clientId, EventTypes.ContactDeleted, null, null, null, "privacy_request", "{}"), Times.Once);
            _uow.Verify(u => u.DiscardChanges(), Times.Once);
            _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            Assert.Equal("contactDetail.data.errors.confirmMismatch", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().DeleteAsync(Owner, _contact.Id, typed))).Message);
            _data.Verify(d => d.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task Strangers_are_not_found_and_the_portal_learns_the_client()
    {
        Assert.False(await Sut().DeleteAsync(Owner, Guid.NewGuid(), "x"));
        Assert.Null(await Sut().DeleteAnyAsync(Guid.NewGuid()));
        Assert.Equal(_clientId, await Sut().DeleteAnyAsync(_contact.Id));
    }

    [Theory]
    [InlineData("Ann", "+961", "a@x.com", "Ann")]
    [InlineData(" ", "+961", "a@x.com", "+961")]
    [InlineData(null, null, "a@x.com", "a@x.com")]
    [InlineData(null, null, null, "")]
    public void The_confirmation_is_the_name_else_the_phone_else_the_email(string? name, string? phone, string? email, string expected)
    {
        Assert.Equal(expected, ContactDataRules.ConfirmationText(name, phone, email));
        Assert.False(ContactDataRules.Confirms("", ""));
    }
}
