import importlib.util
import io
import json
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
TASK_CONTEXT_PATH = ROOT / ".codex" / "task-context.py"


def _load_task_context_module():
    spec = importlib.util.spec_from_file_location("codex_task_context", TASK_CONTEXT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class TaskContextRoutingTests(unittest.TestCase):
    def _run_hook(self, prompt: str) -> str:
        module = _load_task_context_module()
        payload = json.dumps({"hook_event_name": "UserPromptSubmit", "prompt": prompt})
        stdout = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(payload)), mock.patch.object(sys, "stdout", stdout):
            module.main()
        parsed = json.loads(stdout.getvalue())
        context = parsed["hookSpecificOutput"]["additionalContext"]
        self.assertLessEqual(len(context), module.MAX_CONTEXT_CHARS)
        return context

    def test_runtime_and_explorer_keywords_surface_bepinex_mcp_capabilities(self):
        context = self._run_hook("Inspecciona los singletons y DontDestroyOnLoad en la escena activa del juego")
        self.assertIn(".agents/skills/rogue-command-bepinex-mcp/SKILL.md", context)
        self.assertIn("diagnostics_explorer_query", context)
        self.assertIn("diagnostics_explorer_member", context)
        self.assertIn("diagnostics_get_runtime_control_surface", context)

    def test_harmony_and_patch_keywords_surface_harmony_policy_capabilities(self):
        context = self._run_hook("Necesito revisar un transpiler y los patches de Assembly-CSharp")
        self.assertIn(".agents/skills/rogue-command-harmony-policy/SKILL.md", context)
        self.assertIn("RuntimePatchInstaller", context)
        self.assertIn("inspect_patches", context)
        self.assertIn("mod_patch_method", context)

    def test_explicit_skill_mention_includes_capability_excerpt(self):
        context = self._run_hook("[$rogue-command-bepinex-mcp] verifica el UI")
        self.assertIn("Explicitly selected skill: .agents/skills/rogue-command-bepinex-mcp/SKILL.md", context)
        self.assertIn("diagnostics_explorer_query", context)


if __name__ == "__main__":
    unittest.main()
