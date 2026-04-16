# Focus L-AIci

**Focus L-AIci** is a local-first memory palace for engineers, builders, researchers, and AI-assisted teams who do not want important reasoning to disappear into chat history.

It gives you a structured way to capture decisions, incidents, patterns, research, and implementation knowledge in a form that stays readable to humans and useful to future sessions. Instead of burying context in loose notes, Focus L-AIci organizes knowledge into **wings**, **rooms**, **memories**, **tags**, and **relationships** so you can recover the *why* behind your work as fast as the *what*.

## Why people use it

Most teams lose critical context in the same places:

- temporary chats
- debugging sessions
- deployment notes
- architecture discussions
- scattered markdown files
- "we already solved this once" moments

Focus L-AIci turns that lost context into a searchable, browsable, persistent knowledge system you can run on your own machine.

## What makes it compelling

- **Local-first by design** - your knowledge stays with you, backed by SQLite.
- **Built for real engineering memory** - store decisions, incidents, facts, insights, references, tasks, and conversations.
- **Structured, not chaotic** - organize information into wings and rooms instead of dumping everything into a flat note list.
- **Searchable and explorable** - find memories by text, wing, room, tag, or memory type.
- **Relationship-aware** - connect one memory to another so reasoning can be traced instead of guessed.
- **Fast to adopt** - lightweight ASP.NET Core MVC app with minimal setup.
- **Useful on day one** - optional demo data seeds an example palace that shows the model immediately.

## How the model works

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

## Core capabilities

- dashboard with palace stats and recent activity
- wing and room browsing
- memory creation and editing
- pinned memories for high-value knowledge
- tag-based discovery
- full-text style search flows across stored knowledge
- linked-memory relationships for context tracing
- sample data seeding for quick onboarding

## Technology

- **ASP.NET Core MVC**
- **Entity Framework Core**
- **SQLite**
- **xUnit** test coverage for core service behavior

The project is intentionally simple to run, easy to extend, and practical for local use.

## Quick start

```powershell
dotnet restore
dotnet run --project .\FocusLAIci.Web\FocusLAIci.Web.csproj
```

By default, the app listens on:

```text
http://127.0.0.1:5187
```

To run the tests:

```powershell
dotnet test .\FocusLAIci.slnx
```

## Demo data

In development, Focus L-AIci can seed starter content automatically so you can explore the experience immediately. The included sample data demonstrates:

- product strategy memories
- engineering incident notes
- reusable implementation patterns
- linked knowledge across multiple domains

## Why this project stands out

Focus L-AIci is not trying to be another generic note tool. It is designed for **high-value retained context**:

- why an architecture decision was made
- what broke in production
- how a difficult bug was fixed
- which implementation pattern worked before
- what an AI workflow should remember next time

That makes it especially valuable for people building complex systems over time, where the biggest productivity loss is not lack of information, but lack of **recoverable reasoning**.

## Roadmap potential

The current foundation is strong for teams who want to extend it into:

- import pipelines from transcripts or docs
- richer search and ranking
- multi-user collaboration
- AI-assisted retrieval and summarization
- export, backup, and synchronization workflows

## License

This repository is licensed under the terms of the included [LICENSE](LICENSE).
