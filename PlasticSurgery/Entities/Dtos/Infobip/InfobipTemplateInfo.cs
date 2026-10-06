using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

/// <summary>Infobip's view of a template: its id (the WhatsApp template id) and review status (APPROVED, PENDING,
/// REJECTED, FIRST_PAUSED, IN_APPEAL, ...).</summary>
public record InfobipTemplateInfo(string Id, string Status);

// ---- Request/response shapes (Infobip's JSON field names) ----
