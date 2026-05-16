---
name: investigate-static-asset-failures
description: Trace missing CSS and JS quickly by verifying launch root, asset paths, MIME types, and browser-visible GET responses.
category: Task
pinned: true
wing: reusable-patterns
trigger_hints: css, js, static files, mime, bootstrap, site.css, site.js
---

# Investigate static asset failures

Trace missing CSS and JS quickly by verifying launch root, asset paths, MIME types, and browser-visible GET responses.

## When to Use This Skill

Use this when the site renders unstyled, scripts stop loading, or the browser reports MIME errors for static assets.

## Workflow Overview

1. Confirm the app is running from the intended content root or publish root.
2. Check the rendered page for the exact asset URLs being referenced.
3. Fetch the asset URLs with browser-like GET requests and inspect status, content length, and content type.
4. If assets fail, trace static-file middleware, content root resolution, and any stale or dead asset references in layouts.
5. Re-run the app the same way users launch it before concluding the fix works.

## Examples

- Investigate why site.css is not loading.
- Check whether the running Focus instance is serving bootstrap and site.js correctly.
- Trace a MIME type error on a static asset URL.
