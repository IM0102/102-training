# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

OrderHub is an ASP.NET Core 8.0 MVC training application for order management (customers, products, orders). It uses a classic 3-project layered architecture with EF Core / SQL Server persistence, and its UI text/domain data is in Traditional Chinese.

## Commands

Run all commands from the repository root (where `OrderHub.sln` lives).

```
dotnet build                                   # build the whole solution
dotnet run --project src/OrderHub.Web          # run the web app (auto-migrates DB + seeds on startup)
dotnet test                                     # run all tests
dotnet test --filter FullyQualifiedName~OrderServiceCreateTests   # run a single test class
dotnet test --filter FullyQualifiedName~OrderServiceCreateTests.CreateOrder_HappyPath_CreatesPendingOrder  # single test
dotnet ef migrations add <Name> --project src/OrderHub.Infrastructure --startup-project src/OrderHub.Web
dotnet ef database update --project src/OrderHub.Infrastructure --startup-project src/OrderHub.Web
```

There is no separate lint command; formatting/style conventions are enforced via `.editorconfig` (4-space indent for `.cs`/`.cshtml`, file-scoped namespaces, `var` preferred when the type is apparent).

Tests use xUnit with EF Core's InMemory provider (see `tests/OrderHub.Tests/TestSetup.cs`) — no real SQL Server instance is needed to run the test suite.

## Architecture

Three-project layering, referenced strictly one-way (`Web` → `Infrastructure`/`Core`, `Infrastructure` → `Core`; `Core` has no project references):

- **`src/OrderHub.Core`** — domain models (`Domain/`), repository interfaces (`Interfaces/`), and business logic (`Services/`). This is the only project with no EF Core dependency; it defines contracts that Infrastructure implements.
- **`src/OrderHub.Infrastructure`** — EF Core `DbContext` (`Data/OrderHubDbContext.cs`), entity configuration (via `OnModelCreating`), migrations, dev-only seed data (`Data/DbSeeder.cs`), and repository implementations (`Repositories/`) of the Core interfaces.
- **`src/OrderHub.Web`** — ASP.NET Core MVC app: controllers, view models, Razor views, and DI wiring in `Program.cs`.

Data flow: Controller → Service (`Core/Services`, business rules) → Repository (`Core/Interfaces` implemented in `Infrastructure/Repositories`) → `OrderHubDbContext`. Controllers never touch the `DbContext` or repositories directly — always go through a service.

### Key conventions

- **`ServiceResult<T>`** (`Core/Common/ServiceResult.cs`) is the standard return type for service methods that can fail validation (e.g. `CreateOrderAsync`, `CancelOrderAsync`). It carries `Success`, `Value`, and an `Errors` list; controllers check `result.Success` and surface `result.Errors`/`result.ErrorMessage` via `ModelState` or `TempData`. Read-only queries that can't "fail" (e.g. `GetOrderAsync`) just return the entity or `null` directly instead of wrapping in `ServiceResult`.
- **`PagedResult<T>`** (`Core/Common/PagedResult.cs`) is the standard return type for paged list queries (e.g. `GetOrdersAsync`), computing `TotalPages`/`HasPrevious`/`HasNext`.
- Order pricing: `OrderItem.UnitPriceSnapshot` freezes the product's price at order-creation time — line/order totals are always computed from the snapshot, not the current `Product.UnitPrice`. Customer-tier discounts (`CustomerTier.Gold`/`Silver`/`Standard`) are applied via `OrderService.GetDiscountRate` at order-creation time and again when computing `CalculateTotal`.
- Cancelling an order (`OrderService.CancelOrderAsync`) restores product stock and is only allowed from `Pending`/`Confirmed` status.
- Repositories are thin: only query shaping and `Include`/`ThenInclude` graph loading — all validation and business rules live in the `Services` layer.
- EF Core entity configuration (max lengths, decimal precision, indexes, delete behavior) is centralized in `OrderHubDbContext.OnModelCreating`, not via data annotations on the domain classes.

### Startup behavior

`Program.cs` runs `db.Database.Migrate()` and `DbSeeder.SeedAsync(db)` unconditionally on every app startup (not just Development) — the seeder itself is idempotent (skips seeding if any `Customer` rows already exist). `DbSeeder` uses a fixed `Random` seed so seeded data is deterministic across runs.
