namespace PlasticSurgery.Entities.Dtos.TikTok;

/// <summary>TikTok's Login Kit basic profile fields — exactly what "user.info.basic" grants, no more.</summary>
public record TikTokAccountInfo(string OpenId, string? UnionId, string? DisplayName, string? AvatarUrl);
