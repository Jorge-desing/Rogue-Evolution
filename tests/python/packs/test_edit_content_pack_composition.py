"""Tests de las operaciones de composición y auto-sanación de edit_content_pack."""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")
from tools.write_composed_pack import _make_stage_dir, _publish_no_replace, write_composed_pack
from tools.validate_content_pack import validate_pack


class CompositionEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")

    def test_composed_writer_validates_before_publishing_and_preserves_existing(self) -> None:
        pack = self._pack()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            write_composed_pack(pack, [], output)
            self.assertEqual(pack, json.loads((output / "content.json").read_text(encoding="utf-8")))
            self.assertFalse(list(Path(directory).glob(".rogue-compose-*")))
            with self.assertRaises(FileExistsError):
                write_composed_pack(pack, [], output)
            invalid = dict(pack, schemaVersion=999)
            invalid_output = Path(directory) / "invalid"
            with self.assertRaisesRegex(RuntimeError, "etapa conservada"):
                write_composed_pack(invalid, [], invalid_output)
            self.assertFalse(invalid_output.exists())
            self.assertTrue(list(Path(directory).glob(".rogue-compose-*/pack/content.json")))

    def test_validator_hashes_the_same_manifest_bytes_it_parsed(self) -> None:
        pack = self._pack()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            report = {"schemaVersion": 1, "packId": pack["packId"]}
            write_composed_pack(pack, [], output, report)
            from tools.secure_pack_read import open_pack_file as real_open
            reads = []
            def tracking(root, relative, **kwargs):
                if relative == "content.json":
                    reads.append(relative)
                return real_open(root, relative, **kwargs)
            with patch("tools.validate_content_pack.open_pack_file", side_effect=tracking):
                self.assertEqual([], validate_pack(str(output)))
            self.assertEqual(["content.json"], reads)

    def test_new_staging_directory_rejects_preexisting_child(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "pack").mkdir()
            (root / "pack" / "user-data").write_text("preserve", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                _make_stage_dir(root, "pack", exist_ok=False)
            self.assertEqual("preserve", (root / "pack" / "user-data").read_text(encoding="utf-8"))

    def test_atomic_publish_never_replaces_concurrent_destination(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stage, target = root / "stage", root / "target"
            stage.mkdir()
            target.mkdir()
            (target / "user-data").write_text("preserve", encoding="utf-8")
            with self.assertRaises(OSError):
                _publish_no_replace(stage, target)
            self.assertEqual("preserve", (target / "user-data").read_text(encoding="utf-8"))
            self.assertTrue(stage.is_dir())

    @unittest.skipUnless(os.name == "nt", "Windows directory sharing semantics")
    def test_staging_parent_cannot_be_renamed_during_publication(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "output"
            def publish(stage, destination):
                with self.assertRaises(OSError):
                    stage.parent.rename(root / "stolen")
                _publish_no_replace(stage, destination)
            with patch("tools.write_composed_pack._publish_no_replace", side_effect=publish):
                write_composed_pack(self._pack(), [], output)
            self.assertTrue((output / "content.json").is_file())
            self.assertFalse((root / "stolen").exists())

    def test_extract_cli_writes_a_valid_new_pack(self) -> None:
        pack = self._pack()
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "content.json"
            output = Path(directory) / "output"
            source.write_text(json.dumps(pack), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                code = EDIT.main(["--pack", str(source), "--operation", "extract-units",
                                  "--unit-ids", "ns:alpha", "--out-dir", str(output)])
            self.assertEqual(0, code)
            self.assertEqual("ns:alpha", json.loads((output / "content.json").read_text(encoding="utf-8"))["units"][0]["id"])

    def test_extract_batch_routes_and_publishes(self) -> None:
        pack = self._pack()
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "content.json"
            script = Path(directory) / "operations.json"
            output = Path(directory) / "output"
            source.write_text(json.dumps(pack), encoding="utf-8")
            script.write_text(json.dumps([{"op": "extract-units", "unitIds": ["ns:alpha"]}]), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                code = EDIT.main(["--pack", str(source), "--operation", "batch", "--script", str(script), "--out-dir", str(output)])
            self.assertEqual(0, code)
            self.assertTrue((output / "content.json").is_file())

    def test_import_cli_and_batch_write_new_pack(self) -> None:
        destination = self._pack()
        source_pack = SCAFFOLD.generate_scaffold("other:pack", "beta", stage_slug="sb", ai_slug="ab")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dest_path, source_path = root / "dest.json", root / "source.json"
            dest_path.write_text(json.dumps(destination), encoding="utf-8")
            source_path.write_text(json.dumps(source_pack), encoding="utf-8")
            script = root / "operations.json"
            script.write_text(json.dumps([{"op": "import-units", "sourcePath": str(source_path), "unitIds": ["other:beta"]}]), encoding="utf-8")
            cases = [(["--operation", "import-units", "--source-pack", str(source_path), "--unit-ids", "other:beta"], root / "direct"),
                     (["--operation", "batch", "--script", str(script)], root / "batch")]
            for args, output in cases:
                with contextlib.redirect_stdout(io.StringIO()):
                    code = EDIT.main(["--pack", str(dest_path), *args, "--out-dir", str(output)])
                self.assertEqual(0, code)
                result = json.loads((output / "content.json").read_text(encoding="utf-8"))
                self.assertIn("other:beta", [unit["id"] for unit in result["units"]])

    def test_extract_requires_explicit_external_dependency_provider(self) -> None:
        pack = self._pack()
        pack["units"][0]["synergies"] = [{"id": "ns:link", "statName": "damage", "targetUnitIds": ["other:beta"]}]
        provider = SCAFFOLD.generate_scaffold("other:pack", "beta", stage_slug="sb", ai_slug="ab")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, dependency, output = root / "source.json", root / "dependency.json", root / "output"
            source.write_text(json.dumps(pack), encoding="utf-8")
            dependency.write_text(json.dumps(provider), encoding="utf-8")
            arguments = ["--pack", str(source), "--operation", "extract-units", "--unit-ids", "ns:alpha", "--out-dir", str(output)]
            with contextlib.redirect_stdout(io.StringIO()) as output_text, contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1, EDIT.main(arguments))
                self.assertFalse(output.exists())
                self.assertEqual(0, EDIT.main(arguments + ["--dependency-pack", str(dependency)]))
            self.assertTrue((output / "content.json").is_file())
            self.assertIn("other:pack", output_text.getvalue())
            report = json.loads((output / "composition-dependencies.json").read_text(encoding="utf-8"))
            self.assertEqual(["other:pack"], report["requiredPackIds"])
            self.assertEqual(hashlib.sha256((output / "content.json").read_bytes()).hexdigest(), report["contentSha256"])
            self.assertEqual([], validate_pack(str(output)))
            with (output / "content.json").open("a", encoding="utf-8") as stream:
                stream.write(" ")
            self.assertTrue(any("obsoleto" in error for error in validate_pack(str(output))))

    def test_composition_rejects_duplicate_pack_id_even_without_entity_collisions(self) -> None:
        pack = self._pack()
        dependency = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, other, output = root / "source.json", root / "other.json", root / "output"
            source.write_text(json.dumps(pack), encoding="utf-8")
            other.write_text(json.dumps(dependency), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                code = EDIT.main(["--pack", str(source), "--operation", "extract-units", "--unit-ids", "ns:alpha",
                                  "--dependency-pack", str(other), "--out-dir", str(output)])
            self.assertEqual(1, code)
            self.assertFalse(output.exists())

    def test_composition_carries_referenced_independent_building(self) -> None:
        source = self._pack()
        source["units"][0]["copyUpgradesToEntityId"] = "ns:support"
        source["buildings"] = [{"id": "ns:support", "nativeTemplateId": "native"}]
        extracted, errors = EDIT.extract_units_to_pack(source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual(["ns:support"], [row["id"] for row in extracted["buildings"]])
        destination = SCAFFOLD.generate_scaffold("other:pack", "beta", stage_slug="sb", ai_slug="ab")
        imported, errors = EDIT.import_units(destination, source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual(["ns:support"], [row["id"] for row in imported["buildings"]])
        destination["buildings"] = [{"id": "ns:support", "nativeTemplateId": "different"}]
        unchanged, errors = EDIT.import_units(destination, source, ["ns:alpha"])
        self.assertTrue(errors)
        self.assertEqual(destination, unchanged)

    def test_extract_rejects_unit_building_id_collision(self) -> None:
        source = self._pack()
        source["buildings"] = [{"id": "ns:alpha"}]
        extracted, errors = EDIT.extract_units_to_pack(source, ["ns:alpha"])
        self.assertEqual({}, extracted)
        self.assertTrue(any("conflicto" in error for error in errors))

    def test_composition_carries_explicit_unit_cards_and_dependencies(self) -> None:
        source = self._pack()
        extra = SCAFFOLD.generate_scaffold("ns:other", "beta")
        source["units"].extend(extra["units"])
        source["relics"] = [{"id": "ns:relic", "cardChanges": [{"onlyForTheseEntityIds": ["ns:alpha", "ns:beta"]}]}]
        source["upgrades"] = [{"id": "ns:upgrade", "cardChanges": [{"onlyForTheseEntityIds": ["ns:alpha"]}]}]
        extracted, errors = EDIT.extract_units_to_pack(source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual({"ns:alpha", "ns:beta"}, {row["id"] for row in extracted["units"]})
        self.assertEqual("ns:relic", extracted["relics"][0]["id"])
        destination = SCAFFOLD.generate_scaffold("other:pack", "gamma", stage_slug="sc", ai_slug="ac")
        imported, errors = EDIT.import_units(destination, source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual("ns:upgrade", imported["upgrades"][0]["id"])
        destination["relics"] = [{"id": "ns:relic", "cardChanges": []}]
        unchanged, errors = EDIT.import_units(destination, source, ["ns:alpha"])
        self.assertTrue(errors)
        self.assertEqual(destination, unchanged)

    def test_filter_only_cards_are_conservatively_preserved(self) -> None:
        source = self._pack()
        source["relics"] = [{"id": "ns:role", "cardChanges": [{"cardMustHaveOneOfTheseRoles": ["Tank"]}]}]
        source["upgrades"] = [{"id": "ns:side", "cardChanges": [{"side": "True"}]}]
        extracted, errors = EDIT.extract_units_to_pack(source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual("ns:role", extracted["relics"][0]["id"])
        self.assertEqual("ns:side", extracted["upgrades"][0]["id"])
        destination = SCAFFOLD.generate_scaffold("other:pack", "beta", stage_slug="sb", ai_slug="ab")
        imported, errors = EDIT.import_units(destination, source, ["ns:alpha"])
        self.assertEqual([], errors)
        self.assertEqual("ns:role", imported["relics"][0]["id"])

    def test_composition_reports_broad_filter_cards(self) -> None:
        pack = self._pack()
        pack["relics"] = [{"id": "ns:role", "cardChanges": [{"cardMustHaveOneOfTheseRoles": ["Tank"]}]}]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, output = root / "source.json", root / "output"
            source.write_text(json.dumps(pack), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()) as errors:
                with patch("tools.write_composed_pack.write_composed_pack", return_value=output) as writer:
                    result = EDIT.main(["--pack", str(source), "--operation", "extract-units", "--unit-ids", "ns:alpha", "--out-dir", str(output)])
            self.assertEqual(0, result)
            self.assertIn("relics:ns:role", errors.getvalue())
            self.assertEqual(["relics:ns:role"], writer.call_args.args[3]["broadFilterCards"])

    def test_extract_units_to_pack(self) -> None:
        pack = self._pack()
        extracted, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:alpha"])
        self.assertEqual([], diagnostics)
        self.assertEqual(["ns:alpha"], [u["id"] for u in extracted["units"]])
        self.assertEqual("ns:pack", extracted["packId"])
        # stage/AI kept only if they reference extracted units
        self.assertTrue(extracted["stages"])
        extracted["units"][0]["id"] = "ns:changed"
        self.assertEqual("ns:alpha", pack["units"][0]["id"])

    def test_extract_unknown_unit_fails(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:ghost"])
        self.assertTrue(diagnostics)

    def test_composition_closes_stage_ai_factory_and_synergy_dependencies(self) -> None:
        pack = self._pack()
        beta = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")["units"][0]
        gamma = SCAFFOLD.generate_scaffold("ns:pack", "gamma", stage_slug="sg", ai_slug="ag")["units"][0]
        pack["units"].extend([beta, gamma])
        pack["stages"][0]["blueprintPoolEntries"] = [{"poolId": "P", "unitIds": ["ns:beta"]}]
        pack["aiProfiles"][0]["productionBuildingEntries"] = [{"unitId": "ns:beta"}]
        beta["synergies"] = [{"id": "ns:s", "statName": "damage", "targetUnitIds": ["ns:gamma"]}]
        extracted, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:alpha"])
        self.assertEqual([], diagnostics)
        self.assertEqual({"ns:alpha", "ns:beta", "ns:gamma"}, {row["id"] for row in extracted["units"]})
        self.assertIn("ns:aa", {row["id"] for row in extracted["aiProfiles"]})

    def test_composition_includes_profile_that_references_selected_factory(self) -> None:
        pack = self._pack()
        pack["stages"][0]["unitIds"] = []
        pack["aiProfiles"][0]["modules"] = [{"moduleId": "sample:scheduled-base-build", "parameters": [
            {"name": "entityId", "value": pack["units"][0]["productionBuilding"]["id"]}]}]
        extracted, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:alpha"])
        self.assertEqual([], diagnostics)
        self.assertEqual(["ns:aa"], [row["id"] for row in extracted["aiProfiles"]])
        self.assertEqual(["ns:sa"], [row["id"] for row in extracted["stages"]])

    def test_import_units_from_pack(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        updated, diagnostics = EDIT.import_units(pack, other, ["ns:beta"])
        self.assertEqual([], diagnostics)
        self.assertIn("ns:beta", [u["id"] for u in updated["units"]])

        _, d2 = EDIT.import_units(pack, other, ["ns:alpha"])
        self.assertTrue(d2)  # already exists

    def test_import_rejects_cross_kind_and_repeated_identity_collisions(self) -> None:
        for mode in ("unit-is-factory", "factory-is-unit", "duplicate"):
            with self.subTest(mode=mode):
                dest = self._pack()
                source = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
                unit = source["units"][0]
                selected = ["ns:beta"]
                if mode == "unit-is-factory":
                    unit["id"] = dest["units"][0]["productionBuilding"]["id"]
                    selected = [unit["id"]]
                elif mode == "factory-is-unit":
                    unit["productionBuilding"]["id"] = "ns:alpha"
                else:
                    selected.append("ns:beta")
                result, diagnostics = EDIT.import_units(dest, source, selected)
                self.assertTrue(diagnostics)
                self.assertEqual(dest, result)

    def test_import_carries_bundle_declarations_and_rejects_conflicts_atomically(self) -> None:
        dest = self._pack()
        source = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        source["units"][0]["productionBuilding"]["assetBundleId"] = "ns:factory-art"
        source["assetBundles"] = [{"id": "ns:factory-art", "path": "bundles/factory.bundle"}]
        source["assetBundleManifestPath"] = "bundles/manifest.bundle"
        result, diagnostics = EDIT.import_units(dest, source, ["ns:beta"])
        self.assertEqual([], diagnostics)
        self.assertIn(source["assetBundles"][0], result["assetBundles"])
        self.assertEqual("bundles/manifest.bundle", result["assetBundleManifestPath"])
        conflicting = json.loads(json.dumps(dest))
        conflicting["assetBundles"] = [{"id": "ns:factory-art", "path": "bundles/other.bundle"}]
        result, diagnostics = EDIT.import_units(conflicting, source, ["ns:beta"])
        self.assertTrue(diagnostics)
        self.assertEqual(conflicting, result)
        missing = json.loads(json.dumps(source))
        missing["assetBundles"] = []
        result, diagnostics = EDIT.import_units(dest, missing, ["ns:beta"])
        self.assertTrue(diagnostics)
        self.assertEqual(dest, result)
        conflicting_stage = json.loads(json.dumps(dest))
        conflicting_stage["stages"].append({"id": "ns:sb", "unitIds": []})
        result, diagnostics = EDIT.import_units(conflicting_stage, source, ["ns:beta"])
        self.assertTrue(diagnostics)
        self.assertEqual(conflicting_stage, result)

    def test_purge_dangling_references(self) -> None:
        pack = self._pack()
        pack["stages"][0]["unitIds"].append("ns:ghost")
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:ghost", "ns:alpha"]}
        ]
        pack["stages"][0]["aiProfileIds"].append("ns:ghost-profile")

        pack["aiProfiles"][0]["productionBuildingEntries"] = [{"unitId": "other:valid"}, {"unitId": "ns:ghost"}]
        pack["stages"][0]["blueprintPoolEntries"][0]["unitIds"].append("PCXBaseTank")
        unchanged, cleared_empty = EDIT.purge_dangling_references(pack)
        self.assertEqual(pack, unchanged)
        self.assertEqual([], cleared_empty)
        updated, cleared = EDIT.purge_dangling_references(pack, ["ns:ghost"], ["ns:ghost-profile"])
        # ghost purged
        self.assertNotIn("ns:ghost", updated["stages"][0]["unitIds"])
        self.assertNotIn("ns:ghost", updated["stages"][0]["blueprintPoolEntries"][0]["unitIds"])
        self.assertNotIn("ns:ghost-profile", updated["stages"][0]["aiProfileIds"])
        self.assertIn("ns:ghost", cleared)
        self.assertEqual([{"unitId": "other:valid"}], updated["aiProfiles"][0]["productionBuildingEntries"])
        self.assertIn("PCXBaseTank", updated["stages"][0]["blueprintPoolEntries"][0]["unitIds"])
        batched, diagnostics = EDIT.apply_edits(pack, [{"op": "purge-dangling", "unitIds": ["ns:ghost"], "profileIds": ["ns:ghost-profile"]}])
        self.assertEqual(updated, batched)
        self.assertTrue(diagnostics)

    def test_purge_dangling_cli_uses_explicit_ids(self) -> None:
        pack = self._pack()
        pack["stages"][0]["unitIds"].extend(["ns:ghost", "other:valid"])
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "content.json"
            source.write_text(json.dumps(pack), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                result = EDIT.main(["--pack", str(source), "--operation", "purge-dangling",
                                    "--dangling-unit-ids", "ns:ghost", "--dry-run"])
            self.assertEqual(0, result)
            self.assertIn('"other:valid"', output.getvalue())
            self.assertNotIn('"ns:ghost"', output.getvalue())
            self.assertEqual(pack, json.loads(source.read_text(encoding="utf-8")))

    def test_rename_stage_and_profile(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.rename_stage(pack, "ns:sa", "ns:frontline")
        self.assertEqual([], diagnostics)
        self.assertIn("ns:frontline", [s["id"] for s in updated["stages"]])

        updated2, d2 = EDIT.rename_ai_profile(updated, "ns:aa", "ns:guard")
        self.assertEqual([], d2)
        self.assertIn("ns:guard", [p["id"] for p in updated2["aiProfiles"]])
        self.assertIn("ns:guard", updated2["stages"][0]["aiProfileIds"])

    def test_stage_and_profile_operations_are_routed_by_batch_and_cli(self) -> None:
        cases = [
            ("rename-stage", "stageId", "ns:sa", "--stage-id", "stages"),
            ("clone-stage", "stageId", "ns:sa", "--stage-id", "stages"),
            ("rename-ai-profile", "profileId", "ns:aa", "--profile-id", "aiProfiles"),
            ("clone-ai-profile", "profileId", "ns:aa", "--profile-id", "aiProfiles"),
        ]
        for operation, key, original, flag, section in cases:
            with self.subTest(operation=operation):
                pack = self._pack()
                updated, diagnostics = EDIT.apply_edits(pack, [{"op": operation, key: original, "newId": "ns:new"}])
                self.assertEqual([], diagnostics)
                self.assertIn("ns:new", [row["id"] for row in updated[section]])
                invalid, errors = EDIT.apply_edits(pack, [{"op": operation, key: original, "newId": ""}])
                self.assertTrue(errors)
                self.assertEqual(pack, invalid)
                with tempfile.TemporaryDirectory() as directory:
                    source = Path(directory) / "content.json"
                    source.write_text(json.dumps(pack), encoding="utf-8")
                    with contextlib.redirect_stdout(io.StringIO()) as output:
                        code = EDIT.main(["--pack", str(source), "--operation", operation, flag, original,
                                          "--new-id", "ns:new", "--dry-run"])
                    self.assertEqual(0, code)
                    self.assertIn('"ns:new"', output.getvalue())

    def test_bulk_set_balance(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        pack["units"].append(other["units"][0])

        updated, diagnostics = EDIT.bulk_set_balance(pack, "rarity", "Epic")
        self.assertEqual([], diagnostics)
        for unit in updated["units"]:
            values = {b["name"]: b["value"] for b in unit["balanceOverrides"]}
            self.assertEqual("Epic", values["rarity"])

    def test_clone_stage_and_profile(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.clone_stage(pack, "ns:sa", "ns:clone")
        self.assertEqual([], diagnostics)
        self.assertEqual(2, len(updated["stages"]))

        updated2, d2 = EDIT.clone_ai_profile(updated, "ns:aa", "ns:clone-ai")
        self.assertEqual([], d2)
        self.assertEqual(2, len(updated2["aiProfiles"]))


if __name__ == "__main__":
    unittest.main()
