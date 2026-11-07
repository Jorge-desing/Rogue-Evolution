import subprocess, time, urllib.request, urllib.parse, json

BASE = "http://127.0.0.1:8080/mcp"
GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command.exe"
CWD = r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command"
OUT = r"C:\Users\Alex\Documents\Mod Rogue Command\artifacts\live_diag_check.txt"

def log(msg):
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(msg + "\n")
    print(msg, flush=True)

# reset output
with open(OUT, "w", encoding="utf-8") as f:
    f.write("")

def call(ep, params=None, timeout=5):
    url = BASE.rstrip("/") + "/" + ep.lstrip("/")
    if params:
        url += "?" + urllib.parse.urlencode({k: str(v) for k, v in params.items() if v is not None})
    try:
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return r.read().decode("utf-8", errors="replace")
    except Exception as e:
        return f"ERR {e}"

# launch game
log("launching game...")
proc = subprocess.Popen([GAME], cwd=CWD, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
log(f"pid={proc.pid}")

# wait for MCP
up = False
for i in range(60):
    if proc.poll() is not None:
        log(f"game exited early rc={proc.returncode}")
        break
    r = call("system/ping", timeout=3)
    if "error" not in r.lower() or "ping" in r.lower():
        log(f"MCP up after {i*2}s: {r[:300]}")
        up = True
        break
    time.sleep(2)

if not up:
    log("MCP never came up; killing")
    proc.kill()
    raise SystemExit

time.sleep(2)

# find gameobject with LoaderDiagnosticsComponent
r = call("scene:find_objects_with_component", {"componentName": "LoaderDiagnosticsComponent"}, timeout=10)
log("FIND_OBJECTS: " + r[:1000])

# search by component name (finds DontDestroyOnLoad objects too)
r2 = call("scene/search_gameobjects", {"componentName": "LoaderDiagnosticsComponent", "includeInactive": "true", "limit": "100"}, timeout=10)
log("SEARCH by component: " + r2[:1000])

# Determine an id. Try parse JSON.
obj_id = None
preferred = None
for cand in (r, r2):
    try:
        d = json.loads(cand)
        # collect all items with id+name
        items = []
        def collect(o):
            if isinstance(o, dict):
                if "id" in o and "name" in o:
                    items.append(o)
                for v in o.values():
                    collect(v)
            elif isinstance(o, list):
                for it in o:
                    collect(it)
        collect(d)
        # prefer an item whose name contains "Expansion" or "Loader"
        for it in items:
            nm = str(it.get("name", "")).lower()
            if "expansion" in nm or "loader" in nm or "rogue command" in nm:
                preferred = it["id"]
                break
        if preferred is None and items:
            # fall back to first item that is NOT a UnityExplorer UI element
            for it in items:
                path = str(it.get("path", "")).lower()
                if "unityexplorer" not in path and "universelib" not in path:
                    preferred = it["id"]
                    break
        if preferred is not None:
            obj_id = preferred
            break
    except Exception as e:
        log(f"parse err: {e}")

if obj_id is None:
    # last resort: take first id found
    for cand in (r, r2):
        try:
            d = json.loads(cand)
            def walk(o):
                global obj_id
                if isinstance(o, dict):
                    for k, v in o.items():
                        if k in ("id", "gameObjectId", "instanceId", "objectId") and isinstance(v, (int, str)):
                            obj_id = v
                            return True
                        if walk(v):
                            return True
                elif isinstance(o, list):
                    for it in o:
                        if walk(it):
                            return True
                return False
            walk(d)
            if obj_id is not None:
                break
        except Exception as e:
            log(f"parse err2: {e}")

log(f"resolved obj_id={obj_id}")

# If no id, try component/get_details by searching for the plugin GameObject differently.
# Also try a known approach: list components on candidate objects via get_details with no id? 
# We'll attempt call_method with a guess id only if found.

if obj_id is not None:
    # get details
    det = call("component/get_details", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent"}, timeout=10)
    log("GET_DETAILS: " + det[:2000])
    # list methods
    lm = call("component/list_methods", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent"}, timeout=10)
    log("LIST_METHODS: " + lm[:2000])
    # call GetUtilitiesJson
    res = call("component/call_method", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "methodName": "GetUtilitiesJson", "args": "[]"}, timeout=15)
    log("GET_UTILITIES_JSON: " + res[:4000])
    # NEW: call GetRangeColorsJson
    res_rc = call("component/call_method", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "methodName": "GetRangeColorsJson", "args": "[]"}, timeout=10)
    log("GET_RANGE_COLORS_JSON: " + res_rc[:1200])
    # NEW: call GetIdentifierPaletteJson
    res_ip = call("component/call_method", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "methodName": "GetIdentifierPaletteJson", "args": "[]"}, timeout=10)
    log("GET_IDENTIFIER_PALETTE_JSON: " + res_ip[:1600])
    # call GetDiagnosticsJson
    res2 = call("component/call_method", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "methodName": "GetDiagnosticsJson", "args": "[]"}, timeout=15)
    log("GET_DIAGNOSTICS_JSON: " + res2[:2000])
    # call ResolveIdentifierColor
    res3 = call("component/call_method", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "methodName": "ResolveIdentifierColor", "args": '["arachnid-hexapod"]'}, timeout=10)
    log("RESOLVE_COLOR: " + res3[:500])
    # read public members via get_member
    for m in ("OverlayOpen", "RangeDisplayEnabled", "RangeDisplayTrackedEntities", "ActivePackCount", "RegisteredUnitCount", "ProviderReady",
              # NEW overlay/range getters
              "OverlaySelectedTab", "OverlayWatermarkVisible", "OverlayGuiDisabled", "OverlayPluginVersion", "OverlayPatchResultSuccess",
              "RangeDisplayShowWeapon", "RangeDisplayShowSkill", "RangeDisplayShowEffect", "RangeDisplayShowSight",
              "RangeDisplayLineWidth", "RangeDisplaySegments", "RangeDisplayHeightOffset"):
        gm = call("component/get_member", {"id": obj_id, "componentName": "LoaderDiagnosticsComponent", "memberName": m}, timeout=10)
        log(f"MEMBER {m}: " + gm[:300])
else:
    log("no obj_id resolved; dumping find_objects raw already above")

# keep game alive briefly then kill
time.sleep(5)
proc.kill()
log("game killed; done")