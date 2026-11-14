# Agent documentation contract

## Load context at task start

Codex loads the project hook in `.codex/hooks.json` when the hook is trusted in the client. Its `UserPromptSubmit` handler reads startup excerpts from the current checkout and delivers them as context. It routes by task terms, includes multiple matching areas, and always includes the full routing table. The prompt is processed locally and is not stored by the hook. Explicit repository skill mentions take priority. Output is capped at 6,000 characters and a missing or unreadable guide leaves the other routes available. Keep the guide paths in `.codex/task-context.py` aligned with the table when adding a workflow.

Use the startup routing table in [AGENTS.md](../AGENTS.md) to open the documentation that applies to the current task before making changes. When a new canonical guide introduces a distinct workflow, update the startup route that should surface it. Keep the route short enough to scan every time.


Repository documents serve agents implementing, operating, reviewing, or resuming this project. A new agent should be able to find the governing rule, execute the applicable workflow, verify its outcome, and identify the next action using the repository and its evidence.

## Where information belongs

| Document | Put here |
| --- | --- |
| [AGENTS.md](../AGENTS.md) | Durable project rules, authorization boundaries, and required checks |
| [README](../README.md) | Entry points, repository layout, and common commands |
| [Agent handoff](research/AGENT-HANDOFF.md) | Dated decisions, evidence from completed passes, blockers, and next actions |
| Skills and runbooks | Ordered procedures, prerequisites, input limits, outputs, failure handling, and cleanup |
| Schemas and specifications | Authoritative fields, types, invariants, and compatibility rules |

Keep each instruction at its canonical location and link to it from related documents. Update the canonical instruction when behavior changes. Preserve dated evidence and mark it superseded when the replacement affects the next action.

## Write for execution

- State when the procedure applies and link to the exact files, tools, or contracts the agent should open.
- Give commands with the working directory, required inputs, and relevant versions. Label illustrative values and say how to obtain real ones.
- State what proves success, what output to inspect, and what to do when the prerequisite, command, or postcondition fails. For mutations, include the authorized scope and any required rollback or cleanup.
- Distinguish static inspection, local build or validation, and in-game runtime evidence. Identify the checked commit or artifact and link to the report. Flag evidence that is missing or must be refreshed.
- Use stable identifiers, searchable headings, and repository-relative links. Keep long logs and generated output under the relevant artifact directory and cite the path and decisive result. Keep credentials out of examples and reports.

## Leave a resumable handoff

For a completed pass that affects continuation, record the change and its source paths, the verification command and outcome, the remaining gaps, and the next concrete action. Include the exact process or job handle if work is still running, and explain how to check its current state. If a step depends on authorization, an external state, or a specific artifact, record what is required and how to recognize when it is available.
