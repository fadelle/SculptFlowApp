using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

/// <summary>Builds each billing unit of work on a fresh ApplicationDbContext from the app's options (tests pass their own).</summary>
public sealed class BillingUnitOfWorkFactory : IBillingUnitOfWorkFactory
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public BillingUnitOfWorkFactory(DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public IBillingUnitOfWork Create() => new BillingUnitOfWork(new ApplicationDbContext(_options));
}
