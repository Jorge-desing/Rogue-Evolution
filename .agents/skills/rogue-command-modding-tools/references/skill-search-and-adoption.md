# Skill Search and Adoption Workflow

Use when searching for external skills, tools, or workflows to adopt into the Rogue Command mod project. Covers searching, evaluating, and documenting findings.

## Search Sources

1. **GitHub** — search for repos with skills/tools related to the task domain. Use `site:github.com` operator.
2. **skills.sh** — Hermes/Claude skills hub. Search for game dev, modding, Unity skills.
3. **Claude Code marketplace** — `mcpmarket.com`, `claudeskills.info`, `skillsdirectory.com`.
4. **Hermes hub** — `hermes skills search`, `hermes skills browse`, or web search for `site:github.com nousresearch hermes skills`.
5. **Awesome lists** — `ZeroPointRepo/awesome-hermes-skills`, `abagames/agentic-gamedev-skills`.

## Search Strategy

Run parallel web_search queries (4-8 per category). Group by category:
- Unity modding & game dev
- C# / .NET development
- AssetBundle & 3D content
- PBR & texture pipelines
- AI & game research
- Hermes hub / skills catalog

## Evaluation Criteria

For each finding, classify as:
- **Adoptar:** Install/use directly (high relevance, compatible license, clear docs)
- **Adaptar:** Modify for project (medium relevance, needs adjustments)
- **Descartar:** Not useful (low relevance, incompatible license, duplicate of existing skills)

Check: complements (not duplicates) existing skills? Compatible with Unity 2021.3 + BepInEx? Open source/free? Clear docs? Integrates with TDD workflow?

## Installing External Skills

1. Clone to scratch: `git clone --depth 1 <repo-url> $TMPDIR/<repo-name>`
2. Copy skills to Hermes: `cp -r $TMPDIR/<repo>/skills/* ~/.hermes/skills/<repo-name>/`
3. Verify: `ls ~/.hermes/skills/<repo-name>/` shows SKILL.md files
4. For project-specific skills (`.agents/skills/`), adapt from the cloned skill by rewriting for Rogue Command context.

## Creating New Hermes Skills

1. Create directory: `~/.hermes/skills/<skill-name>/`
2. Write `SKILL.md` with YAML frontmatter (name, description, version, license, author, metadata with tags/related_skills)
3. Include: When to Use, procedure steps, pitfalls, references, known limitations
4. The skill should be self-contained: a future session should follow it and produce the desired result.

## Skill Availability and Discovery

### Project Skills (`.agents/skills/`)
- Project skills in `.agents/skills/` are NOT available with `/` by default.
- Run `hermes skills trust .` from the project root to enable them.
- Trust is persistent per directory; new sessions inside the trusted repo will load them.
- If a skill is still not available after trusting, restart Hermes or start a new session.

### User-Level Skills (`~/.hermes/skills/`)
- **CRITICAL PATH**: Hermes uses `$HERMES_HOME/skills/` = `C:\Users\Alex\AppData\Local\hermes\skills/` on Windows, NOT `~/.hermes/skills/` (which resolves to `C:\Users\Alex\.hermes\skills/`). Always use `get_skills_dir()` or `$HERMES_HOME` to resolve the path — never hardcode `~/.hermes/`. Copying to the wrong directory means skills won't be discovered.
- Skills added to `$HERMES_HOME/skills/` after session start are NOT available until Hermes restarts.
- `hermes skills install` only accepts GitHub identifiers (e.g., `owner/repo`) or HTTP(S) URLs to SKILL.md files — NOT local file paths.
- To install local skills: copy them to `$HERMES_HOME/skills/<category>/<skill-name>/SKILL.md` manually, then restart Hermes.
- Verify with `hermes skills list` (CLI) or `skills_list` (in-session) after restart.

### Skill Snapshot Caching
- Hermes caches the skills list in `$HERMES_HOME/.skills_prompt_snapshot.json`.
- New skills added after session start won't appear until the cache is deleted or Hermes is restarted.
- To force rebuild: `rm $HERMES_HOME/.skills_prompt_snapshot.json` then restart Hermes.

### Project Skills and Session CWD
- Project skills in `.agents/skills/` only load when `resolve_agent_cwd()` returns a path inside the project.
- The terminal's `os.getcwd()` is NOT used — the session's bound working directory is.
- Start sessions with `--cwd "C:/Users/Alex/Documents/Mod Rogue Command"` or from the project directory.
- If `find_project_root()` returns None, check `resolve_agent_cwd()` — it may resolve to home instead of the project.

## Documenting Findings

Create `docs/research/SKILL-SEARCH-REPORT.md` with:
- Executive summary
- Per-category results (queries, URLs, descriptions)
- Candidate skills for adoption (name, URL, classification, install steps, risks)
- Conclusions (top 3 adopt, top 3 adapt, avoid list, next steps)

## Pitfalls

- Web searches may return irrelevant results or spam; use specific queries with `site:` operators.
- Community modding skills may have restrictive licenses; verify before recommending adoption.
- Skills that duplicate existing project skills (in `.agents/skills/`) should be classified as Descartar.
- The Hermes hub has no game-modding skills (only minecraft-modpack-server and pokemon-player); custom authoring is required.
- Always verify the skill format (SKILL.md with YAML frontmatter) before installing.
- **Skills not available with `/`**: The session was started before the skill was added or the project was trusted. Restart Hermes or start a new session.
- **`hermes skills install` fails on local paths**: Use manual copy to `$HERMES_HOME/skills/` instead.
- **Skills list is cached at startup**: Any skill added after session start requires a restart or cache deletion. No hot-reload for skill discovery.
- **Project skills take precedence**: When both a project skill and a user-level skill have the same name, the project skill wins (in trusted repos).
- **Wrong skills directory**: Copying to `~/.hermes/skills/` instead of `$HERMES_HOME/skills/` means skills won't be discovered. Always verify with `get_skills_dir()` or `hermes_constants.get_skills_dir()`.
- **Session cwd mismatch**: If project skills don't load, check `resolve_agent_cwd()` — it may return home instead of the project directory. Start session from project dir.
