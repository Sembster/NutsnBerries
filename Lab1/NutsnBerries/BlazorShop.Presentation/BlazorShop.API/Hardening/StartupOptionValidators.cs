namespace BlazorShop.API.Hardening
{
    using System.Text;
    using System.Text.RegularExpressions;

    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;

    using Microsoft.Extensions.Options;

    /// <summary>
    /// Settings read from the <c>Jwt</c> section. Upstream reads these keys straight from configuration without
    /// checking them, so a missing secret silently fell back to the committed placeholder text as the signing key.
    /// </summary>
    public sealed class JwtKeyOptions
    {
        public const string SectionName = "Jwt";

        public string? Key { get; set; }

        public string? Issuer { get; set; }

        public string? Audience { get; set; }
    }

    /// <summary>
    /// Fails startup when the JWT signing key is missing, still a <c>&lt;placeholder&gt;</c>, or too short.
    /// </summary>
    public sealed class JwtKeyOptionsValidator : IValidateOptions<JwtKeyOptions>
    {
        /// <summary>HS256 needs at least 128 bits; 256 bits (32 bytes) is the practical minimum.</summary>
        public const int MinimumKeyBytes = 32;

        public ValidateOptionsResult Validate(string? name, JwtKeyOptions options)
        {
            var failures = new List<string>();

            if (!ConfigValueRules.IsUsable(options.Key))
            {
                failures.Add(
                    "Jwt:Key is missing or still the placeholder value. Set it with "
                    + "'dotnet user-secrets set \"Jwt:Key\" <random secret of 32 or more characters>' or the Jwt__Key environment variable.");
            }
            else if (Encoding.UTF8.GetByteCount(options.Key!) < MinimumKeyBytes)
            {
                failures.Add($"Jwt:Key must be at least {MinimumKeyBytes} characters long (256 bits).");
            }

            if (!ConfigValueRules.IsUsable(options.Issuer))
            {
                failures.Add("Jwt:Issuer is required.");
            }

            if (!ConfigValueRules.IsUsable(options.Audience))
            {
                failures.Add("Jwt:Audience is required.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }

    /// <summary>
    /// Stripe is optional (<c>Stripe:Enabled</c> is false by default). When it is switched on, both secrets must be
    /// present, not placeholders, and look like Stripe keys.
    /// </summary>
    public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
    {
        public ValidateOptionsResult Validate(string? name, StripeOptions options)
        {
            if (!options.Enabled)
            {
                return ValidateOptionsResult.Success;
            }

            var failures = new List<string>();

            if (!ConfigValueRules.IsUsable(options.SecretKey))
            {
                failures.Add("Stripe:SecretKey is required when Stripe:Enabled is true.");
            }
            else if (!options.SecretKey.StartsWith("sk_", StringComparison.Ordinal)
                     && !options.SecretKey.StartsWith("rk_", StringComparison.Ordinal))
            {
                failures.Add("Stripe:SecretKey must be a Stripe secret or restricted key (starts with sk_ or rk_).");
            }

            if (!ConfigValueRules.IsUsable(options.WebhookSecret))
            {
                failures.Add("Stripe:WebhookSecret is required when Stripe:Enabled is true.");
            }
            else if (!options.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            {
                failures.Add("Stripe:WebhookSecret must be a Stripe webhook signing secret (starts with whsec_).");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }

    /// <summary>
    /// Bank transfer details are shown to customers who pay by transfer. Leaving the whole section empty is allowed
    /// (the method is simply not configured); a partly filled or placeholder section is rejected. The IBAN is
    /// checked for shape only: the sample value shipped in the repository does not pass the mod-97 checksum.
    /// </summary>
    public sealed class BankTransferSettingsValidator : IValidateOptions<BankTransferSettings>
    {
        private static readonly Regex IbanShape = new(
            "^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));

        public ValidateOptionsResult Validate(string? name, BankTransferSettings options)
        {
            var anyValue = !string.IsNullOrWhiteSpace(options.Iban)
                           || !string.IsNullOrWhiteSpace(options.Beneficiary)
                           || !string.IsNullOrWhiteSpace(options.BankName);

            if (!anyValue)
            {
                return ValidateOptionsResult.Success;
            }

            var failures = new List<string>();

            if (!ConfigValueRules.IsUsable(options.Beneficiary))
            {
                failures.Add("BankTransfer:Beneficiary is required when bank transfer details are configured.");
            }

            if (!ConfigValueRules.IsUsable(options.BankName))
            {
                failures.Add("BankTransfer:BankName is required when bank transfer details are configured.");
            }

            if (!ConfigValueRules.IsUsable(options.Iban))
            {
                failures.Add("BankTransfer:Iban is required when bank transfer details are configured.");
            }
            else if (!IbanShape.IsMatch(options.Iban.Replace(" ", string.Empty).ToUpperInvariant()))
            {
                failures.Add("BankTransfer:Iban does not look like an IBAN (two letters, two digits, then 11 to 30 letters or digits).");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }

    /// <summary>
    /// Keeps the recommendation tuning numbers inside sane ranges so a typo cannot produce a zero-length cache or
    /// an enormous query.
    /// </summary>
    public sealed class RecommendationOptionsValidator : IValidateOptions<RecommendationOptions>
    {
        public ValidateOptionsResult Validate(string? name, RecommendationOptions options)
        {
            var failures = new List<string>();

            if (options.MaxRecommendations is < 1 or > 50)
            {
                failures.Add("Recommendations:MaxRecommendations must be between 1 and 50.");
            }

            if (options.CacheDurationHours is < 1 or > 168)
            {
                failures.Add("Recommendations:CacheDurationHours must be between 1 and 168.");
            }

            if (options.SlidingExpirationMinutes is < 1 or > 1440)
            {
                failures.Add("Recommendations:SlidingExpirationMinutes must be between 1 and 1440.");
            }

            if (options.MinimumOrderCount < 0)
            {
                failures.Add("Recommendations:MinimumOrderCount cannot be negative.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
