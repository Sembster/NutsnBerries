namespace BlazorShop.Web.Shared.Helper
{
    using System.Globalization;

    /// <summary>
    /// Formats prices for display in the storefront and the Blazor client.
    /// Configured once at startup from <c>Commerce:Currency</c>. Number formats are built by hand
    /// so the output does not depend on the ICU/culture data available in the host (WASM, Docker).
    /// </summary>
    public static class PriceFormatter
    {
        private static readonly NumberFormatInfo Swedish = new()
        {
            NumberDecimalSeparator = ",",
            NumberGroupSeparator = "\u00A0",
            NumberDecimalDigits = 2,
        };

        public static string CurrencyCode { get; private set; } = "SEK";

        public static void Configure(string? currencyCode)
        {
            if (!string.IsNullOrWhiteSpace(currencyCode))
            {
                CurrencyCode = currencyCode.Trim().ToUpperInvariant();
            }
        }

        public static string Format(decimal amount, string? currencyCode = null)
        {
            var code = string.IsNullOrWhiteSpace(currencyCode)
                ? CurrencyCode
                : currencyCode.Trim().ToUpperInvariant();

            return code switch
            {
                "SEK" => amount.ToString("N2", Swedish) + "\u00A0kr",
                "EUR" => "€\u00A0" + amount.ToString("N2", CultureInfo.InvariantCulture),
                "GBP" => "£\u00A0" + amount.ToString("N2", CultureInfo.InvariantCulture),
                "USD" => "$" + amount.ToString("N2", CultureInfo.InvariantCulture),
                _ => code + "\u00A0" + amount.ToString("N2", CultureInfo.InvariantCulture),
            };
        }
    }
}
