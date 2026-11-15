namespace Payments.Service.Domain;

public enum PaymentStatus
{
    NotCreated,
    Pending,
    WaitingForCapture,
    Succeeded,
    Canceled
}
