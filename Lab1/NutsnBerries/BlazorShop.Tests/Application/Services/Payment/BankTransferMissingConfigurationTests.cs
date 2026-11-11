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
    /// Documents what a bank-transfer checkout does today when the bank details are missing (upstream issue #130:
    /// a hardcoded IBAN is invented, shown and emailed, and the order is created and stock reserved anyway).
    /// These tests assert the CURRENT, defective behavior so it is reproducible; when the defect is fixed they are
    /// meant to be replaced by tests that expect the checkout to be refused.
    /// </summary>
    public sealed class BankTransferMissingConfigurationTests
    {
        private const string BuiltInIban = "BG00UNCR70001512345678";

        private readonly Mock<IProductReadRepository> _products = new();
        private readonly Mock<IPaymentMethodService> _paymentMethods = new();
        private readonly Mock<IInventoryReservationService> _inventory = new();
        private readonly Mock<IEmailService> _email = new();

        [Fact]
        public async Task AllBankDetailsMissing_StillCreatesTheOrderAndReturnsTheBuiltInIban()
        {
            var orchestrator = CreateOrchestrator(new BankTransferSettings());
            string? body = null;
            CaptureEmailBody(html => body = html);

            var result = await CheckoutBankTransferAsync(orchestrator);

            Assert.True(result.Success);
            var info = result.Payload!.BankTransfer!;
            Assert.Equal(BuiltInIban, info.Iban);
            Assert.Equal(string.Empty, info.Beneficiary);
            Assert.Equal(string.Empty, info.BankName);
            Assert.NotNull(body);
            Assert.Contains($"IBAN: <b>{BuiltInIban}</b>", body, StringComparison.Ordinal);
            Assert.Contains("Bank: <b></b>", body, StringComparison.Ordinal);
            Assert.Contains("Beneficiary: <b></b>", body, StringComparison.Ordinal);
            _inventory.Verify(service => service.CreateOrderWithInventoryAsync(
                It.IsAny<Order>(),
                InventoryReservationStatus.Reserved,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnlyTheIbanMissing_PairsTheBuiltInIbanWithTheConfiguredBeneficiaryAndBank()
        {
            var orchestrator = CreateOrchestrator(new BankTransferSettings
            {
                Iban = string.Empty,
                Beneficiary = "Nuts Beneficiary",
                BankName = "Berry Bank",
            });
            string? body = null;
            CaptureEmailBody(html => body = html);

            var result = await CheckoutBankTransferAsync(orchestrator);

            Assert.True(result.Success);
            var info = result.Payload!.BankTransfer!;
            Assert.Equal(BuiltInIban, info.Iban);
            Assert.Equal("Nuts Beneficiary", info.Beneficiary);
            Assert.Equal("Berry Bank", info.BankName);
            Assert.NotNull(body);
            Assert.Contains("Beneficiary: <b>Nuts Beneficiary</b>", body, StringComparison.Ordinal);
            Assert.Contains($"IBAN: <b>{BuiltInIban}</b>", body, StringComparison.Ordinal);
        }

        private void CaptureEmailBody(Action<string> capture)
        {
            _email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, _, html) => capture(html))
                .Returns(Task.CompletedTask);
        }

        private Task<CheckoutExecutionResult> CheckoutBankTransferAsync(CheckoutOrchestrator orchestrator)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Product",
                Description = "Description",
                Price = 20m,
                Quantity = 5,
            };
            _products.Setup(repository => repository.GetProductsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, Product> { [product.Id] = product });
            _paymentMethods.Setup(service => service.GetPaymentMethodsAsync())
                .ReturnsAsync([new GetPaymentMethod { Id = PaymentMethodIds.BankTransfer, Name = "Bank Transfer" }]);

            return orchestrator.CheckoutAsync(
                new Checkout
                {
                    PaymentMethodId = PaymentMethodIds.BankTransfer,
                    Carts = [new CartLineRequest(product.Id, null, 1)],
                },
                "customer-1",
                Guid.NewGuid());
        }

        private CheckoutOrchestrator CreateOrchestrator(BankTransferSettings bankTransfer)
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

            var idempotency = new Mock<ICheckoutIdempotencyStore>();
            idempotency.Setup(store => store.ClaimAsync(
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
                            OrderReference = $"BT-{Guid.NewGuid():N}",
                            LeaseOwnerId = ownerId,
                        },
                        ownerId);
                });
            idempotency.Setup(store => store.SetPendingOutcomeAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PersistedCheckoutOutcome>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            idempotency.Setup(store => store.CompleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<PersistedCheckoutOutcome>(),
                    It.IsAny<CheckoutIdempotencyState>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            return new CheckoutOrchestrator(
                _products.Object,
                _paymentMethods.Object,
                Mock.Of<IPaymentService>(),
                users.Object,
                _inventory.Object,
                idempotency.Object,
                Mock.Of<IPaymentTransactionStore>(),
                Mock.Of<IStripePaymentStateTransitionService>(),
                orders.Object,
                _email.Object,
                Options.Create(bankTransfer),
                Options.Create(new ClientAppOptions { BaseUrl = "https://shop.test" }),
                Options.Create(new CheckoutIdempotencyOptions()),
                Options.Create(new CommerceOptions { Currency = "EUR" }),
                Mock.Of<ILogger<CheckoutOrchestrator>>());
        }
    }
}
