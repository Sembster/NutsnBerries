namespace BlazorShop.Application.Services.Payment
{
    using BlazorShop.Application.DTOs.Payment;
    using BlazorShop.Domain.Contracts;
    using BlazorShop.Domain.Contracts.Payment;
    using BlazorShop.Domain.Entities;

    /// <summary>
    /// Re-reads every cart line from the catalog and checks that it can be purchased at its current
    /// price and availability. The client's prices are never trusted.
    /// </summary>
    internal sealed class CheckoutCartResolver
    {
        private readonly IProductReadRepository _productReadRepository;

        public CheckoutCartResolver(IProductReadRepository productReadRepository)
        {
            _productReadRepository = productReadRepository;
        }

        internal async Task<CartLineResolution> ResolveCartLinesAsync(IEnumerable<CartLineRequest> carts)
        {
            var cartList = carts?.ToList() ?? [];
            var productLookup = await _productReadRepository.GetProductsByIdsAsync(
                cartList.Select(line => line.ProductId));
            var variantLookup = await _productReadRepository.GetProductVariantsByIdsAsync(
                cartList.Where(line => line.VariantId.HasValue).Select(line => line.VariantId!.Value));
            var productIdsWithVariants = await _productReadRepository.GetProductIdsWithVariantsAsync(
                cartList.Select(line => line.ProductId));
            var resolvedLines = new List<ResolvedCartLine>(cartList.Count);

            foreach (var line in cartList)
            {
                if (!productLookup.TryGetValue(line.ProductId, out var product))
                {
                    return CartLineResolution.Failure("A product in the cart no longer exists.");
                }

                if (!product.IsPublished || product.PublishedOn is null)
                {
                    return CartLineResolution.Failure("A product in the cart is not currently purchasable.");
                }

                ProductVariant? variant = null;
                if (line.VariantId.HasValue)
                {
                    if (!variantLookup.TryGetValue(line.VariantId.Value, out variant))
                    {
                        return CartLineResolution.Failure("A selected product variant no longer exists.");
                    }

                    if (variant.ProductId != line.ProductId)
                    {
                        return CartLineResolution.Failure(
                            "A selected product variant does not belong to the requested product.");
                    }

                    if (variant.Stock <= 0 || line.Quantity > variant.Stock)
                    {
                        return CartLineResolution.Failure(
                            "The requested quantity exceeds the selected product variant's current availability.");
                    }
                }
                else
                {
                    if (productIdsWithVariants.Contains(line.ProductId))
                    {
                        return CartLineResolution.Failure("A product variant must be selected for this product.");
                    }

                    if (product.Quantity <= 0 || line.Quantity > product.Quantity)
                    {
                        return CartLineResolution.Failure(
                            "The requested quantity exceeds the product's current availability.");
                    }
                }

                var unitPrice = variant?.Price ?? product.Price;
                if (unitPrice <= 0)
                {
                    return CartLineResolution.Failure(
                        "A product in the cart does not have a valid current price.");
                }

                resolvedLines.Add(new ResolvedCartLine(
                    product.Id,
                    variant?.Id,
                    line.Quantity,
                    unitPrice,
                    product.Name ?? "Product",
                    product.Description,
                    variant?.Sku,
                    variant?.SizeScale,
                    variant?.SizeValue,
                    variant?.Color));
            }

            return CartLineResolution.Success(resolvedLines);
        }

        internal sealed record CartLineResolution(
            IReadOnlyList<ResolvedCartLine> Lines,
            string? ErrorMessage)
        {
            public static CartLineResolution Success(IReadOnlyList<ResolvedCartLine> lines) => new(lines, null);

            public static CartLineResolution Failure(string message) => new([], message);
        }
    }
}
