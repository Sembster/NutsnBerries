namespace BlazorShop.Tests.Infrastructure
{
    using BlazorShop.Domain.Constants;
    using BlazorShop.Domain.Entities;
    using BlazorShop.Infrastructure.Data;

    using Microsoft.EntityFrameworkCore;

    using Xunit;

    public class NutsnBerriesSeederTests
    {
        [Fact]
        public async Task SeedAsync_CreatesSeoSettings_WhenNoneExist()
        {
            await using var context = CreateContext();

            await NutsnBerriesSeeder.SeedAsync(context);

            var settings = await context.SeoSettings.SingleAsync();
            Assert.Equal("NutsnBerries", settings.SiteName);
            Assert.Equal("| NutsnBerries", settings.DefaultTitleSuffix);
            Assert.Equal("NutsnBerries", settings.CompanyName);
            Assert.False(string.IsNullOrWhiteSpace(settings.DefaultMetaDescription));
        }

        [Fact]
        public async Task SeedAsync_RespectsTheSeoFieldLengthLimits()
        {
            await using var context = CreateContext();

            await NutsnBerriesSeeder.SeedAsync(context);

            var settings = await context.SeoSettings.SingleAsync();
            Assert.True(settings.SiteName!.Length <= SeoConstraints.SiteNameMaxLength);
            Assert.True(settings.DefaultTitleSuffix!.Length <= SeoConstraints.TitleSuffixMaxLength);
            Assert.True(settings.DefaultMetaDescription!.Length <= SeoConstraints.MetaDescriptionMaxLength);
            Assert.True(settings.CompanyName!.Length <= SeoConstraints.CompanyNameMaxLength);
        }

        [Fact]
        public async Task SeedAsync_IsIdempotent()
        {
            await using var context = CreateContext();

            await NutsnBerriesSeeder.SeedAsync(context);
            await NutsnBerriesSeeder.SeedAsync(context);

            Assert.Equal(1, await context.SeoSettings.CountAsync());
        }

        [Fact]
        public async Task SeedAsync_DoesNotOverwriteSettingsEditedInTheAdminArea()
        {
            await using var context = CreateContext();
            context.SeoSettings.Add(new SeoSettings
            {
                Id = Guid.NewGuid(),
                SiteName = "Edited by admin",
                DefaultTitleSuffix = "| Edited",
            });
            await context.SaveChangesAsync();

            await NutsnBerriesSeeder.SeedAsync(context);

            var settings = await context.SeoSettings.SingleAsync();
            Assert.Equal("Edited by admin", settings.SiteName);
            Assert.Equal("| Edited", settings.DefaultTitleSuffix);
        }

        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"nutsnberries-seeder-tests-{Guid.NewGuid()}")
                .Options;

            return new AppDbContext(options);
        }
    }
}
