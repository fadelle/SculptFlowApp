using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.BusinessTypes;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.BusinessTypes;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;

namespace Omni.App.Tests.Repositories;

/// <summary>The built-in business types schema.sql seeds have their name, six words (one and other) and every stage label in
/// English, Arabic and French (plan §3.6), and an Arabic or French client gets them through the same services the Contacts
/// and Inbox pages use.</summary>
public class BuiltInTypeWordsTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);

    public static TheoryData<string, string> TypesAndLanguages => new()
    {
        { TextKit.Clinic, AppLanguage.Arabic }, { TextKit.Clinic, AppLanguage.French },
        { TextKit.General, AppLanguage.Arabic }, { TextKit.General, AppLanguage.French }
    };

    [PostgresTheory]
    [MemberData(nameof(TypesAndLanguages))]
    public async Task Every_name_word_and_stage_label_exists_in_the_language(string code, string language)
    {
        await using var db = NewContext();
        var type = await db.BusinessTypes.AsNoTracking().SingleAsync(t => t.Code == code);

        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(type.NameJson)!;
        Assert.False(string.IsNullOrWhiteSpace(name.GetValueOrDefault(language)));
        Assert.NotEqual(name[AppLanguage.English], name[language]);

        var words = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, TermForms>>>(type.TerminologyJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(TermName.All.Order(), words[language].Keys.Order());
        Assert.All(words[language].Values, forms => Assert.False(string.IsNullOrWhiteSpace(forms.One) || string.IsNullOrWhiteSpace(forms.Other)));

        var stages = await db.PipelineStages.AsNoTracking().Where(s => s.BusinessTypeCode == code).ToListAsync();
        Assert.NotEmpty(stages);
        Assert.All(stages, stage =>
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(stage.LabelsJson)!;
            Assert.False(string.IsNullOrWhiteSpace(labels.GetValueOrDefault(language)), $"{code}.{stage.Key} has no {language} label");
        });
    }

    [PostgresTheory]
    [MemberData(nameof(TypesAndLanguages))]
    public async Task A_client_in_that_language_sees_its_stage_labels_and_words(string code, string language)
    {
        var client = MakeClient(businessType: code);
        client.DefaultLanguage = language;
        await SeedAsync(client);

        await using var db = NewContext();
        var types = new BusinessTypeRepository(db, _cache);
        var english = new BusinessTypeService(types, new ClientRepository(db, _cache), new ContactRepository(db), new UnitOfWork(db),
            TextKit.Context(), TimeProvider.System);
        var local = new BusinessTypeService(types, new ClientRepository(db, _cache), new ContactRepository(db), new UnitOfWork(db),
            TextKit.Context(language), TimeProvider.System);

        var englishLabels = (await english.ListStagesForClientAsync(client.Id)).Select(s => s.Label).ToList();
        var labels = (await local.ListStagesForClientAsync(client.Id)).Select(s => s.Label).ToList();
        Assert.Equal(englishLabels.Count, labels.Count);
        // Every label is the language's own, except where French and English share the word.
        Assert.All(labels.Zip(englishLabels), pair => Assert.True(pair.First != pair.Second || language == AppLanguage.French && pair.First == "Client",
            $"'{pair.First}' is still English"));

        var terminology = new Terminology(TextKit.Catalog, types, Mock.Of<ICurrentClientContext>());
        var words = await terminology.ForBusinessTypeAsync(code, language);
        var englishWords = await terminology.ForBusinessTypeAsync(code, AppLanguage.English);
        Assert.NotEqual(englishWords[TermName.Member], words[TermName.Member]); // (French shares some words, e.g. "consultation")
        Assert.NotEqual(englishWords[TermName.Client], words[TermName.Client]);
    }
}
