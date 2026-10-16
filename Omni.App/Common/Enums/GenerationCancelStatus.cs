using System.Text.Json;

namespace Omni.App.Common.Enums;

public enum GenerationCancelStatus
{
    /// <summary>The pending generation was stopped.</summary>
    Cancelled,
    /// <summary>No such generation for this client.</summary>
    NotFound,
    /// <summary>It already finished (completed / failed / cancelled), so there is nothing to stop.</summary>
    NotPending
}
