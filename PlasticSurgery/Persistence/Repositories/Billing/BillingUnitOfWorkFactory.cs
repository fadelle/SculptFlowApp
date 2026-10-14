using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

using PlasticSurgery.Business.Contracts.Managers;

namespace PlasticSurgery.Persistence.Repositories.Billing;

/// <summary>Builds each billing unit of work on a fresh ApplicationDbContext from the app's options (tests pass their own).</summary>
public sealed class BillingUnitOfWorkFactory : IBillingUnitOfWorkFactory
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ICacheManager? _cache;

    public BillingUnitOfWorkFactory(DbContextOptions<ApplicationDbContext> options, ICacheManager? cache = null)
    {
        _options = options;
        _cache = cache;
    }

    public IBillingUnitOfWork Create() => new BillingUnitOfWork(new ApplicationDbContext(_options), _cache);
}
