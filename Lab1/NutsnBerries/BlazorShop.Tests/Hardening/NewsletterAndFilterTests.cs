namespace BlazorShop.Tests.Hardening
{
    using BlazorShop.API.Controllers;
    using BlazorShop.API.Hardening;
    using BlazorShop.Application.DTOs;
    using BlazorShop.Application.DTOs.Product;
    using BlazorShop.Application.Validations.Catalog;

    using FluentValidation;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Abstractions;
    using Microsoft.AspNetCore.Mvc.Controllers;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;

    using Xunit;

    public class NewsletterValidatorTests
    {
        private readonly SubscribeEmailDtoValidator _validator = new();

        [Theory]
        [InlineData("anna@example.com")]
        [InlineData("anna.svensson+news@example.se")]
        [InlineData("  anna@example.com  ")]
        [InlineData("åsa@exempel.se")]
        public void PlainAddressesPass(string email)
        {
            Assert.True(_validator.Validate(new SubscribeEmailDto { Email = email }).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-email")]
        [InlineData("a@b")]
        [InlineData("<img src=x onerror=alert(1)>@x.com")]
        [InlineData("\"quoted\"@example.com")]
        [InlineData("Anna <anna@example.com>")]
        [InlineData("anna@exa mple.com")]
        [InlineData("anna@example.com\r\nBcc: victim@example.com")]
        public void MalformedOrAbusiveAddressesFail(string email)
        {
            Assert.False(_validator.Validate(new SubscribeEmailDto { Email = email }).IsValid);
        }

        [Fact]
        public void OverlongAddressFails()
        {
            var email = new string('a', SubscribeEmailDtoValidator.MaxEmailLength) + "@example.com";

            Assert.False(_validator.Validate(new SubscribeEmailDto { Email = email }).IsValid);
        }
    }

    public class FluentValidationActionFilterTests
    {
        private static ActionExecutingContext CreateContext(IServiceProvider services, params object?[] arguments)
        {
            var httpContext = new DefaultHttpContext { RequestServices = services };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
            var args = arguments.Select((value, index) => (name: $"arg{index}", value))
                                .ToDictionary(item => item.name, item => item.value);

            return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, controller: new object());
        }

        private static ServiceProvider ProviderWithCatalogValidators()
        {
            var services = new ServiceCollection();
            services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();
            services.AddScoped<IValidator<SubscribeEmailDto>, SubscribeEmailDtoValidator>();
            return services.BuildServiceProvider();
        }

        private static CreateProduct ValidProduct() => new()
        {
            Name = "Serum",
            Description = "Light serum.",
            Price = 100m,
            Quantity = 0,
            Image = "/x.png",
            CategoryId = Guid.NewGuid(),
        };

        [Fact]
        public async Task ValidArgument_ContinuesToTheAction()
        {
            using var provider = ProviderWithCatalogValidators();
            var context = CreateContext(provider, ValidProduct());
            var nextCalled = false;

            await new FluentValidationActionFilter().OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task InvalidArgument_ShortCircuitsWithServiceResponse()
        {
            using var provider = ProviderWithCatalogValidators();
            var product = ValidProduct();
            product.Price = -5m;
            product.Name = "";
            var context = CreateContext(provider, product);
            var nextCalled = false;

            await new FluentValidationActionFilter().OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

            Assert.False(nextCalled);
            var bad = Assert.IsType<BadRequestObjectResult>(context.Result);
            var response = Assert.IsType<ServiceResponse>(bad.Value);
            Assert.False(response.Success);
            Assert.Contains("Product name is required.", response.Message);
            Assert.Contains("Price must be greater than 0.", response.Message);
            var details = Assert.IsAssignableFrom<IDictionary<string, string[]>>(response.Payload);
            Assert.Contains("Price", details.Keys);
        }

        [Fact]
        public async Task ArgumentsWithoutValidator_PassThrough()
        {
            using var provider = ProviderWithCatalogValidators();
            var context = CreateContext(provider, Guid.NewGuid(), "plain string", null);
            var nextCalled = false;

            await new FluentValidationActionFilter().OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task NewsletterBody_IsValidatedThroughTheSameFilter()
        {
            using var provider = ProviderWithCatalogValidators();
            var context = CreateContext(provider, new SubscribeEmailDto { Email = "<script>@x.com" });

            await new FluentValidationActionFilter().OnActionExecutionAsync(
                context,
                () => Task.FromResult<ActionExecutedContext>(null!));

            Assert.IsType<BadRequestObjectResult>(context.Result);
        }
    }
}
