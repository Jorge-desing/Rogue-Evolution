---
name: rogue-command-ai-authoring
description: "Implement or review Rogue Command AI profiles and compiled behavior modules with bounded parameters and lifecycle handling."
---

# Rogue Command AI Authoring

Use this skill when implementing an AI profile, adding a compiled module, or changing how game AI instances receive modules. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing content or game-facing integration.

## Keep code and pack data separate

- New AI logic belongs in a C# implementation of `IRogueCommandAiModule` compiled with the loader. A content pack selects a stable `moduleId` and passes string-pair parameters; it cannot ship an executable DLL or new game `MonoBehaviour` class.
- Give modules a stable ID and API version, reject unknown/duplicate/out-of-range parameters, and bound time, attempts, target count, and other repeated work.
- Follow initialization, update, and destruction callbacks. If a module fails, disable its behavior and attempt cleanup for all created module states in reverse order.

## Choose profile scope deliberately

- A native-template profile overlays the named native `DefaultAiBehaviour`; its modules may run for every use of that template.
- Use `scopeToReferencedStages: true` only when a profile should be limited to same-pack stage references. The loader creates an `RCL_` alias and calculates the contiguous stage range; validation rejects missing, cross-pack, or non-contiguous references.
- Custom AI prefabs must satisfy the schema's active state, reserved `RCL_` name, single `DefaultAiBehaviour`, and serialized data requirements. Do not infer that the Unity Editor can author these components from precompiled game assemblies.

## Be precise about behavior

The scheduled-build sample constructs buildings already present in the native AI deck; it does not add a deck. The reactive-defense sample dispatches bounded counterattacks; it does not prove a new strategy works. Treat every module as unverified in-game until a Steam-launched match shows the intended decision and lifecycle. Use static inspection and offline parser/registry checks first, then a reversible test profile for authorized runtime validation.
