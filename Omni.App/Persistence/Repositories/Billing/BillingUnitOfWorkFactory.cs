using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Billing;

using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Persistence.Repositories.Billing;

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
