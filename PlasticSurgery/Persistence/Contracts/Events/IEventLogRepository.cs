using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Events;

public interface IEventLogRepository
{
    /// <summary>Queues an event row; it is written with the caller's next save.</summary>
    void Add(EventLog entry);
}
