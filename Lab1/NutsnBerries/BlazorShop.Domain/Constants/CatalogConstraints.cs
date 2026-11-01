namespace BlazorShop.Domain.Constants
{
    /// <summary>
    /// Input limits for catalog data entered by admins. Kept in one place so the validators, tests and
    /// documentation agree. Upstream's database columns for these fields are unbounded text, so these are
    /// application-level limits, not schema limits.
    /// </summary>
    public static class CatalogConstraints
    {
        public const int ProductNameMaxLength = 200;
        public const int ProductDescriptionMaxLength = 5000;
        public const int CategoryNameMaxLength = 100;

        public const decimal MaxPrice = 10_000_000m;
        public const int MaxStock = 1_000_000;

        public const int VariantSkuMaxLength = 64;
        public const int VariantSizeValueMaxLength = 16;
        public const int VariantColorMaxLength = 32;
    }
}
