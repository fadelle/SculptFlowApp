using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class KnowledgeSettingsService : IKnowledgeSettingsService
{
    public const int MinChunkSizeTokens = 50;
    public const int MaxChunkSizeTokens = 1000;
    public const int MinTopK = 1;
    public const int MaxTopK = 10;

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public KnowledgeSettingsService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<KnowledgeSettingsResponse> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        if (row is not null) return ToResponse(row);

        var now = DateTimeOffset.UtcNow;
        var created = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            EmbeddingModel = ConfigString("Embeddings:Model", "text-embedding-3-small"),
            VectorDimension = ConfigInt("Embeddings:Dimensions", 1536),
            SimilarityMethod = KnowledgeSimilarityMethod.Cosine,
            VectorIndexType = KnowledgeVectorIndexType.None,
            ChunkSizeTokens = Math.Clamp(ConfigInt("Knowledge:ChunkMaxChars", 1000) / KnowledgeChunkingService.CharsPerToken, MinChunkSizeTokens, MaxChunkSizeTokens),
            ChunkOverlapTokens = (int)Math.Round(ConfigInt("Knowledge:ChunkOverlapChars", 150) / (double)KnowledgeChunkingService.CharsPerToken),
            TopK = 5,
            MinimumSimilarity = Math.Clamp(ConfigDouble("Knowledge:MinScore", 0.30), 0, 1),
            CreatedAt = now,
            UpdatedAt = now
        };
        created.ChunkOverlapTokens = Math.Clamp(created.ChunkOverlapTokens, 0, created.ChunkSizeTokens / 2);

        _db.KnowledgeSearchSettings.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
            return ToResponse(created);
        }
        catch (DbUpdateException)
        {
            // Another request created this clinic's row first (unique clinic_id) — use theirs.
            _db.Entry(created).State = EntityState.Detached;
            var existing = await _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
            return existing is null ? throw new InvalidOperationException("Couldn't create Knowledge Base settings.") : ToResponse(existing);
        }
    }

    public async Task<KnowledgeSettingsResponse> UpdateAsync(Guid clinicId, UpdateKnowledgeSettingsRequest request, CancellationToken ct = default)
    {
        Validate(request);
        await GetAsync(clinicId, ct); // make sure the row exists

        var row = await _db.KnowledgeSearchSettings.FirstAsync(s => s.ClinicId == clinicId, ct);
        row.ChunkSizeTokens = request.ChunkSizeTokens;
        row.ChunkOverlapTokens = request.ChunkOverlapTokens;
        row.TopK = request.TopK;
        row.MinimumSimilarity = request.MinimumSimilarity;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    private static void Validate(UpdateKnowledgeSettingsRequest r)
    {
        if (r.ChunkSizeTokens is < MinChunkSizeTokens or > MaxChunkSizeTokens)
        {
            throw new ArgumentException($"Chunk size must be between {MinChunkSizeTokens} and {MaxChunkSizeTokens} tokens.");
        }
        if (r.ChunkOverlapTokens < 0 || r.ChunkOverlapTokens > r.ChunkSizeTokens / 2)
        {
            throw new ArgumentException("Chunk overlap must be between 0 and half the chunk size.");
        }
        if (r.TopK is < MinTopK or > MaxTopK)
        {
            throw new ArgumentException($"Top K must be between {MinTopK} and {MaxTopK}.");
        }
        if (double.IsNaN(r.MinimumSimilarity) || r.MinimumSimilarity is < 0 or > 1)
        {
            throw new ArgumentException("Minimum similarity must be between 0 and 1.");
        }
    }

    private string ConfigString(string key, string fallback) => _configuration[key] is { Length: > 0 } v ? v : fallback;

    private int ConfigInt(string key, int fallback) => int.TryParse(_configuration[key], out var v) && v > 0 ? v : fallback;

    private double ConfigDouble(string key, double fallback) =>
        double.TryParse(_configuration[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static KnowledgeSettingsResponse ToResponse(KnowledgeSearchSettings s) => new(
        s.EmbeddingModel, s.VectorDimension, s.SimilarityMethod, s.VectorIndexType,
        s.ChunkSizeTokens, s.ChunkOverlapTokens, s.TopK, s.MinimumSimilarity, s.UpdatedAt);
}
