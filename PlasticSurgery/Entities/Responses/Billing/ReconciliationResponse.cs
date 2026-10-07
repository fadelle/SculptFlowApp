namespace PlasticSurgery.Entities.Responses.Billing;

public record ReconciliationResponse(bool IsBalanced, decimal WalletBalance, decimal WalletLedgerTotal, decimal IncludedCreditBalance,
    decimal IncludedCreditLedgerTotal, decimal ReservedAmount, decimal OpenReservationsTotal);
