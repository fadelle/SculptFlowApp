namespace PlasticSurgery.Entities.Requests.Clinics;

/// <summary>One weekday row posted by the Clinic Info availability tab.</summary>
public class AvailabilityDayInput
{
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
}
