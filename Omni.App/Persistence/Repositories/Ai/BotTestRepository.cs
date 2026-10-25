using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Ai;

namespace Omni.App.Persistence.Repositories.Ai;

public class BotTestRepository : IBotTestRepository
{
    private readonly ApplicationDbContext _db;

    public BotTestRepository(ApplicationDbContext db) => _db = db;

    public Task<BotTestSession?> GetSessionAsync(Guid clientId, Guid sessionId, CancellationToken ct = default) =>
        _db.BotTestSessions.FirstOrDefaultAsync(s => s.ClientId == clientId && s.Id == sessionId, ct);

    public Task<BotTestSession?> GetSessionForUpdateAsync(Guid clientId, Guid sessionId, CancellationToken ct = default) =>
        _db.BotTestSessions
            .FromSqlInterpolated($"select * from crm.bot_test_sessions where id = {sessionId} and client_id = {clientId} for update")
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BotTestMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct = default) =>
        await _db.BotTestMessages.AsNoTracking().Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);

    public void AddSession(BotTestSession session) => _db.BotTestSessions.Add(session);

    public void AddMessage(BotTestMessage message) => _db.BotTestMessages.Add(message);

    public Task<int> DeleteSessionsOlderThanAsync(DateTimeOffset before, CancellationToken ct = default) =>
        _db.BotTestSessions.Where(s => s.UpdatedAt < before).ExecuteDeleteAsync(ct);

    public Task<int> DeleteSessionAsync(Guid clientId, Guid sessionId, CancellationToken ct = default) =>
        _db.BotTestSessions.Where(s => s.ClientId == clientId && s.Id == sessionId).ExecuteDeleteAsync(ct);
}
