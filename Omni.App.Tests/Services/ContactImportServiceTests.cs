using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Services.Contacts;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Dtos.Contacts;
using Omni.App.Entities.Dtos.Dashboard;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Tests.Services;

/// <summary>QA t12-fix-05: the contact list from and to CSV.</summary>
public class ContactImportServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IContactService> _contacts = new();
    private readonly Mock<IContactRepository> _rows = new();
    private readonly Mock<IDashboardService> _dashboard = new();

    public ContactImportServiceTests()
    {
        _contacts.Setup(c => c.ListStagesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new PipelineStageOption("new", "open", "New"), new PipelineStageOption("booked", "booked", "Booked")]);
        _contacts.Setup(c => c.AddManualAsync(_clientId, It.IsAny<ManualContactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ManualContactRequest r, CancellationToken _) =>
                r.Email == "bad" ? throw new ArgumentException("contacts.errors.invalidEmail") : Blank<ContactResponse>() with { Id = Guid.NewGuid() });
    }

    private static T Blank<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

    private ContactImportService Sut() => new(_contacts.Object, _rows.Object, _dashboard.Object, TextKit.Localizer());

    [Fact]
    public void Csv_reads_quotes_semicolons_and_a_bom_and_writes_safe_cells()
    {
        var rows = ContactCsv.Parse(ContactCsv.Bom + "Name;Phone;Notes\r\n\"Haddad; Rana\";+961 70 123456;\"said \"\"hi\"\"\nthen left\"\r\n\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["Haddad; Rana", "+961 70 123456", "said \"hi\"\nthen left"], rows[1]);
        // Each row keeps the line it starts on, after a blank line and a cell spanning two lines.
        Assert.Equal([1, 2, 5], ContactCsv.ParseLines("h\n\"a\nb\"\n\nc\n").Select(r => r.Line));
        Assert.Equal(',', ContactCsv.GuessDelimiter("a,b,c"));
        Assert.Equal('\t', ContactCsv.GuessDelimiter("a\tb\tc"));

        // A cell that starts like a formula never runs in a spreadsheet; quotes are doubled.
        Assert.Equal("\"'=HYPERLINK(1)\"", ContactCsv.Cell("=HYPERLINK(1)"));
        Assert.Equal("\"a \"\"b\"\"\"", ContactCsv.Cell("a \"b\""));
        Assert.StartsWith(ContactCsv.Bom.ToString(), ContactCsv.Write([["x"]]));
    }

    [Fact]
    public void The_preview_guesses_columns_from_headers_in_any_language()
    {
        var preview = Sut().Preview("الاسم,Téléphone,E-mail,Other\nRana,96170123456,r@x.test,1\n");
        Assert.Equal(1, preview.RowCount);
        Assert.Equal(0, preview.Mapping[ContactImportField.Name]);
        Assert.Equal(1, preview.Mapping[ContactImportField.Phone]);
        Assert.Equal(2, preview.Mapping[ContactImportField.Email]);
        Assert.False(preview.Mapping.ContainsKey(ContactImportField.City));
    }

    [Fact]
    public async Task Rows_are_added_known_phones_skipped_or_updated_and_bad_rows_reported_by_line()
    {
        var existing = new Entities.Models.Contact { Id = Guid.NewGuid(), ClientId = _clientId, Phone = "96170000001" };
        _rows.Setup(r => r.FindByPhoneAsync(_clientId, "96170000001", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        const string csv = "Name,Phone,Email,Stage\nRana,+961 70 000001,,booked\nSami,96170000002,,New\nBad,96170000003,bad,\n,,,\nOdd,96170000004,,nope\n";
        var mapping = new Dictionary<string, int> { ["name"] = 0, ["phone"] = 1, ["email"] = 2, ["stage"] = 3 };

        var skip = await Sut().ImportAsync(_clientId, csv, mapping, updateExisting: false);
        Assert.Equal((1, 0, 1), (skip.Added, skip.Updated, skip.Skipped)); // Sami added; Rana (known) skipped; a blank line is no row
        Assert.Equal([4, 6], skip.Failed.Select(f => f.Line));               // the bad email and the unknown stage, by their line in the file
        _contacts.Verify(c => c.UpdateStageAsync(_clientId, It.IsAny<Guid>(), It.Is<UpdateContactStageRequest>(s => s.StageKey == "new"), It.IsAny<CancellationToken>()), Times.Once);

        var update = await Sut().ImportAsync(_clientId, csv, mapping, updateExisting: true);
        Assert.Equal((1, 1), (update.Added, update.Updated));
        // Only filled cells change the known contact: the name, not the empty email.
        _contacts.Verify(c => c.UpdateAsync(_clientId, existing.Id, It.Is<UpdateContactRequest>(u => u.FullName == "Rana" && u.Email == null), It.IsAny<CancellationToken>()), Times.Once);
        _contacts.Verify(c => c.UpdateStageAsync(_clientId, existing.Id, It.Is<UpdateContactStageRequest>(s => s.StageKey == "booked"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "contacts.import.errors.empty")]
    [InlineData("Name\n", "contacts.import.errors.empty")]
    public void An_empty_file_is_refused(string csv, string key) =>
        Assert.Equal(key, Assert.Throws<ArgumentException>(() => Sut().Preview(csv)).Message);

    [Fact]
    public async Task Too_many_rows_or_no_name_or_phone_column_is_refused_before_anything_is_saved()
    {
        var big = "Name\n" + string.Concat(Enumerable.Repeat("x\n", ContactImportService.MaxRows + 1));
        Assert.Equal("contacts.import.errors.tooManyRows", Assert.Throws<ArgumentException>(() => Sut().Preview(big)).Message);
        var noKey = await Assert.ThrowsAsync<ArgumentException>(() => Sut().ImportAsync(_clientId, "Email\na@b.test\n", new Dictionary<string, int> { ["email"] = 0 }, false));
        Assert.Equal("contacts.import.errors.nameOrPhoneColumn", noKey.Message);
        _contacts.Verify(c => c.AddManualAsync(It.IsAny<Guid>(), It.IsAny<ManualContactRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_export_follows_the_list_filters_and_names_the_stage()
    {
        _dashboard.Setup(d => d.GetContactsAsync(_clientId, "booked", "ra", null, 0, 500, It.IsAny<CancellationToken>())).ReturnsAsync(
            ([new DashboardContactRow(Guid.NewGuid(), "Rana", "96170000001", null, "manual", "booked", "unqualified", null, null,
                new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero), "r@x.test", "Beirut")], 1));

        var csv = await Sut().ExportAsync(_clientId, "booked", "ra", null);

        var lines = ContactCsv.Parse(csv);
        Assert.Equal(2, lines.Count);
        Assert.Equal(["Rana", "96170000001", "r@x.test", "Beirut", "Booked"], lines[1].Take(5));
        Assert.Equal("2026-11-07 09:00 UTC", lines[1][^1]);
    }
}
