# Focus L-AIci Copilot Instructions

Focus L-AIci is a local-first ASP.NET Core MVC application backed by EF Core and SQLite.

## Project priorities

- Preserve the local-first workflow and avoid introducing hosted-service assumptions.
- Prefer small, explicit, reversible changes over wide refactors.
- Keep controller actions thin and move domain/query logic into services.
- Reuse existing view models, security policies, and helpers before adding new abstractions.
- Favor readable server-rendered MVC patterns over unnecessary client-side complexity.

## Data and service rules

- Treat `FocusMemoryContext` as the single source of truth for persisted state.
- Use `AsNoTracking()` for read-only EF Core queries unless tracked entities are required.
- Keep SQLite-compatible query behavior in mind; avoid provider-specific assumptions.
- Preserve existing cancellation-token flow on async service and controller methods.
- Do not bypass `LocalPathPolicy`, `RequestInputPolicy`, or security middleware when adding new entry points.

## Web and UX rules

- Keep forms antiforgery-protected.
- Preserve current routing patterns and slug normalization behavior.
- Prefer improving existing view models and view composition over adding ad hoc `ViewData`/`ViewBag` behavior.
- Keep JavaScript optional and progressive; server-rendered pages should remain functional without custom JS where practical.

## Testing and validation

- Add or update focused tests for behavior changes, especially around security, routing, and service logic.
- Run `dotnet test .\FocusLAIci.slnx` for validation after meaningful code changes.
- If a change affects app startup or data access, also validate the web project builds cleanly.

## Collaboration guidance

- When touching files that are already modified in the worktree, read them fully first and avoid overwriting unrelated edits.
- Call out tradeoffs when changing persistence, security, or context-retrieval behavior.
- Prefer new files or isolated helpers when repo-local Copilot customization is sufficient.
