using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

/// <summary>Infobip's synchronous answer to a send: the message id (ours, echoed back) and its first status —
/// normally group PENDING ("accepted, on its way"). Delivered/read/failed arrive later on the webhook.</summary>
public record InfobipSendResult(string MessageId, string? StatusGroup, string? StatusName);
