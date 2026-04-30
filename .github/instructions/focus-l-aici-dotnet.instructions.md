---
description: "Focused coding guidance for the Focus L-AIci ASP.NET Core MVC and EF Core codebase."
applyTo: "**/*.cs, **/*.csproj, **/*.cshtml, **/*.json, **/*.js, **/*.css"
---

# Focus L-AIci .NET Web Guidance

## Architecture

- Keep MVC controllers orchestration-focused; push persistence and ranking logic into services.
- Prefer extending existing services such as `PalaceService`, `ContextService`, `SiteSettingsService`, and security helpers before creating new service layers.
- Use strongly typed view models instead of dynamic view data.

## EF Core

- Use `AsNoTracking()` on read paths by default.
- Keep LINQ expressions SQLite-friendly and deterministic.
- Preserve existing ordering, filtering, and cancellation-token behavior unless the change explicitly improves correctness.

## Security

- Preserve antiforgery on HTML form posts.
- Reuse `RequestInputPolicy`, `LocalPathPolicy`, `SecurityHeadersMiddleware`, and `ApiWriteOriginGuardMiddleware` patterns.
- Validate and normalize untrusted route/query/form input close to the edge.

## Maintainability

- Avoid speculative abstractions.
- Prefer small private helpers when reducing duplication inside a file.
- When fixing a bug, add or extend the narrowest test that proves the behavior.

## UI and Razor

- Keep Razor views accessible and consistent with existing layout/components.
- Prefer semantic HTML and progressive enhancement over JavaScript-only flows.
- Keep copy concise and engineering-focused.
