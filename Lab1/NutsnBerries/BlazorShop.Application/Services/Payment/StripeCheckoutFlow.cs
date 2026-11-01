namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;
    using BlazorShop.Application.Services.Contracts.Payment;
    using BlazorShop.Domain.Entities.Payment;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using static BlazorShop.Application.Services.Payment.CheckoutOutcomes;

    /// <summary>
    /// Creates the Stripe Checkout Session for an order that is already committed, and settles every
    /// outcome of that attempt (paid, definitively failed, or needing reconciliation) in the idempotency record.
    /// </summary>
    internal sealed class StripeCheckoutFlow
    {
        private const int StripeInitializationVersion = 2;
        private static readonly TimeSpan StripeRecoveryTimeout = TimeSpan.FromSeconds(15);
        private readonly IPaymentService _paymentService;
        private readonly ICheckoutIdempotencyStore _idempotencyStore;
        private readonly IPaymentTransactionStore _paymentTransactionStore;
        private readonly IStripePaymentStateTransitionService _stripePaymentStateTransitionService;
        private readonly ClientAppOptions _clientAppOptions;
        private readonly CheckoutIdempotencyOptions _idempotencyOptions;
        private readonly ILogger _logger;

        public StripeCheckoutFlow(
            IPaymentService paymentService,
            ICheckoutIdempotencyStore idempotencyStore,
            IPaymentTransactionStore paymentTransactionStore,
            IStripePaymentStateTransitionService stripePaymentStateTransitionService,
            IOptions<ClientAppOptions> clientAppOptions,
            IOptions<CheckoutIdempotencyOptions> idempotencyOptions,
            ILogger logger)
        {
            _paymentService = paymentService;
            _idempotencyStore = idempotencyStore;
            _paymentTransactionStore = paymentTransactionStore;
            _stripePaymentStateTransitionService = stripePaymentStateTransitionService;
            _clientAppOptions = clientAppOptions.Value;
            _idempotencyOptions = idempotencyOptions.Value;
            _logger = logger;
        }

        internal async Task<CheckoutExecutionResult> InitializeAsync(
            Order order,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken)
        {
            PaymentTransaction paymentTransaction;
            try
            {
                paymentTransaction = await _paymentTransactionStore.GetOrCreateStripeAsync(
                    order.Id,
                    CurrencyMoney.ToMinorUnits(order.TotalAmount, order.Currency),
                    order.Currency,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or ArgumentOutOfRangeException
                or OverflowException)
            {
                _logger.LogError(
                    exception,
                    "Could not create or recover the Stripe payment transaction for order {OrderId}.",
                    order.Id);
                await _idempotencyStore.ReleaseLeaseAsync(
                    claim.Record.Id,
                    claim.LeaseOwnerId,
                    cancellationToken);
                return CheckoutExecutionResult.InProgress();
            }

            var terminalResult = await FinalizeExistingStripeStateAsync(
                order,
                paymentTransaction,
                claim,
                cancellationToken);
            if (terminalResult is not null)
            {
                return terminalResult;
            }

            // This timestamp is persisted before the provider call. If it was already present
            // when this lease was claimed, an earlier process may have dispatched to Stripe
            // even when it crashed before recording the provider response.
            var mayHaveEarlierProviderSideEffects = claim.Record.ProviderInitializationStartedOn.HasValue;
            CheckoutProviderInitialization? providerInitialization;
            try
            {
                providerInitialization = await _idempotencyStore.PrepareProviderInitializationAsync(
                    claim.Record.Id,
                    claim.LeaseOwnerId,
                    CreateStripeInitialization(order, paymentTransaction),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.Text.Json.JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Stripe initialization snapshot for transaction {PaymentTransactionId} cannot be reconstructed; provider state requires reconciliation.",
                    paymentTransaction.Id);
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "The persisted Stripe initialization snapshot could not be reconstructed.",
                    cancellationToken);
            }

            if (providerInitialization is null)
            {
                return CheckoutExecutionResult.InProgress();
            }

            var initialization = providerInitialization.Initialization;
            if (initialization.OrderId != order.Id
                || initialization.PaymentTransactionId != paymentTransaction.Id
                || initialization.ExpectedAmountMinor != paymentTransaction.ExpectedAmountMinor
                || !string.Equals(initialization.Currency, paymentTransaction.Currency, StringComparison.Ordinal)
                || !string.Equals(initialization.OrderReference, order.Reference, StringComparison.Ordinal))
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "The persisted Stripe initialization snapshot does not match the immutable order or payment transaction.",
                    cancellationToken);
            }

            if (!StripeCheckoutInitializationValidator.TryValidateAuthoritativeTotal(
                initialization,
                out var snapshotValidationError))
            {
                if (!mayHaveEarlierProviderSideEffects)
                {
                    return await FailDefinitiveInitialStripeAttemptAsync(
                        order,
                        paymentTransaction,
                        claim,
                        snapshotValidationError
                        ?? "The persisted Stripe line snapshot could not be validated.",
                        cancellationToken);
                }

                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    snapshotValidationError
                    ?? "The persisted Stripe line snapshot could not be validated.",
                    cancellationToken);
            }

            var pendingFailure = _idempotencyStore.ReadOutcome(claim.Record);
            if (pendingFailure?.Status == CheckoutExecutionStatus.BadRequest)
            {
                if (pendingFailure.DefinitiveInitialRejectionId.HasValue
                    && string.IsNullOrWhiteSpace(paymentTransaction.ProviderSessionId)
                    && string.IsNullOrWhiteSpace(paymentTransaction.ProviderPaymentIntentId))
                {
                    return await TerminalizeDefinitiveInitialStripeFailureAsync(
                        order,
                        paymentTransaction,
                        claim,
                        pendingFailure,
                        pendingFailure.DefinitiveInitialRejectionId.Value,
                        cancellationToken);
                }

                return await RecoverStripeProviderStateAsync(
                    order,
                    paymentTransaction,
                    claim,
                    initialization,
                    pendingFailure.DefinitiveInitialRejectionId.HasValue
                        ? "Provider identity exists, so the saved initial-rejection decision no longer authorizes local compensation."
                        : "The stored failure outcome does not prove a definitive initial rejection without earlier provider side effects.",
                    cancellationToken);
            }

            if (DateTime.UtcNow >= providerInitialization.StartedOn
                .AddHours(_idempotencyOptions.ProviderRecoveryWindowHours))
            {
                return await RecoverStripeProviderStateAsync(
                    order,
                    paymentTransaction,
                    claim,
                    initialization,
                    "The provider retry deadline elapsed without a durable Stripe Session identifier.",
                    cancellationToken);
            }

            var paymentResult = await _paymentService.Pay(
                initialization,
                $"blazorshop-checkout-{claim.Record.Id:N}",
                cancellationToken);

            if (paymentResult.Success
                && !string.IsNullOrWhiteSpace(paymentResult.RedirectUrl)
                && !string.IsNullOrWhiteSpace(paymentResult.ProviderSessionId))
            {
                PaymentProviderIdentityPersistenceResult identityResult;
                try
                {
                    identityResult = await _paymentTransactionStore.PersistProviderIdentityAsync(
                        paymentTransaction.Id,
                        paymentResult.ProviderSessionId,
                        paymentResult.ProviderPaymentIntentId,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Stripe returned session {ProviderSessionId} for transaction {PaymentTransactionId}, but provider identity persistence was ambiguous.",
                        paymentResult.ProviderSessionId,
                        paymentTransaction.Id);
                    return await MarkStripeReconciliationRequiredAsync(
                        claim,
                        "Stripe returned a Session, but its identity could not be persisted conclusively.",
                        cancellationToken);
                }

                if (identityResult.Outcome is not (PaymentProviderIdentityPersistenceOutcome.Persisted
                    or PaymentProviderIdentityPersistenceOutcome.AlreadyPersisted))
                {
                    _logger.LogWarning(
                        "Stripe provider identity for transaction {PaymentTransactionId} could not be persisted safely: {Outcome}.",
                        paymentTransaction.Id,
                        identityResult.Outcome);
                    return await MarkStripeReconciliationRequiredAsync(
                        claim,
                        "Stripe returned a Session whose identity could not be bound safely to the local transaction.",
                        cancellationToken);
                }

                if (identityResult.PaymentStatus == PaymentTransactionStatus.Paid)
                {
                    return await FinalizePaidStripeCheckoutAsync(order, claim, cancellationToken);
                }

                if (identityResult.PaymentStatus is PaymentTransactionStatus.Failed
                    or PaymentTransactionStatus.Cancelled)
                {
                    return await FinalizeTerminalStripeFailureAsync(
                        identityResult.PaymentStatus.Value,
                        claim,
                        cancellationToken);
                }

                if (identityResult.PaymentStatus != PaymentTransactionStatus.Pending)
                {
                    return await MarkStripeReconciliationRequiredAsync(
                        claim,
                        "The local payment state could not be classified after Stripe Session creation.",
                        cancellationToken);
                }

                var outcome = CreateSuccessOutcome(
                    order,
                    CheckoutStatus.PendingPayment,
                    CheckoutPaymentKind.Stripe,
                    "Order created. Continue to the secure card payment page.",
                    paymentResult.RedirectUrl);
                var completed = await _idempotencyStore.CompleteAsync(
                    claim.Record.Id,
                    claim.LeaseOwnerId,
                    outcome,
                    CheckoutIdempotencyState.Completed,
                    cancellationToken);
                return completed
                    ? FromPersistedOutcome(outcome)
                    : CheckoutExecutionResult.InProgress();
            }

            if (paymentResult.FailureKind == PaymentInitializationFailureKind.Ambiguous)
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    paymentResult.ErrorMessage
                    ?? "Stripe checkout-session creation returned an ambiguous result.",
                    cancellationToken);
            }

            if (mayHaveEarlierProviderSideEffects)
            {
                return await RecoverStripeProviderStateAsync(
                    order,
                    paymentTransaction,
                    claim,
                    initialization,
                    "The current Stripe retry was rejected, but an earlier request may have executed.",
                    cancellationToken);
            }

            return await FailDefinitiveInitialStripeAttemptAsync(
                order,
                paymentTransaction,
                claim,
                paymentResult.ErrorMessage
                ?? "Unable to initialize the card payment session. Please start a new checkout attempt.",
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> FailDefinitiveInitialStripeAttemptAsync(
            Order order,
            PaymentTransaction paymentTransaction,
            CheckoutIdempotencyClaim claim,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            var definitiveInitialRejectionId = Guid.NewGuid();
            var failure = CreateFailureOutcome(errorMessage) with
            {
                DefinitiveInitialRejectionId = definitiveInitialRejectionId,
            };
            if (!await _idempotencyStore.SetPendingOutcomeAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                failure,
                cancellationToken))
            {
                return CheckoutExecutionResult.InProgress();
            }

            return await TerminalizeDefinitiveInitialStripeFailureAsync(
                order,
                paymentTransaction,
                claim,
                failure,
                definitiveInitialRejectionId,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> TerminalizeDefinitiveInitialStripeFailureAsync(
            Order order,
            PaymentTransaction paymentTransaction,
            CheckoutIdempotencyClaim claim,
            PersistedCheckoutOutcome failure,
            Guid definitiveInitialRejectionId,
            CancellationToken cancellationToken)
        {
            var transition = await _stripePaymentStateTransitionService
                .TransitionDefinitiveInitialFailureAsync(
                    paymentTransaction.Id,
                    claim.Record.Id,
                    claim.LeaseOwnerId,
                    definitiveInitialRejectionId,
                    cancellationToken);

            if (transition.PaymentStatus == PaymentTransactionStatus.Paid)
            {
                return await FinalizePaidStripeCheckoutAsync(order, claim, cancellationToken);
            }

            if (transition.PaymentStatus is PaymentTransactionStatus.Failed
                or PaymentTransactionStatus.Cancelled)
            {
                return await FinalizeTerminalStripeFailureAsync(
                    transition.PaymentStatus.Value,
                    claim,
                    cancellationToken,
                    failure);
            }

            _logger.LogWarning(
                "Could not apply definitive initial Stripe rejection {DefinitiveInitialRejectionId} for transaction {PaymentTransactionId}: {Outcome}, current state {PaymentStatus}, reason {Reason}",
                definitiveInitialRejectionId,
                paymentTransaction.Id,
                transition.Outcome,
                transition.PaymentStatus,
                transition.ErrorMessage);
            await _idempotencyStore.ReleaseLeaseAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                cancellationToken);
            return CheckoutExecutionResult.InProgress();
        }

        private async Task<CheckoutExecutionResult> RecoverStripeProviderStateAsync(
            Order order,
            PaymentTransaction paymentTransaction,
            CheckoutIdempotencyClaim claim,
            StripeCheckoutInitialization initialization,
            string missingProviderIdentityReason,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(paymentTransaction.ProviderSessionId))
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    missingProviderIdentityReason,
                    cancellationToken);
            }

            StripePaymentRecoveryResult recovery;
            using var recoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            recoveryTimeout.CancelAfter(StripeRecoveryTimeout);
            try
            {
                recovery = await _paymentService.RecoverAsync(
                    new StripePaymentRecoveryRequest(
                        initialization,
                        paymentTransaction.ProviderSessionId,
                        paymentTransaction.ProviderPaymentIntentId),
                    recoveryTimeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "Stripe provider lookup or expiration timed out.",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Stripe recovery lookup for transaction {PaymentTransactionId} was inconclusive.",
                    paymentTransaction.Id);
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "Stripe provider lookup was unavailable or timed out.",
                    cancellationToken);
            }

            if (recovery.Outcome is StripePaymentRecoveryOutcome.Open
                or StripePaymentRecoveryOutcome.Unresolved)
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    recovery.Reason ?? "Stripe has not confirmed a safe terminal payment outcome.",
                    cancellationToken);
            }

            PaymentProviderIdentityPersistenceResult identityResult;
            try
            {
                identityResult = await _paymentTransactionStore.PersistProviderIdentityAsync(
                    paymentTransaction.Id,
                    paymentTransaction.ProviderSessionId,
                    recovery.ProviderPaymentIntentId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Stripe recovery identity persistence for transaction {PaymentTransactionId} was inconclusive.",
                    paymentTransaction.Id);
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "The confirmed Stripe state could not be bound durably to the local transaction.",
                    cancellationToken);
            }

            if (identityResult.Outcome is not (PaymentProviderIdentityPersistenceOutcome.Persisted
                or PaymentProviderIdentityPersistenceOutcome.AlreadyPersisted))
            {
                return await MarkStripeReconciliationRequiredAsync(
                    claim,
                    "Stripe provider identity contradicted the local payment transaction.",
                    cancellationToken);
            }

            if (identityResult.PaymentStatus == PaymentTransactionStatus.Paid)
            {
                return await FinalizePaidStripeCheckoutAsync(order, claim, cancellationToken);
            }

            if (identityResult.PaymentStatus is PaymentTransactionStatus.Failed
                or PaymentTransactionStatus.Cancelled)
            {
                return await FinalizeTerminalStripeFailureAsync(
                    identityResult.PaymentStatus.Value,
                    claim,
                    cancellationToken);
            }

            var targetStatus = recovery.Outcome == StripePaymentRecoveryOutcome.Paid
                ? PaymentTransactionStatus.Paid
                : PaymentTransactionStatus.Cancelled;
            var terminalFailure = recovery.Outcome == StripePaymentRecoveryOutcome.Expired
                ? CreateFailureOutcome(
                    "Stripe confirmed that the payment session expired without payment. Start a new checkout attempt.")
                : null;
            return await TerminalizeStripeAsync(
                order,
                paymentTransaction,
                claim,
                terminalFailure,
                targetStatus,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> MarkStripeReconciliationRequiredAsync(
            CheckoutIdempotencyClaim claim,
            string reason,
            CancellationToken cancellationToken)
        {
            const string browserMessage =
                "Card payment status requires reconciliation. Inventory remains reserved; retry with the same Idempotency-Key.";
            var marker = new PersistedCheckoutOutcome(
                OutcomeVersion,
                CheckoutExecutionStatus.InProgress,
                new ServiceResponse<CheckoutResult>(false, $"{browserMessage} Reason: {reason}")
                {
                    ResponseType = ServiceResponseType.Conflict,
                });
            if (!await _idempotencyStore.SetPendingOutcomeAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                marker,
                cancellationToken))
            {
                return CheckoutExecutionResult.InProgress();
            }

            await _idempotencyStore.ReleaseLeaseAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                cancellationToken);
            _logger.LogWarning(
                "Stripe checkout {CheckoutIdempotencyRecordId} requires reconciliation: {Reason}",
                claim.Record.Id,
                reason);
            return CheckoutExecutionResult.InProgress(marker.Response.Message);
        }

        private async Task<CheckoutExecutionResult> TerminalizeStripeAsync(
            Order order,
            PaymentTransaction paymentTransaction,
            CheckoutIdempotencyClaim claim,
            PersistedCheckoutOutcome? failure,
            PaymentTransactionStatus targetStatus,
            CancellationToken cancellationToken)
        {
            var transition = await _stripePaymentStateTransitionService.TransitionAsync(
                paymentTransaction.Id,
                targetStatus,
                cancellationToken);

            if (transition.PaymentStatus == PaymentTransactionStatus.Paid)
            {
                return await FinalizePaidStripeCheckoutAsync(order, claim, cancellationToken);
            }

            if (transition.PaymentStatus is PaymentTransactionStatus.Failed
                or PaymentTransactionStatus.Cancelled)
            {
                return await FinalizeTerminalStripeFailureAsync(
                    transition.PaymentStatus.Value,
                    claim,
                    cancellationToken,
                    failure);
            }

            _logger.LogWarning(
                "Could not terminalize Stripe transaction {PaymentTransactionId} as {TargetStatus}: {Outcome}, current state {PaymentStatus}, reason {Reason}",
                paymentTransaction.Id,
                targetStatus,
                transition.Outcome,
                transition.PaymentStatus,
                transition.ErrorMessage);
            await _idempotencyStore.ReleaseLeaseAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                cancellationToken);
            return CheckoutExecutionResult.InProgress();
        }

        private async Task<CheckoutExecutionResult?> FinalizeExistingStripeStateAsync(
            Order order,
            PaymentTransaction paymentTransaction,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken)
        {
            if (paymentTransaction.Status == PaymentTransactionStatus.Pending)
            {
                return null;
            }

            if (paymentTransaction.Status == PaymentTransactionStatus.Paid)
            {
                return await FinalizePaidStripeCheckoutAsync(order, claim, cancellationToken);
            }

            return await FinalizeTerminalStripeFailureAsync(
                paymentTransaction.Status,
                claim,
                cancellationToken);
        }

        private async Task<CheckoutExecutionResult> FinalizePaidStripeCheckoutAsync(
            Order order,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken)
        {
            var paidOutcome = CreateSuccessOutcome(
                order,
                CheckoutStatus.Confirmed,
                CheckoutPaymentKind.Stripe,
                "Card payment was already confirmed for this order.");
            var completed = await _idempotencyStore.CompleteAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                paidOutcome,
                CheckoutIdempotencyState.Completed,
                cancellationToken);
            return completed
                ? FromPersistedOutcome(paidOutcome)
                : CheckoutExecutionResult.InProgress();
        }

        private async Task<CheckoutExecutionResult> FinalizeTerminalStripeFailureAsync(
            PaymentTransactionStatus paymentStatus,
            CheckoutIdempotencyClaim claim,
            CancellationToken cancellationToken,
            PersistedCheckoutOutcome? existingFailure = null)
        {
            var storedOutcome = _idempotencyStore.ReadOutcome(claim.Record);
            var defaultFailure = CreateFailureOutcome(paymentStatus == PaymentTransactionStatus.Cancelled
                ? "The card-payment attempt expired or was cancelled. Start a new checkout attempt."
                : "Unable to initialize the card payment session. Please start a new checkout attempt.");
            var failure = existingFailure?.Status == CheckoutExecutionStatus.BadRequest
                ? existingFailure
                : storedOutcome?.Status == CheckoutExecutionStatus.BadRequest
                    ? storedOutcome
                    : defaultFailure;
            var completed = await _idempotencyStore.CompleteAsync(
                claim.Record.Id,
                claim.LeaseOwnerId,
                failure,
                CheckoutIdempotencyState.Failed,
                cancellationToken);
            return completed
                ? FromPersistedOutcome(failure)
                : CheckoutExecutionResult.InProgress();
        }

        private StripeCheckoutInitialization CreateStripeInitialization(
            Order order,
            PaymentTransaction paymentTransaction)
        {
            var lines = order.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => line.ProductVariantId)
                .Select(line => new StripeCheckoutLineItem(
                    line.ProductId,
                    line.ProductVariantId,
                    line.ProductNameSnapshot,
                    BuildStripeDescription(line),
                    line.Quantity,
                    CurrencyMoney.ToMinorUnits(line.UnitPrice, order.Currency),
                    order.Currency.ToLowerInvariant()))
                .ToArray();
            var stripeTotal = lines.Aggregate(
                0L,
                (total, line) => checked(total + checked(line.UnitAmount * line.Quantity)));
            if (stripeTotal != paymentTransaction.ExpectedAmountMinor)
            {
                throw new InvalidOperationException(
                    "The Stripe line total does not match the immutable payment transaction amount.");
            }

            return new StripeCheckoutInitialization(
                StripeInitializationVersion,
                order.Id,
                order.Reference,
                paymentTransaction.Id,
                paymentTransaction.ExpectedAmountMinor,
                paymentTransaction.Currency,
                ["card"],
                "payment",
                lines,
                BuildClientUrl(
                    $"payment-success?pm=card&order_id={order.Id:D}&reference={Uri.EscapeDataString(order.Reference)}&session_id={{CHECKOUT_SESSION_ID}}"),
                BuildClientUrl($"payment-cancel?order_id={order.Id:D}"));
        }

        private static string? BuildStripeDescription(OrderLine line)
        {
            var details = new[]
                {
                    string.IsNullOrWhiteSpace(line.SkuSnapshot) ? null : $"SKU: {line.SkuSnapshot}",
                    string.IsNullOrWhiteSpace(line.SizeValueSnapshot)
                        ? null
                        : string.IsNullOrWhiteSpace(line.SizeScaleSnapshot)
                            ? $"Size: {line.SizeValueSnapshot}"
                            : $"Size: {line.SizeScaleSnapshot} {line.SizeValueSnapshot}",
                    string.IsNullOrWhiteSpace(line.ColorSnapshot) ? null : $"Color: {line.ColorSnapshot}",
                }
                .Where(value => value is not null)
                .ToArray();
            return details.Length == 0 ? null : string.Join(", ", details);
        }

        private string BuildClientUrl(string path) =>
            $"{_clientAppOptions.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }
}
