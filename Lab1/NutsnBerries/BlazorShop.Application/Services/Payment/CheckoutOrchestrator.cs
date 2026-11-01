namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;
    using BlazorShop.Application.Services.Contracts.Payment;
    using BlazorShop.Domain.Contracts;
    using BlazorShop.Domain.Contracts.Authentication;
    using BlazorShop.Domain.Contracts.Payment;
    using BlazorShop.Domain.Entities.Identity;
    using BlazorShop.Domain.Entities.Payment;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using static BlazorShop.Application.Services.Payment.CheckoutOutcomes;

    public sealed class CheckoutOrchestrator : ICheckoutOrchestrator
    {
        private readonly CheckoutCartResolver _cartResolver;
        private readonly IPaymentMethodService _paymentMethodService;
        private readonly IAppUserManager _userManager;
        private readonly IInventoryReservationService _inventoryReservationService;
        private readonly ICheckoutIdempotencyStore _idempotencyStore;
        private readonly IPaymentTransactionStore _paymentTransactionStore;
        private readonly IOrderRepository _orderRepository;
        private readonly BankTransferNotifier _bankTransferNotifier;
        private readonly CommerceOptions _commerceOptions;
        private readonly StripeCheckoutFlow _stripeFlow;

        public CheckoutOrchestrator(
            IProductReadRepository productReadRepository,
            IPaymentMethodService paymentMethodService,
            IPaymentService paymentService,
            IAppUserManager userManager,
            IInventoryReservationService inventoryReservationService,
            ICheckoutIdempotencyStore idempotencyStore,
            IPaymentTransactionStore paymentTransactionStore,
            IStripePaymentStateTransitionService stripePaymentStateTransitionService,
            IOrderRepository orderRepository,
            IEmailService emailService,
            IOptions<BankTransferSettings> bankTransferOptions,
            IOptions<ClientAppOptions> clientAppOptions,
            IOptions<CheckoutIdempotencyOptions> idempotencyOptions,
            IOptions<CommerceOptions> commerceOptions,
            ILogger<CheckoutOrchestrator> logger)
        {
            _cartResolver = new CheckoutCartResolver(productReadRepository);
            _paymentMethodService = paymentMethodService;
            _userManager = userManager;
            _inventoryReservationService = inventoryReservationService;
            _idempotencyStore = idempotencyStore;
            _paymentTransactionStore = paymentTransactionStore;
            _orderRepository = orderRepository;
            _bankTransferNotifier = new BankTransferNotifier(emailService, bankTransferOptions, logger);
            _commerceOptions = commerceOptions.Value;
            _stripeFlow = new StripeCheckoutFlow(
                paymentService,
                idempotencyStore,
                paymentTransactionStore,
                stripePaymentStateTransitionService,
                clientAppOptions,
                idempotencyOptions,
                logger);
        }

        public async Task<CheckoutExecutionResult> CheckoutAsync(
            Checkout checkout,
            string userId,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("A signed-in user is required to place an order.");
            }

            if (checkout is null)
            {
                return BadRequest("Checkout details are required.");
            }

            var canonicalization = CheckoutIntentCanonicalizer.Canonicalize(checkout);
            if (!canonicalization.IsValid)
            {
                return BadRequest(canonicalization.ErrorMessage!);
            }

            var customer = await _userManager.GetUserByIdAsync(userId);
            if (customer is null)
            {
                return BadRequest("The authenticated customer account could not be found.");
            }

            var claim = await _idempotencyStore.ClaimAsync(
                userId,
                idempotencyKey,
                canonicalization.Fingerprint!,
                checkout.PaymentMethodId,
                cancellationToken);

            if (claim.Status == CheckoutIdempotencyClaimStatus.Conflict)
            {
                return CheckoutExecutionResult.Conflict(
                    "The Idempotency-Key was already used for different checkout intent.");
            }

            if (claim.Status == CheckoutIdempotencyClaimStatus.InProgress)
            {
                return CheckoutExecutionResult.InProgress();
            }

            if (claim.Status == CheckoutIdempotencyClaimStatus.Replayed)
            {
                return await ReplayPersistedOutcomeAsync(claim.Outcome!, cancellationToken);
            }

            var normalizedCheckout = new Checkout
            {
                PaymentMethodId = checkout.PaymentMethodId,
                Carts = canonicalization.Lines,
            };
            return await ExecuteOwnedCheckoutAsync(
                normalizedCheckout,
                userId,
                customer,
                claim,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> ExecuteOwnedCheckoutAsync(
            Checkout checkout,
            string userId,
            AppUser customer,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken)
        {
            var paymentKind = await ResolvePaymentKindAsync(checkout.PaymentMethodId);
            if (!paymentKind.HasValue)
            {
                return await FinishFailureAsync(claim, "Invalid payment method", cancellationToken);
            }

            var existingOrder = await _orderRepository.GetByIdAsync(claim.Record.OrderId);
            if (existingOrder is not null)
            {
                if (!string.Equals(existingOrder.UserId, userId, StringComparison.Ordinal)
                    || !string.Equals(existingOrder.Reference, claim.Record.OrderReference, StringComparison.Ordinal))
                {
                    return await FinishFailureAsync(
                        claim,
                        "The existing order does not match the durable checkout identity.",
                        cancellationToken);
                }

                return await ResumeCommittedOrderAsync(
                    existingOrder,
                    paymentKind.Value,
                    customer,
                    claim,
                    cancellationToken);
            }

            var resolution = await _cartResolver.ResolveCartLinesAsync(checkout.Carts);
            if (resolution.ErrorMessage is not null)
            {
                return await FinishFailureAsync(claim, resolution.ErrorMessage, cancellationToken);
            }

            Order order;
            try
            {
                order = CheckoutOrderFactory.CreateOrder(
                    resolution.Lines,
                    userId,
                    claim.Record.OrderId,
                    claim.Record.OrderReference,
                    paymentKind.Value,
                    _commerceOptions.Currency,
                    customer);
            }
            catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
            {
                return await FinishFailureAsync(
                    claim,
                    "The order amount cannot be represented safely in the configured currency.",
                    cancellationToken);
            }
            var successOutcome = paymentKind.Value switch
            {
                CheckoutPaymentKind.CashOnDelivery => CreateSuccessOutcome(
                    order,
                    CheckoutStatus.Confirmed,
                    CheckoutPaymentKind.CashOnDelivery,
                    "Order placed with Cash on Delivery. You will pay upon delivery."),
                CheckoutPaymentKind.BankTransfer => CreateSuccessOutcome(
                    order,
                    CheckoutStatus.PendingPayment,
                    CheckoutPaymentKind.BankTransfer,
                    "Order placed. Complete the bank transfer using the supplied instructions.",
                    bankTransfer: _bankTransferNotifier.BuildInfo(order)),
                _ => null,
            };

            if (successOutcome is not null
                && !await _idempotencyStore.SetPendingOutcomeAsync(
                    claim.Record.Id,
                    claim.LeaseOwnerId,
                    successOutcome,
                    cancellationToken))
            {
                return CheckoutExecutionResult.InProgress();
            }

            var reservationStatus = paymentKind == CheckoutPaymentKind.CashOnDelivery
                ? InventoryReservationStatus.Consumed
                : InventoryReservationStatus.Reserved;
            var inventoryResult = await _inventoryReservationService.CreateOrderWithInventoryAsync(
                order,
                reservationStatus,
                claim.Record.Id,
                cancellationToken);
            if (!inventoryResult.Success)
            {
                return await FinishFailureAsync(
                    claim,
                    inventoryResult.ErrorMessage ?? "Unable to reserve the requested inventory.",
                    cancellationToken);
            }

            if (paymentKind == CheckoutPaymentKind.Stripe)
            {
                return await _stripeFlow.InitializeAsync(order, claim, cancellationToken);
            }

            return await CompleteLocalPaymentAsync(
                paymentKind.Value,
                customer,
                claim,
                successOutcome!,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> ResumeCommittedOrderAsync(
            Order order,
            CheckoutPaymentKind paymentKind,
            AppUser customer,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken)
        {
            if (paymentKind == CheckoutPaymentKind.Stripe)
            {
                return await _stripeFlow.InitializeAsync(order, claim, cancellationToken);
            }

            var outcome = _idempotencyStore.ReadOutcome(claim.Record)
                ?? (paymentKind == CheckoutPaymentKind.CashOnDelivery
                    ? CreateSuccessOutcome(
                        order,
                        CheckoutStatus.Confirmed,
                        paymentKind,
                        "Order placed with Cash on Delivery. You will pay upon delivery.")
                    : CreateSuccessOutcome(
                        order,
                        CheckoutStatus.PendingPayment,
                        paymentKind,
                        "Order placed. Complete the bank transfer using the supplied instructions.",
                        bankTransfer: _bankTransferNotifier.BuildInfo(order)));
            return await CompleteLocalPaymentAsync(
                paymentKind,
                customer,
                claim,
                outcome,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> CompleteLocalPaymentAsync(
            CheckoutPaymentKind paymentKind,
            AppUser customer,
            CheckoutIdempotencyClaim claim,
            PersistedCheckoutOutcome outcome,
            CancellationToken cancellationToken)
        {
            var completed = await _idempotencyStore.CompleteAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                outcome,
                CheckoutIdempotencyState.Completed,
                cancellationToken);
            if (!completed)
            {
                return CheckoutExecutionResult.InProgress();
            }

            if (paymentKind == CheckoutPaymentKind.BankTransfer
                && outcome.Response.Payload?.BankTransfer is not null)
            {
                await _bankTransferNotifier.TrySendEmailAsync(customer, outcome.Response.Payload.BankTransfer);
            }

            return FromPersistedOutcome(outcome);
        }

        private async Task<CheckoutExecutionResult> FinishFailureAsync(
            CheckoutIdempotencyClaim claim,
            string message,
            CancellationToken cancellationToken)
        {
            var outcome = CreateFailureOutcome(message);
            var completed = await _idempotencyStore.CompleteAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                outcome,
                CheckoutIdempotencyState.Failed,
                cancellationToken);
            return completed
                ? FromPersistedOutcome(outcome)
                : CheckoutExecutionResult.InProgress();
        }

        private async Task<CheckoutPaymentKind?> ResolvePaymentKindAsync(Guid paymentMethodId)
        {
            var availableMethods = await _paymentMethodService.GetPaymentMethodsAsync();
            if (!availableMethods.Any(method => method.Id == paymentMethodId))
            {
                return null;
            }

            return paymentMethodId switch
            {
                var id when id == PaymentMethodIds.CreditCard => CheckoutPaymentKind.Stripe,
                var id when id == PaymentMethodIds.CashOnDelivery => CheckoutPaymentKind.CashOnDelivery,
                var id when id == PaymentMethodIds.BankTransfer => CheckoutPaymentKind.BankTransfer,
                _ => null,
            };
        }

        private async Task<CheckoutExecutionResult> ReplayPersistedOutcomeAsync(
            PersistedCheckoutOutcome outcome,
            CancellationToken cancellationToken)
        {
            var checkout = outcome.Response.Payload;
            if (checkout?.PaymentKind != CheckoutPaymentKind.Stripe)
            {
                return FromPersistedOutcome(outcome, isReplay: true);
            }

            var paymentTransaction = await _paymentTransactionStore.GetStripeByOrderIdAsync(
                checkout.OrderId,
                cancellationToken);
            if (paymentTransaction?.Status == PaymentTransactionStatus.Paid)
            {
                var paidOutcome = new PersistedCheckoutOutcome(
                    OutcomeVersion,
                    CheckoutExecutionStatus.Succeeded,
                    new ServiceResponse<CheckoutResult>(
                        true,
                        "Card payment was already confirmed for this order.",
                        checkout.OrderId)
                    {
                        Payload = checkout with
                        {
                            Status = CheckoutStatus.Confirmed,
                            RedirectUrl = null,
                        },
                        ResponseType = ServiceResponseType.Success,
                    });
                return FromPersistedOutcome(paidOutcome, isReplay: true);
            }

            if (paymentTransaction?.Status is PaymentTransactionStatus.Failed
                or PaymentTransactionStatus.Cancelled)
            {
                var failure = CreateFailureOutcome(
                    paymentTransaction.Status == PaymentTransactionStatus.Cancelled
                        ? "The card-payment attempt expired or was cancelled. Start a new checkout attempt."
                        : "The card-payment attempt failed. Start a new checkout attempt.");
                return FromPersistedOutcome(failure, isReplay: true);
            }

            return FromPersistedOutcome(outcome, isReplay: true);
        }

        private static CheckoutExecutionResult BadRequest(string message) =>
            CheckoutExecutionResult.Invalid(CreateFailureOutcome(message).Response);
    }
}
