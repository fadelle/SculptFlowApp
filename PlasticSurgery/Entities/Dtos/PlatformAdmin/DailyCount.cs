namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record DailyCount(DateOnly Day, int Inbound, int Ai, int Staff, int Failed);
