using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Managers;

/// <summary>
/// Builds the main app's cookie for a user: Identity's own claims (id, email, security stamp) plus the full name and
/// role from identity.users (MemberClaims), for display. Access itself is re-checked on every request
/// (CurrentClientContext.ValidatePrincipalAsync), never trusted from these claims. Identity's own extension point,
/// resolved by type, so it has no Contracts interface.
/// </summary>
public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser>
{
    public AppUserClaimsPrincipalFactory(UserManager<AppUser> userManager, IOptions<IdentityOptions> options)
        : base(userManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.FullName)) identity.AddClaim(new Claim(MemberClaims.FullName, user.FullName));
        identity.AddClaim(new Claim(MemberClaims.Role, user.Role));
        return identity;
    }
}
