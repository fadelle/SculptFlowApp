using System.Globalization;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>The language a localizer words its texts in (JsonStringLocalizer), so helpers built on IStringLocalizer
/// (plurals, HTML texts) pick the same plural rules and number formats.</summary>
public interface ITextLanguage
{
    string Language { get; }

    CultureInfo Culture { get; }
}
