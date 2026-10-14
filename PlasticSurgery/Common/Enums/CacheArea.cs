namespace PlasticSurgery.Common.Enums;

/// <summary>A group of table caches sharing one lifetime setting (Cache section of the configuration).</summary>
public enum CacheArea
{
    Clinics,
    StaffLookup,
    Procedures,
    Scheduling,
    Channels,
    BillingRates
}
