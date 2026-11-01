namespace BlazorShop.Tests.Hardening
{
    using BlazorShop.API;
    using BlazorShop.API.Hardening;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;
    using BlazorShop.Storefront.Hardening;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;

    using Xunit;

    public class JwtKeyOptionsValidatorTests
    {
        private readonly JwtKeyOptionsValidator _validator = new();

        private static JwtKeyOptions Valid() => new()
        {
            Key = new string('k', 48),
            Issuer = "https://localhost:7094",
            Audience = "https://localhost:7094",
        };

        [Fact]
        public void StrongKey_Passes() => Assert.True(_validator.Validate(null, Valid()).Succeeded);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<set-in-user-secrets>")]
        public void MissingOrPlaceholderKey_Fails(string? key)
        {
            var options = Valid();
            options.Key = key;

            var result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains("Jwt:Key", result.FailureMessage);
        }

        [Fact]
        public void ShortKey_Fails()
        {
            var options = Valid();
            options.Key = new string('k', JwtKeyOptionsValidator.MinimumKeyBytes - 1);

            Assert.True(_validator.Validate(null, options).Failed);
        }

        [Fact]
        public void KeyOfExactlyTheMinimum_Passes()
        {
            var options = Valid();
            options.Key = new string('k', JwtKeyOptionsValidator.MinimumKeyBytes);

            Assert.True(_validator.Validate(null, options).Succeeded);
        }

        [Fact]
        public void MissingIssuerOrAudience_Fails()
        {
            var options = Valid();
            options.Issuer = "";
            options.Audience = "<placeholder>";

            var result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains("Jwt:Issuer", result.FailureMessage);
            Assert.Contains("Jwt:Audience", result.FailureMessage);
        }
    }

    public class OtherOptionValidatorsTests
    {
        // ---------- Stripe ----------

        [Fact]
        public void Stripe_DisabledNeedsNothing()
        {
            Assert.True(new StripeOptionsValidator().Validate(null, new StripeOptions { Enabled = false }).Succeeded);
        }

        [Fact]
        public void Stripe_EnabledWithRealLookingKeys_Passes()
        {
            var options = new StripeOptions { Enabled = true, SecretKey = "sk_test_abc123", WebhookSecret = "whsec_abc123" };

            Assert.True(new StripeOptionsValidator().Validate(null, options).Succeeded);
        }

        [Theory]
        [InlineData("", "whsec_x")]
        [InlineData("<set-in-user-secrets>", "whsec_x")]
        [InlineData("pk_test_publishable", "whsec_x")]
        [InlineData("sk_test_x", "")]
        [InlineData("sk_test_x", "<set-in-user-secrets>")]
        [InlineData("sk_test_x", "not-a-webhook-secret")]
        public void Stripe_EnabledWithBadKeys_Fails(string secret, string webhook)
        {
            var options = new StripeOptions { Enabled = true, SecretKey = secret, WebhookSecret = webhook };

            Assert.True(new StripeOptionsValidator().Validate(null, options).Failed);
        }

        // ---------- bank transfer ----------

        private readonly BankTransferSettingsValidator _bank = new();

        [Fact]
        public void BankTransfer_EmptySectionIsAllowed()
        {
            Assert.True(_bank.Validate(null, new BankTransferSettings()).Succeeded);
        }

        [Fact]
        public void BankTransfer_ShippedSampleValuesPass()
        {
            var sample = new BankTransferSettings
            {
                Iban = "BG00UNCR70001512345678",
                Beneficiary = "NutsnBerries Ltd.",
                BankName = "Unicredit Bulbank",
                AdditionalInfo = "Include the reference in the transfer reason.",
            };

            Assert.True(_bank.Validate(null, sample).Succeeded);
        }

        [Fact]
        public void BankTransfer_IbanWithSpacesAndLowercasePasses()
        {
            var settings = new BankTransferSettings { Iban = "se45 5000 0000 0583 9825 7466", Beneficiary = "B", BankName = "N" };

            Assert.True(_bank.Validate(null, settings).Succeeded);
        }

        [Theory]
        [InlineData("not an iban", "Beneficiary", "Bank")]
        [InlineData("SE45", "Beneficiary", "Bank")]
        [InlineData("<your-iban>", "Beneficiary", "Bank")]
        [InlineData("SE4550000000058398257466", "", "Bank")]
        [InlineData("SE4550000000058398257466", "Beneficiary", "<bank>")]
        [InlineData("", "Beneficiary", "Bank")]
        public void BankTransfer_PartialOrBadValuesFail(string iban, string beneficiary, string bankName)
        {
            var settings = new BankTransferSettings { Iban = iban, Beneficiary = beneficiary, BankName = bankName };

            Assert.True(_bank.Validate(null, settings).Failed);
        }

        // ---------- recommendations ----------

        [Fact]
        public void Recommendations_DefaultsPass()
        {
            Assert.True(new RecommendationOptionsValidator().Validate(null, new RecommendationOptions()).Succeeded);
        }

        [Theory]
        [InlineData(0, 1, 30, 5)]
        [InlineData(51, 1, 30, 5)]
        [InlineData(6, 0, 30, 5)]
        [InlineData(6, 1, 0, 5)]
        [InlineData(6, 1, 30, -1)]
        public void Recommendations_OutOfRangeFails(int max, int cacheHours, int slidingMinutes, int minOrders)
        {
            var options = new RecommendationOptions
            {
                MaxRecommendations = max,
                CacheDurationHours = cacheHours,
                SlidingExpirationMinutes = slidingMinutes,
                MinimumOrderCount = minOrders,
            };

            Assert.True(new RecommendationOptionsValidator().Validate(null, options).Failed);
        }

        // ---------- Storefront demo options ----------

        [Fact]
        public void StorefrontDemo_DefaultsPass()
        {
            Assert.True(new StorefrontDemoOptionsValidator().Validate(null, new DemoOptions()).Succeeded);
        }

        [Theory]
        [InlineData("", "X-Demo", 30, 20)]
        [InlineData("demo", "", 30, 20)]
        [InlineData("demo", "X-Demo", 4, 20)]
        [InlineData("demo", "X-Demo", 121, 20)]
        [InlineData("demo", "X-Demo", 30, 0)]
        [InlineData("demo", "X-Demo", 30, 101)]
        public void StorefrontDemo_BadValuesFail(string cookie, string header, int lifetime, int sessions)
        {
            var options = new DemoOptions { CookieName = cookie, HeaderName = header, LifetimeMinutes = lifetime, MaxConcurrentSessions = sessions };

            Assert.True(new StorefrontDemoOptionsValidator().Validate(null, options).Failed);
        }
    }

    /// <summary>
    /// Proves the registration wires validation into host startup, not just that the validators work in isolation.
    /// </summary>
    public class HardeningRegistrationTests
    {
        private static ServiceProvider Build(Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();

            // Stand-ins for what AddInfrastructure / AddApplication already bind in the real host.
            services.Configure<StripeOptions>(configuration.GetSection("Stripe"));
            services.Configure<BankTransferSettings>(configuration.GetSection("BankTransfer"));
            services.Configure<RecommendationOptions>(configuration.GetSection("Recommendations"));
            services.AddNutsnBerriesHardening(configuration);

            return services.BuildServiceProvider();
        }

        private static Dictionary<string, string?> GoodSettings() => new()
        {
            ["Jwt:Key"] = new string('k', 64),
            ["Jwt:Issuer"] = "https://localhost:7094",
            ["Jwt:Audience"] = "https://localhost:7094",
        };

        [Fact]
        public void PlaceholderJwtKey_StopsStartup()
        {
            var settings = GoodSettings();
            settings["Jwt:Key"] = "<set-in-user-secrets>";
            using var provider = Build(settings);

            var exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains("Jwt:Key", exception.Message);
        }

        [Fact]
        public void MissingJwtSection_StopsStartup()
        {
            using var provider = Build(new Dictionary<string, string?>());

            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());
        }

        [Fact]
        public void EnabledStripeWithoutKeys_StopsStartup()
        {
            var settings = GoodSettings();
            settings["Stripe:Enabled"] = "true";
            using var provider = Build(settings);

            var exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains("Stripe", exception.Message);
        }

        [Fact]
        public void ValidConfiguration_StartsCleanly()
        {
            using var provider = Build(GoodSettings());

            provider.GetRequiredService<IStartupValidator>().Validate();
        }

        [Fact]
        public void StorefrontRegistration_ValidatesDemoOptionsAtStartup()
        {
            var services = new ServiceCollection();
            services.Configure<DemoOptions>(o => o.CookieName = "");
            services.AddNutsnBerriesStorefrontOptionValidation();
            using var provider = services.BuildServiceProvider();

            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());
        }
    }
}
