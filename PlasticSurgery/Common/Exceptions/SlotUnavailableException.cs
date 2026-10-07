namespace PlasticSurgery.Common.Exceptions;

/// <summary>The requested slot is not (or is no longer) bookable; Message is safe to show the patient/AI.</summary>
public class SlotUnavailableException : Exception
{
    public SlotUnavailableException(string message) : base(message) { }
}
