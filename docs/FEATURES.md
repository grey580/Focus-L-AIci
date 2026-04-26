# Focus L-AIci Features

## Core capabilities

- dashboard with palace stats and recent activity
- dashboard context workspace with multi-source retrieval, match explanations, and export/save actions
- native Inspect page for database/app-state inspection and missing-context review
- wing and room browsing
- palace visualizer with both list and **3D graph views** for wings, rooms, memories, links, and active work
- memory creation and editing
- todo tracking with large prompt-sized details
- ticket tracking with subtickets, notes, time logs, and activity history
- persistent code-graph scanning for repositories, symbols, imports, and inferred references
- persistent theme preference with a navbar light/dark mode toggle
- pinned memories for high-value knowledge
- explicit memory trust state with verification, review timing, freshness warnings, and decay-aware retrieval
- separate memory lifecycle governance with `Active`, `Archived`, and `Superseded` states
- tag-based discovery
- full-text style search flows across stored knowledge
- linked-memory relationships for context tracing
- API diagnostics for checking the active database target and homepage backing content
- read/write APIs for todos and tickets
- workspace export API and Inspect-page export panel for cold-start AI sessions
- recent-changes feed plus structured context provenance for inspectable retrieval reasoning
- sample data seeding for quick onboarding

## Memory model

Focus L-AIci uses a memory-palace inspired structure:

- **Wing** - a major domain such as Product Strategy, Engineering Operations, or Customer Delivery
- **Room** - a focused area within a wing, such as Installer Reliability or Admin UX
- **Memory** - an individual knowledge entry with title, summary, content, source, importance, timestamps, and tags
- **Links** - relationships between memories so context stays connected

This makes the system much better than a basic note app for operational work, because knowledge is grouped by meaning and can be navigated intentionally.

## Good fit for

- solo builders who want a private knowledge system
- engineering teams preserving architecture and debugging context
- consultants and MSPs tracking repeatable patterns across clients
- AI-assisted workflows that need durable local memory between sessions
- product teams who want decisions and rationale preserved, not just outcomes

## Recent additions

### Prompt-friendly todo details

Focus L-AIci's **Todos** page supports **very large prompt bodies** in the details field.

That means you can:

- store full AI work prompts directly in a todo
- keep long execution instructions attached to the task instead of splitting them across notes
- use the todo list as a real work queue for complex implementation, research, or debugging sessions

### Persistent dark mode

Focus L-AIci includes a navbar theme toggle that switches between light and dark mode and remembers your preference locally.

Dark mode covers the shared layout, cards, forms, ticket board, and code graph surfaces so longer research and implementation sessions are easier on the eyes without creating a separate theme-specific UI.

### Engineering ticket board

Focus L-AIci includes a dedicated **Tickets** area for larger workstreams that outgrow a simple todo.

Tickets support:

- top-level tickets plus nested subtickets
- notes with edit and delete flows
- time logs by model or operator
- an automatic activity timeline
- Git branch / commit context
- completion memories written back into the palace when a ticket is finished

### Repository code graph

Focus L-AIci includes a dedicated **Code Graph** area implemented natively inside the app with C#.

Code Graph supports:

- scanning a local repository path and storing the result in Focus's SQLite database
- tracking source files, namespaces, types, methods, properties, imports, and external module references
- surfacing inferred cross-file references when a file mentions a uniquely identifiable symbol from another file
- reopening the graph later without rescanning the whole repository just to answer structure questions
- browsing hotspots, relationship edges, tracked files, and a server-rendered graph neighborhood for a selected node
- exploring a dark **3D structural visualizer / palace graph** with smaller nodes, wider spacing, hover labels, click-to-focus navigation, and a zoom slider for dense repositories

The intended workflow is to use Code Graph first for orientation and narrowing, then open the exact files that matter once the graph has identified the hotspot, symbol, or relationship worth changing.

### Dashboard context and diagnostics

Focus L-AIci's homepage includes a richer **context workspace** that can pull from memories, todos, tickets, ticket history, and code graph data to build a task-specific pack before you start working.

It also exposes:

- `POST /api/context/brief` for structured context-pack retrieval
- `GET /api/palace/dashboard-diagnostics` for checking the **active database path**, homepage section contents, top context-match count, and detected content gaps
- `GET /api/palace/workspace` for a prompt-ready export of pinned memories, active work, code-graph projects, and recent changes
- `GET /api/palace/recent-changes` for a cross-source recent-change feed across memories, todos, tickets, and code graph projects
- `POST /api/palace/memories/{id}/verify` and `POST /api/palace/memories/{id}/mark-review` for the memory trust lifecycle
- `POST /api/palace/memories/{id}/archive`, `POST /api/palace/memories/{id}/restore`, `POST /api/palace/memories/{id}/supersede`, and `POST /api/palace/memories/bulk-governance` for lifecycle governance and triage
- `GET/POST/PUT /api/todos` plus `PUT /api/todos/{id}/status` for direct application-layer todo inspection and updates
- `GET/POST/PUT /api/tickets`, `PUT /api/tickets/{id}/status`, `POST /api/tickets/{id}/notes`, and `POST /api/tickets/{id}/time-logs` for closing the ticket write-back loop without opening SQLite manually
- string-enum JSON payloads for the mutation APIs, so callers can send values like `"InProgress"` and `"Medium"` instead of remembering numeric enum IDs

There is also a first-class **Inspect** page in the app navigation that brings those diagnostics together into one operator-facing screen: active database target, missing-context warnings, recent changes, section-by-section dashboard truth, a copyable workspace export block for fast AI/session handoff, and a memory governance queue for bulk verification, review, archive, and restore work.

Memory detail, search, dashboard, workspace, and Inspect expose the anti-context-rot and lifecycle-governance signals directly:

- `Verified`, `Unverified`, and `Needs review` memory states
- `Active`, `Archived`, and `Superseded` lifecycle states kept separate from trust
- `Last verified` plus `Review after` timestamps on memory detail pages
- archive / restore / supersede actions on memory detail pages plus bulk triage on Inspect
- active-only default retrieval so archived and superseded memories stay historical without polluting normal search, dashboard, workspace, or context flows
- freshness chips and warnings in dashboard/context results so stale items do not silently outrank fresher ones
- pinned-memory export annotations like `[Verified]` in the workspace snapshot
