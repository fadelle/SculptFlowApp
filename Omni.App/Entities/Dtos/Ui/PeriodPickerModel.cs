namespace Omni.App.Entities.Dtos.Ui;

/// <summary>Pages/Shared/_PeriodPicker: the selected period (Common/Helpers/ReportPeriod) and the custom range's dates.</summary>
public sealed record PeriodPickerModel(string Period, DateOnly? From, DateOnly? To);
