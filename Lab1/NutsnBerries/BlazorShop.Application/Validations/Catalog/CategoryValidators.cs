namespace BlazorShop.Application.Validations.Catalog
{
    using BlazorShop.Application.DTOs.Category;
    using BlazorShop.Domain.Constants;

    using FluentValidation;

    public sealed class CategoryBaseValidator : AbstractValidator<CategoryBase>
    {
        public CategoryBaseValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Category name is required.")
                .MaximumLength(CatalogConstraints.CategoryNameMaxLength)
                .WithMessage($"Category name must be at most {CatalogConstraints.CategoryNameMaxLength} characters.");
        }
    }

    public sealed class CreateCategoryValidator : AbstractValidator<CreateCategory>
    {
        public CreateCategoryValidator()
        {
            Include(new CategoryBaseValidator());
        }
    }

    public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategory>
    {
        public UpdateCategoryValidator()
        {
            Include(new CategoryBaseValidator());

            RuleFor(x => x.Id)
                .NotEqual(Guid.Empty).WithMessage("Category id is required.");
        }
    }
}
