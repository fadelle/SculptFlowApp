using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Managers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;

namespace Omni.App.Common.Extensions;

public static class IdentityModule
{
    /// <summary>
    /// ASP.NET Core Identity over identity.users (AppUser): password rules, the lockout (AccountRules: 5 failed sign-ins
    /// lock for 15 minutes, for every role), the main app's sign-in rules (AppSignInManager: client and member only) and
    /// the cookie's claims (AppUserClaimsPrincipalFactory). AddIdentityCore, not AddIdentity: the role is a column, so
    /// there are no role tables. The cookie schemes are added by the caller.
    /// </summary>
    public static IdentityBuilder AddOmniIdentity(this IServiceCollection services) =>
        services.AddIdentityCore<AppUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = AccountRules.MaxFailedSignIns;
                options.Lockout.DefaultLockoutTimeSpan = AccountRules.LockoutDuration;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();
}
