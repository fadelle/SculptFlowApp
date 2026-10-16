using System.Text.RegularExpressions;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Mappers.BusinessTypes;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.BusinessTypes;
using Omni.App.Entities.Responses.BusinessTypes;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.BusinessTypes;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Leads;

namespace Omni.App.Business.Services.BusinessTypes;

public partial class BusinessTypeService : IBusinessTypeService
{
    public const int MaxNameLength = 100;
    public const int MaxTermLength = 50;
    public const int MaxLabelLength = 100;
    public const int MaxAiProfileLength = 20000;
    public const int MaxStages = 50;

    private readonly IBusinessTypeRepository _types;
    private readonly IClientRepository _clients;
    private readonly ILeadRepository _leads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITextContext _text;
    private readonly TimeProvider _time;

    public BusinessTypeService(IBusinessTypeRepository types, IClientRepository clients, ILeadRepository leads, IUnitOfWork unitOfWork,
        ITextContext text, TimeProvider time)
    {
        _types = types;
        _clients = clients;
        _leads = leads;
        _unitOfWork = unitOfWork;
        _text = text;
        _time = time;
    }

    public async Task<IReadOnlyList<BusinessTypeOption>> ListOptionsAsync(CancellationToken ct = default) =>
        (await _types.ListReadOnlyAsync(ct)).Where(t => t.IsActive).Select(t => BusinessTypeMapper.ToOption(t, _text.Language)).ToList();

    public async Task<bool> IsActiveAsync(string? code, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(code) && await _types.GetReadOnlyAsync(code.Trim(), ct) is { IsActive: true };

    public async Task<IReadOnlyList<PipelineStageOption>> ListStagesForClientAsync(Guid clientId, CancellationToken ct = default) =>
        (await StagesOfClientAsync(clientId, ct)).Select(s => BusinessTypeMapper.ToOption(s, _text.Language)).ToList();

    public async Task<bool> IsStageOfClientAsync(Guid clientId, string? key, CancellationToken ct = default) =>
        !string.IsNullOrEmpty(key) && (await StagesOfClientAsync(clientId, ct)).Any(s => s.Key == key);

    public async Task<IReadOnlyList<BusinessTypeResponse>> ListAsync(CancellationToken ct = default)
    {
        var counts = await _types.CountClientsByTypeAsync(ct);
        return (await _types.ListReadOnlyAsync(ct)).Select(t => BusinessTypeMapper.ToResponse(t, counts.GetValueOrDefault(t.Code))).ToList();
    }

    public async Task<BusinessTypeResponse> GetAsync(string code, CancellationToken ct = default)
    {
        var type = await _types.GetReadOnlyAsync(code, ct) ?? throw NotFound(code);
        var counts = await _types.CountClientsByTypeAsync(ct);
        return BusinessTypeMapper.ToResponse(type, counts.GetValueOrDefault(type.Code));
    }

    public async Task<BusinessTypeResponse> CreateAsync(SaveBusinessTypeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = (request.Code ?? string.Empty).Trim();
        if (!CodePattern().IsMatch(code)) throw new ArgumentException("businessTypes.errors.codeInvalid");
        var input = Validate(request);
        if (await _types.ExistsAsync(code, ct)) throw new DuplicateRecordException("businessTypes.errors.codeTaken").WithArgs(code);

        var now = _time.GetUtcNow();
        var type = new BusinessType { Code = code, IsActive = true, CreatedAt = now };
        Apply(type, input, now);
        _types.Add(type);
        await _unitOfWork.SaveChangesAsync(ct);
        return BusinessTypeMapper.ToResponse(type, 0);
    }

    public async Task<BusinessTypeResponse> UpdateAsync(string code, SaveBusinessTypeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = Validate(request);
        var type = await _types.GetAsync(code, ct) ?? throw NotFound(code);

        var keep = input.Stages.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var removed = type.Stages.Where(s => !keep.Contains(s.Key)).ToList();
        if (removed.Count > 0)
        {
            var inUse = await _types.CountLeadsByStageAsync(code, ct);
            var blocked = removed.Where(s => inUse.GetValueOrDefault(s.Key) > 0).Select(s => s.Key).ToList();
            if (blocked.Count > 0) throw new InvalidOperationException("businessTypes.errors.stageInUse").WithArgs(string.Join(", ", blocked));
        }
        foreach (var stage in removed)
        {
            type.Stages.Remove(stage);
            _types.RemoveStage(stage);
        }

        Apply(type, input, _time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<BusinessTypeResponse> SetActiveAsync(string code, bool active, CancellationToken ct = default)
    {
        var type = await _types.GetAsync(code, ct) ?? throw NotFound(code);
        if (type.IsActive != active)
        {
            if (!active && !(await _types.ListReadOnlyAsync(ct)).Any(t => t.IsActive && t.Code != code))
                throw new InvalidOperationException("businessTypes.errors.lastActive");
            type.IsActive = active;
            type.UpdatedAt = _time.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return await GetAsync(code, ct);
    }

    public async Task<BusinessTypeChangePreview> PreviewClientChangeAsync(Guid clientId, string? toCode, CancellationToken ct = default)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var (from, to) = await LoadChangeAsync(client.BusinessTypeCode, toCode, ct);
        return Preview(clientId, from, to, await _leads.CountByStatusAsync(clientId, ct));
    }

    public async Task<BusinessTypeChangePreview> ChangeClientTypeAsync(Guid clientId, string? toCode, CancellationToken ct = default)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        var client = await _clients.GetAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var (from, to) = await LoadChangeAsync(client.BusinessTypeCode, toCode, ct);
        var preview = Preview(clientId, from, to, await _leads.CountByStatusAsync(clientId, ct));
        if (from.Code == to.Code) return preview;

        var now = _time.GetUtcNow();
        foreach (var move in preview.Moves.Where(m => m.FromKey != m.ToKey))
        {
            await _leads.MoveStatusAsync(clientId, move.FromKey, move.ToKey, now, ct);
        }
        client.BusinessTypeCode = to.Code;
        client.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return preview;
    }

    /// <summary>Where each stage key with contacts goes from <paramref name="from"/> to <paramref name="to"/> (see
    /// <see cref="IBusinessTypeService.ChangeClientTypeAsync"/>), in the old pipeline's order; keys the old type doesn't
    /// know come last. Both lists must be in pipeline order.</summary>
    internal static IReadOnlyList<StageMove> PlanMoves(IReadOnlyList<PipelineStage> from, IReadOnlyList<PipelineStage> to,
        IReadOnlyDictionary<string, int> counts)
    {
        var moves = new List<StageMove>();
        var known = from.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var stage in from)
        {
            if (counts.GetValueOrDefault(stage.Key) is not (> 0 and var count)) continue;
            var target = to.FirstOrDefault(s => s.Key == stage.Key && s.Category == stage.Category)
                         ?? to.FirstOrDefault(s => s.Category == stage.Category)
                         ?? FirstOpen(to);
            moves.Add(new StageMove(stage.Key, BusinessTypeMapper.LabelOf(stage, AppLanguage.English), stage.Category, count,
                target.Key, BusinessTypeMapper.LabelOf(target, AppLanguage.English)));
        }
        foreach (var (key, count) in counts.Where(c => c.Value > 0 && !known.Contains(c.Key)).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var target = to.FirstOrDefault(s => s.Key == key) ?? FirstOpen(to);
            moves.Add(new StageMove(key, key, string.Empty, count, target.Key, BusinessTypeMapper.LabelOf(target, AppLanguage.English)));
        }
        return moves;
    }

    private static PipelineStage FirstOpen(IReadOnlyList<PipelineStage> stages) =>
        stages.FirstOrDefault(s => s.Category == StageCategory.Open) ?? stages[0];

    private static BusinessTypeChangePreview Preview(Guid clientId, BusinessType from, BusinessType to, IReadOnlyDictionary<string, int> counts)
    {
        var moves = PlanMoves(from.Stages, to.Stages, counts);
        return new BusinessTypeChangePreview(clientId, from.Code, to.Code, moves.Sum(m => m.Count), moves);
    }

    private async Task<(BusinessType From, BusinessType To)> LoadChangeAsync(string fromCode, string? toCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toCode)) throw new ArgumentException("businessTypes.errors.codeInvalid");
        var to = await _types.GetReadOnlyAsync(toCode.Trim(), ct) ?? throw NotFound(toCode.Trim());
        if (!to.IsActive) throw new InvalidOperationException("businessTypes.errors.inactive").WithArgs(to.Code);
        if (to.Stages.Count == 0) throw new InvalidOperationException("businessTypes.errors.stagesRequired");
        var from = await _types.GetReadOnlyAsync(fromCode, ct) ?? throw NotFound(fromCode);
        return (from, to);
    }

    private async Task<IReadOnlyList<PipelineStage>> StagesOfClientAsync(Guid clientId, CancellationToken ct) =>
        await _clients.GetReadOnlyAsync(clientId, ct) is { } client && await _types.GetReadOnlyAsync(client.BusinessTypeCode, ct) is { } type
            ? type.Stages
            : [];

    private static KeyNotFoundException NotFound(string code) => new KeyNotFoundException("businessTypes.errors.notFound").WithArgs(code);

    // ---- Validation

    private sealed record ValidStage(string Key, string Category, Dictionary<string, string> Labels);

    private sealed record ValidInput(Dictionary<string, string> Name, Dictionary<string, Dictionary<string, TermForms>> Terminology,
        string AiProfile, int SortOrder, List<ValidStage> Stages);

    private static ValidInput Validate(SaveBusinessTypeRequest request)
    {
        var name = Texts(request.Name, MaxNameLength, "name");
        if (!name.ContainsKey(AppLanguage.English)) throw new ArgumentException("businessTypes.errors.nameRequired");

        var terminology = new Dictionary<string, Dictionary<string, TermForms>>(StringComparer.Ordinal);
        foreach (var (rawLanguage, rawTerms) in request.Terminology ?? new())
        {
            var language = Language(rawLanguage);
            var terms = new Dictionary<string, TermForms>(StringComparer.Ordinal);
            foreach (var (rawTerm, forms) in rawTerms ?? new())
            {
                var term = (rawTerm ?? string.Empty).Trim().ToLowerInvariant();
                if (!TermName.All.Contains(term)) throw new ArgumentException("businessTypes.errors.unknownTerm").WithArgs(rawTerm);
                var one = Limit(forms?.One, MaxTermLength, $"{language}.{term}.one");
                var other = Limit(forms?.Other, MaxTermLength, $"{language}.{term}.other");
                if (one.Length > 0 && other.Length > 0) terms[term] = new TermForms(one, other);
            }
            if (terms.Count > 0) terminology[language] = terms;
        }
        var englishTerms = terminology.GetValueOrDefault(AppLanguage.English) ?? new Dictionary<string, TermForms>();
        var missing = TermName.All.Where(t => !englishTerms.ContainsKey(t)).ToList();
        if (missing.Count > 0) throw new ArgumentException("businessTypes.errors.termsIncomplete").WithArgs(string.Join(", ", missing));

        var aiProfile = (request.AiProfile ?? string.Empty).Trim();
        if (aiProfile.Length > MaxAiProfileLength) throw new ArgumentException("businessTypes.errors.textTooLong").WithArgs("aiProfile", MaxAiProfileLength);

        var rawStages = request.Stages ?? [];
        if (rawStages.Count == 0) throw new ArgumentException("businessTypes.errors.stagesRequired");
        if (rawStages.Count > MaxStages) throw new ArgumentException("businessTypes.errors.tooManyStages").WithArgs(MaxStages);
        var stages = new List<ValidStage>();
        foreach (var raw in rawStages)
        {
            var key = (raw?.Key ?? string.Empty).Trim();
            if (!StageKeyPattern().IsMatch(key)) throw new ArgumentException("businessTypes.errors.stageKeyInvalid").WithArgs(key);
            if (stages.Any(s => s.Key == key)) throw new ArgumentException("businessTypes.errors.stageKeyDuplicate").WithArgs(key);
            var category = (raw!.Category ?? string.Empty).Trim();
            if (!StageCategory.All.Contains(category))
                throw new ArgumentException("businessTypes.errors.stageCategoryInvalid").WithArgs(key, category);
            var labels = Texts(raw.Labels, MaxLabelLength, key);
            if (!labels.ContainsKey(AppLanguage.English)) throw new ArgumentException("businessTypes.errors.stageLabelRequired").WithArgs(key);
            stages.Add(new ValidStage(key, category, labels));
        }
        var uncovered = StageCategory.All.Where(c => stages.All(s => s.Category != c)).ToList();
        if (uncovered.Count > 0) throw new ArgumentException("businessTypes.errors.categoryMissing").WithArgs(string.Join(", ", uncovered));

        return new ValidInput(name, terminology, aiProfile, request.SortOrder, stages);
    }

    /// <summary>Per-language texts: known languages only, trimmed, blanks dropped, each at most <paramref name="max"/> long.</summary>
    private static Dictionary<string, string> Texts(Dictionary<string, string>? raw, int max, string field)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawLanguage, rawText) in raw ?? new())
        {
            var language = Language(rawLanguage);
            var text = Limit(rawText, max, $"{field}.{language}");
            if (text.Length > 0) texts[language] = text;
        }
        return texts;
    }

    private static string Language(string? raw)
    {
        var language = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (!AppLanguage.All.Contains(language)) throw new ArgumentException("businessTypes.errors.unknownLanguage").WithArgs(raw);
        return language;
    }

    private static string Limit(string? raw, int max, string field)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length > max) throw new ArgumentException("businessTypes.errors.textTooLong").WithArgs(field, max);
        return text;
    }

    private static void Apply(BusinessType type, ValidInput input, DateTimeOffset now)
    {
        type.NameJson = BusinessTypeMapper.ToJson(input.Name);
        type.TerminologyJson = BusinessTypeMapper.ToJson(input.Terminology);
        type.AiProfile = input.AiProfile;
        type.SortOrder = input.SortOrder;
        type.UpdatedAt = now;

        var position = 0;
        foreach (var valid in input.Stages)
        {
            position += 10;
            var stage = type.Stages.FirstOrDefault(s => s.Key == valid.Key);
            if (stage is null)
            {
                stage = new PipelineStage { BusinessTypeCode = type.Code, Key = valid.Key, CreatedAt = now };
                type.Stages.Add(stage);
            }
            stage.Category = valid.Category;
            stage.LabelsJson = BusinessTypeMapper.ToJson(valid.Labels);
            stage.SortOrder = position;
            stage.UpdatedAt = now;
        }
        type.Stages = type.Stages.OrderBy(s => s.SortOrder).ToList();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,49}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,49}$")]
    private static partial Regex StageKeyPattern();
}
