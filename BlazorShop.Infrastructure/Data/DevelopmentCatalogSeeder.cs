namespace BlazorShop.Infrastructure.Data
{
    using BlazorShop.Domain.Entities;

    using Microsoft.EntityFrameworkCore;

    // Sample NutsnBerries catalog for local development. Names, descriptions and prices are
    // placeholders written for the demo store; replace them with real products before launch.
    public static class DevelopmentCatalogSeeder
    {
        public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
        {
            var categories = new[]
            {
                new SeedCategory(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Skincare",
                    "skincare",
                    "Browse plant-based serums, cleansers and creams in the NutsnBerries storefront.",
                    "Gentle daily skincare made with nut oils, berry extracts and botanicals, from cleansing to moisture."),
                new SeedCategory(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Hair Care",
                    "hair-care",
                    "Browse nourishing shampoos, conditioners and hair oils in the NutsnBerries storefront.",
                    "Hair care inspired by the forest: nut oils and berry extracts for cleansing, conditioning and shine."),
                new SeedCategory(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Body Care",
                    "body-care",
                    "Browse body butters, scrubs and hand creams in the NutsnBerries storefront.",
                    "Rich, naturally scented body care for soft skin, from whipped butters to gentle berry scrubs."),
                new SeedCategory(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    "Bath & Soap",
                    "bath-and-soap",
                    "Browse natural soap bars and bath salts in the NutsnBerries storefront.",
                    "Slow, simple bath rituals with soap bars and mineral bath salts scented by the forest."),
                new SeedCategory(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    "Lips & Face",
                    "lips-and-face",
                    "Browse lip balms and face masks in the NutsnBerries storefront.",
                    "Small everyday essentials for lips and face, made with berries, butters and gentle botanicals."),
                new SeedCategory(
                    Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    "Gift Sets",
                    "gift-sets",
                    "Browse natural beauty gift sets in the NutsnBerries storefront.",
                    "Thoughtfully put-together sets of our favourites, ready to give or to start your own routine.")
            };

            var categoryIds = categories.Select(category => category.Id).ToArray();
            var categoryNames = categories.Select(category => category.Name).ToArray();

            var existingCategories = await dbContext.Categories
                .Where(category => categoryIds.Contains(category.Id)
                    || (category.Name != null && categoryNames.Contains(category.Name)))
                .ToListAsync(cancellationToken);

            var resolvedCategoryIds = new Dictionary<Guid, Guid>();
            var hasChanges = false;

            foreach (var categorySeed in categories)
            {
                var existingCategory = existingCategories.FirstOrDefault(category => category.Id == categorySeed.Id)
                    ?? existingCategories.FirstOrDefault(category => string.Equals(category.Name, categorySeed.Name, StringComparison.OrdinalIgnoreCase));

                if (existingCategory is not null)
                {
                    hasChanges |= ApplyCategorySeed(existingCategory, categorySeed);
                    resolvedCategoryIds[categorySeed.Id] = existingCategory.Id;
                    continue;
                }

                var category = new Category
                {
                    Id = categorySeed.Id
                };

                ApplyCategorySeed(category, categorySeed);

                await dbContext.Categories.AddAsync(category, cancellationToken);
                existingCategories.Add(category);
                resolvedCategoryIds[categorySeed.Id] = category.Id;
                hasChanges = true;
            }

            var products = new[]
            {
                new SeedProduct(
                    Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                    "Wild Berry Face Serum",
                    "wild-berry-face-serum",
                    "A lightweight daily serum with blackcurrant seed oil and rosehip, in a dropper bottle for a few drops at a time.",
                    349.00m,
                    "/images/products/wild-berry-face-serum.svg",
                    24,
                    new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
                    categories[0].Id,
                    "Meet Wild Berry Face Serum, a lightweight daily serum made with blackcurrant seed oil and rosehip.",
                    "Wild Berry Face Serum is a light, fast-absorbing serum for morning or evening. A few drops are enough to leave skin feeling soft and comfortable."),
                new SeedProduct(
                    Guid.Parse("eeeeeeee-5555-5555-5555-555555555555"),
                    "Almond Milk Cleanser",
                    "almond-milk-cleanser",
                    "A creamy, gentle face cleanser with sweet almond oil that rinses clean and leaves skin soft rather than tight.",
                    229.00m,
                    "/images/products/almond-milk-cleanser.svg",
                    30,
                    new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc),
                    categories[0].Id,
                    "Shop Almond Milk Cleanser, a creamy face cleanser with sweet almond oil for a soft, comfortable finish.",
                    "Almond Milk Cleanser lifts away the day with a soft creamy texture. Use it morning and night, with water, as the first step of your routine."),
                new SeedProduct(
                    Guid.Parse("ffffffff-6666-6666-6666-666666666666"),
                    "Hazelnut Day Cream",
                    "hazelnut-day-cream",
                    "A comforting face cream with hazelnut oil and shea butter that sinks in quickly and sits well under make-up.",
                    299.00m,
                    "/images/products/hazelnut-day-cream.svg",
                    18,
                    new DateTime(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc),
                    categories[0].Id,
                    "Discover Hazelnut Day Cream, a comforting daily moisturiser with hazelnut oil and shea butter.",
                    "Hazelnut Day Cream gives everyday moisture without a heavy feel. The nutty, naturally warm texture melts in and works well under make-up."),
                new SeedProduct(
                    Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"),
                    "Walnut Repair Shampoo",
                    "walnut-repair-shampoo",
                    "A mild cleansing shampoo with walnut oil, made to leave hair clean, soft and easy to comb.",
                    179.00m,
                    "/images/products/walnut-repair-shampoo.svg",
                    28,
                    new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
                    categories[1].Id,
                    "Shop Walnut Repair Shampoo, a mild cleansing shampoo with walnut oil for soft, manageable hair.",
                    "Walnut Repair Shampoo cleans gently and leaves hair soft. It suits daily use and pairs well with our Elderberry Shine Conditioner."),
                new SeedProduct(
                    Guid.Parse("99999999-7777-7777-7777-777777777777"),
                    "Elderberry Shine Conditioner",
                    "elderberry-shine-conditioner",
                    "A smoothing conditioner with elderberry extract that detangles and adds shine without weighing hair down.",
                    189.00m,
                    "/images/products/elderberry-shine-conditioner.svg",
                    26,
                    new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                    categories[1].Id,
                    "Explore Elderberry Shine Conditioner, a smoothing conditioner with elderberry extract for shiny, tangle-free hair.",
                    "Elderberry Shine Conditioner detangles and smooths from roots to ends. A little goes a long way for soft, glossy hair."),
                new SeedProduct(
                    Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    "Macadamia Hair Oil",
                    "macadamia-hair-oil",
                    "A light finishing oil with macadamia nut oil to tame frizz and add shine to dry lengths and ends.",
                    249.00m,
                    "/images/products/macadamia-hair-oil.svg",
                    14,
                    new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc),
                    categories[1].Id,
                    "Meet Macadamia Hair Oil, a light finishing oil that tames frizz and adds shine.",
                    "Macadamia Hair Oil is a lightweight finishing oil. Warm a drop or two between your palms and smooth over dry lengths and ends."),
                new SeedProduct(
                    Guid.Parse("cccccccc-3333-3333-3333-333333333333"),
                    "Shea & Cashew Body Butter",
                    "shea-and-cashew-body-butter",
                    "A rich whipped body butter with shea butter and cashew oil for dry skin, with a soft nutty scent.",
                    269.00m,
                    "/images/products/shea-and-cashew-body-butter.svg",
                    20,
                    new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc),
                    categories[2].Id,
                    "Shop Shea & Cashew Body Butter, a rich whipped body butter for dry skin with a soft nutty scent.",
                    "Shea & Cashew Body Butter is a rich, whipped texture made for dry skin. Massage in after a shower for lasting softness."),
                new SeedProduct(
                    Guid.Parse("dddddddd-4444-4444-4444-444444444444"),
                    "Raspberry Body Scrub",
                    "raspberry-body-scrub",
                    "A gentle sugar scrub with raspberry seed and nut oil that buffs away rough skin and rinses off easily.",
                    219.00m,
                    "/images/products/raspberry-body-scrub.svg",
                    22,
                    new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
                    categories[2].Id,
                    "Complete your routine with Raspberry Body Scrub, a gentle sugar scrub with raspberry seed.",
                    "Raspberry Body Scrub buffs rough skin and leaves it soft. Use it once or twice a week on damp skin and rinse well."),
                new SeedProduct(
                    Guid.Parse("77777777-9999-9999-9999-999999999999"),
                    "Pistachio Hand Cream",
                    "pistachio-hand-cream",
                    "A non-greasy hand cream with pistachio oil in a handbag-sized tube, for dry hands through the day.",
                    129.00m,
                    "/images/products/pistachio-hand-cream.svg",
                    40,
                    new DateTime(2026, 10, 3, 15, 0, 0, DateTimeKind.Utc),
                    categories[2].Id,
                    "Carry Pistachio Hand Cream, a non-greasy hand cream in a handbag-sized tube.",
                    "Pistachio Hand Cream absorbs quickly without a greasy finish. The compact tube slips easily into a bag or pocket."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    "Blueberry Soap Bar",
                    "blueberry-soap-bar",
                    "A gentle soap bar with blueberry and oat, scented with a fresh, fruity note.",
                    69.00m,
                    "/images/products/blueberry-soap-bar.svg",
                    50,
                    new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc),
                    categories[3].Id,
                    "Meet Blueberry Soap Bar, a gentle soap bar with blueberry and oat.",
                    "Blueberry Soap Bar lathers softly and rinses clean. Keep it dry between uses on a soap dish to make it last."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    "Pine Forest Bath Salts",
                    "pine-forest-bath-salts",
                    "Mineral bath salts scented with pine and cedar for a slow evening soak.",
                    159.00m,
                    "/images/products/pine-forest-bath-salts.svg",
                    16,
                    new DateTime(2026, 10, 4, 11, 0, 0, DateTimeKind.Utc),
                    categories[3].Id,
                    "Explore Pine Forest Bath Salts, mineral bath salts scented with pine and cedar.",
                    "Pine Forest Bath Salts turn an ordinary bath into a calm ritual. Stir a few spoonfuls into warm running water."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000003"),
                    "Cranberry Lip Balm",
                    "cranberry-lip-balm",
                    "A nourishing lip balm with cranberry seed oil and cocoa butter and a hint of natural berry tint.",
                    79.00m,
                    "/images/products/cranberry-lip-balm.svg",
                    60,
                    new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
                    categories[4].Id,
                    "Discover Cranberry Lip Balm, a nourishing lip balm with cranberry seed oil and cocoa butter.",
                    "Cranberry Lip Balm keeps lips soft and comfortable with a subtle berry tint. Small enough to carry everywhere."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000004"),
                    "Rosehip Clay Face Mask",
                    "rosehip-clay-face-mask",
                    "A creamy clay mask with rosehip that gently draws out the day, to use once or twice a week.",
                    189.00m,
                    "/images/products/rosehip-clay-face-mask.svg",
                    15,
                    new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
                    categories[4].Id,
                    "Shop Rosehip Clay Face Mask, a creamy clay mask with rosehip for a weekly reset.",
                    "Rosehip Clay Face Mask is a creamy mask for a weekly pause. Apply a thin layer, relax, and rinse with warm water."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000005"),
                    "Berry Bliss Gift Set",
                    "berry-bliss-gift-set",
                    "A gift set with our Wild Berry Face Serum, Raspberry Body Scrub and Cranberry Lip Balm in a keepsake box.",
                    549.00m,
                    "/images/products/berry-bliss-gift-set.svg",
                    10,
                    new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc),
                    categories[5].Id,
                    "Give Berry Bliss Gift Set, a keepsake box with a face serum, body scrub and lip balm.",
                    "Berry Bliss Gift Set brings together three of our berry favourites in one keepsake box, ready to give."),
                new SeedProduct(
                    Guid.Parse("10000000-0000-0000-0000-000000000006"),
                    "Forest Floor Starter Set",
                    "forest-floor-starter-set",
                    "A starter set with Almond Milk Cleanser, Hazelnut Day Cream and Pistachio Hand Cream for a simple daily routine.",
                    449.00m,
                    "/images/products/forest-floor-starter-set.svg",
                    12,
                    new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc),
                    categories[5].Id,
                    "Start with Forest Floor Starter Set, a cleanser, day cream and hand cream in one box.",
                    "Forest Floor Starter Set is a simple way to begin: a cleanser, a day cream and a hand cream, packed together.")
            };

            var productIds = products.Select(product => product.Id).ToArray();
            var existingProducts = await dbContext.Products
                .Where(product => productIds.Contains(product.Id))
                .ToListAsync(cancellationToken);

            var missingProducts = products
                .Where(product => existingProducts.All(existingProduct => existingProduct.Id != product.Id))
                .Select(product =>
                {
                    var seededProduct = new Product
                    {
                        Id = product.Id
                    };

                    ApplyProductSeed(seededProduct, product, resolvedCategoryIds[product.CategorySeedId]);
                    return seededProduct;
                })
                .ToList();

            foreach (var existingProduct in existingProducts)
            {
                var productSeed = products.First(product => product.Id == existingProduct.Id);
                hasChanges |= ApplyProductSeed(existingProduct, productSeed, resolvedCategoryIds[productSeed.CategorySeedId]);
            }

            if (!hasChanges && missingProducts.Count == 0)
            {
                return;
            }

            if (missingProducts.Count > 0)
            {
                await dbContext.Products.AddRangeAsync(missingProducts, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static bool ApplyCategorySeed(Category category, SeedCategory seed)
        {
            var hasChanges = false;

            hasChanges |= AssignIfDifferent(() => category.Name, value => category.Name = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => category.Slug, value => category.Slug = value, seed.Slug);
            hasChanges |= AssignIfDifferent(() => category.MetaTitle, value => category.MetaTitle = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => category.MetaDescription, value => category.MetaDescription = value, seed.MetaDescription);
            hasChanges |= AssignIfDifferent(() => category.OgTitle, value => category.OgTitle = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => category.OgDescription, value => category.OgDescription = value, seed.MetaDescription);
            hasChanges |= AssignIfDifferent(() => category.SeoContent, value => category.SeoContent = value, seed.SeoContent);

            if (!category.IsPublished)
            {
                category.IsPublished = true;
                hasChanges = true;
            }

            if (!category.RobotsIndex)
            {
                category.RobotsIndex = true;
                hasChanges = true;
            }

            if (!category.RobotsFollow)
            {
                category.RobotsFollow = true;
                hasChanges = true;
            }

            return hasChanges;
        }

        private static bool ApplyProductSeed(Product product, SeedProduct seed, Guid categoryId)
        {
            var hasChanges = false;

            hasChanges |= AssignIfDifferent(() => product.Name, value => product.Name = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => product.Slug, value => product.Slug = value, seed.Slug);
            hasChanges |= AssignIfDifferent(() => product.Description, value => product.Description = value, seed.Description);
            hasChanges |= AssignIfDifferent(() => product.Price, value => product.Price = value, seed.Price);
            hasChanges |= AssignIfDifferent(() => product.Image, value => product.Image = value, seed.Image);
            hasChanges |= AssignIfDifferent(() => product.Quantity, value => product.Quantity = value, seed.Quantity);
            hasChanges |= AssignIfDifferent(() => product.CreatedOn, value => product.CreatedOn = value, seed.CreatedOn);
            hasChanges |= AssignIfDifferent(() => product.MetaTitle, value => product.MetaTitle = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => product.MetaDescription, value => product.MetaDescription = value, seed.MetaDescription);
            hasChanges |= AssignIfDifferent(() => product.OgTitle, value => product.OgTitle = value, seed.Name);
            hasChanges |= AssignIfDifferent(() => product.OgDescription, value => product.OgDescription = value, seed.MetaDescription);
            hasChanges |= AssignIfDifferent(() => product.OgImage, value => product.OgImage = value, seed.Image);
            hasChanges |= AssignIfDifferent(() => product.SeoContent, value => product.SeoContent = value, seed.SeoContent);

            if (product.CategoryId != categoryId)
            {
                product.CategoryId = categoryId;
                hasChanges = true;
            }

            if (!product.IsPublished)
            {
                product.IsPublished = true;
                hasChanges = true;
            }

            var publishedOn = seed.CreatedOn.AddHours(2);
            if (product.PublishedOn != publishedOn)
            {
                product.PublishedOn = publishedOn;
                hasChanges = true;
            }

            if (!product.RobotsIndex)
            {
                product.RobotsIndex = true;
                hasChanges = true;
            }

            if (!product.RobotsFollow)
            {
                product.RobotsFollow = true;
                hasChanges = true;
            }

            return hasChanges;
        }

        private static bool AssignIfDifferent<T>(Func<T> getter, Action<T> setter, T value)
            where T : IEquatable<T>
        {
            if (getter().Equals(value))
            {
                return false;
            }

            setter(value);
            return true;
        }

        private static bool AssignIfDifferent(Func<string?> getter, Action<string?> setter, string? value)
        {
            if (string.Equals(getter(), value, StringComparison.Ordinal))
            {
                return false;
            }

            setter(value);
            return true;
        }

        private sealed record SeedCategory(Guid Id, string Name, string Slug, string MetaDescription, string SeoContent);

        private sealed record SeedProduct(
            Guid Id,
            string Name,
            string Slug,
            string Description,
            decimal Price,
            string Image,
            int Quantity,
            DateTime CreatedOn,
            Guid CategorySeedId,
            string MetaDescription,
            string SeoContent);
    }
}
