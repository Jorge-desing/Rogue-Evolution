"""Reload request handlers between RPCs while retaining the stdio owner."""
from __future__ import annotations

import hashlib
import json
import sys
import types
from pathlib import Path


class ReloadingHandler:
    def __init__(self, server_path: Path):
        self.server_path = server_path.resolve()
        self.builder_path = self.server_path.with_name("build_bepinex_mcp.py")
        self.module = None
        self.fingerprint = None
        self.catalog = None

    def dispatch(self, message: dict) -> tuple[dict | None, bool]:
        # Read and compile the same bytes that were fingerprinted. A failed
        # reload never falls back to executing a mutation on stale code.
        server = self.server_path.read_bytes()
        builder = self.builder_path.read_bytes()
        fingerprint = hashlib.sha256(server + b"\0" + builder).hexdigest()
        changed = False
        if self.module is None or fingerprint != self.fingerprint:
            old_builder = sys.modules.get("build_bepinex_mcp")
            try:
                specs = types.ModuleType("build_bepinex_mcp")
                specs.__file__ = str(self.builder_path)
                exec(compile(builder, str(self.builder_path), "exec"), specs.__dict__)
                sys.modules["build_bepinex_mcp"] = specs
                candidate = types.ModuleType("_bepinex_live_handler")
                candidate.__file__ = str(self.server_path)
                exec(compile(server, str(self.server_path), "exec"), candidate.__dict__)
                if not candidate.TOOLS:
                    raise ValueError("Reload produced an empty tool catalog")
                catalog = json.dumps(candidate.TOOLS, sort_keys=True)
                changed = self.catalog is not None and catalog != self.catalog
                self.module = candidate
                self.catalog = catalog
                self.fingerprint = fingerprint
            except Exception:
                if old_builder is None:
                    sys.modules.pop("build_bepinex_mcp", None)
                else:
                    sys.modules["build_bepinex_mcp"] = old_builder
                raise

        # Unity instance IDs are not persistent across game/scene reloads.
        # Keep the cache only within this request (including its sequence).
        self.module._DIAGNOSTICS_GO_ID = None
        response = self.module.handle_request(message)
        if isinstance(message, dict) and message.get("method") == "initialize" and response and "result" in response:
            response["result"]["capabilities"]["tools"]["listChanged"] = True
        return response, changed
