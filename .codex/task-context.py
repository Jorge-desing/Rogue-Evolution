"""Deliver repository startup guidance to Codex before each user turn."""

import json
from pathlib import Path
import re
import sys


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


def excerpt(relative_path, heading=None):
    """Read one startup section from its canonical file."""
    try:
        source = (ROOT / relative_path).read_text(encoding="utf-8-sig")
    except OSError:
        return f"{relative_path}: unavailable in this checkout."
    if heading:
        _, found, source = source.partition(f"\n{heading}\n")
        if not found:
            return f"{relative_path}: startup section missing. Open the file directly."
    else:
        source = source.lstrip()
    if relative_path == "AGENTS.md":
        # The routing table is followed by project rules at the same heading level.
        routes = [line for line in source.splitlines() if line.startswith("|")]
        return f"{relative_path} routing table\n" + "\n".join(routes)
    section = re.split(r"\n#{1,3} ", source, maxsplit=1)[0].strip()
    return f"{relative_path}\n{section.split("\n\n", 1)[0][:1000]}"


def main():
    request = json.load(sys.stdin)
    if request.get("hook_event_name") != "UserPromptSubmit":
        return
    prompt = request.get("prompt", "")
    guides = dict.fromkeys(
        guide
        for pattern, paths in ROUTES
        if re.search(r"\b(?:" + pattern + r")\b", prompt, re.IGNORECASE)
        for guide in paths
    )
    context = [
        "Rogue Evolution task startup. Read README.md and docs/research/AGENT-HANDOFF.md. "
        "Apply every relevant AGENTS.md route, including explicitly named skills. "
        "These excerpts orient the task. Open the complete applicable guides before acting.",
        excerpt("AGENTS.md", "### Load task documentation at startup"),
    ]
    for guide in guides:
        source = (ROOT / guide)
        heading = next(
            (line for line in source.read_text(encoding="utf-8-sig").splitlines()
             if line.startswith(("## Start here", "## Load before", "## Load context", "**Current policy:**"))),
            None,
        ) if source.is_file() else None
        if heading and heading.startswith("**"):
            context.append(f"{guide}\n{heading}")
        else:
            context.append(excerpt(guide, heading))
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "UserPromptSubmit",
        "additionalContext": "\n\n".join(context),
    }}))


if __name__ == "__main__":
    main()
