using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Managers;

/// <summary>
/// The main app's sign-in rules on top of Identity's (password, Google, lockout): only an active <c>client</c> or
/// <c>member</c> user with an active client membership may sign in, so an admin is refused here and can only use the
/// admin portal. Password and Google sign-in both go through <see cref="CanSignInAsync"/>; a refused user gets
/// <see cref="SignInResult.NotAllowed"/>, which the pages show as the same generic error as a wrong password. Every
/// successful sign-in stamps users.last_login_at. Identity's own extension point (consumers ask for
/// SignInManager&lt;AppUser&gt;), so it has no Contracts interface.
/// </summary>
public class AppSignInManager : SignInManager<AppUser>
{
    private readonly IClientRepository _clients;
    private readonly TimeProvider _time;

    public AppSignInManager(UserManager<AppUser> userManager, IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<AppUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<AppUser>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<AppUser> confirmation,
        IClientRepository clients, TimeProvider time)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _clients = clients;
        _time = time;
    }

    public override async Task<bool> CanSignInAsync(AppUser user) =>
        user.IsActive && UserRole.UsesMainApp(user.Role)
                      && await _clients.GetUserAccessAsync(user.Id) is not null
                      && await base.CanSignInAsync(user);

    public override async Task SignInWithClaimsAsync(AppUser user, AuthenticationProperties? authenticationProperties,
        IEnumerable<System.Security.Claims.Claim> additionalClaims)
    {
        user.LastLoginAt = _time.GetUtcNow();
        await UserManager.UpdateAsync(user);
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
    }
}
