using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.BusinessTypes;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.BusinessTypes;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;

namespace Omni.App.Tests.Repositories;

/// <summary>QA t12-fix-06: a client names its own stages; keys, order and categories stay the type's.</summary>
public class ClientStageLabelTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);

    private BusinessTypeService Sut(Persistence.Contexts.ApplicationDbContext db, string language = AppLanguage.English) =>
        new(new BusinessTypeRepository(db, _cache), new ClientRepository(db, _cache), new ContactRepository(db), new UnitOfWork(db),
            TextKit.Context(language), TimeProvider.System);

    [PostgresFact]
    public async Task Own_labels_replace_the_types_in_their_language_and_clearing_them_goes_back()
    {
        var client = MakeClient(businessType: TextKit.General);
        var other = MakeClient(businessType: TextKit.General);
        await SeedAsync(client, other);

        await using var db = NewContext();
        var before = await Sut(db).ListStagesForClientAsync(client.Id);
        var first = before[0];

        await Sut(db).SaveClientStageLabelsAsync(client.Id, new Dictionary<string, Dictionary<string, string>>
        {
            [first.Key] = new() { [AppLanguage.English] = "  Fresh lead ", [AppLanguage.Arabic] = "عميل جديد" }
        });

        await using var read = NewContext();
        var english = await Sut(read).ListStagesForClientAsync(client.Id);
        Assert.Equal("Fresh lead", english[0].Label);
        Assert.Equal((first.Key, first.Category), (english[0].Key, english[0].Category)); // key and category unchanged
        Assert.Equal(before.Select(s => s.Key), english.Select(s => s.Key));               // order unchanged
        Assert.Equal("عميل جديد", (await Sut(read, AppLanguage.Arabic).ListStagesForClientAsync(client.Id))[0].Label);
        // French has no own label: the type's French label, not the client's English one.
        var typeFrench = (await Sut(read, AppLanguage.French).ListStagesForClientAsync(other.Id))[0].Label;
        Assert.Equal(typeFrench, (await Sut(read, AppLanguage.French).ListStagesForClientAsync(client.Id))[0].Label);
        Assert.Equal(first.Label, (await Sut(read).ListStagesForClientAsync(other.Id))[0].Label); // another client keeps the type's

        var row = Assert.Single(await Sut(read).ListClientStageLabelsAsync(client.Id), r => r.Key == first.Key);
        Assert.Equal("Fresh lead", row.OwnLabels[AppLanguage.English]);

        // Saving without it goes back to the type's name (the cached list is dropped by the save).
        await Sut(read).SaveClientStageLabelsAsync(client.Id, new Dictionary<string, Dictionary<string, string>>());
        await using var after = NewContext();
        Assert.Equal(first.Label, (await Sut(after).ListStagesForClientAsync(client.Id))[0].Label);
    }

    [PostgresFact]
    public async Task A_key_that_is_not_the_clients_stage_or_a_too_long_label_is_refused()
    {
        var client = MakeClient(businessType: TextKit.General);
        await SeedAsync(client);
        await using var db = NewContext();
        var key = (await Sut(db).ListStagesForClientAsync(client.Id))[0].Key;

        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => Sut(db).SaveClientStageLabelsAsync(client.Id,
            new Dictionary<string, Dictionary<string, string>> { ["not_a_stage"] = new() { ["en"] = "X" } }));
        Assert.Equal("businessTypes.errors.stageNotOfClient", unknown.Message);
        var tooLong = await Assert.ThrowsAsync<ArgumentException>(() => Sut(db).SaveClientStageLabelsAsync(client.Id,
            new Dictionary<string, Dictionary<string, string>> { [key] = new() { ["en"] = new string('x', BusinessTypeService.MaxLabelLength + 1) } }));
        Assert.Equal("businessTypes.errors.textTooLong", tooLong.Message);
    }
}
