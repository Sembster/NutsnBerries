# NutsnBerries

Natural beauty webshop, forked from [unrealbg/BlazorShop](https://github.com/unrealbg/BlazorShop) (MIT).
.NET 10, Blazor (server-rendered storefront + WASM admin), ASP.NET Core API, PostgreSQL.

Only user-facing text and theming are rebranded; the `BlazorShop.*` project and namespace names are
kept so upstream changes stay easy to merge.

## Prerequisites
- .NET SDK 10.0.x (see `global.json`)
- Docker Desktop

## Run locally
```bash
cp .env.example .env            # then edit the password
docker compose -f compose.dev.yml up -d

cd BlazorShop.Presentation/BlazorShop.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=nutsnberries;Username=nutsnberries;Password=<your password>"
dotnet user-secrets set "Jwt:Key" "<random 32+ char string>"
dotnet run
```
The API applies EF Core migrations on startup. Run the Storefront and Web projects alongside it,
or use the upstream Aspire AppHost (`dotnet run --project BlazorShop.AppHost`).

## Theme
Storefront colours live at the bottom of `BlazorShop.Storefront/wwwroot/css/storefront.css`
(forest `#1e3428`, sage `#8aa68a`, cream `#fbf8f1`, berry `#b83b5e`).
