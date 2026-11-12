using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Contacts;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Business.Services.Contacts;

/// <summary>
/// Contacts from and to CSV (QA t12-fix-05). Import goes through the same rules as "Add contact" and "Edit details"
/// (IContactService: name or phone, phones as 6-15 digits, a valid email, the length limits), one row at a time, so a bad
/// row is reported with its line number and the others still go in. A phone the client already has is the same person:
/// skipped, or updated with the row's non-empty cells when the member chose to. A stage cell may be a stage key or its name.
/// Export writes the list the member is looking at (its stage, search and source filters).
/// </summary>
public class ContactImportService : IContactImportService
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxRows = 2000;
    public const int MaxExportRows = 20000;
    private const int ExportPage = 500;

    private readonly IContactService _contacts;
    private readonly IContactRepository _contactRows;
    private readonly IDashboardService _dashboard;
    private readonly IStringLocalizer _text;

    public ContactImportService(IContactService contacts, IContactRepository contactRows, IDashboardService dashboard, IStringLocalizer text)
    {
        _contacts = contacts;
        _contactRows = contactRows;
        _dashboard = dashboard;
        _text = text;
    }

    /// <summary>The header names each field is guessed from (lower case, any language a client might use).</summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        [ContactImportField.Name] = ["name", "full name", "fullname", "contact", "contact name", "الاسم", "nom", "nom complet"],
        [ContactImportField.Phone] = ["phone", "phone number", "mobile", "whatsapp", "tel", "telephone", "téléphone", "الهاتف", "رقم الهاتف", "الجوال"],
        [ContactImportField.Email] = ["email", "e-mail", "mail", "البريد", "البريد الإلكتروني", "courriel"],
        [ContactImportField.City] = ["city", "town", "المدينة", "ville"],
        [ContactImportField.Notes] = ["notes", "note", "comments", "ملاحظات", "remarques"],
        [ContactImportField.Stage] = ["stage", "status", "المرحلة", "étape"],
    };

    public ContactImportPreview Preview(string csv)
    {
        var rows = Read(csv);
        var headers = rows[0].Cells;
        var mapping = new Dictionary<string, int>();
        foreach (var (field, names) in Aliases)
        {
            var index = Array.FindIndex(headers, h => names.Contains(h.Trim().ToLowerInvariant()));
            if (index >= 0 && !mapping.ContainsValue(index)) mapping[field] = index;
        }
        return new ContactImportPreview(headers, rows.Skip(1).Take(5).Select(r => r.Cells).ToList(), rows.Count - 1, mapping);
    }

    public async Task<ContactImportResult> ImportAsync(Guid clientId, string csv, IReadOnlyDictionary<string, int> mapping, bool updateExisting,
        CancellationToken ct = default)
    {
        var rows = Read(csv);
        if (!mapping.ContainsKey(ContactImportField.Name) && !mapping.ContainsKey(ContactImportField.Phone))
            throw new ArgumentException("contacts.import.errors.nameOrPhoneColumn");
        if (mapping.Values.Any(i => i < 0 || i >= rows[0].Cells.Length) || mapping.Keys.Any(k => !ContactImportField.All.Contains(k)))
            throw new ArgumentException("contacts.import.errors.badMapping");

        var stages = await _contacts.ListStagesAsync(clientId, ct);
        int added = 0, updated = 0, skipped = 0;
        var failures = new List<ContactImportFailure>();
        for (var r = 1; r < rows.Count; r++)
        {
            var (line, cells) = rows[r]; // the file's own line (the header is line 1)
            string? Cell(string field) => mapping.TryGetValue(field, out var i) && i < cells.Length && cells[i].Trim() is { Length: > 0 } v ? v : null;
            var (name, phone, email, city, notes, stageCell) = (Cell(ContactImportField.Name), Cell(ContactImportField.Phone),
                Cell(ContactImportField.Email), Cell(ContactImportField.City), Cell(ContactImportField.Notes), Cell(ContactImportField.Stage));
            if (name is null && phone is null && email is null) { skipped++; continue; }
            try
            {
                string? stageKey = null;
                if (stageCell is not null)
                {
                    stageKey = stages.FirstOrDefault(s => string.Equals(s.Key, stageCell, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.Label, stageCell, StringComparison.CurrentCultureIgnoreCase))?.Key
                        ?? throw new ArgumentException("contacts.errors.unknownStage").WithArgs(stageCell);
                }

                var digits = phone is null ? null : new string(phone.Where(char.IsAsciiDigit).ToArray());
                var existing = digits is { Length: >= 6 } ? await _contactRows.FindByPhoneAsync(clientId, digits, ct) : null;
                Guid id;
                if (existing is not null)
                {
                    if (!updateExisting) { skipped++; continue; }
                    // Only the row's filled cells change the contact; an empty cell leaves the field as it is.
                    await _contacts.UpdateAsync(clientId, existing.Id, new UpdateContactRequest(null, name, null, null, null, email, null, city,
                        null, notes, null), ct);
                    id = existing.Id;
                    updated++;
                }
                else
                {
                    var created = await _contacts.AddManualAsync(clientId, new ManualContactRequest(name, phone, email, null, notes), ct);
                    id = created.Id;
                    if (city is not null)
                        await _contacts.UpdateAsync(clientId, id, new UpdateContactRequest(null, null, null, null, null, null, null, city, null, null, null), ct);
                    added++;
                }
                if (stageKey is not null) await _contacts.UpdateStageAsync(clientId, id, new UpdateContactStageRequest(stageKey, null), ct);
            }
            catch (ArgumentException ex)
            {
                failures.Add(new ContactImportFailure(line, _text.Localize(ex)));
            }
        }
        return new ContactImportResult(added, updated, skipped, failures);
    }

    public async Task<string> ExportAsync(Guid clientId, string? stage, string? search, string? source, CancellationToken ct = default)
    {
        var stages = (await _contacts.ListStagesAsync(clientId, ct)).ToDictionary(s => s.Key, s => s.Label);
        var lines = new List<IReadOnlyList<string?>>
        {
            new[]
            {
                _text["contacts.export.name"].Value, _text["contacts.export.phone"].Value, _text["contacts.export.email"].Value,
                _text["contacts.export.city"].Value, _text["contacts.export.stage"].Value, _text["contacts.export.service"].Value,
                _text["contacts.export.source"].Value, _text["contacts.export.language"].Value, _text["contacts.export.optedOut"].Value,
                _text["contacts.export.lastContact"].Value, _text["contacts.export.nextFollowup"].Value, _text["contacts.export.created"].Value
            }
        };
        for (var skip = 0; skip < MaxExportRows; skip += ExportPage)
        {
            var (items, total) = await _dashboard.GetContactsAsync(clientId, stage, search, source, skip, ExportPage, ct);
            foreach (var c in items)
            {
                lines.Add(new[]
                {
                    c.FullName, c.Phone, c.Email, c.City, stages.GetValueOrDefault(c.StageKey, c.StageKey), c.ServiceName, c.Source,
                    c.PreferredLanguage, c.OptedOutAt is null ? "" : Iso(c.OptedOutAt), Iso(c.LastContactAt), Iso(c.NextFollowupAt), Iso(c.CreatedAt)
                });
            }
            if (items.Count < ExportPage || skip + ExportPage >= total) break;
        }
        return ContactCsv.Write(lines);
    }

    private static string Iso(DateTimeOffset? at) => at is null ? "" : at.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>The parsed file: at least a header and one row, at most <see cref="MaxRows"/> rows.</summary>
    private static IReadOnlyList<(int Line, string[] Cells)> Read(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("contacts.import.errors.empty");
        if (System.Text.Encoding.UTF8.GetByteCount(csv) > MaxBytes) throw new ArgumentException("contacts.import.errors.tooBig").WithArgs(MaxBytes / 1024);
        var rows = ContactCsv.ParseLines(csv);
        if (rows.Count < 2) throw new ArgumentException("contacts.import.errors.empty");
        if (rows.Count - 1 > MaxRows) throw new ArgumentException("contacts.import.errors.tooManyRows").WithArgs(MaxRows);
        return rows;
    }
}
