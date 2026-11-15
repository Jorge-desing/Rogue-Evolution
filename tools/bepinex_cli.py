#!/usr/bin/env python3
"""Convenient CLI to invoke BepInEx MCP queries against the running Rogue Command process."""

import json
import math
import sys

try:
    from .bepinex_mcp_server import execute_tool
except ImportError:
    from bepinex_mcp_server import execute_tool


def _parse_cli_value(value: str):
    v_strip = value.strip()
    if (v_strip.startswith("{") and v_strip.endswith("}")) or (v_strip.startswith("[") and v_strip.endswith("]")):
        try:
            return json.loads(v_strip)
        except Exception:
            return value
    low = v_strip.lower()
    if low == "true":
        return True
    if low == "false":
        return False
    try:
        if v_strip and (v_strip.isdigit() or (v_strip[0] in "+-" and v_strip[1:].isdigit())):
            return int(v_strip)
        parsed_float = float(v_strip)
        if math.isfinite(parsed_float):
            return parsed_float
    except ValueError:
        pass
    return value


def main():
    if len(sys.argv) < 2:
        print("Usage: python bepinex_cli.py <tool_name> [key=value ...]")
        return 1

    tool_name = sys.argv[1]
    args = {}
    for item in sys.argv[2:]:
        if "=" in item:
            k, v = item.split("=", 1)
            args[k] = _parse_cli_value(v)
        else:
            args[item] = True

    res = execute_tool(tool_name, args)
    for c in res.get("content", []):
        try:
            parsed = json.loads(c["text"])
            print(json.dumps(parsed, indent=2))
        except Exception:
            print(c["text"])

    return 1 if res.get("isError") else 0

if __name__ == "__main__":
    sys.exit(main())
