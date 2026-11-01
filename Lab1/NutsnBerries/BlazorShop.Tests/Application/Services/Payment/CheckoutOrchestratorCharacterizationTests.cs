namespace BlazorShop.Tests.Application.Services.Payment
{
    using BlazorShop.Application.DTOs;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;
    using BlazorShop.Application.Services.Contracts.Payment;
    using BlazorShop.Application.Services.Payment;
    using BlazorShop.Domain.Contracts;
    using BlazorShop.Domain.Contracts.Authentication;
    using BlazorShop.Domain.Contracts.Payment;
    using BlazorShop.Domain.Entities;
    using BlazorShop.Domain.Entities.Identity;
    using BlazorShop.Domain.Entities.Payment;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using Moq;

    using Xunit;

    /// <summary>
    /// Pins checkout behavior that <see cref="CheckoutOrchestratorTests"/> does not assert, so that the
    /// orchestrator can be split into smaller classes without changing what customers see. Each test was
    /// written against the unsplit orchestrator and must keep passing, unchanged, after every extraction.
    /// </summary>
    public sealed class CheckoutOrchestratorCharacterizationTests
    {
        private const string BankTransferSubject = "Bank Transfer Instructions";

        private readonly Mock<IProductReadRepository> _products = new();
        private readonly Mock<IPaymentMethodService> _paymentMethods = new();
        private readonly Mock<IPaymentService> _payment = new();
        private readonly Mock<IInventoryReservationService> _inventory = new();
        private readonly Mock<ICheckoutIdempotencyStore> _idempotency = new();
        private readonly Mock<IPaymentTransactionStore> _paymentTransactions = new();
        private readonly Mock<IEmailService> _email = new();
        private readonly CheckoutOrchestrator _orchestrator;

        public CheckoutOrchestratorCharacterizationTests()
        {
            var users = new Mock<IAppUserManager>();
            users.Setup(manager => manager.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new AppUser
                {
                    Id = "customer-1",
                    FullName = "Original Customer",
                    Email = "customer@example.com",
                });
            var orders = new Mock<IOrderRepository>();
            orders.Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Order?)null);
            var stripeTransitions = new Mock<IStripePaymentStateTransitionService>();

            _products.Setup(repository => repository.GetProductVariantsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, ProductVariant>());
            _products.Setup(repository => repository.GetProductIdsWithVariantsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new HashSet<Guid>());
            _inventory.Setup(service => service.CreateOrderWithInventoryAsync(
                    It.IsAny<Order>(),
                    It.IsAny<InventoryReservationStatus>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InventoryReservationResult(true));
            _idempotency.Setup(store => store.ClaimAsync(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, Guid key, string fingerprint, Guid paymentMethodId, CancellationToken _) =>
                {
                    var ownerId = Guid.NewGuid();
                    return new CheckoutIdempotencyClaim(
                        CheckoutIdempotencyClaimStatus.Acquired,
                        new CheckoutIdempotencyRecord
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            IdempotencyKey = key,
                            RequestFingerprint = fingerprint,
                            PaymentMethodId = paymentMethodId,
                            OrderId = Guid.NewGuid(),
                            OrderReference = $"REF-{Guid.NewGuid():N}",
                            LeaseOwnerId = ownerId,
                        },
                        ownerId);
                });
            _idempotency.Setup(store => store.SetPendingOutcomeAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PersistedCheckoutOutcome>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _idempotency.Setup(store => store.PrepareProviderInitializationAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<StripeCheckoutInitialization>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid _, StripeCheckoutInitialization initialization, CancellationToken _) =>
                    new CheckoutProviderInitialization(DateTime.UtcNow, initialization));
            _idempotency.Setup(store => store.CompleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<PersistedCheckoutOutcome>(),
                    It.IsAny<CheckoutIdempotencyState>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _idempotency.Setup(store => store.ReleaseLeaseAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _paymentTransactions.Setup(store => store.GetOrCreateStripeAsync(
                    It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid orderId, long amount, string currency, CancellationToken _) =>
                    new PaymentTransaction
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = PaymentProviderNames.Stripe,
                        ExpectedAmountMinor = amount,
                        Currency = currency,
                    });

            _orchestrator = new CheckoutOrchestrator(
                _products.Object,
                _paymentMethods.Object,
                _payment.Object,
                users.Object,
                _inventory.Object,
                _idempotency.Object,
                _paymentTransactions.Object,
                stripeTransitions.Object,
                orders.Object,
                _email.Object,
                Options.Create(new BankTransferSettings
                {
                    Iban = "BG00TEST",
                    Beneficiary = "Nuts Beneficiary",
                    BankName = "Berry Bank",
                    AdditionalInfo = "Use the order reference.",
                }),
                Options.Create(new ClientAppOptions { BaseUrl = "https://shop.test" }),
                Options.Create(new CheckoutIdempotencyOptions()),
                Options.Create(new CommerceOptions { Currency = "EUR" }),
                Mock.Of<ILogger<CheckoutOrchestrator>>());
        }

        [Fact]
        public async Task UnpublishedProduct_IsRejectedBeforeOrderCreation()
        {
            var product = ConfigureProduct(price: 10m, quantity: 5);
            product.IsPublished = false;
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(PaymentMethodIds.CashOnDelivery, new CartLineRequest(product.Id, null, 1));

            Assert.False(result.Success);
            Assert.Contains("not currently purchasable", result.Message, StringComparison.OrdinalIgnoreCase);
            VerifyInventoryWasNotCreated();
        }

        [Fact]
        public async Task ProductWithoutPublishDate_IsRejectedBeforeOrderCreation()
        {
            var product = ConfigureProduct(price: 10m, quantity: 5);
            product.PublishedOn = null;
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(PaymentMethodIds.CashOnDelivery, new CartLineRequest(product.Id, null, 1));

            Assert.False(result.Success);
            Assert.Contains("not currently purchasable", result.Message, StringComparison.OrdinalIgnoreCase);
            VerifyInventoryWasNotCreated();
        }

        [Fact]
        public async Task ProductMissingFromCatalog_IsRejectedBeforeOrderCreation()
        {
            _products.Setup(repository => repository.GetProductsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, Product>());
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(PaymentMethodIds.CashOnDelivery, new CartLineRequest(Guid.NewGuid(), null, 1));

            Assert.False(result.Success);
            Assert.Contains("no longer exists", result.Message, StringComparison.OrdinalIgnoreCase);
            VerifyInventoryWasNotCreated();
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        public async Task VariantQuantityAboveStock_IsRejectedBeforeOrderCreation(int stock, int quantity)
        {
            var product = ConfigureProduct(price: 10m, quantity: 0);
            var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Price = 12m, Stock = stock };
            _products.Setup(repository => repository.GetProductVariantsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, ProductVariant> { [variant.Id] = variant });
            _products.Setup(repository => repository.GetProductIdsWithVariantsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new HashSet<Guid> { product.Id });
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(
                PaymentMethodIds.CashOnDelivery,
                new CartLineRequest(product.Id, variant.Id, quantity));

            Assert.False(result.Success);
            Assert.Contains("variant's current availability", result.Message, StringComparison.OrdinalIgnoreCase);
            VerifyInventoryWasNotCreated();
        }

        [Fact]
        public async Task ProductWithoutAPositivePrice_IsRejectedBeforeOrderCreation()
        {
            var product = ConfigureProduct(price: 0m, quantity: 5);
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(PaymentMethodIds.CashOnDelivery, new CartLineRequest(product.Id, null, 1));

            Assert.False(result.Success);
            Assert.Contains("valid current price", result.Message, StringComparison.OrdinalIgnoreCase);
            VerifyInventoryWasNotCreated();
        }

        [Fact]
        public async Task UnknownPaymentMethod_FailsAsValidationErrorAndPersistsTheFailedOutcome()
        {
            var product = ConfigureProduct(price: 10m, quantity: 5);
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);
            PersistedCheckoutOutcome? persisted = null;
            CheckoutIdempotencyState? state = null;
            _idempotency.Setup(store => store.CompleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<PersistedCheckoutOutcome>(),
                    It.IsAny<CheckoutIdempotencyState>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, Guid, PersistedCheckoutOutcome, CheckoutIdempotencyState, CancellationToken>(
                    (_, _, outcome, completedState, _) =>
                    {
                        persisted = outcome;
                        state = completedState;
                    })
                .ReturnsAsync(true);

            var result = await CheckoutAsync(PaymentMethodIds.BankTransfer, new CartLineRequest(product.Id, null, 1));

            Assert.False(result.Success);
            Assert.Equal(CheckoutExecutionStatus.BadRequest, result.Status);
            Assert.Equal(ServiceResponseType.ValidationError, result.Response.ResponseType);
            Assert.Equal("Invalid payment method", result.Message);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted!.Version);
            Assert.Equal(CheckoutExecutionStatus.BadRequest, persisted.Status);
            Assert.Equal(CheckoutIdempotencyState.Failed, state);
        }

        [Fact]
        public async Task BankTransfer_ReturnsAndEmailsEveryConfiguredField()
        {
            var product = ConfigureProduct(price: 20m, quantity: 5);
            ConfigurePaymentMethod(PaymentMethodIds.BankTransfer);
            string? body = null;
            _email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, _, html) => body = html)
                .Returns(Task.CompletedTask);

            var result = await CheckoutAsync(PaymentMethodIds.BankTransfer, new CartLineRequest(product.Id, null, 1));

            var info = result.Payload!.BankTransfer!;
            Assert.Equal("Nuts Beneficiary", info.Beneficiary);
            Assert.Equal("Berry Bank", info.BankName);
            Assert.Equal("BG00TEST", info.Iban);
            Assert.Equal("Use the order reference.", info.AdditionalInfo);
            Assert.Equal("EUR", info.Currency);
            Assert.Equal(20m, info.Amount);
            Assert.Equal(result.Payload.OrderReference, info.Reference);
            _email.Verify(
                service => service.SendEmailAsync("customer@example.com", BankTransferSubject, It.IsAny<string>()),
                Times.Once);
            Assert.NotNull(body);
            Assert.Contains("Bank: <b>Berry Bank</b>", body, StringComparison.Ordinal);
            Assert.Contains("Beneficiary: <b>Nuts Beneficiary</b>", body, StringComparison.Ordinal);
            Assert.Contains("IBAN: <b>BG00TEST</b>", body, StringComparison.Ordinal);
            Assert.Contains($"Reference: <b>{info.Reference}</b>", body, StringComparison.Ordinal);
            Assert.Contains("Use the order reference.", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CashOnDelivery_SendsNoBankTransferEmail()
        {
            var product = ConfigureProduct(price: 20m, quantity: 5);
            ConfigurePaymentMethod(PaymentMethodIds.CashOnDelivery);

            var result = await CheckoutAsync(PaymentMethodIds.CashOnDelivery, new CartLineRequest(product.Id, null, 1));

            Assert.True(result.Success);
            Assert.Null(result.Payload!.BankTransfer);
            _email.Verify(
                service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task StripeAmbiguousFailure_RecordsReconciliationMarkerAndReleasesTheLease()
        {
            var product = ConfigureProduct(price: 15m, quantity: 1);
            ConfigurePaymentMethod(PaymentMethodIds.CreditCard);
            PersistedCheckoutOutcome? marker = null;
            _idempotency.Setup(store => store.SetPendingOutcomeAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PersistedCheckoutOutcome>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, Guid, PersistedCheckoutOutcome, CancellationToken>((_, _, outcome, _) => marker = outcome)
                .ReturnsAsync(true);
            _payment.Setup(service => service.Pay(
                    It.IsAny<StripeCheckoutInitialization>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentInitializationResult(
                    false,
                    ErrorMessage: "Provider response uncertain",
                    FailureKind: PaymentInitializationFailureKind.Ambiguous));

            var result = await CheckoutAsync(PaymentMethodIds.CreditCard, new CartLineRequest(product.Id, null, 1));

            Assert.Equal(CheckoutExecutionStatus.InProgress, result.Status);
            Assert.Contains("requires reconciliation", result.Message, StringComparison.Ordinal);
            Assert.Contains("Provider response uncertain", result.Message, StringComparison.Ordinal);
            Assert.NotNull(marker);
            Assert.Equal(CheckoutExecutionStatus.InProgress, marker!.Status);
            Assert.Equal(ServiceResponseType.Conflict, marker.Response.ResponseType);
            _idempotency.Verify(
                store => store.ReleaseLeaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _idempotency.Verify(
                store => store.CompleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<PersistedCheckoutOutcome>(),
                    It.IsAny<CheckoutIdempotencyState>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task StripeLineTotalDifferingFromTheTransactionAmount_NeverCallsTheProvider()
        {
            var product = ConfigureProduct(price: 10m, quantity: 1);
            ConfigurePaymentMethod(PaymentMethodIds.CreditCard);
            _paymentTransactions.Setup(store => store.GetOrCreateStripeAsync(
                    It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid orderId, long amount, string currency, CancellationToken _) =>
                    new PaymentTransaction
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = PaymentProviderNames.Stripe,
                        ExpectedAmountMinor = amount + 1,
                        Currency = currency,
                    });

            var result = await CheckoutAsync(PaymentMethodIds.CreditCard, new CartLineRequest(product.Id, null, 1));

            Assert.Equal(CheckoutExecutionStatus.InProgress, result.Status);
            Assert.Contains("could not be reconstructed", result.Message, StringComparison.Ordinal);
            _payment.Verify(
                service => service.Pay(
                    It.IsAny<StripeCheckoutInitialization>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private Product ConfigureProduct(decimal price, int quantity)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Product",
                Description = "Description",
                Price = price,
                Quantity = quantity,
            };
            _products.Setup(repository => repository.GetProductsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, Product> { [product.Id] = product });
            return product;
        }

        private void ConfigurePaymentMethod(Guid id)
        {
            _paymentMethods.Setup(service => service.GetPaymentMethodsAsync())
                .ReturnsAsync([new GetPaymentMethod { Id = id, Name = "Payment method" }]);
        }

        private Task<CheckoutExecutionResult> CheckoutAsync(Guid paymentMethodId, params CartLineRequest[] lines)
        {
            return _orchestrator.CheckoutAsync(
                new Checkout { PaymentMethodId = paymentMethodId, Carts = lines },
                "customer-1",
                Guid.NewGuid());
        }

        private void VerifyInventoryWasNotCreated()
        {
            _inventory.Verify(service => service.CreateOrderWithInventoryAsync(
                It.IsAny<Order>(),
                It.IsAny<InventoryReservationStatus>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
