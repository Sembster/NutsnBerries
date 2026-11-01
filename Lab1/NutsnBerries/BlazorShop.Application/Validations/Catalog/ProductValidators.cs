namespace BlazorShop.Application.Validations.Catalog
{
    using BlazorShop.Application.DTOs.Product;
    using BlazorShop.Application.Validations.Seo;
    using BlazorShop.Domain.Constants;

    using FluentValidation;

    /// <summary>
    /// Rules shared by creating and updating a product. The same limits appear in the admin UI messages.
    /// </summary>
    public sealed class ProductBaseValidator : AbstractValidator<ProductBase>
    {
        public ProductBaseValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Product name is required.")
                .MaximumLength(CatalogConstraints.ProductNameMaxLength)
                .WithMessage($"Product name must be at most {CatalogConstraints.ProductNameMaxLength} characters.");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Product description is required.")
                .MaximumLength(CatalogConstraints.ProductDescriptionMaxLength)
                .WithMessage($"Product description must be at most {CatalogConstraints.ProductDescriptionMaxLength} characters.");

            // [Required] on a decimal is a no-op, so the range is enforced here instead.
            RuleFor(x => x.Price)
                .GreaterThan(0m).WithMessage("Price must be greater than 0.")
                .LessThanOrEqualTo(CatalogConstraints.MaxPrice)
                .WithMessage($"Price must be at most {CatalogConstraints.MaxPrice:N0}.");

            RuleFor(x => x.Quantity)
                .InclusiveBetween(0, CatalogConstraints.MaxStock)
                .WithMessage($"Quantity must be between 0 and {CatalogConstraints.MaxStock:N0}.");

            RuleFor(x => x.Image)
                .NotEmpty().WithMessage("Product image is required.")
                .MaximumLength(SeoConstraints.UrlMaxLength)
                .WithMessage($"Image address must be at most {SeoConstraints.UrlMaxLength} characters.")
                .Must(SeoValidationRules.BeAbsoluteUrlOrRootRelativePath)
                .WithMessage("Image must be an absolute http/https URL or a root-relative path.");

            RuleFor(x => x.CategoryId)
                .NotEqual(Guid.Empty).WithMessage("A category is required.");
        }
    }

    public sealed class CreateProductValidator : AbstractValidator<CreateProduct>
    {
        public CreateProductValidator()
        {
            Include(new ProductBaseValidator());
        }
    }

    public sealed class UpdateProductValidator : AbstractValidator<UpdateProduct>
    {
        public UpdateProductValidator()
        {
            Include(new ProductBaseValidator());

            RuleFor(x => x.Id)
                .NotEqual(Guid.Empty).WithMessage("Product id is required.");
        }
    }
}
