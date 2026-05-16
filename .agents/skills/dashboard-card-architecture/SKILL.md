---
name: dashboard-card-architecture
description: Build cleaner dashboard layouts with consistent cards, panels, and grid structure. Use when requests mention "split this card", "use more width", "make cards uniform", "panel layout", or "dashboard grid".
---

# Dashboard Card Architecture

Turn an overloaded dashboard into a structured grid of purpose-specific cards with consistent spacing, width, and alignment.

## When to Use This Skill

- One card is trying to do too many jobs
- The dashboard wastes width or leaves awkward empty rails
- Card sizes feel inconsistent or random
- Related content needs to be grouped into clearer sections or panels
- You need to decide how many columns a dashboard should use

## Workflow Overview

1. Inventory every visible card and define its single job.
2. Split any card that mixes brand, action, metrics, and deep detail in one surface.
3. Group related cards into sections such as summary, work status, context, recommendations, and utilities.
4. Apply a grid that uses the available page width intentionally.
5. Normalize card family traits: padding, heading treatment, border radius, and internal spacing.

## Layout Rules

- Use the grid as the organizing backbone; avoid one-off card widths unless there is a strong reason.
- Keep cards purpose-specific: one dominant action, one data summary, or one list surface.
- Let wide layouts use width for meaningful parallel cards, not decorative whitespace.
- Reserve full-width cards for search, key actions, or broad utility surfaces.
- Use panel groupings to break the page into digestible zones.
- Keep similar cards visually consistent so users can scan by pattern.

## Examples

- "Break this giant dashboard card into smaller uniform cards."
- "Use more of the page width and remove the empty right side."
- "Put these two utility cards in their own row."
- "Make the dashboard feel more modular and less stitched together."

