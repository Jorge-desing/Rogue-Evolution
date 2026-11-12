#!/usr/bin/env python3
"""Convenient CLI to invoke BepInEx MCP queries against the running Rogue Command process."""

import json
import sys
from bepinex_mcp_server import execute_tool

def main():
    if len(sys.argv) < 2:
        print("Usage: python bepinex_cli.py <tool_name> [key=value ...]")
        return 1

    tool_name = sys.argv[1]
    args = {}
    for item in sys.argv[2:]:
        if "=" in item:
            k, v = item.split("=", 1)
            # Try parsing json if object, array, number or bool
            v_strip = v.strip()
            if (v_strip.startswith("{") and v_strip.endswith("}")) or (v_strip.startswith("[") and v_strip.endswith("]")):
                try:
                    args[k] = json.loads(v_strip)
                except Exception:
                    args[k] = v
            elif v.lower() == "true":
                args[k] = True
            elif v.lower() == "false":
                args[k] = False
            elif v.isdigit():
                args[k] = int(v)
            else:
                args[k] = v
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
