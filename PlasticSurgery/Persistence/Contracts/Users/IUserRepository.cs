namespace PlasticSurgery.Persistence.Contracts.Users;

/// <summary>Direct reads/deletes of Identity users. Sign-in, passwords and claims go through ASP.NET Identity's UserManager.</summary>
public interface IUserRepository
{
    Task<IReadOnlyList<string>> ListEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    Task DeleteAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);
}
