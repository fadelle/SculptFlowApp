using System.Globalization;
using Microsoft.Extensions.Options;
using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Billing;

public sealed record EntitlementDefinition(string Key, EntitlementKind Kind, string Label);
