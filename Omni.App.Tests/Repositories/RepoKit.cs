using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Contexts;

namespace Omni.App.Tests.Repositories;

/// <summary>
/// Base for repository tests against the throwaway Postgres. Every test works in its own client(s), so tests never see each
/// other's rows. Seeding goes through one context and reading back through a fresh one, so nothing is served from the
/// change tracker.
/// </summary>
[Collection("Postgres")]
public abstract class RepoTestBase
{
    private readonly PostgresFixture _fixture;

    protected RepoTestBase(PostgresFixture fixture) => _fixture = fixture;

    protected ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_fixture.ConnectionString).Options);

    /// <summary>Seeds with a short-lived context and saves.</summary>
    protected async Task SeedAsync(params object[] entities)
    {
        await using var db = NewContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <summary>Inserts a knowledge chunk by SQL: the embedding column is NOT NULL and its type depends on whether the
    /// server has pgvector (vector) or not (real[]).</summary>
    protected async Task<Guid> SeedChunkAsync(Guid clientId, Guid documentId, int index, string content)
    {
        await using var db = NewContext();
        var hasVector = await db.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from pg_extension where extname = 'vector'").SingleAsync() > 0;
        var embedding = hasVector
            ? "('[' || array_to_string(array_fill(0.1::real, ARRAY[1536]), ',') || ']')::vector"
            : "array_fill(0.1::real, ARRAY[3])";
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "insert into knowledge.knowledge_chunks (id, client_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at) " +
            $"values (@p0, @p1, @p2, @p3, @p4, {embedding}, now(), now())", id, clientId, documentId, index, content);
        return id;
    }

    protected static KnowledgeDocument MakeDocument(Guid clientId, string title = "Doc", bool active = true) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Title = title, Content = "content", IsActive = active, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    protected static Client MakeClient(string? name = null, string timezone = "UTC") => new()
    {
        Id = Guid.NewGuid(),
        Name = name ?? "Client " + Guid.NewGuid().ToString("N")[..8],
        Slug = "c-" + Guid.NewGuid().ToString("N")[..12],
        Timezone = timezone,
        // The first built-in business type: its stage keys are the LeadStatus values the code writes.
        BusinessTypeCode = TextKit.Clinic,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    protected static Service MakeService(Guid clientId, string name = "Rhinoplasty", bool active = true, int? duration = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Name = name, IsActive = active, BookingDuration = duration, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static Lead MakeLead(Guid clientId, string? name = "Ann", string? phone = null, Guid? serviceId = null, string status = LeadStatus.New,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, FullName = name, Phone = phone, ServiceId = serviceId, Status = status,
        CreatedAt = createdAt ?? Now, UpdatedAt = createdAt ?? Now,
    };

    protected static Conversation MakeConversation(Guid clientId, Guid leadId, string channel = "whatsapp", string? thread = null,
        DateTimeOffset? lastMessageAt = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, LeadId = leadId, Channel = channel, ExternalThreadId = thread,
        LastMessageAt = lastMessageAt, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static Message MakeMessage(Conversation c, string direction = MessageDirection.Inbound, string origin = MessageOrigin.WhatsAppCustomer,
        string content = "hi", DateTimeOffset? createdAt = null, string? externalId = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = c.ClientId, ConversationId = c.Id, LeadId = c.LeadId, Direction = direction, SenderType = "lead",
        Channel = c.Channel, Content = content, Origin = origin, ExternalMessageId = externalId, CreatedAt = createdAt ?? Now,
    };

    protected static Booking MakeBooking(Guid clientId, Guid leadId, DateTimeOffset start, string status = BookingStatus.Booked,
        Guid? serviceId = null, DateTimeOffset? end = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, LeadId = leadId, ServiceId = serviceId, ScheduledStart = start, ScheduledEnd = end,
        Status = status, CreatedAt = Now, UpdatedAt = Now,
    };
}
