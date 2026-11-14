"""Deliver repository startup guidance to Codex before each user turn."""

import json
from pathlib import Path
import re
import sys
import unicodedata


ROOT = Path(__file__).resolve().parents[1]
ROUTES = (
    (r"packs?|unidad\w*|units?|stage\w*|etapa\w*|ai|blueprint\w*",
     ("content/schema/README.md", "docs/research/CONTENT-AUTHORING-TOOLS.md")),
    (r"loader|cargador|registry|registro|tests?|pruebas?|validaci[oó]n",
     ("docs/research/CONTENT-LOADER-TOOLS.md",)),
    (r"harmony|rea|reflection|reflexi[oó]n|decompil\w*|ingenier[ií]a inversa",
     ("docs/research/MANAGED-DLL-DECOMPILATION.md",)),
    (r"steam|intermission|gameplay|runtime|bepinex|partida\w*",
     ("docs/research/STEAM-RUNTIME-CONTROL.md",)),
    (r"blender|unity|fbx|rig\w*|model\w*|prefab\w*",
     ("docs/research/UNIT-VISUAL-AUTHORING.md",)),
    (r"pbr|textur\w*|material\w*|shader\w*",
     ("docs/research/PBR-TEXTURE-WORKFLOW.md",)),
    (r"doc\w*|handoff|readme|runbook\w*|agents\.md",
     ("docs/AGENT-DOCUMENTATION.md",)),
)


MAX_CONTEXT_CHARS = 6000
STARTUP_HEADING = "### Load task documentation at startup"
SKILL_MENTION = re.compile(r"\[\$([a-z0-9][a-z0-9-]*)\]", re.IGNORECASE)


def excerpt(relative_path):
    """Read the routing table or one short paragraph from a canonical file."""
    try:
        source = (ROOT / relative_path).read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError):
        return f"{relative_path}: unavailable in this checkout."
    lines = source.splitlines()
    if relative_path == "AGENTS.md":
        try:
            first = lines.index(STARTUP_HEADING)
            table = next(i for i in range(first + 1, len(lines)) if lines[i].startswith("|"))
        except (ValueError, StopIteration):
            return "AGENTS.md: open the startup routing section directly."
        rows = []
        for line in lines[table:]:
            if not line.startswith("|"):
                break
            rows.append(line)
        return "AGENTS.md routing table\n" + "\n".join(rows)

    for index, line in enumerate(lines):
        if line.startswith("**Current policy:**"):
            return f"{relative_path}\n{line[:600]}"
        if line.startswith(("## Start here", "## Load before", "## Load context")):
            source = "\n".join(lines[index + 1:]).strip()
            break
    else:
        source = source.partition("\n")[2].strip()
    paragraph = re.split(r"\n\s*\n|\n#", source, maxsplit=1)[0]
    return f"{relative_path}\n{paragraph[:600]}"


def main():
    try:
        request = json.load(sys.stdin)
    except (ValueError, UnicodeError):
        return
    if not isinstance(request, dict) or request.get("hook_event_name") != "UserPromptSubmit":
        return
    prompt = request.get("prompt")
    if not isinstance(prompt, str):
        prompt = ""
    terms = unicodedata.normalize("NFC", prompt)
    guides = dict.fromkeys(
        guide
        for pattern, paths in ROUTES
        if re.search(r"\b(?:" + pattern + r")\b", terms, re.IGNORECASE)
        for guide in paths
    )
    context = [
        "Rogue Evolution task startup. Read README.md and docs/research/AGENT-HANDOFF.md. "
        "Apply every relevant AGENTS.md route, including explicitly named skills. "
        "These excerpts orient the task. Open the complete applicable guides before acting.",
        excerpt("AGENTS.md"),
    ]
    # Resolve only named repository skills. Ignore the link target in the prompt.
    explicit = dict.fromkeys(
        match.group(1).lower() for match in SKILL_MENTION.finditer(prompt)
    )
    for name in explicit:
        path = f".agents/skills/{name}/SKILL.md"
        if (ROOT / path).is_file():
            guides.setdefault(path, None)

    ordered_guides = sorted(guides, key=lambda path: not path.startswith(".agents/skills/"))
    for guide in ordered_guides:
        entry = (f"Explicitly selected skill: {guide}"
                 if guide.startswith(".agents/skills/") else excerpt(guide))
        if sum(len(block) + 2 for block in context) + len(entry) + 2 > MAX_CONTEXT_CHARS:
            break
        context.append(entry)
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "UserPromptSubmit",
        "additionalContext": "\n\n".join(context)[:MAX_CONTEXT_CHARS],
    }}))


if __name__ == "__main__":
    main()
