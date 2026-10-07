namespace PlasticSurgery.Common.Enums;

/// <summary>Known event_type values — free text at the DB level (Meta may add more), these are
/// just the ones WhatsAppHealthService currently knows how to normalize into a severity/HealthLevel.</summary>
public static class WhatsAppHealthEventType
{
    public const string PhoneNumberQualityUpdate = "phone_number_quality_update";
    public const string PhoneNumberNameUpdate = "phone_number_name_update";
    public const string AccountUpdate = "account_update";
    public const string AccountReviewUpdate = "account_review_update";
    public const string ConnectionUpdate = "connection_update";
    public const string TemplateProblem = "template_problem";
    public const string WebhookProblem = "webhook_problem";
    public const string UnknownMetaHealthEvent = "unknown_meta_health_event";
}
