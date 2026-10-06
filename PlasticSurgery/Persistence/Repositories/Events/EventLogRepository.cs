using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Events;

namespace PlasticSurgery.Persistence.Repositories.Events;

public class EventLogRepository : IEventLogRepository
{
    private readonly ApplicationDbContext _db;

    public EventLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(EventLog entry) => _db.Events.Add(entry);
}
