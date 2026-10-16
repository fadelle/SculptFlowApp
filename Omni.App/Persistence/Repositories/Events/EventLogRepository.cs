using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Events;

namespace Omni.App.Persistence.Repositories.Events;

public class EventLogRepository : IEventLogRepository
{
    private readonly ApplicationDbContext _db;

    public EventLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(EventLog entry) => _db.Events.Add(entry);
}
