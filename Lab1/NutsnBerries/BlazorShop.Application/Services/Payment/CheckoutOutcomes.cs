namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Domain.Entities.Payment;

    /// <summary>
    /// Builds the persisted checkout outcomes and maps them to results. Pure: no services, no I/O.
    /// </summary>
    internal static class CheckoutOutcomes
    {
        internal const int OutcomeVersion = 1;

        internal static PersistedCheckoutOutcome CreateSuccessOutcome(
            Order order,
            CheckoutStatus status,
            CheckoutPaymentKind paymentKind,
            string message,
            string? redirectUrl = null,
            BankTransferInfo? bankTransfer = null) => new(
                OutcomeVersion,
                CheckoutExecutionStatus.Succeeded,
                new ServiceResponse<CheckoutResult>(true, message, order.Id)
                {
                    Payload = new CheckoutResult(
                        order.Id,
                        order.Reference,
                        status,
                        paymentKind,
                        redirectUrl,
                        bankTransfer),
                    ResponseType = ServiceResponseType.Success,
                });

        internal static PersistedCheckoutOutcome CreateFailureOutcome(string message) => new(
            OutcomeVersion,
            CheckoutExecutionStatus.BadRequest,
            new ServiceResponse<CheckoutResult>(false, message)
            {
                ResponseType = ServiceResponseType.ValidationError,
            });

        internal static CheckoutExecutionResult FromPersistedOutcome(
            PersistedCheckoutOutcome outcome,
            bool isReplay = false) => outcome.Status switch
            {
                CheckoutExecutionStatus.Succeeded => CheckoutExecutionResult.Succeeded(outcome.Response, isReplay),
                CheckoutExecutionStatus.BadRequest => CheckoutExecutionResult.Invalid(outcome.Response, isReplay),
                _ => throw new InvalidOperationException("Only terminal checkout outcomes may be persisted."),
            };
    }
}
