import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tests.python.helpers import load_tool_module

runtime_profile_checkpoint = load_tool_module("runtime_profile_checkpoint")


class RuntimeProfileCheckpointTests(unittest.TestCase):
    def _make_layout(self):
        root = Path(tempfile.mkdtemp())
        profiles = root / "Profiles"
        profiles.mkdir()
        (profiles / "Profile1").mkdir()
        (profiles / "Profile2").mkdir()
        (profiles / "Profile1" / "metaSavegame.dat").write_bytes(b"profile-1")
        (profiles / "Profile2" / "savegame.dat").write_bytes(b"profile-2-save")
        (profiles / "Profile2" / "metaSavegame.dat").write_bytes(b"profile-2-meta")
        selector = profiles / "currentProfile.txt"
        selector.write_bytes(b"2\n")
        return root, profiles, selector

    def test_create_checkpoint_covers_all_profiles_and_selector(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "artifacts" / "checkpoint.json"

        manifest = runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)

        self.assertEqual(manifest["schema"], runtime_profile_checkpoint.SCHEMA)
        self.assertEqual(
            manifest["profilesRoot"],
            str(runtime_profile_checkpoint._normalise_path(profiles)),
        )
        self.assertEqual(manifest["selector"]["value"], "2\n")
        self.assertEqual(
            manifest["selector"]["path"],
            str(runtime_profile_checkpoint._normalise_path(selector)),
        )
        self.assertEqual(
            [entry["path"] for entry in manifest["profileFiles"]],
            [
                "Profile1/metaSavegame.dat",
                "Profile2/metaSavegame.dat",
                "Profile2/savegame.dat",
                "currentProfile.txt",
            ],
        )
        self.assertEqual(json.loads(manifest_path.read_text(encoding="utf-8")), manifest)

    def test_verify_reports_unexpected_profile_change(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "checkpoint.json"
        runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)
        (profiles / "Profile2" / "savegame.dat").write_bytes(b"changed")

        result = runtime_profile_checkpoint.verify_checkpoint(manifest_path, profiles, selector)

        self.assertFalse(result["safe_to_finalize"])
        self.assertEqual(result["unexpectedChanged"], ["Profile2/savegame.dat"])
        self.assertEqual(result["allowedChanged"], [])

    def test_verify_allows_explicit_meta_save_change_but_reports_it(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "checkpoint.json"
        runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)
        (profiles / "Profile2" / "metaSavegame.dat").write_bytes(b"new-progress")

        result = runtime_profile_checkpoint.verify_checkpoint(
            manifest_path,
            profiles,
            selector,
            allowed_relative_paths={"Profile2/metaSavegame.dat"},
        )

        self.assertTrue(result["safe_to_finalize"])
        self.assertEqual(result["unexpectedChanged"], [])
        self.assertEqual(result["allowedChanged"], ["Profile2/metaSavegame.dat"])
        self.assertFalse(result["profileFilesExactMatch"])

    def test_verify_rejects_added_missing_and_selector_changes(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "checkpoint.json"
        runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)
        (profiles / "Profile1" / "metaSavegame.dat").unlink()
        (profiles / "Profile2" / "unexpected.bin").write_bytes(b"unexpected")
        selector.write_text("1\n", encoding="utf-8")

        result = runtime_profile_checkpoint.verify_checkpoint(manifest_path, profiles, selector)

        self.assertFalse(result["safe_to_finalize"])
        self.assertEqual(result["missing"], ["Profile1/metaSavegame.dat"])
        self.assertEqual(result["added"], ["Profile2/unexpected.bin"])
        self.assertFalse(result["selectorMatch"])

    def test_require_layout_rejects_selector_symlink(self):
        root, profiles, _ = self._make_layout()
        outside = root / "outside-selector.txt"
        outside.write_text("2\n", encoding="utf-8")
        selector_link = profiles / "linked-selector.txt"
        try:
            selector_link.symlink_to(outside)
        except (OSError, NotImplementedError) as exc:
            original_lstat = runtime_profile_checkpoint._lstat_no_links

            def reject_selector(path):
                if Path(path) == selector_link:
                    raise ValueError(f"Refusing to use a profile symlink: {path}")
                return original_lstat(path)

            with mock.patch.object(
                runtime_profile_checkpoint,
                "_lstat_no_links",
                side_effect=reject_selector,
            ):
                with self.assertRaisesRegex(ValueError, "symlink"):
                    runtime_profile_checkpoint._require_layout(profiles, selector_link)
            return

        with self.assertRaisesRegex(ValueError, "symlink"):
            runtime_profile_checkpoint._require_layout(profiles, selector_link)

    def test_iter_regular_files_aborts_on_walk_error(self):
        root, profiles, _ = self._make_layout()

        def fake_walk(*args, **kwargs):
            kwargs["onerror"](PermissionError("inaccessible directory"))
            return iter(())

        with mock.patch.object(runtime_profile_checkpoint.os, "walk", fake_walk):
            with self.assertRaisesRegex(PermissionError, "inaccessible"):
                list(runtime_profile_checkpoint._iter_regular_files(profiles))

    def test_iter_regular_files_rejects_non_regular_file_entry(self):
        root, profiles, _ = self._make_layout()
        entry = profiles / "Profile1"

        def fake_walk(*args, **kwargs):
            return iter([(str(profiles), [], [entry.name])])

        with mock.patch.object(runtime_profile_checkpoint.os, "walk", fake_walk):
            with self.assertRaisesRegex(ValueError, "non-regular"):
                list(runtime_profile_checkpoint._iter_regular_files(profiles))

    def test_iter_regular_files_rejects_special_entry_when_supported(self):
        mkfifo = getattr(os, "mkfifo", None)
        if mkfifo is None:
            self.skipTest("named pipes are not available on this platform")
        root, profiles, _ = self._make_layout()
        special = profiles / "special.pipe"
        try:
            mkfifo(special)
        except OSError as exc:
            self.skipTest(f"named pipe creation is unavailable: {exc}")

        with self.assertRaisesRegex(ValueError, "non-regular"):
            list(runtime_profile_checkpoint._iter_regular_files(profiles))

    def test_verify_binds_to_manifest_root_and_selector_paths(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "checkpoint.json"
        runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)

        same_target = runtime_profile_checkpoint.verify_checkpoint(
            manifest_path,
            profiles.resolve(),
            selector.resolve(),
        )
        self.assertTrue(same_target["safe_to_finalize"])

        _, other_profiles, other_selector = self._make_layout()
        with self.assertRaisesRegex(ValueError, "profiles root"):
            runtime_profile_checkpoint.verify_checkpoint(
                manifest_path,
                other_profiles,
                other_selector,
            )

        alternate_selector = profiles / "alternate-selector.txt"
        alternate_selector.write_text("2\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "selector path"):
            runtime_profile_checkpoint.verify_checkpoint(
                manifest_path,
                profiles,
                alternate_selector,
            )

    def test_verify_rejects_non_qa_allow_list(self):
        root, profiles, selector = self._make_layout()
        manifest_path = root / "checkpoint.json"
        runtime_profile_checkpoint.create_checkpoint(profiles, selector, manifest_path)

        with self.assertRaisesRegex(ValueError, "designated QA autosave"):
            runtime_profile_checkpoint.verify_checkpoint(
                manifest_path,
                profiles,
                selector,
                allowed_relative_paths={"Profile2/savegame.dat"},
            )


if __name__ == "__main__":
    unittest.main()
