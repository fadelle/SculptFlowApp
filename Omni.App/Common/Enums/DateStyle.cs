namespace Omni.App.Common.Enums;

/// <summary>How a date is shown to people (TextCulture.Date): the word order follows the language.</summary>
public enum DateStyle
{
    /// <summary>"May 5, 2026" / "5 mai 2026" / "5 مايو 2026".</summary>
    Date,
    /// <summary>"May 5" / "5 mai" (the start of a range whose end shows the year).</summary>
    MonthDay,
    /// <summary>"Tue, May 5, 2026" / "mar. 5 mai 2026".</summary>
    WeekdayDate,
    /// <summary>"May 5, 14:30" / "5 mai 14:30" (24-hour).</summary>
    MonthDayTime,
    /// <summary>"May" / "mai".</summary>
    MonthShort
}
