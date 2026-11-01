namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Domain.Entities.Identity;
    using BlazorShop.Domain.Entities.Payment;

    /// <summary>
    /// Builds the <see cref="Order"/> aggregate from resolved cart lines. Pure: no services, no I/O.
    /// </summary>
    internal static class CheckoutOrderFactory
    {
        internal static Order CreateOrder(
            IReadOnlyCollection<ResolvedCartLine> lines,
            string userId,
            Guid orderId,
            string orderReference,
            CheckoutPaymentKind paymentKind,
            string currency,
            AppUser customer)
        {
            var normalizedCurrency = CurrencyMoney.NormalizeCurrency(currency);
            var orderLines = lines.Select(line =>
            {
                var unitPrice = CurrencyMoney.NormalizeAmount(line.UnitPrice, normalizedCurrency);
                var lineTotal = checked(unitPrice * line.Quantity);
                return new OrderLine
                {
                    ProductId = line.ProductId,
                    ProductVariantId = line.VariantId,
                    ProductNameSnapshot = line.ProductName,
                    SkuSnapshot = line.Sku,
                    SizeScaleSnapshot = line.SizeScale?.ToString(),
                    SizeValueSnapshot = line.SizeValue,
                    ColorSnapshot = line.Color,
                    Quantity = line.Quantity,
                    UnitPrice = unitPrice,
                    LineTotal = lineTotal,
                };
            }).ToList();
            var totalAmount = orderLines.Aggregate(0m, (total, line) => checked(total + line.LineTotal));
            _ = CurrencyMoney.ToMinorUnits(totalAmount, normalizedCurrency);
            return new Order
            {
                Id = orderId,
                UserId = userId,
                OrderStatus = paymentKind == CheckoutPaymentKind.Stripe
                    ? OrderStatus.Pending
                    : OrderStatus.Confirmed,
                PaymentStatus = OrderPaymentStatus.Pending,
                PaymentMethod = paymentKind switch
                {
                    CheckoutPaymentKind.CashOnDelivery => OrderPaymentMethod.CashOnDelivery,
                    CheckoutPaymentKind.BankTransfer => OrderPaymentMethod.BankTransfer,
                    CheckoutPaymentKind.Stripe => OrderPaymentMethod.Stripe,
                    _ => throw new ArgumentOutOfRangeException(nameof(paymentKind), paymentKind, null),
                },
                FulfillmentStatus = FulfillmentStatus.NotStarted,
                Reference = orderReference,
                TotalAmount = totalAmount,
                SubtotalAmount = totalAmount,
                DiscountAmount = 0m,
                ShippingAmount = 0m,
                TaxAmount = 0m,
                Currency = normalizedCurrency,
                CustomerNameSnapshot = string.IsNullOrWhiteSpace(customer.FullName)
                    ? customer.UserName
                    : customer.FullName,
                CustomerEmailSnapshot = customer.Email,
                Lines = orderLines,
            };
        }
    }
}
