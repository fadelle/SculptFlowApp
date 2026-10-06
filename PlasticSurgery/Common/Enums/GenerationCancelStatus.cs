using System.Text.Json;

namespace PlasticSurgery.Common.Enums;

public enum GenerationCancelStatus
{
    /// <summary>The pending generation was stopped.</summary>
    Cancelled,
    /// <summary>No such generation for this clinic.</summary>
    NotFound,
    /// <summary>It already finished (completed / failed / cancelled), so there is nothing to stop.</summary>
    NotPending
}
