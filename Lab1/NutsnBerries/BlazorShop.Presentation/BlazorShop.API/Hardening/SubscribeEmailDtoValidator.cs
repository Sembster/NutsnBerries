namespace BlazorShop.API.Hardening
{
    using System.Text.RegularExpressions;

    using BlazorShop.API.Controllers;

    using FluentValidation;

    /// <summary>
    /// Newsletter sign-up is an anonymous endpoint that sends an email to whatever address it receives, so the
    /// address must be a plain, bounded mailbox address. The pattern deliberately rejects quotes, angle brackets,
    /// spaces and display-name forms; those are valid in the email standard but only ever useful for abuse here.
    /// </summary>
    public sealed class SubscribeEmailDtoValidator : AbstractValidator<SubscribeEmailDto>
    {
        public const int MaxEmailLength = 254;

        private static readonly Regex PlainMailbox = new(
            @"^[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.\p{L}{2,}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));

        public SubscribeEmailDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .MaximumLength(MaxEmailLength).WithMessage($"Email must be at most {MaxEmailLength} characters.")
                .Must(BeAPlainMailbox).WithMessage("Email is not valid.");
        }

        private static bool BeAPlainMailbox(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return true; // reported by NotEmpty instead
            }

            try
            {
                return PlainMailbox.IsMatch(email.Trim());
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
