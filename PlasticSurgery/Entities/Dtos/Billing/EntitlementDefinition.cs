using System.Globalization;
using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

public sealed record EntitlementDefinition(string Key, EntitlementKind Kind, string Label);
