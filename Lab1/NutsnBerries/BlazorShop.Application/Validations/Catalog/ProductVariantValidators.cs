namespace BlazorShop.Application.Validations.Catalog
{
    using BlazorShop.Application.DTOs.Product.ProductVariant;
    using BlazorShop.Domain.Constants;
    using BlazorShop.Domain.Entities;

    using FluentValidation;

    /// <summary>
    /// Creating a variant: the product id comes from the route (the controller overwrites the body value),
    /// so it is deliberately not required in the body.
    /// </summary>
    public sealed class CreateProductVariantValidator : AbstractValidator<CreateProductVariant>
    {
        public CreateProductVariantValidator()
        {
            RuleFor(x => x.Sku).MaximumLength(CatalogConstraints.VariantSkuMaxLength)
                .WithMessage($"SKU must be at most {CatalogConstraints.VariantSkuMaxLength} characters.");

            RuleFor(x => x.SizeScale)
                .Must(value => Enum.IsDefined(typeof(SizeScale), value))
                .WithMessage("Size scale is not valid.");

            RuleFor(x => x.SizeValue)
                .NotEmpty().WithMessage("Size value is required.")
                .MaximumLength(CatalogConstraints.VariantSizeValueMaxLength)
                .WithMessage($"Size value must be at most {CatalogConstraints.VariantSizeValueMaxLength} characters.");

            RuleFor(x => x.Price)
                .GreaterThan(0m).When(x => x.Price.HasValue)
                .WithMessage("Variant price must be greater than 0.")
                .LessThanOrEqualTo(CatalogConstraints.MaxPrice).When(x => x.Price.HasValue)
                .WithMessage($"Variant price must be at most {CatalogConstraints.MaxPrice:N0}.");

            RuleFor(x => x.Stock)
                .InclusiveBetween(0, CatalogConstraints.MaxStock)
                .WithMessage($"Stock must be between 0 and {CatalogConstraints.MaxStock:N0}.");

            RuleFor(x => x.Color).MaximumLength(CatalogConstraints.VariantColorMaxLength)
                .WithMessage($"Color must be at most {CatalogConstraints.VariantColorMaxLength} characters.");
        }
    }

    public sealed class UpdateProductVariantValidator : AbstractValidator<UpdateProductVariant>
    {
        public UpdateProductVariantValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("Variant id is required.");
            RuleFor(x => x.ProductId).NotEqual(Guid.Empty).WithMessage("Product id is required.");

            RuleFor(x => x.Sku).MaximumLength(CatalogConstraints.VariantSkuMaxLength)
                .WithMessage($"SKU must be at most {CatalogConstraints.VariantSkuMaxLength} characters.");

            RuleFor(x => x.SizeScale)
                .Must(value => Enum.IsDefined(typeof(SizeScale), value))
                .WithMessage("Size scale is not valid.");

            RuleFor(x => x.SizeValue)
                .NotEmpty().WithMessage("Size value is required.")
                .MaximumLength(CatalogConstraints.VariantSizeValueMaxLength)
                .WithMessage($"Size value must be at most {CatalogConstraints.VariantSizeValueMaxLength} characters.");

            RuleFor(x => x.Price)
                .GreaterThan(0m).When(x => x.Price.HasValue)
                .WithMessage("Variant price must be greater than 0.")
                .LessThanOrEqualTo(CatalogConstraints.MaxPrice).When(x => x.Price.HasValue)
                .WithMessage($"Variant price must be at most {CatalogConstraints.MaxPrice:N0}.");

            RuleFor(x => x.Stock)
                .InclusiveBetween(0, CatalogConstraints.MaxStock)
                .WithMessage($"Stock must be between 0 and {CatalogConstraints.MaxStock:N0}.");

            RuleFor(x => x.Color).MaximumLength(CatalogConstraints.VariantColorMaxLength)
                .WithMessage($"Color must be at most {CatalogConstraints.VariantColorMaxLength} characters.");
        }
    }
}
