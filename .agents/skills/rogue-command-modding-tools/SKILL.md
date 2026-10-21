---
name: rogue-command-modding-tools
description: "Modding tools and pipelines for Rogue Command: asset pipeline, research, automation, and showcase. Use when preparing game-ready assets, researching game internals, or automating mod validation."
version: 1.0.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, modding, asset pipeline, research, automation]
    related_skills: [rogue-command-reverse-engineering, rogue-command-project-continuity]
---

# Rogue Command Modding Tools

## When to Use

Use this skill when preparing game-ready assets, researching game internals, automating mod validation, or showcasing mods. Consolidates patterns from universal-modder adapted for Rogue Command's Unity 2021.3 + BepInEx + Mono environment.

## Asset Pipeline

### Learn the target format first
Before converting anything, inspect the game's own assets:
- **Dimensions:** frame size, frames per sheet, layout (vertical strip / grid / one file per frame)
- **Orientation:** which way art faces, pivot/hotspot position
- **Alpha:** hard 1-bit edges (BC1 punch-through) or soft
- **Style:** palette size, outline width
- **Format:** PNG, DDS BC1/BC3/BC7, XNB, atlas + JSON

### 2D Pipeline Commands
```bash
um sprite info raw.png                          # size, alpha coverage, corner colour
um sprite cutout raw.png cut.png                # flat bg → transparent
um sprite fit cut.png item.png --size 64x26 --hard-alpha  # trim + scale into frame
um sprite pixelate cut.png px.png --size 32x32 --colors 16 --outline  # pixel art
um sprite sheet sheet.png frames/*.png --vertical  # Terraria-style strip
um sprite team-mask unit.png unit_grey.png --hue blue  # player-colour mask
um sprite preview item.png look.png --scale 6  # checkerboard + zoom
```

### 3D → Sprite Rendering
```bash
um render3d model.glb out/ --views 8 --size 128 --ortho --transparent
um render3d model.glb out/ --views 16 --size 256 --ortho --transparent --aa 4
```
- 8 headings (N, NE, E, SE, S, SW, W, NW) for RTS
- 16 headings for finer rotation
- Orthographic camera, transparent background

### Rules
- Pixel art: scale once with nearest neighbour. Never scale twice.
- Painted/HD art: use `fit --smooth`.
- Always preview before shipping.

## Research Pipeline

### Game Recon
1. Find install: `um scan "<game name>"` or `um scan "<folder path>"`
2. Fingerprint: engine, version, managed vs native, anti-cheat, loaders, saves
3. Research community: Nexus Mods, Thunderstore, GitHub, modding wikis
4. Capture: loader name, version, install steps, supported game versions, hello-world example, log location

### Rogue Command Specific
- Game version: 1.0.9, Unity 2021.3.45f2, Mono 64-bit
- Loader: BepInEx 5.4.23.5 + Harmony
- Assembly: `Assembly-CSharp.dll` (SHA-256: d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b)
- Modding route: managed patching via Harmony, AssetBundles for content
- Community: Thunderstore (BepInEx packs), Nexus Mods

## Game Automation

### Launch and Drive
- Launch game via Steam: `steam steam://rungameid/<id>`
- Capture screenshots: `tools/Capture-RogueCommand-Background.ps1`
- Read logs: BepInEx console + game log files
- Never let game run unattended without pause verification

### Rogue Command Runtime
- Bridge: BepInEx MCP (read-only first)
- Pause verification: `Time.timeScale == 0` or stable game clock
- Profile safety: use isolated QA profiles (Profile 4+), never modify user's active profile
- Steam Cloud: keep in instructed state

## Showcase

### Recording
- Use OBS or built-in capture for gameplay footage
- Highlight: unit registration, stage generation, AI behavior, visual rendering
- Label evidence tier: static inspection / local build / in-game runtime

### Documentation
- Write findings to `docs/research/` with evidence labels
- Update `AGENT-HANDOFF.md` with durable findings
- Never promote one evidence tier to another

## References
- Universal modder skills: `C:\Users\Alex\AppData\Local\hermes\skills\universal-modder\`
- Rogue Command reverse engineering: `.agents/skills/rogue-command-reverse-engineering/SKILL.md`
- Rogue Command project continuity: `.agents/skills/rogue-command-project-continuity/SKILL.md`
- Content authoring tools: `docs/research/CONTENT-AUTHORING-TOOLS.md`