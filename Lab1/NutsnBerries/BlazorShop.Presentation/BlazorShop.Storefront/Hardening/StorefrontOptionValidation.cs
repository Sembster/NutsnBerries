namespace BlazorShop.Storefront.Hardening
{
    using BlazorShop.Application.Options;

    using Microsoft.Extensions.Options;

    /// <summary>
    /// The Storefront binds <see cref="DemoOptions"/> without any check (the API validates the same type). It only
    /// needs the cookie and header names, but a blank name would silently disable demo-session forwarding, so the
    /// same bounds the API enforces are applied here.
    /// </summary>
    public sealed class StorefrontDemoOptionsValidator : IValidateOptions<DemoOptions>
    {
        public ValidateOptionsResult Validate(string? name, DemoOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.CookieName))
            {
                failures.Add("Demo:CookieName is required.");
            }

            if (string.IsNullOrWhiteSpace(options.HeaderName))
            {
                failures.Add("Demo:HeaderName is required.");
            }

            if (options.LifetimeMinutes is < 5 or > 120)
            {
                failures.Add("Demo:LifetimeMinutes must be between 5 and 120.");
            }

            if (options.MaxConcurrentSessions is < 1 or > 100)
            {
                failures.Add("Demo:MaxConcurrentSessions must be between 1 and 100.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    using BlazorShop.Application.Options;
    using BlazorShop.Storefront.Hardening;

    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registered from one line in the Storefront's Program.cs. Lives in the DependencyInjection namespace so
    /// that line needs no extra using.
    /// </summary>
    public static class NutsnBerriesStorefrontHardeningExtensions
    {
        public static IServiceCollection AddNutsnBerriesStorefrontOptionValidation(this IServiceCollection services)
        {
            services.AddSingleton<IValidateOptions<DemoOptions>, StorefrontDemoOptionsValidator>();
            services.AddOptions<DemoOptions>().ValidateOnStart();
            return services;
        }
    }
}
