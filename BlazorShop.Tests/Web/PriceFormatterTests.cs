namespace BlazorShop.Tests.Web
{
    using BlazorShop.Web.Shared.Helper;

    using Xunit;

    public sealed class PriceFormatterTests
    {
        [Theory]
        [InlineData(249.5, "SEK", "249,50\u00A0kr")]
        [InlineData(1249.5, "SEK", "1\u00A0249,50\u00A0kr")]
        [InlineData(12.3, "EUR", "€\u00A012.30")]
        [InlineData(5, "usd", "$5.00")]
        [InlineData(9, "GBP", "£\u00A09.00")]
        public void Format_UsesCurrencySpecificLayout(double amount, string currency, string expected)
        {
            Assert.Equal(expected, PriceFormatter.Format((decimal)amount, currency));
        }
    }
}
