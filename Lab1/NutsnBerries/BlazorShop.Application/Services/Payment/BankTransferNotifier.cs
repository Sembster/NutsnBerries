namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Domain.Contracts;
    using BlazorShop.Domain.Entities.Identity;
    using BlazorShop.Domain.Entities.Payment;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Builds the bank-transfer instructions for an order and emails them to the customer.
    /// A failed email never invalidates the committed order.
    /// </summary>
    internal sealed class BankTransferNotifier
    {
        private readonly IEmailService _emailService;
        private readonly BankTransferSettings _bankTransferSettings;
        private readonly ILogger _logger;

        public BankTransferNotifier(
            IEmailService emailService,
            IOptions<BankTransferSettings> bankTransferOptions,
            ILogger logger)
        {
            _emailService = emailService;
            _bankTransferSettings = bankTransferOptions.Value;
            _logger = logger;
        }

        internal BankTransferInfo BuildInfo(Order order) => new()
        {
            Iban = string.IsNullOrWhiteSpace(_bankTransferSettings.Iban)
                ? "BG00UNCR70001512345678"
                : _bankTransferSettings.Iban,
            Beneficiary = _bankTransferSettings.Beneficiary,
            BankName = _bankTransferSettings.BankName,
            Reference = order.Reference,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            AdditionalInfo = _bankTransferSettings.AdditionalInfo,
        };

        internal async Task TrySendEmailAsync(AppUser customer, BankTransferInfo info)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(customer.Email))
                {
                    return;
                }

                var html = $@"<p>Thank you for your order.</p>
<p>Please make a bank transfer to the following account:</p>
<ul>
<li>Bank: <b>{info.BankName}</b></li>
<li>Beneficiary: <b>{info.Beneficiary}</b></li>
<li>IBAN: <b>{info.Iban}</b></li>
<li>Amount: <b>{info.Amount:F2} {info.Currency}</b></li>
<li>Reference: <b>{info.Reference}</b></li>
</ul>
<p>{info.AdditionalInfo}</p>
<p>Your order will be processed once we receive the payment.</p>";
                await _emailService.SendEmailAsync(customer.Email, "Bank Transfer Instructions", html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Order {OrderReference} was created, but its bank-transfer email could not be sent.",
                    info.Reference);
            }
        }
    }
}
