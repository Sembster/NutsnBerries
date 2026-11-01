namespace BlazorShop.API
{
    using BlazorShop.API.Hardening;
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Application.Options;

    using FluentValidation;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Single registration point for the NutsnBerries input and configuration hardening. Program.cs calls this
    /// once; everything else lives in new files, so upstream's controllers and services stay untouched.
    /// </summary>
    public static class NutsnBerriesHardeningExtensions
    {
        public static IServiceCollection AddNutsnBerriesHardening(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Request validation: product, category and variant validators live in the Application assembly and are
            // already registered by AddApplication; this registers the API-side ones (newsletter) and the filter
            // that runs every matching validator before the controller action.
            services.AddValidatorsFromAssemblyContaining<SubscribeEmailDtoValidator>();
            services.Configure<MvcOptions>(options => options.Filters.Add<FluentValidationActionFilter>());

            // Startup validation: the host refuses to start with a bad value instead of failing later or, for the
            // JWT key, silently signing tokens with a public placeholder.
            services.AddOptions<JwtKeyOptions>()
                .Bind(configuration.GetSection(JwtKeyOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<JwtKeyOptions>, JwtKeyOptionsValidator>();

            AddValidatedOptions<StripeOptions, StripeOptionsValidator>(services);
            AddValidatedOptions<BankTransferSettings, BankTransferSettingsValidator>(services);
            AddValidatedOptions<RecommendationOptions, RecommendationOptionsValidator>(services);

            return services;
        }

        /// <summary>
        /// For options that upstream already binds: attach a validator and make it run at startup.
        /// </summary>
        private static void AddValidatedOptions<TOptions, TValidator>(IServiceCollection services)
            where TOptions : class
            where TValidator : class, IValidateOptions<TOptions>
        {
            services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
            services.AddOptions<TOptions>().ValidateOnStart();
        }
    }
}
