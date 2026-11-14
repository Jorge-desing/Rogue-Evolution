extends Node

# Material Maker 1.7 command-line export workaround.
# The 1.7 Windows release starts mm_renderer's RenderingDevice on a worker thread,
# while parse_args.gd begins export during the same startup pass. Waiting here makes
# the command-line bake deterministic without changing the installed executable.

const TIMEOUT_MS: int = 30000

func _ready() -> void:
    var renderer: Node = get_node_or_null("/root/mm_renderer")
    if renderer == null:
        print("mm_wait_device: no /root/mm_renderer, not waiting")
        return

    var started_at: int = Time.get_ticks_msec()
    var device = null
    while Time.get_ticks_msec() - started_at < TIMEOUT_MS:
        var mutex: Mutex = renderer.get("rendering_mutex")
        if mutex != null and mutex.try_lock():
            var thread_running: bool = renderer.get("rendering_thread_running")
            device = renderer.get("rendering_device")
            mutex.unlock()
            if device != null and not thread_running:
                break
            device = null
        OS.delay_msec(5)

    print("mm_wait_device: device %s after %d ms" % [str(device), Time.get_ticks_msec() - started_at])


func _exit_tree() -> void:
    # Material Maker 1.7 constructs this non-RefCounted Object but never frees it.
    # It is no longer needed once the CLI scene exits. Leave the renderer/device
    # lifecycle to upstream: freeing that device here races its worker and GPU RIDs.
    var renderer: Node = get_node_or_null("/root/mm_renderer")
    if renderer == null:
        return
    var handler = renderer.get("shader_error_handler")
    if is_instance_valid(handler):
        handler.free()
        renderer.set("shader_error_handler", null)
        print("mm_wait_device: shader error handler released")
