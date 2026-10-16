using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Entities.Dtos.Knowledge;

/// <summary>One line of the Knowledge Base list: either a document (manual/upload) or a website source.</summary>
public sealed record KnowledgeListRow(DateTimeOffset SortDate, KnowledgeDocumentResponse? Document, WebsiteSourceResponse? Website);
