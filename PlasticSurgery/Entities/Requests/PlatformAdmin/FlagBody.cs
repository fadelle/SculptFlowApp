namespace PlasticSurgery.Entities.Requests.PlatformAdmin;

/// <summary>A yes/no switch (locked, email confirmed, calendar sync).</summary>
public record FlagBody(bool Value);
