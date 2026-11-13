"""Loader hygiene guards: diagnostic counters-first ordering."""
from pathlib import Path
import re
import unittest

REPO_ROOT = Path(__file__).resolve().parents[2]
FORMATTER = REPO_ROOT / "src" / "RogueCommandExpansionLoader" / "Runtime" / "AgentDiagnosticsFormatter.cs"
SCOPE = REPO_ROOT / "src" / "RogueCommandExpansionLoader" / "Runtime" / "BlueprintRewardFactoryScope.cs"


class TestDiagnosticCountersFirst(unittest.TestCase):
    def test_formatter_views_put_omitted_before_collections(self):
        src = FORMATTER.read_text(encoding="utf-8")
        # For each view dict that declares an omitted* counter, the counter
        # must appear before any collection key in that dict literal.
        # Approximation: find omitted lines and ensure a known collection
        # name does not appear on an earlier line within the same view.
        views = re.findall(r'new Dictionary<string, object>\s*\{([^}]+)\}', src, re.DOTALL)
        self.assertGreater(len(views), 20, "expected many serializer views")
        checked = 0
        for view in views:
            if '"omitted' not in view:
                continue
            omitted_idx = view.find('"omitted')
            # collections that must come after
            for coll in ['"recommendations"', '"combatActiveUnits"', '"units"', '"rows"', '"nodes"']:
                coll_idx = view.find(coll)
                if coll_idx != -1:
                    self.assertLess(omitted_idx, coll_idx, f"omitted must precede {coll} in view: {view[:120]}")
            checked += 1
        self.assertGreaterEqual(checked, 5, "too few omitted-counter views found")

    def test_blueprint_scope_counters_before_offered_products(self):
        src = SCOPE.read_text(encoding="utf-8")
        omitted = src.find("omittedIdObservations")
        offered = src.find("offeredProducts=")
        self.assertGreater(omitted, -1, "missing omittedIdObservations")
        self.assertGreater(offered, -1, "missing offeredProducts")
        self.assertLess(omitted, offered, "counters must precede offeredProducts in trace")



if __name__ == "__main__":
    unittest.main()
