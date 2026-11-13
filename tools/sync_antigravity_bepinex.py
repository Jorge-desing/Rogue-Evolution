"""Install the project's BepInEx skill and schemas into Antigravity.

Run from the repository root: python -m tools.sync_antigravity_bepinex
Use --check to compare without writing. Existing differing files are backed up.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

from tools.build_bepinex_mcp import TOOL_SPECS

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / ".agents/skills/rogue-command-bepinex-mcp"


def payloads(home: Path) -> dict[Path, bytes]:
    schema_dir = home / "mcp/bepinex_mcp"
    skill_dir = home / "skills/rogue-command-bepinex-mcp"
    result = {
        schema_dir / (spec["name"] + ".json"): json.dumps({
            "name": spec["name"], "description": spec["description"], "parameters": spec["parameters"]
        }, indent=2).encode("utf-8")
        for spec in TOOL_SPECS
    }
    text = (SOURCE / "SKILL.md").read_text(encoding="utf-8")
    # The global copy resolves project-specific supporting skills against its source checkout.
    for name in ("rogue-command-steam-runtime-validation", "rogue-command-clean-room-diagnostics", "rogue-command-harmony-policy"):
        text = text.replace(f"../{name}/SKILL.md", (SOURCE.parent / name / "SKILL.md").as_posix())
    text = text.replace("this skill's `scripts/start-server.ps1`", f"the project launcher `{(SOURCE / 'scripts/start-server.ps1').as_posix()}`")
    text = text.replace("The checked-in absolute path targets this user's checkout; adjust it for other checkouts.", "Use the repository checkout containing this skill as the source of truth.")
    text += f"\n## Antigravity installation\n\nSource checkout: `{ROOT.as_posix()}`. Run `python -m tools.sync_antigravity_bepinex` from that checkout to resync this skill and its tool schemas; use `--check` to detect drift. Use the project's `bepinex_mcp` server and its live inventory.\n"
    result[skill_dir / "SKILL.md"] = text.encode("utf-8")
    result[skill_dir / "references/ui-intermission.md"] = (SOURCE / "references/ui-intermission.md").read_bytes()
    result[skill_dir / "references/automation.md"] = (SOURCE / "references/automation.md").read_bytes()
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--home", type=Path, default=Path.home() / ".gemini/antigravity")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    files = payloads(args.home)
    changed = {path: data for path, data in files.items() if not path.is_file() or path.read_bytes() != data}
    if args.check:
        print(json.dumps({"expected": len(files), "changed": len(changed), "paths": [str(p) for p in changed]}, indent=2))
        return 1 if changed else 0
    backup = ROOT / "artifacts/antigravity-backups" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    manifest = []
    for path, data in changed.items():
        old = path.read_bytes() if path.is_file() else None
        relative = path.relative_to(args.home)
        if old is not None:
            dest = backup / relative
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, dest)
            if dest.read_bytes() != old:
                raise RuntimeError(f"Backup verification failed: {path}")
        manifest.append({"path": str(path), "beforeSha256": hashlib.sha256(old).hexdigest() if old is not None else None,
                         "afterSha256": hashlib.sha256(data).hexdigest()})
    backup.mkdir(parents=True, exist_ok=True)
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    for path, data in changed.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        if path.read_bytes() != data:
            raise RuntimeError(f"Verification failed: {path}")
    print(json.dumps({"expected": len(files), "updated": len(changed), "backup": str(backup)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
