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
    count = content.count(before)
    if count == 0 and after in content:
        return False
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


def _remove_ignored_hdr_metadata(source: Path) -> list[str]:
    changed: list[str] = []
    expected = {
        b"GAMMA=1": 0,
        b"PRIMARIES=0 0 0 0 0 0 0 0": 0,
    }
    for path in source.rglob("*.hdr"):
        raw = path.read_bytes()
        parts = raw.split(b"\n\n", 1)
        if len(parts) != 2:
            if b"GAMMA=" in raw[:4096] or b"PRIMARIES=" in raw[:4096]:
                raise ValueError(f"Cannot safely locate the pixel payload in HDR file {path}.")
            continue
        header, payload = parts
        retained_lines: list[bytes] = []
        removed = False
        for line in header.split(b"\n"):
            field = line.rstrip(b"\r")
            if field.startswith(b"GAMMA=") or field.startswith(b"PRIMARIES="):
                if field not in expected:
                    raise ValueError(f"Unexpected HDR metadata in {path}: {field!r}")
                expected[field] += 1
                removed = True
                continue
            retained_lines.append(line)
        if not removed:
            continue
        updated = b"\n".join(retained_lines) + b"\n\n" + payload
        if updated.split(b"\n\n", 1)[1] != payload:
            raise ValueError(f"HDR pixel payload changed while removing metadata in {path}.")
        path.write_bytes(updated)
        changed.append(path.relative_to(source).as_posix())
    if expected[b"GAMMA=1"] != 3 or expected[b"PRIMARIES=0 0 0 0 0 0 0 0"] != 3:
        raise ValueError(
            "Expected exactly three identity GAMMA and three empty PRIMARIES metadata lines; "
            f"found {expected!r}."
        )
    return changed


def patch_source(source: Path) -> list[str]:
    project_file = source / "project.godot"
    project = _read(project_file)
    if 'config/release="1.7.0.0"' not in project or 'config/features=PackedStringArray("4.7")' not in project:
        raise ValueError("The source is not the pinned Material Maker 1.7 / Godot 4.7 project.")

    changed: list[str] = []
    gut_plugin = source / "addons/gut/gut_plugin.gd"
    if _replace_once(
        gut_plugin,
        "\tawait get_tree().create_timer(1).timeout\n",
        "\tif DisplayServer.get_name() != \"headless\":\n"
        "\t\tawait get_tree().create_timer(1).timeout\n",
        "preserve GUT plugin while skipping its GUI startup delay in headless editor",
    ):
        changed.append("preserve GUT plugin and skip its GUI-only startup wait in headless editor")

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

    ptex_import = source / "addons/material_maker/import_plugin/ptex_import.gd"
    if _replace_once(
        ptex_import,
        "\t\tvar file : File = File.new()\n"
        "\t\tif file.open(source_file, File.READ) == OK:\n"
        "\t\t\tvar test_json_conv = JSON.new()\n"
        "\t\t\ttest_json_conv.parse(file.get_as_text())))\n"
        "\t\t\tmaterial.set_ptex_no_render(JSON.new().stringify(test_json_conv.get_data()\n"
        "\t\t\tfile.close()\n",
        "\t\tvar file = FileAccess.open(source_file, FileAccess.READ)\n"
        "\t\tif file != null:\n"
        "\t\t\tvar test_json_conv = JSON.new()\n"
        "\t\t\tvar parse_status : int = test_json_conv.parse(file.get_as_text())\n"
        "\t\t\tif parse_status == OK:\n"
        "\t\t\t\tmaterial.set_ptex_no_render(JSON.new().stringify(test_json_conv.get_data()))\n"
        "\t\t\tfile.close()\n",
        "Godot 4 PTeX importer syntax and FileAccess compatibility",
    ):
        changed.append("Godot 4 PTeX importer syntax and FileAccess compatibility")
    ptex_import_migrations = (
        (
            "func _get_import_options(preset : int) -> Array:\n\treturn PRESET_OPTIONS[preset]\n",
            "func _get_import_options(path: String, preset: int) -> Array[Dictionary]:\n"
            "\tvar import_options : Array[Dictionary] = []\n"
            "\tfor option in PRESET_OPTIONS[preset]:\n"
            "\t\timport_options.append(option)\n"
            "\treturn import_options\n",
            "Godot 4.7 EditorImportPlugin import-options signature",
        ),
        (
            "func _get_option_visibility(option: String, options: Dictionary) -> bool:\n",
            "func _get_option_visibility(path: String, option_name: StringName, options: Dictionary) -> bool:\n",
            "Godot 4.7 EditorImportPlugin option-visibility signature",
        ),
        (
            "func _get_recognized_extensions() -> Array:\n\treturn [ \"ptex\" ]\n",
            "func _get_recognized_extensions() -> PackedStringArray:\n\treturn PackedStringArray([ \"ptex\" ])\n",
            "Godot 4.7 EditorImportPlugin recognized-extensions signature",
        ),
        (
            "\t\tvar gen = mm_loader.load_gen(source_file)\n",
            "\t\tvar gen = await mm_loader.load_gen(source_file)\n",
            "await asynchronous PTeX graph loading",
        ),
        (
            "\t\t\t\t\tvar result = c.export_material(source_file.get_basename(), \"Godot\")\n"
            "\t\t\t\t\twhile result is GDScriptFunctionState:\n"
            "\t\t\t\t\t\tresult = await result.completed\n",
            "\t\t\t\t\tawait c.export_material(source_file.get_basename(), \"Godot\")\n",
            "await asynchronous PTeX material export",
        ),
    )
    for before, after, label in ptex_import_migrations:
        if _replace_once(ptex_import, before, after, label):
            changed.append(label)

    new_painter = source / "material_maker/windows/new_painter/new_painter.gd"
    if _replace_once(
        new_painter,
        "func _on_ViewportContainer_resized():\n\tviewport.size = viewport_container.size\n",
        "func _on_ViewportContainer_resized():\n"
        "\tif not viewport_container.stretch:\n"
        "\t\tviewport.size = viewport_container.size\n",
        "avoid redundant stretched new-painter viewport resizing",
    ):
        changed.append("avoid redundant stretched new-painter viewport resizing")

    sdf_preview = source / "material_maker/windows/sdf_builder/preview_3d.gd"
    if _replace_once(
        sdf_preview,
        "func _on_Preview3D_resized():\n"
        "\tif viewport != null:\n"
        "\t\tviewport.size = size\n"
        "\t\tupdate_viewport()\n",
        "func _on_Preview3D_resized():\n"
        "\tif viewport != null:\n"
        "\t\tif not stretch:\n"
        "\t\t\tviewport.size = size\n"
        "\t\tupdate_viewport()\n",
        "avoid redundant stretched SDF-preview viewport resizing",
    ):
        changed.append("avoid redundant stretched SDF-preview viewport resizing")

    stretched_viewports = (
        (
            "material_maker/panels/paint/paint.tscn",
            '[node name="MainView" type="SubViewport" parent="VSplitContainer/HSplitContainer/Painter/View"]\n'
            "own_world_3d = true\n"
            "handle_input_locally = false\n"
            "size = Vector2i(800, 480)\n"
            "render_target_update_mode = 4\n",
            '[node name="MainView" type="SubViewport" parent="VSplitContainer/HSplitContainer/Painter/View"]\n'
            "own_world_3d = true\n"
            "handle_input_locally = false\n"
            "render_target_update_mode = 4\n",
            "omit fixed size from stretched paint viewport",
        ),
        (
            "material_maker/panels/preview_3d/preview_3d.tscn",
            '[node name="MaterialPreview" type="SubViewport" parent="." unique_id=1572255243]\n'
            "handle_input_locally = false\n"
            "physics_object_picking = true\n"
            "size = Vector2i(200, 200)\n"
            "render_target_clear_mode = 1\n"
            "render_target_update_mode = 4\n",
            '[node name="MaterialPreview" type="SubViewport" parent="." unique_id=1572255243]\n'
            "handle_input_locally = false\n"
            "physics_object_picking = true\n"
            "render_target_clear_mode = 1\n"
            "render_target_update_mode = 4\n",
            "omit fixed size from stretched material-preview viewport",
        ),
        (
            "material_maker/panels/preview_3d/preview_3d_panel.tscn",
            '[node name="MaterialPreview" parent="." index="1" unique_id=1572255243]\n'
            'world_3d = SubResource("World3D_3xlky")\n'
            "size = Vector2i(250, 256)\n",
            '[node name="MaterialPreview" parent="." index="1" unique_id=1572255243]\n'
            'world_3d = SubResource("World3D_3xlky")\n',
            "omit fixed size from stretched material-preview panel instance",
        ),
        (
            "material_maker/windows/new_painter/new_painter.tscn",
            '[node name="SubViewport" type="SubViewport" parent="VBoxContainer/Main/SubViewportContainer"]\n'
            "own_world_3d = true\n"
            'world_3d = SubResource("World3D_pmwks")\n'
            "handle_input_locally = false\n"
            "size = Vector2i(2, 2)\n"
            "render_target_update_mode = 4\n",
            '[node name="SubViewport" type="SubViewport" parent="VBoxContainer/Main/SubViewportContainer"]\n'
            "own_world_3d = true\n"
            'world_3d = SubResource("World3D_pmwks")\n'
            "handle_input_locally = false\n"
            "render_target_update_mode = 4\n",
            "omit fixed size from stretched new-painter viewport",
        ),
        (
            "material_maker/windows/environment_editor/environment_editor.tscn",
            '[node name="SubViewport" parent="Main/HSplitContainer/SubViewportContainer" instance=ExtResource("1")]\n'
            "size = Vector2i(2, 2)\n"
            "render_target_update_mode = 4\n",
            '[node name="SubViewport" parent="Main/HSplitContainer/SubViewportContainer" instance=ExtResource("1")]\n'
            "render_target_update_mode = 4\n",
            "omit fixed size from stretched environment-editor viewport",
        ),
        (
            "material_maker/windows/environment_editor/environment_editor_viewport.tscn",
            '[node name="SubViewport" type="SubViewport"]\n'
            'world_3d = SubResource("World3D_1qbrr")\n'
            "handle_input_locally = false\n"
            "size = Vector2i(200, 200)\n"
            "render_target_update_mode = 3\n",
            '[node name="SubViewport" type="SubViewport"]\n'
            'world_3d = SubResource("World3D_1qbrr")\n'
            "handle_input_locally = false\n"
            "render_target_update_mode = 3\n",
            "omit fixed size from stretched environment-editor viewport source",
        ),
        (
            "material_maker/windows/sdf_builder/preview_3d.tscn",
            '[node name="SubViewport" type="SubViewport" parent="."]\n'
            "own_world_3d = true\n"
            'world_3d = SubResource("World3D_5ccqg")\n'
            "handle_input_locally = false\n"
            "physics_object_picking = true\n"
            "size = Vector2i(1280, 720)\n"
            "render_target_update_mode = 4\n",
            '[node name="SubViewport" type="SubViewport" parent="."]\n'
            "own_world_3d = true\n"
            'world_3d = SubResource("World3D_5ccqg")\n'
            "handle_input_locally = false\n"
            "physics_object_picking = true\n"
            "render_target_update_mode = 4\n",
            "omit fixed size from stretched SDF-preview viewport",
        ),
    )
    for relative_path, before, after, label in stretched_viewports:
        if _replace_once(source / relative_path, before, after, label):
            changed.append(label)

    animation_scene = source / "material_maker/windows/export_animation/export_animation.tscn"
    for shader_id, material_id in (("1", "2"), ("3", "4"), ("10", "11")):
        before = (
            f'[sub_resource type="Shader" id="{shader_id}"]\n\n'
            f'[sub_resource type="ShaderMaterial" id="{material_id}"]\n'
        )
        after = (
            f'[sub_resource type="Shader" id="{shader_id}"]\n'
            'code = "shader_type canvas_item;\\nvoid fragment() { COLOR = vec4(0.0); }\\n"\n\n'
            f'[sub_resource type="ShaderMaterial" id="{material_id}"]\n'
        )
        label = f"valid transparent fallback for animation shader {shader_id}"
        if _replace_once(animation_scene, before, after, label):
            changed.append(label)

    ptex_spatial = source / "addons/material_maker/import_plugin/ptex_spatial_material.gd"
    if _replace_once(
        ptex_spatial,
        "\ttest_json_conv.parse(ptex))\n\tvar mm_graph = mm_loader.create_gen(test_json_conv.get_data()\n",
        "\tvar parse_status : int = test_json_conv.parse(ptex)\n"
        "\tif parse_status != OK:\n"
        "\t\treturn\n"
        "\tvar mm_graph = mm_loader.create_gen(test_json_conv.get_data())\n",
        "Godot 4 PTeX material importer syntax",
    ):
        changed.append("Godot 4 PTeX material importer syntax")
    if _replace_once(
        ptex_spatial,
        "\tvar mm_graph = mm_loader.create_gen(test_json_conv.get_data())\n",
        "\tvar mm_graph = await mm_loader.create_gen(test_json_conv.get_data())\n",
        "await asynchronous PTeX graph creation",
    ):
        changed.append("await asynchronous PTeX graph creation")
    if _replace_once(
        ptex_spatial,
        "\tvar status = mm_material.render_textures()\n"
        "\twhile status is GDScriptFunctionState:\n"
        "\t\tstatus = await status.completed\n",
        "\tawait mm_material.render_textures()\n",
        "await asynchronous PTeX texture rendering",
    ):
        changed.append("await asynchronous PTeX texture rendering")

    fixup = source / "scripts/fixup.gd"
    if _replace_once(
        fixup,
        "\tvar dir : Directory = Directory.new()\n"
        "\tdir.open(\"res://addons/material_maker/nodes\")\n"
        "\tdir.list_dir_begin() # TODOGODOT4 fill missing arguments https://github.com/godotengine/godot/pull/40547\n",
        "\tvar dir = DirAccess.open(\"res://addons/material_maker/nodes\")\n"
        "\tif dir == null:\n"
        "\t\treturn\n"
        "\tdir.list_dir_begin()\n",
        "Godot 4 directory access in the manual fixup script",
    ):
        changed.append("Godot 4 directory access in the manual fixup script")
    if _replace_once(
        fixup,
        "\t\t\tvar g = mm_loader.load_gen(file_path)\n",
        "\t\t\tvar g = await mm_loader.load_gen(file_path)\n",
        "await asynchronous graph load in the manual fixup script",
    ):
        changed.append("await asynchronous graph load in the manual fixup script")

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
    extension_reference = '"res://addons/godotsteam/godotsteam.gdextension"'
    reference_count = preset_text.count(extension_reference)
    if reference_count != 3:
        raise ValueError(f"Expected three bundled GodotSteam export references, found {reference_count}.")
    extension = source / "addons/godotsteam/godotsteam.gdextension"
    if not extension.is_file():
        raise FileNotFoundError(f"Bundled GodotSteam extension manifest is missing: {extension}")
    steam = source / "material_maker/steam.gd"
    if _replace_once(
        steam,
        "\tvar initialize_response: Dictionary = steam_api.steamInitEx()\n"
        "\tprint(\"Did Steam initialize?: %s \" % initialize_response)\n"
        "\tsteam_api.initAuthentication()\n"
        "\tis_subscribed = steam_api.isSubscribed()\n",
        "\tvar initialize_response: Dictionary = steam_api.steamInitEx()\n"
        "\tprint(\"Did Steam initialize?: %s \" % initialize_response)\n"
        "\tif int(initialize_response.get(\"status\", -1)) != 0:\n"
        "\t\treturn\n"
        "\tsteam_api.initAuthentication()\n"
        "\tis_subscribed = steam_api.isSubscribed()\n",
        "check Steam initialization result before calling authenticated APIs",
    ):
        changed.append("check Steam initialization result before calling authenticated APIs")

    extension_content = _read(extension)
    debug_library = 'windows.debug.x86_64 = "res://addons/godotsteam/win64/libgodotsteam.windows.template_debug.x86_64.dll"'
    release_library = 'windows.release.x86_64 = "res://addons/godotsteam/win64/libgodotsteam.windows.template_release.x86_64.dll"'
    if debug_library not in extension_content or release_library not in extension_content:
        raise ValueError("GodotSteam manifest does not map both Windows x86_64 debug and release libraries.")
    for relative_path in (
        "addons/godotsteam/win64/libgodotsteam.windows.template_debug.x86_64.dll",
        "addons/godotsteam/win64/libgodotsteam.windows.template_release.x86_64.dll",
        "addons/godotsteam/win64/steam_api64.dll",
    ):
        binary = source / relative_path
        if not binary.is_file() or binary.stat().st_size <= 0:
            raise FileNotFoundError(f"Bundled GodotSteam Windows release dependency is missing: {binary}")

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
    changed_hdr_files = _remove_ignored_hdr_metadata(source)
    changed.extend(f"remove ignored identity HDR metadata (pixel bytes preserved): {path}" for path in changed_hdr_files)

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
