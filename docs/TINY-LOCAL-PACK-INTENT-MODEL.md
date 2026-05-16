# Tiny Local Pack Intent Model

This document explains the small built-in model that helps Focus L-AIci decide **what kind of task a context-pack question is asking about**.

It is not a generative model and it is not a neural network. It is a **tiny local classifier** that runs inside the app process and helps the retrieval/ranking pipeline behave more intelligently.

## What problem it solves

Context-pack quality depends heavily on asking the right retrieval system the right question.

Before this model, generic keyword matching could confuse requests such as:

- Active Directory / automation questions
- repository/code questions
- architecture/codebase overview questions
- broad scripting and reporting asks

The tiny model gives Focus a fast first guess about the **intent lane** of the question, so the rest of the app can:

- suppress obviously wrong result classes
- rank the right memories and skills higher
- avoid code-graph noise for non-code questions
- keep ambiguous packs safer by preferring empty-over-wrong in some lanes

## Where it lives

- Interface: `FocusLAIci.Web\Services\PackIntentModel.cs`
- Runtime implementation: `TinyLocalPackIntentModel`
- DI registration: `FocusLAIci.Web\Program.cs`

The app registers it here:

```csharp
builder.Services.AddSingleton<IPackIntentModel, TinyLocalPackIntentModel>();
```

That keeps the pack builder decoupled from any one implementation. The rest of the app depends on `IPackIntentModel`, not on a hard-coded concrete class.

## What it predicts

The model outputs a `PackIntentPrediction` with five scores:

1. `ExternalOperationsScore`
2. `DirectoryAdminScore`
3. `CodeIntentScore`
4. `GenericAutomationScore`
5. `RepositoryArchitectureScore`

Each score is converted into a boolean decision using lane-specific thresholds:

- external operations: `>= 0.54`
- directory admin: `>= 0.56`
- explicit code intent: `>= 0.56`
- generic automation: `>= 0.55`
- repository architecture: `>= 0.56`

## How it works

The model is intentionally simple:

1. normalize the question to lowercase text
2. tokenize it with a small fixed separator set
3. score each lane with weighted lexical features
4. add a lane-specific bias
5. pass the raw score through a sigmoid
6. compare the probability to the lane threshold

In code terms, each lane is just a list of weighted features like:

- positive signals such as `powershell`, `active directory`, `ldap`, `repo`, `architecture`
- negative signals that push a lane down when the question clearly belongs somewhere else
- phrase features for cases where exact multi-word expressions matter more than single tokens

This makes the model:

- deterministic
- tiny
- cheap to run
- easy to inspect and tune
- easy to replace later

## Current shape

At the time of writing, the model has:

- **5 intent heads**
- **155 weighted lexical features**
- **5 biases**
- **5 thresholds**
- model id: `tiny-local-pack-intent-v2`

That makes it closer to a hand-tuned logistic classifier than to a neural network.

## Integration path

The model is consumed directly by `ContextService` when building a context pack:

```csharp
var intentPrediction = _packIntentModel.Predict(normalizedQuestion);
```

That prediction then influences downstream behavior such as:

- whether the question should be treated as external admin work
- whether it has explicit code intent
- whether generic automation should stay separated from directory-admin intent
- whether code-graph sections should be suppressed
- how skills are filtered and reranked

## How it affects `ContextService`

`ContextService` uses the prediction as an early routing signal before retrieval is finalized.

Examples:

- **Directory / AD asks** can demote or suppress irrelevant code-graph results.
- **Explicit code asks** can keep code graph and repo-oriented matches active.
- **Architecture asks** can stay routed toward repo/codebase explanation instead of operational noise.
- **Automation asks** can stay in scripting/reporting space without drifting into unrelated product context.

The key point is that the model does **not** generate the pack. It changes how the pack builder **interprets the question**.

## How it affects `SkillRecommendationEngine`

`SkillRecommendationEngine` also accepts the same `PackIntentPrediction`.

That lets skill recommendations align with the same intent lane the pack builder is using, instead of each part of the app making separate guesses.

This is especially useful for:

- suppressing skills on some non-skill-heavy local-support asks
- narrowing directory-admin skills for AD / email-attribute questions
- preferring cloud/web/desktop/repo skills only when the question shape supports them

## Why this approach was chosen

The goal was to improve pack quality **without** pulling in a larger runtime or local LLM.

This approach was chosen because it is:

- small enough to stay effectively free in disk/RAM terms
- fast enough to run on every pack build
- inspectable enough to debug with tests
- pluggable enough to replace later with a stronger local classifier if needed

Most of the remaining pack-quality work in Focus is still about **retrieval policy and reranking**, not about making this model dramatically larger.

## What it is not

This tiny model is **not**:

- self-learning
- online-training itself from production traffic
- doing embeddings
- doing semantic generation
- replacing the retrieval engine

It is a compact local decision layer that helps the retrieval engine choose the right path.

## If we ever replace it

Because the app depends on `IPackIntentModel`, a future implementation could swap in:

- a retrained lightweight local classifier
- an ONNX-based tiny model
- a richer reranker/classifier pair

without rewriting the entire dashboard/context workflow.

That interface seam is the main integration decision that makes future upgrades practical.
