namespace BlazorShop.Infrastructure.Data
{
    using BlazorShop.Domain.Entities;

    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// NutsnBerries-specific seed data, kept in its own class (an extension) so upstream's
    /// <see cref="DevelopmentCatalogSeeder"/> and its other seed code stay untouched.
    /// </summary>
    /// <remarks>
    /// Runs from one line in the API's Program.cs, next to the upstream catalog seeder. Values are
    /// placeholders for the lab; edit them under Admin > SEO once signed in.
    /// </remarks>
    public static class NutsnBerriesSeeder
    {
        public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
        {
            await SeedSeoSettingsAsync(dbContext, cancellationToken);
        }

        /// <summary>
        /// Creates the single SEO settings row if there is none. Without it the API returns empty settings
        /// and the Storefront drops the site name from page titles. An existing row is never overwritten,
        /// so changes made in the admin area survive restarts.
        /// </summary>
        private static async Task SeedSeoSettingsAsync(AppDbContext dbContext, CancellationToken cancellationToken)
        {
            if (await dbContext.SeoSettings.AnyAsync(cancellationToken))
            {
                return;
            }

            await dbContext.SeoSettings.AddAsync(
                new SeoSettings
                {
                    Id = Guid.Parse("5e0c0000-0000-0000-0000-000000000001"),
                    SiteName = "NutsnBerries",
                    DefaultTitleSuffix = "| NutsnBerries",
                    DefaultMetaDescription = "Natural beauty products inspired by nature: skincare, hair care, body care, soap and gift sets made with nuts, berries and botanicals.",
                    CompanyName = "NutsnBerries",
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
