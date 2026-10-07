namespace PlasticSurgery.Entities.Dtos.Appointments;

/// <summary>Weekly window as staff edit it. DayOfWeek uses .NET numbering (0 = Sunday). Times are "HH:mm" clinic-local.</summary>
public record DayRuleDto(int DayOfWeek, bool IsOpen, string Start, string End);
