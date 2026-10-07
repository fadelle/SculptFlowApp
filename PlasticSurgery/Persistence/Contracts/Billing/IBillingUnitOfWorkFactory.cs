namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>
/// Gives every money operation its OWN unit of work (its own database context), so a billing transaction never flushes
/// (or is rolled back with) unrelated tracked changes of the request that called it — e.g. MessageService's half-built
/// message row.
/// </summary>
public interface IBillingUnitOfWorkFactory
{
    IBillingUnitOfWork Create();
}
