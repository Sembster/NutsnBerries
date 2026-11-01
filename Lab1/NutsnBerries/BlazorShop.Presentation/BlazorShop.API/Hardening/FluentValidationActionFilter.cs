namespace BlazorShop.API.Hardening
{
    using BlazorShop.Application.DTOs;

    using FluentValidation;
    using FluentValidation.Results;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;

    /// <summary>
    /// Validates request bodies before a controller action runs: for every action argument it looks up a
    /// registered <see cref="IValidator{T}"/> for the argument's exact type and, if one exists, runs it.
    /// Arguments without a validator (route ids, types validated elsewhere) pass through untouched.
    /// </summary>
    /// <remarks>
    /// Failures are returned as the same <see cref="ServiceResponse"/> shape the services use, so the Web client
    /// shows them like any other failed operation. ASP.NET's built-in DataAnnotations check (<c>[ApiController]</c>)
    /// still runs first and is unchanged; this filter adds the rules DataAnnotations cannot express.
    /// This is a new extension: no upstream controller or service was edited to add validation.
    /// </remarks>
    public sealed class FluentValidationActionFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var failures = new List<ValidationFailure>();

            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is null)
                {
                    continue;
                }

                var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
                if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                {
                    continue;
                }

                var result = await validator.ValidateAsync(
                    new ValidationContext<object>(argument),
                    context.HttpContext.RequestAborted);

                failures.AddRange(result.Errors);
            }

            if (failures.Count == 0)
            {
                await next();
                return;
            }

            var message = string.Join(" ", failures.Select(failure => failure.ErrorMessage).Distinct());

            context.Result = new BadRequestObjectResult(new ServiceResponse(false, message)
            {
                Payload = failures
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray()),
            });
        }
    }
}
