using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Clients;

namespace Omni.App.Common.Helpers;

/// <summary>Who may manage a client's members: only the <c>client</c> role (UnauthorizedAccessException, 403, otherwise).</summary>
public static class MemberRules
{
    public static void EnsureClientRole(UserAccess actor)
    {
        if (actor.Role != UserRole.Client) throw new UnauthorizedAccessException("members.errors.clientOnly");
    }
}
