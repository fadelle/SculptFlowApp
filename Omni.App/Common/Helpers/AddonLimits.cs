using System.Globalization;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;

namespace Omni.App.Common.Helpers;

/// <summary>A client's limits with its add-ons: the plan's value + the add-on quantity for the same key ("unlimited" stays
/// unlimited; a key the plan doesn't list counts as 0). Features are untouched.</summary>
public static class AddonLimits
{
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> planValues, IEnumerable<ClientAddon> addons)
    {
        var merged = new Dictionary<string, string>(planValues);
        foreach (var group in addons.Where(a => a.Addon is not null && a.Quantity > 0).GroupBy(a => a.Addon!.EntitlementKey))
        {
            var extra = group.Sum(a => a.Quantity);
            if (merged.TryGetValue(group.Key, out var value) && value == EntitlementCatalog.Unlimited) continue;
            var planValue = merged.TryGetValue(group.Key, out var v) && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            merged[group.Key] = (planValue + extra).ToString(CultureInfo.InvariantCulture);
        }
        return merged;
    }
}
