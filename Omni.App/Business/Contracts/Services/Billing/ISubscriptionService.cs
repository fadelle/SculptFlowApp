using Microsoft.Extensions.Options;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>
/// Subscription lifecycle, kept deliberately small:
///   start/change plan  -> active, new period from now, plan price charged from the wallet (unless waived),
///                         remaining included credit expired, the plan's credit granted
///   renewal (due)      -> period advances, price charged, credit reset; can't pay -> past_due (grace period)
///   past_due + grace   -> expired       cancel -> cancelled now, or at period end
/// No proration, no invoices, no payment gateway: the wallet is the payment method (top-ups are admin actions).
/// Every money movement goes through the ledger in the same transaction as the status change, under the client's
/// billing account lock; ledger keys derived from the period make a repeated run a no-op.
/// </summary>
public interface ISubscriptionService
{
    Task<ClientSubscription?> GetAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Starts a subscription or switches the client to another plan (a new period starts now). Throws
    /// BillingDeniedException when the wallet can't pay the price, ArgumentException for an unknown/inactive plan.</summary>
    Task<ClientSubscription> StartAsync(StartSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Renews (or moves to past_due / expired / cancelled) when the client's period has ended.</summary>
    Task<RenewalOutcome> RenewIfDueAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Immediately = ends now (no refund); otherwise it ends at the period end instead of renewing.</summary>
    Task<ClientSubscription?> CancelAsync(Guid clientId, bool immediately, string source, string? actor, string? reason, CancellationToken ct = default);

    /// <summary>Undoes a scheduled cancellation.</summary>
    Task<ClientSubscription?> ResumeAsync(Guid clientId, string source, string? actor, CancellationToken ct = default);

    /// <summary>Worker entry point: every subscription whose period ended or that is past due.</summary>
    Task<int> ProcessDueAsync(CancellationToken ct = default);
}
