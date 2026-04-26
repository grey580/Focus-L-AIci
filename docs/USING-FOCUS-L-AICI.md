# Using Focus L-AIci Effectively

This guide explains how to use **Focus L-AIci** as a real working memory system, not just a note bucket.

It also shows how to make AI models work well with it **today**, even without direct built-in model integration.

## The right mindset

Focus L-AIci works best when you treat it as your **durable reasoning layer**.

Do **not** put everything into it.
Put the things you will regret losing:

- architecture decisions
- debugging findings
- incident timelines
- deployment lessons
- reusable patterns
- important customer or product context
- prompts and workflows that consistently work

If a future version of you or your AI assistant would benefit from seeing it again, it belongs in Focus.

## The structure to follow

Use the data model consistently:

- **Wing** = big domain
- **Room** = focused area within the domain
- **Memory** = one important unit of knowledge
- **Tags** = fast retrieval hints
- **Links** = relationships between related memories

### Example structure

- Wing: `Grey Canary`
  - Room: `Endpoint Installer`
  - Room: `Platform Admin`
  - Room: `Incident Reports`
- Wing: `Product Strategy`
  - Room: `Roadmap`
  - Room: `Pricing`
  - Room: `Customer Feedback`

## What makes a good memory

A strong memory is:

- specific
- retrievable
- reusable
- understandable without the original chat

Each memory should answer at least one of these:

- What happened?
- Why did we decide this?
- What failed?
- What fixed it?
- What should we do next time?

## How to write memories properly

When creating a memory:

1. **Title** - short and specific  
   Good: `Heartbeat should preserve uninstalling status`

2. **Summary** - one or two sentences with the key takeaway  
   Good: `Do not let a normal heartbeat overwrite an active uninstall state. This causes endpoints marked for removal to appear healthy again.`

3. **Content** - the actual reasoning, evidence, edge cases, and outcome  
   Include what was observed, what was wrong, what changed, and why it matters.

4. **Kind** - pick the right category  
   Use `Decision`, `Incident`, `Insight`, `Fact`, `Reference`, `Conversation`, or `Task`.

5. **Source** - explain where it came from  
   Examples: debug session, meeting, deployment, research, architecture.

6. **Tags** - use a small consistent set  
   Prefer useful terms like `installer`, `websocket`, `endpoint`, `search`, `security`.

## Recommended daily workflow

### Before you start work

1. Search for the product, subsystem, or problem you are about to touch.
2. Open **Inspect** and verify the active database, warnings, recent changes, and workspace export.
3. Open pinned memories and recent related entries.
4. Review linked memories if the issue spans multiple areas.

### Use Inspect as the trust surface

The **Inspect** page is now the fastest way to answer:

- Am I looking at the right database?
- Is the dashboard missing context?
- What changed recently across memories, todos, tickets, and code graph?
- What is the current workspace snapshot I should give to an AI model?
- Which memories should be verified, reviewed, archived, or restored next?

Recommended pattern:

1. Open Inspect before assuming the homepage tells the whole story.
2. Use the workspace export block when you need to brief a fresh AI session quickly.
3. Use the governance queue there for bulk triage when memory hygiene is drifting.
4. Use the diagnostics URLs on that page when you want application-layer truth without opening SQLite.

### Use memory trust state to fight context rot

Memories are no longer just editable text records. Each memory now carries a visible trust state:

- **Unverified** for new or never-reviewed entries
- **Verified** for entries that were checked against current source truth
- **Needs review** when material edits changed the memory and it should be re-verified

Recommended pattern:

1. Treat **Verified** memories as the strongest reusable context.
2. If you materially edit an existing memory, expect it to move to **Needs review**.
3. Re-verify durable memories after checking them against the current repo, app state, or production evidence.
4. Pay attention to freshness warnings in context results and on the memory detail page before reusing old guidance.

### Use lifecycle state to retire memories without deleting them

Trust answers **"how believable is this memory right now?"** Lifecycle answers **"should this memory participate in normal retrieval?"**

Each memory now also has a separate lifecycle state:

- **Active** for memories that should appear in normal search, dashboard, workspace, and context retrieval
- **Archived** for historical or low-value memories you want to keep without surfacing by default
- **Superseded** for memories replaced by a newer canonical memory

Recommended pattern:

1. Archive memories that are no longer useful in daily retrieval but still worth preserving.
2. Supersede a memory when a newer memory replaces it; use the replacement link instead of editing the old one into ambiguity.
3. Restore archived or superseded memories only when they should rejoin active retrieval.
4. Assume normal retrieval is **active-only**; if something disappeared, check the memory detail page or Inspect governance queue before concluding it was deleted.

### Use Code Graph before raw code search

When you are about to change a codebase, start with **Code Graph** instead of opening files blindly.

Recommended pattern:

1. Open the repository's Code Graph project.
2. Use hotspots, the node filter, and relationship edges to narrow the likely files and symbols.
3. Use the 3D visualizer to see structural clusters quickly. Hover nodes for labels, click a node to focus its neighborhood, and use the zoom slider when dense areas overlap.
4. Only after that, open the exact files that the graph has surfaced.

This keeps repository orientation fast and reduces repeated broad searching through unrelated files.

### Use todos for working prompts

Focus L-AIci's todo details field supports **large prompt-sized content**.

Use that space for:

- implementation prompts you want to reuse
- agent instructions tied to a specific task
- multi-step execution context
- long-form acceptance criteria or handoff notes

This makes the todo list useful as an **active execution queue**, not just a short reminder list.

Recommended pattern:

1. Keep the todo title short and action-oriented.
2. Put the full working prompt or instructions in the todo details field.
3. Move the durable outcome into a memory after the work is complete.

### During work

1. Capture decisions when they become clear.
2. Record failed approaches only if they teach something reusable.
3. Add a memory when you discover a non-obvious fix, pattern, or constraint.
4. Use **Tickets** when the work needs subtasks, notes, tracked time, or a richer audit trail than a todo card.

### Use the APIs for write-back when automation matters

Focus is no longer just a read surface. If your workflow is scriptable or AI-assisted, prefer the application APIs over raw database edits:

- `POST /api/palace/memories/{id}/verify`
- `POST /api/palace/memories/{id}/mark-review`
- `GET/POST/PUT /api/todos`
- `PUT /api/todos/{id}/status`
- `GET/POST/PUT /api/tickets`
- `PUT /api/tickets/{id}/status`
- `POST /api/tickets/{id}/notes`
- `POST /api/tickets/{id}/time-logs`

These endpoints now accept readable string-enum payloads such as `Pending`, `InProgress`, `Medium`, and `Completed`, so callers do not need to know numeric enum values.

### After work

1. Store the final decision or outcome.
2. Link it to earlier incidents or related design notes.
3. Pin it if future work will depend on it repeatedly.
4. Verify it once the final text matches current reality so later sessions can trust it quickly.

## When to create a new memory vs edit an old one

Create a **new** memory when:

- a new incident happened
- the context materially changed
- the solution is distinct from the earlier one
- you want a historical trail

Edit an **existing** memory when:

- you are clarifying the same finding
- you are fixing wording, tags, or summary
- the entry represents a living reference rather than a dated event

If an edit materially changes the meaning, treat the updated memory as **Needs review** until it is checked and verified again.

## Tagging strategy that actually works

Do not over-tag.

A good rule is **3 to 6 tags per memory**:

- one for the product or domain
- one for the subsystem
- one for the kind of issue
- one for a key technical concept

Example:

```text
grey-canary, uninstall, endpoint, heartbeat, reliability
```

## How to make AI models use Focus L-AIci well

Right now, the most effective pattern is **human-guided retrieval**:

1. You search Focus L-AIci.
2. You copy the relevant memory summaries or full entries.
3. You give that retrieved context to your AI model.
4. You ask the model to work from those memories instead of from raw guesswork.

This is already powerful because it lets the model reason from your retained history, not just from the current chat.

## The best AI workflow

Use Focus in two directions:

### 1. Retrieve before asking

Before a serious prompt, gather relevant memories from Focus and include them in the prompt.

This helps the model:

- avoid repeating old mistakes
- preserve prior decisions
- continue architecture consistently
- understand your local conventions

For the fastest version of this flow, copy the **workspace export** from Inspect or call `GET /api/palace/workspace` and use that as the first context block in the prompt.

### 2. Distill after working

After a long session, ask the model to convert the session into one or more Focus-ready memories.

This helps you turn raw conversation into structured, reusable knowledge.

### Use the todo list to store reusable prompts

If you have prompts that are tied to a specific piece of active work, store them in **Todos** instead of burying them in a transient chat.

Good examples:

- a full implementation prompt for a feature
- a debugging checklist for a flaky subsystem
- a deployment handoff prompt
- a research prompt with constraints and expected outputs

Then, once the work is done, keep the lasting knowledge in memories and keep the todo focused on execution.

## When to use Todos vs Tickets

Use **Todos** when:

- the work is small and direct
- you mainly need a prompt-sized execution note
- you only care about status and quick recovery later

Use **Tickets** when:

- the work needs subtickets
- multiple notes or investigation updates will accumulate
- you want time tracking by model or operator
- you need Git branch / commit context attached to the work
- you want completion to create a durable summary memory automatically

Recommended pattern:

1. Put quick prompts and short execution queues in Todos.
2. Promote larger work to Tickets once it needs structure.
3. Let completed tickets feed back into the palace as reusable implementation memory.

## Prompt patterns for AI models

These prompts work well with ChatGPT, Claude, Copilot, and similar models.

### Prompt: use retrieved Focus memories as context

```text
Use the following Focus L-AIci memories as authoritative project context.
Do not ignore them unless I explicitly say they are outdated.
Base your reasoning on them first, then fill gaps carefully.

[Paste memory summaries or full memory entries here]

Task:
[Describe the work you want done]
```

### Prompt: summarize a session into a Focus-ready memory

```text
Convert the following working session into a Focus L-AIci memory.

Return:
- Title
- Summary
- Content
- Memory kind
- Source kind
- 3 to 6 tags
- Whether it should be pinned
- Suggested wing
- Suggested room

Only keep information that will be useful later.

[Paste transcript, notes, or chat excerpt here]
```

### Prompt: decide whether something belongs in Focus

```text
Review the following note or chat excerpt and decide whether it should be stored in Focus L-AIci.

If yes, return:
- why it is worth keeping
- the best title
- the summary
- the content to store
- the best tags
- the best wing and room

If no, explain why it is too temporary, too obvious, or too low value.

[Paste content here]
```

### Prompt: turn retrieved memories into an execution plan

```text
Using the following Focus L-AIci memories as constraints and prior decisions, create an implementation plan.
Do not contradict prior decisions unless you explicitly explain why.

[Paste memories here]

Goal:
[Describe goal]
```

### Prompt: ask the AI what to search for in Focus

```text
I am about to work on this problem:
[Describe problem]

Before giving me a solution, tell me what terms, subsystems, failure modes, and tags I should search for in Focus L-AIci so I can retrieve relevant prior context.
```

### Prompt: create multiple memories from one complex session

```text
Split the following session into multiple Focus L-AIci memories if needed.

Rules:
- separate incidents from decisions
- separate reusable patterns from one-off events
- keep each memory focused
- suggest links between related memories

Return the result as a list of memory drafts.

[Paste notes here]
```

## Recommended instructions to give your AI assistant

If you use an AI assistant regularly, give it standing guidance like this:

```text
We use Focus L-AIci as the durable memory system for this project.

When I give you retrieved Focus memories, treat them as authoritative context.
When a session produces an important decision, incident, pattern, or lesson, help me convert it into a Focus-ready memory draft.
Prefer consistency with previously stored memories.
If you think I should search Focus before proceeding, say so explicitly.
```

## Best practices for high-quality memory capture

- store decisions with the reason behind them
- store incidents with symptoms, root cause, and fix
- store patterns with when to use them and when not to
- keep titles concrete
- keep summaries tight
- keep tags consistent across similar topics
- link related memories when one explains another
- pin only high-leverage entries

## Mistakes to avoid

- dumping raw transcripts without distillation
- creating vague titles like `Notes` or `Meeting thoughts`
- over-tagging every entry
- storing trivial facts that are easy to rediscover
- failing to record *why* a choice was made
- letting important knowledge stay only in chat history

## A simple operating loop

Use this loop repeatedly:

1. **Search first**
2. **Work second**
3. **Distill third**
4. **Store fourth**
5. **Retrieve again next time**

That is how Focus L-AIci becomes genuinely valuable over time.

## If you want the best results

The most effective teams use Focus L-AIci for:

- architecture memory
- production incident memory
- implementation pattern memory
- AI workflow memory
- repeated customer or environment knowledge

When used that way, it becomes a practical local knowledge layer that makes both humans and AI assistants more consistent, faster, and less forgetful.
