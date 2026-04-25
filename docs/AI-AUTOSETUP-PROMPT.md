# AI Auto-Setup Prompt for Focus L-AIci

This page is for **AI models, coding agents, and operators** who need to get **Focus L-AIci** running quickly on a new machine and immediately understand how to use it well.

If you are an AI model being asked to bootstrap Focus L-AIci, use the prompt in the **Copy-paste setup prompt** section below as your operating instructions.

## What Focus L-AIci is

Focus L-AIci is a **local-first engineering memory system** built with **ASP.NET Core MVC** and **SQLite**.

It is designed to preserve:

- architecture decisions
- incident history
- debugging findings
- reusable implementation patterns
- prompt-sized todos
- ticket work with subtasks, notes, and time logs
- code graph scans for repository orientation

It is not just a notes app. It is a structured local memory layer for humans and AI-assisted development workflows.

## Fast human setup

1. Install the **.NET SDK** required by the repo.
2. Clone the repository.
3. From the repository root, run:

```powershell
dotnet restore
dotnet run --project .\FocusLAIci.Web\FocusLAIci.Web.csproj
```

4. Open the local URL printed by the app. By default that is:

```text
http://127.0.0.1:5187
```

## Important database note

Focus L-AIci normally uses the SQLite connection from `FocusLAIci.Web\appsettings.json`.

If the machine already has an existing populated database and you want the app to use it, create this file in `FocusLAIci.Web`:

```json
{
  "DatabasePath": "C:\\full\\path\\to\\focus-palace.dev.db"
}
```

File name:

```text
focus-palace.database-target.json
```

This tells Focus L-AIci to use that existing SQLite database instead of the default empty one.

## What to do immediately after startup

Once the site is open:

1. Review the **Dashboard** to understand the current palace state.
2. Open **Todos** to see active work.
3. Open **Tickets** for larger tracked workstreams.
4. Open **Code Graph** before broad code searching when repository orientation matters.
5. Add or update memories only when the information is durable and worth preserving.

## How AI models should use Focus L-AIci

The right workflow is:

1. **Retrieve first** - search Focus for relevant memories, todos, tickets, and code graph context before making assumptions.
2. **Work second** - perform implementation, debugging, or research using that retrieved context.
3. **Write back third** - store durable conclusions, fixes, patterns, and decisions back into Focus.

An AI model should treat Focus as:

- the local source of project memory
- the place to check before guessing
- the place to store reusable reasoning after a task is done

## Copy-paste setup prompt

Use the following prompt for an AI model that needs to set up and begin using Focus L-AIci on a machine:

```text
You are setting up Focus L-AIci on this computer.

Focus L-AIci is a local-first engineering memory system built with ASP.NET Core MVC and SQLite. Your job is to get it running correctly, point it at the right existing database if one already contains data, and then operate it as the durable local memory layer for future work.

Required behavior:

1. Locate the Focus L-AIci repository on disk.
2. Read the README and this document before making changes.
3. Determine whether there is an existing populated SQLite database already associated with Focus L-AIci. If there is, use it instead of creating or relying on a blank default database.
4. If needed, configure the database override file:
   FocusLAIci.Web\\focus-palace.database-target.json
5. Restore and start the web app.
6. Confirm the site is responding and that CSS/static assets load correctly.
7. Open the dashboard, todos, tickets, and code graph areas to understand the current state.
8. Treat Focus as the primary durable memory system for ongoing engineering work.

Operating rules:

- Search Focus before making assumptions.
- Use Code Graph before broad raw code searching when repository structure matters.
- Store durable outcomes, decisions, incidents, and reusable patterns back into Focus.
- Use Todos for prompt-sized execution work.
- Use Tickets for larger tracked work with notes, subtasks, and completion history.
- Do not create throwaway memories for trivial chatter.
- Preserve existing data and avoid resetting or replacing the active database unless explicitly instructed.

Startup commands:

dotnet restore
dotnet run --project .\\FocusLAIci.Web\\FocusLAIci.Web.csproj

Default local URL:

http://127.0.0.1:5187

Your first goal after startup is to identify the correct active database, confirm the app is serving static assets correctly, and summarize how the current Focus instance is organized.
```

## Copy-paste usage prompt

Use this prompt when an AI model already has Focus L-AIci available and should actively work from it:

```text
Use Focus L-AIci as the durable memory layer for this task.

Before implementation:
- search for relevant memories
- inspect related todos and tickets
- check code graph context if repository orientation matters

During implementation:
- preserve existing conventions
- rely on retrieved Focus context instead of guessing
- add or update todos and tickets when the work needs durable tracking

After implementation:
- write back durable findings, decisions, fixes, or reusable patterns into Focus
- prefer concise, high-signal memory entries that remain useful without the original chat

Use Focus as a working system of memory, not as a dump of raw conversation.
```

## Best companion guide

For deeper everyday usage guidance, also read:

- [Using Focus L-AIci Effectively](USING-FOCUS-L-AICI.md)
