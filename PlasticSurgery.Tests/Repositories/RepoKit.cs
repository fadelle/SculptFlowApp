using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Contexts;

namespace PlasticSurgery.Tests.Repositories;

/// <summary>
/// Base for repository tests against the throwaway Postgres. Every test works in its own clinic(s), so tests never see each
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
    protected async Task<Guid> SeedChunkAsync(Guid clinicId, Guid documentId, int index, string content)
    {
        await using var db = NewContext();
        var hasVector = await db.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from pg_extension where extname = 'vector'").SingleAsync() > 0;
        var embedding = hasVector
            ? "('[' || array_to_string(array_fill(0.1::real, ARRAY[1536]), ',') || ']')::vector"
            : "array_fill(0.1::real, ARRAY[3])";
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "insert into knowledge.knowledge_chunks (id, clinic_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at) " +
            $"values (@p0, @p1, @p2, @p3, @p4, {embedding}, now(), now())", id, clinicId, documentId, index, content);
        return id;
    }

    protected static KnowledgeDocument MakeDocument(Guid clinicId, string title = "Doc", bool active = true) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Title = title, Content = "content", IsActive = active, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    protected static Clinic MakeClinic(string? name = null, string timezone = "UTC") => new()
    {
        Id = Guid.NewGuid(),
        Name = name ?? "Clinic " + Guid.NewGuid().ToString("N")[..8],
        Slug = "c-" + Guid.NewGuid().ToString("N")[..12],
        Timezone = timezone,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    protected static Procedure MakeProcedure(Guid clinicId, string name = "Rhinoplasty", bool active = true, int? duration = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Name = name, IsActive = active, ConsultationDuration = duration, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static Lead MakeLead(Guid clinicId, string? name = "Ann", string? phone = null, Guid? procedureId = null, string status = LeadStatus.New,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, FullName = name, Phone = phone, ProcedureId = procedureId, Status = status,
        CreatedAt = createdAt ?? Now, UpdatedAt = createdAt ?? Now,
    };

    protected static Conversation MakeConversation(Guid clinicId, Guid leadId, string channel = "whatsapp", string? thread = null,
        DateTimeOffset? lastMessageAt = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, LeadId = leadId, Channel = channel, ExternalThreadId = thread,
        LastMessageAt = lastMessageAt, CreatedAt = Now, UpdatedAt = Now,
    };

    protected static Message MakeMessage(Conversation c, string direction = MessageDirection.Inbound, string origin = MessageOrigin.WhatsAppCustomer,
        string content = "hi", DateTimeOffset? createdAt = null, string? externalId = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = c.ClinicId, ConversationId = c.Id, LeadId = c.LeadId, Direction = direction, SenderType = "lead",
        Channel = c.Channel, Content = content, Origin = origin, ExternalMessageId = externalId, CreatedAt = createdAt ?? Now,
    };

    protected static Appointment MakeAppointment(Guid clinicId, Guid leadId, DateTimeOffset start, string status = AppointmentStatus.Booked,
        Guid? procedureId = null, DateTimeOffset? end = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, LeadId = leadId, ProcedureId = procedureId, ScheduledStart = start, ScheduledEnd = end,
        Status = status, CreatedAt = Now, UpdatedAt = Now,
    };
}
