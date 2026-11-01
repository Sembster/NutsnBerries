namespace BlazorShop.Tests.Presentation.Storefront
{
    using Xunit;

    /// <summary>
    /// Guards the NutsnBerries theme extension: the theme lives in its own stylesheet that is loaded after
    /// upstream's storefront.css, so upstream's file can stay untouched.
    /// </summary>
    public class NutsnBerriesThemeTests
    {
        private const string StorefrontRoot = "BlazorShop.Presentation/BlazorShop.Storefront";

        [Fact]
        public void ThemeStylesheet_IsLinkedAfterUpstreamStorefrontCss()
        {
            var app = ReadRepositoryFile($"{StorefrontRoot}/App.razor");

            var upstreamIndex = app.IndexOf("css/storefront.css", StringComparison.Ordinal);
            var themeIndex = app.IndexOf("css/nutsnberries-theme.css", StringComparison.Ordinal);

            Assert.True(upstreamIndex >= 0, "App.razor must still link upstream's storefront.css.");
            Assert.True(themeIndex > upstreamIndex, "The theme must be linked after storefront.css so it wins the cascade.");
        }

        [Fact]
        public void ThemeStylesheet_DefinesTheNutsnBerriesPalette()
        {
            var theme = ReadRepositoryFile($"{StorefrontRoot}/wwwroot/css/nutsnberries-theme.css");

            Assert.Contains("--nb-forest: #1e3428;", theme);
            Assert.Contains("--nb-berry: #b83b5e;", theme);
            Assert.Contains(".bs-storefront-header__shell::before", theme);
            Assert.Contains("rgba(30, 52, 40, 0.98)", theme);
            Assert.Contains(".bs-storefront-deal-button", theme);
        }

        [Fact]
        public void ThemeStylesheet_RespectsTheUpstreamGradientDesignGuard()
        {
            // Upstream's header test forbids 135deg gradients in storefront.css; keep the theme consistent with it.
            var theme = ReadRepositoryFile($"{StorefrontRoot}/wwwroot/css/nutsnberries-theme.css");

            Assert.DoesNotContain("linear-gradient(135deg", theme);
        }

        private static string ReadRepositoryFile(string relativePath)
        {
            return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BlazorShop.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate BlazorShop.sln from the test output directory.");
        }
    }
}
