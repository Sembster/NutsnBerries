namespace BlazorShop.Tests.Hardening
{
    using BlazorShop.Application.DTOs.Category;
    using BlazorShop.Application.DTOs.Product;
    using BlazorShop.Application.DTOs.Product.ProductVariant;
    using BlazorShop.Application.Validations.Catalog;
    using BlazorShop.Domain.Constants;

    using Xunit;

    public class CatalogValidatorsTests
    {
        private readonly CreateProductValidator _createProduct = new();
        private readonly UpdateProductValidator _updateProduct = new();
        private readonly CreateCategoryValidator _createCategory = new();
        private readonly UpdateCategoryValidator _updateCategory = new();
        private readonly CreateProductVariantValidator _createVariant = new();
        private readonly UpdateProductVariantValidator _updateVariant = new();

        // ---------- products ----------

        private static CreateProduct ValidProduct() => new()
        {
            Name = "Wild Berry Face Serum",
            Description = "A light daily serum.",
            Price = 349m,
            Quantity = 0,
            Image = "/images/products/wild-berry-face-serum.svg",
            CategoryId = Guid.NewGuid(),
        };

        [Fact]
        public void Product_Valid_Passes()
        {
            Assert.True(_createProduct.Validate(ValidProduct()).IsValid);
        }

        [Fact]
        public void Product_AbsoluteImageUrl_Passes()
        {
            var product = ValidProduct();
            product.Image = "https://cdn.example.com/serum.jpg";

            Assert.True(_createProduct.Validate(product).IsValid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Product_NameRequired(string? name)
        {
            var product = ValidProduct();
            product.Name = name;

            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Fact]
        public void Product_NameAtLimitPasses_OverLimitFails()
        {
            var product = ValidProduct();
            product.Name = new string('a', CatalogConstraints.ProductNameMaxLength);
            Assert.True(_createProduct.Validate(product).IsValid);

            product.Name = new string('a', CatalogConstraints.ProductNameMaxLength + 1);
            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Fact]
        public void Product_DescriptionRequiredAndBounded()
        {
            var product = ValidProduct();
            product.Description = "";
            Assert.False(_createProduct.Validate(product).IsValid);

            product.Description = new string('d', CatalogConstraints.ProductDescriptionMaxLength);
            Assert.True(_createProduct.Validate(product).IsValid);

            product.Description = new string('d', CatalogConstraints.ProductDescriptionMaxLength + 1);
            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-0.01)]
        public void Product_PriceMustBePositive(double price)
        {
            var product = ValidProduct();
            product.Price = (decimal)price;

            var result = _createProduct.Validate(product);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Price must be greater than 0.");
        }

        [Fact]
        public void Product_PriceBoundedByMax()
        {
            var product = ValidProduct();
            product.Price = CatalogConstraints.MaxPrice;
            Assert.True(_createProduct.Validate(product).IsValid);

            product.Price = CatalogConstraints.MaxPrice + 1;
            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(CatalogConstraints.MaxStock + 1)]
        public void Product_QuantityOutOfRangeFails(int quantity)
        {
            var product = ValidProduct();
            product.Quantity = quantity;

            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("javascript:alert(1)")]
        [InlineData("//evil.example.com/x.png")]
        [InlineData("ftp://example.com/x.png")]
        [InlineData("images/relative-without-slash.png")]
        public void Product_BadImageFails(string? image)
        {
            var product = ValidProduct();
            product.Image = image;

            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Fact]
        public void Product_EmptyCategoryFails()
        {
            var product = ValidProduct();
            product.CategoryId = Guid.Empty;

            Assert.False(_createProduct.Validate(product).IsValid);
        }

        [Fact]
        public void UpdateProduct_RequiresIdAndSharesTheRules()
        {
            var update = new UpdateProduct
            {
                Id = Guid.Empty,
                Name = "Name",
                Description = "Description",
                Price = 10m,
                Quantity = 1,
                Image = "/x.png",
                CategoryId = Guid.NewGuid(),
            };
            Assert.False(_updateProduct.Validate(update).IsValid);

            update.Id = Guid.NewGuid();
            Assert.True(_updateProduct.Validate(update).IsValid);

            update.Price = 0m;
            Assert.False(_updateProduct.Validate(update).IsValid);
        }

        // ---------- categories ----------

        [Fact]
        public void Category_NameRequiredAndBounded()
        {
            Assert.True(_createCategory.Validate(new CreateCategory { Name = "Skincare" }).IsValid);
            Assert.False(_createCategory.Validate(new CreateCategory { Name = "  " }).IsValid);
            Assert.False(_createCategory.Validate(new CreateCategory { Name = null }).IsValid);
            Assert.False(_createCategory.Validate(
                new CreateCategory { Name = new string('c', CatalogConstraints.CategoryNameMaxLength + 1) }).IsValid);
        }

        [Fact]
        public void UpdateCategory_RequiresId()
        {
            Assert.False(_updateCategory.Validate(new UpdateCategory { Id = Guid.Empty, Name = "Skincare" }).IsValid);
            Assert.True(_updateCategory.Validate(new UpdateCategory { Id = Guid.NewGuid(), Name = "Skincare" }).IsValid);
        }

        // ---------- variants ----------

        private static CreateProductVariant ValidVariant() => new()
        {
            ProductId = Guid.Empty, // the route supplies it
            Sku = "SKU-1",
            SizeScale = 1,
            SizeValue = "M",
            Price = 99m,
            Stock = 5,
            Color = "Green",
        };

        [Fact]
        public void Variant_Valid_PassesWithoutBodyProductId()
        {
            Assert.True(_createVariant.Validate(ValidVariant()).IsValid);
        }

        [Fact]
        public void Variant_NullPriceMeansUseProductPrice()
        {
            var variant = ValidVariant();
            variant.Price = null;

            Assert.True(_createVariant.Validate(variant).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Variant_ExplicitPriceMustBePositive(double price)
        {
            var variant = ValidVariant();
            variant.Price = (decimal)price;

            Assert.False(_createVariant.Validate(variant).IsValid);
        }

        [Fact]
        public void Variant_InvalidFieldsFail()
        {
            var variant = ValidVariant();
            variant.SizeScale = 999; // not a defined SizeScale
            Assert.False(_createVariant.Validate(variant).IsValid);

            variant = ValidVariant();
            variant.SizeValue = "";
            Assert.False(_createVariant.Validate(variant).IsValid);

            variant = ValidVariant();
            variant.Stock = -1;
            Assert.False(_createVariant.Validate(variant).IsValid);

            variant = ValidVariant();
            variant.Sku = new string('s', CatalogConstraints.VariantSkuMaxLength + 1);
            Assert.False(_createVariant.Validate(variant).IsValid);

            variant = ValidVariant();
            variant.Color = new string('c', CatalogConstraints.VariantColorMaxLength + 1);
            Assert.False(_createVariant.Validate(variant).IsValid);
        }

        [Fact]
        public void UpdateVariant_RequiresBothIds()
        {
            var update = new UpdateProductVariant
            {
                Id = Guid.Empty,
                ProductId = Guid.NewGuid(),
                SizeScale = 1,
                SizeValue = "M",
                Stock = 1,
            };
            Assert.False(_updateVariant.Validate(update).IsValid);

            update.Id = Guid.NewGuid();
            update.ProductId = Guid.Empty;
            Assert.False(_updateVariant.Validate(update).IsValid);

            update.ProductId = Guid.NewGuid();
            Assert.True(_updateVariant.Validate(update).IsValid);
        }
    }
}
