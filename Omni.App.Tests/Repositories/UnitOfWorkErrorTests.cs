using Omni.App.Persistence.Repositories;

namespace Omni.App.Tests.Repositories;

/// <summary>QA t1-accounts-02: a save the data itself makes fail is a 400 with a clear message, never a 500 page.</summary>
public class UnitOfWorkErrorTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task A_reference_to_a_missing_row_is_a_missing_reference()
    {
        var client = MakeClient();
        await SeedAsync(client);
        await using var db = NewContext();
        var orphan = MakeContact(client.Id);
        orphan.ServiceId = Guid.NewGuid();   // no such service
        db.Contacts.Add(orphan);

        var ex = await Assert.ThrowsAsync<InvalidRecordException>(() => new UnitOfWork(db).SaveChangesAsync());

        Assert.Equal(InvalidRecordException.MissingReferenceKey, ex.Message);
        Assert.Equal(400, Omni.App.Controllers.Filters.ApiErrorsAttribute.StatusFor(ex));
    }

    [PostgresFact]
    public async Task Text_longer_than_its_column_is_an_invalid_value()
    {
        var client = MakeClient();
        await SeedAsync(client);
        await using var db = NewContext();
        var contact = MakeContact(client.Id);
        contact.City = new string('c', 500);   // crm.contacts.city is varchar(100)
        db.Contacts.Add(contact);

        var ex = await Assert.ThrowsAsync<InvalidRecordException>(() => new UnitOfWork(db).TrySaveChangesAsync());
        Assert.Equal(InvalidRecordException.InvalidValueKey, ex.Message);
    }
}
