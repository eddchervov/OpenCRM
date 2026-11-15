namespace Payments.Service.Domain;

public enum OperationStatus
{
    Prepared,
    Sending,
    OutcomeUnknown,
    AwaitingProvider,
    Resolved,
    Rejected,
    ManualReview
}
