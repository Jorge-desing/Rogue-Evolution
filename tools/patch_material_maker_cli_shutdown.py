"""Apply the pinned Rogue Command CLI shutdown fix to Material Maker 1.7 source."""

from __future__ import annotations

import argparse
from pathlib import Path


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def _write(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8", newline="\n")


def _replace_once(path: Path, before: str, after: str, label: str) -> bool:
    content = _read(path)
    if after in content:
        return False
    count = content.count(before)
    if count != 1:
        raise ValueError(f"Expected one upstream {label} block in {path}, found {count}.")
    _write(path, content.replace(before, after, 1))
    return True


def _upgrade_shader_parameter_keys(source: Path) -> list[str]:
    changed: list[str] = []
    for pattern in ("*.tres", "*.tscn"):
        for path in source.rglob(pattern):
            content = _read(path)
            updated = content.replace("shader_param/", "shader_parameter/")
            if updated != content:
                _write(path, updated)
                changed.append(path.relative_to(source).as_posix())
    return changed


def _remove_missing_example_material_libraries(source: Path) -> list[str]:
    changed: list[str] = []
    for mesh_name in ("suzanne", "pillow", "vase"):
        obj_path = source / "material_maker" / "meshes" / f"{mesh_name}.obj"
        if not obj_path.is_file():
            raise ValueError(f"Expected Material Maker example mesh is missing: {obj_path}")
        content = _read(obj_path)
        lines = content.splitlines(keepends=True)
        references = [line for line in lines if line.lstrip().startswith("mtllib ")]
        expected = f"mtllib {mesh_name}.mtl"
        if not references:
            continue
        if len(references) != 1 or references[0].strip() != expected:
            raise ValueError(f"Unexpected material library reference in {obj_path}: {references!r}")
        material_path = obj_path.with_name(f"{mesh_name}.mtl")
        if material_path.is_file():
            continue
        _write(obj_path, "".join(line for line in lines if line not in references))
        changed.append(obj_path.relative_to(source).as_posix())
    return changed


def patch_source(source: Path) -> list[str]:
    project_file = source / "project.godot"
    project = _read(project_file)
    if 'config/release="1.7.0.0"' not in project or 'config/features=PackedStringArray("4.7")' not in project:
        raise ValueError("The source is not the pinned Material Maker 1.7 / Godot 4.7 project.")

    changed: list[str] = []
    renderer = source / "addons/material_maker/engine/multi_renderer.gd"
    if _replace_once(
        renderer,
        "func _ready() -> void:\n\tshader_error_handler = load(\"res://addons/material_maker/engine/shader_error_handler.gd\").new()\n",
        "func _ready() -> void:\n"
        "\tif Engine.is_editor_hint() and DisplayServer.get_name() == \"headless\":\n"
        "\t\treturn\n"
        "\tshader_error_handler = load(\"res://addons/material_maker/engine/shader_error_handler.gd\").new()\n",
        "headless editor renderer initialization guard",
    ):
        changed.append("headless editor renderer initialization guard")
    if _replace_once(
        renderer,
        "var rendering_device_user = null\n",
        "var rendering_device_user = null\nvar pending_rid_cleanup : Array = []\n",
        "renderer cleanup queue",
    ):
        changed.append("renderer cleanup queue")
    if _replace_once(
        renderer,
        "func destroy_rendering_device():\n\tpass\n",
        "func destroy_rendering_device():\n"
        "\tif rendering_device != null:\n"
        "\t\tif OS.get_environment(\"RCL_MM_DIAG\") == \"1\":\n"
        "\t\t\tprint(\"mm_shutdown: freeing RenderingDevice\")\n"
        "\t\trendering_device.free()\n"
        "\t\trendering_device = null\n"
        "\t\tif OS.get_environment(\"RCL_MM_DIAG\") == \"1\":\n"
        "\t\t\tprint(\"mm_shutdown: freed RenderingDevice\")\n",
        "owner-thread RenderingDevice shutdown",
    ):
        changed.append("owner-thread RenderingDevice shutdown")

    compute = source / "addons/material_maker/engine/pipeline/compute_shader.gd"
    if _replace_once(
        compute,
        "func _init():\n\tclear()\n",
        "func _init():\n\tclear()\n\n"
        "func _notification(what : int) -> void:\n"
        "\tif what == NOTIFICATION_PREDELETE and shader.is_valid() and mm_renderer.rendering_device != null:\n"
        "\t\tmm_renderer.pending_rid_cleanup.append({ \"callable\": in_thread_free_shader, \"args\": [ shader, mm_renderer.rendering_device ] })\n"
        "\t\tshader = RID()\n\n"
        "static func in_thread_free_shader(shader_rid : RID, rendering_device : RenderingDevice) -> void:\n"
        "\tif rendering_device != null and shader_rid.is_valid():\n"
        "\t\trendering_device.free_rid(shader_rid)\n",
        "compute shader RID cleanup",
    ):
        changed.append("compute shader RID cleanup")
    if _replace_once(
        compute,
        "\tvar rd : RenderingDevice = mm_renderer.rendering_device\n"
        "\tshader = do_compile_shader(rd, { compute=string }, replaces)\n",
        "\tvar rd : RenderingDevice = mm_renderer.rendering_device\n"
        "\tif shader.is_valid():\n"
        "\t\tmm_renderer.pending_rid_cleanup.append({ \"callable\": in_thread_free_shader, \"args\": [ shader, rd ] })\n"
        "\t\tshader = RID()\n"
        "\tshader = do_compile_shader(rd, { compute=string }, replaces)\n",
        "replaced shader RID cleanup",
    ):
        changed.append("replaced shader RID cleanup")

    texture = source / "addons/material_maker/engine/pipeline/texture.gd"
    if _replace_once(
        texture,
        "func _notification(what : int) -> void:\n"
        "\tmatch what:\n"
        "\t\tNOTIFICATION_PREDELETE:\n"
        "\t\t\tif mm_renderer:\n"
        "\t\t\t\tawait mm_renderer.thread_run(in_thread_free_rid, [rid, rd])\n"
        "\n"
        "static func in_thread_free_rid(texture_rid, rendering_device):\n"
        "\tif texture_rid.is_valid():\n"
        "\t\trendering_device.free_rid(texture_rid)\n",
        "func _notification(what : int) -> void:\n"
        "\tmatch what:\n"
        "\t\tNOTIFICATION_PREDELETE:\n"
        "\t\t\tif mm_renderer and rid.is_valid() and rd != null:\n"
        "\t\t\t\tmm_renderer.pending_rid_cleanup.append({ \"callable\": in_thread_free_rid, \"args\": [rid, rd] })\n\n"
        "static func in_thread_free_rid(texture_rid, rendering_device):\n"
        "\tif rendering_device != null and texture_rid.is_valid():\n"
        "\t\trendering_device.free_rid(texture_rid)\n",
        "texture RID cleanup",
    ):
        changed.append("texture RID cleanup")

    parse_args = source / "parse_args.gd"
    if _replace_once(
        parse_args,
        "\t\t\tgen.queue_free()\n",
        "\t\t\tgen.queue_free()\n"
        "\t\t\tawait get_tree().process_frame\n"
        "\t\t\twhile not mm_renderer.pending_rid_cleanup.is_empty():\n"
        "\t\t\t\tvar cleanup : Dictionary = mm_renderer.pending_rid_cleanup.pop_front()\n"
        "\t\t\t\tif OS.get_environment(\"RCL_MM_DIAG\") == \"1\":\n"
        "\t\t\t\t\tprint(\"mm_shutdown: freeing queued GPU resource (%d pending)\" % mm_renderer.pending_rid_cleanup.size())\n"
        "\t\t\t\tawait mm_renderer.thread_run(cleanup.callable, cleanup.args)\n",
        "post-export graph and RID cleanup",
    ):
        changed.append("post-export graph and RID cleanup")
    if _replace_once(
        parse_args,
        "\tget_tree().quit()\n",
        "\tif OS.get_environment(\"RCL_MM_DIAG\") == \"1\":\n"
        "\t\tprint(\"mm_shutdown: stopping renderer resources=%d rendering=%s\" % [mm_renderer.pending_rid_cleanup.size(), str(mm_renderer.rendering_thread_working)])\n"
        "\tawait mm_renderer.stop_rendering_thread()\n"
        "\tif OS.get_environment(\"RCL_MM_DIAG\") == \"1\":\n"
        "\t\tprint(\"mm_shutdown: renderer stopped\")\n"
        "\tget_tree().quit()\n",
        "renderer thread join before CLI exit",
    ):
        changed.append("renderer thread join before CLI exit")

    presets = source / "export_presets.cfg"
    preset_text = _read(presets)
    missing_extension_reference = '"res://addons/godotsteam/godotsteam.gdextension", '
    if missing_extension_reference in preset_text:
        reference_count = preset_text.count(missing_extension_reference)
        if reference_count != 3:
            raise ValueError(f"Expected three optional Steam export references, found {reference_count}.")
        preset_text = preset_text.replace(missing_extension_reference, "")
        _write(presets, preset_text)
        changed.append("exclude unavailable optional Steam extension from CLI pack")
    elif "godotsteam.gdextension" in preset_text:
        raise ValueError("The optional GodotSteam reference has an unexpected export-preset layout.")

    start_scene = source / "start.tscn"
    if _replace_once(
        start_scene,
        '[ext_resource type="Texture2D" uid="uid://cfugy4gny4tfh" path="res://material_maker/icons/godot_logo.svg" id="3"]',
        '[ext_resource type="Texture2D" path="res://material_maker/icons/godot_logo.svg" id="3"]',
        "stale Godot logo UID fallback",
    ):
        changed.append("stale Godot logo UID fallback")

    changed_shader_resources = _upgrade_shader_parameter_keys(source)
    changed.extend(f"Godot 4 shader parameter key: {path}" for path in changed_shader_resources)
    changed_obj_files = _remove_missing_example_material_libraries(source)
    changed.extend(f"remove absent optional example MTL reference: {path}" for path in changed_obj_files)

    extension_list = source / ".godot/extension_list.cfg"
    if extension_list.is_file():
        extension_lines = _read(extension_list).splitlines()
        filtered_extension_lines = [
            line for line in extension_lines if line.strip() != "res://addons/godotsteam/godotsteam.gdextension"
        ]
        if filtered_extension_lines != extension_lines:
            if filtered_extension_lines:
                _write(extension_list, "\n".join(filtered_extension_lines) + "\n")
            else:
                extension_list.unlink()
            changed.append("remove optional Steam extension from the cached CLI extension list")

    extension = source / "addons/godotsteam/godotsteam.gdextension"
    if extension.is_file():
        extension.unlink()
        changed.append("omit optional Steam extension from the CLI-only source copy")

    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path, help="Extracted official Material Maker 1.7 source root")
    args = parser.parse_args()
    source = args.source.resolve()
    changes = patch_source(source)
    print("Material Maker CLI shutdown patch is ready.")
    if changes:
        for change in changes:
            print(f"  applied: {change}")
    else:
        print("  source was already patched; no changes were needed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
