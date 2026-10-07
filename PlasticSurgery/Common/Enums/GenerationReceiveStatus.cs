using System.Text.Json;

namespace PlasticSurgery.Common.Enums;

public enum GenerationReceiveStatus
{
    /// <summary>The questions were validated and stored.</summary>
    Completed,
    /// <summary>This generation was already completed (a repeated delivery) — nothing was changed.</summary>
    AlreadyProcessed,
    /// <summary>No generation has this id.</summary>
    NotFound,
    /// <summary>The generation failed or expired, so it no longer accepts questions.</summary>
    NotAccepting
}
