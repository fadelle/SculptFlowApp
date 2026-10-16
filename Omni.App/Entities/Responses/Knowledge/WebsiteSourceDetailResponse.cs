namespace Omni.App.Entities.Responses.Knowledge;

public record WebsiteSourceDetailResponse(
    WebsiteSourceResponse Source,
    IReadOnlyList<WebsiteRunResponse> RecentRuns,
    IReadOnlyDictionary<string, int> PageStatusCounts
);
