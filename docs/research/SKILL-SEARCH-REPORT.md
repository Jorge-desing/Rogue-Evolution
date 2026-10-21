# Reporte de búsqueda de skills para Rogue Command Mod

**Fecha:** 2026-10-20  
**Proyecto:** Rogue Command Mod Expansion (Unity 2021.3.45f2, BepInEx 5 + Harmony, C# .NET netstandard2.1)  
**Alcance:** Tasks 1-7 del plan (reporte + búsquedas Unity modding, C#/.NET, AssetBundle/3D, PBR/texturas, AI/game research, hub de Hermes). Tasks 8-11 (evaluación, documentación, conclusiones, commit) completadas por el orquestador.

## Resumen ejecutivo

Se ejecutaron 8 búsquedas web (4 para Unity modding/game dev, 4 para C#/.NET). Se obtuvieron ~8 resultados por query (64 totales). Hallazgos destacados:

- **Skills Claude/Hermes de BepInEx/Harmony**: repositorios `universal-modder` / `universal-modder-hwa` (skills `mod-any-game` con referencia Unity/BepInEx/Harmony) y skill dedicada "BepInEx Game Modding" en mcpmarket, además de `bepinex-modding` (KaelLim) para IL2CPP.
- **Skills .NET oficiales**: `dotnet/skills` (marketplace) con plugins dotnet, dotnet-msbuild, dotnet-test, etc.; `richlander/dotnet-skills`, `novotnyllc/dotnet-artisan`.
- **Skills game dev en skills.sh**: `gamedev-skills/awesome-gamedev-agent-skills` (75 skills, incl. unity-csharp-scripting, unity-animation, game-ai), `mindrally/skills` (game-development, c-sharp), `jeffallan/claude-skills` (game-developer ECS).
- **Hermes**: docs de skills y catálogos (bundled/optional) consultados; sin skills específicas de game modding en resultados directos.
- **Tasks 6-7 (AI & Game Research + Hub de Hermes)**: `universal-modder` (MIT, 8.4k stars) skill de reverse engineering aplicable al flujo Mono; `game-ai` (Apache-2.0) para FSM/BT; hub de Hermes sin skills de game modding (brecha confirmada: solo minecraft-modpack-server y pokemon-player).

## Categoría 1: Unity Modding & Game Dev

### Query: `"Rogue Command" mod skills unity`

**Resultados:** 8

1. **Rogue Command Combat and Units | Blueprinted Gaming**
   - URL: https://blueprintedgaming.com/docs/rogue-command/combat-and-units
   - Descripción: ## The Starter Set ​ ### Specialists (8) ​ - **Castle:** An immobile battleship gun for turtle play. Its passive gives all buildings 20% more attack and skill damage and 20% more health, and its skill drops six missiles anywhere on the map at global range. ... - **Mantis Mech:** The melee bruiser. Halves incoming damage, and its skill **Devours a friendly unit** for permanent bonus damage. If you …

2. **Blueprint**
   - URL: https://roguecommand.wiki.gg/wiki/Blueprint
   - Descripción: # Blueprint **Blueprints** are cards that unlock the various units and buildings players can build in Rogue Command. Each Blueprint displays the unit or structure, along with their relevant stats, passive abilities, and Skills. ... ## Stats |Mana Points (MP)|The amount of Mana the unit has, a resource spent when using Skills. Mana regenerates at a rate of 0.2 points per second.| ... ### Abilities …

3. **Rogue Command Review | A Clever RTS Twist That's Easy to ...**
   - URL: https://lifeisagamemagazine.substack.com/p/rogue-command-review-a-clever-rts
   - Descripción: ## Reinvents the RTS Formula Progression is handled entirely outside of gameplay. Every time you complete a battle, you select rewards on a reward map that unlock new units, unit upgrades, consumable abilities and bonuses. And with over 200 units, 100 upgrades, 50 hacks, 30 abilities and 70 enemy units, each run feels completely different. ... Those units are generally more powerful so that you ca…

4. **Get 13 cheats for Rogue Command with XMODhub, the Ultimate PC Game Modding App**
   - URL: https://www.xmodhub.com/en/xmod-cheats/rogue-command-trainers
   - Descripción: Get 13 cheats for Rogue Command with XMODhub, the Ultimate PC Game Modding App ... # Rogue Command Trainers & Mods ... Steam Get [Toggle UI,Set Game Speed] & [11] other mods for [Rogue Command] with XMODhub ... ## 13 Mods ### Miscellaneous ... - Unlock All Database Entries ### Units - Set Unit Cap ### Money/Resources - Set Crystals - Set coins ### Systems - Set Game Speed - Toggle UI ### Skills/Ex…

5. **Rogue Command (RTS+ Roguelike) - Demo - Let's Play**
   - URL: https://www.youtube.com/watch?v=Upbg_MuV5AA
   - Descripción: ## Sampstra Games ### Transcript {ts:0} welcome back to Summer Games the place to find new strategy games and today I {ts:4} have for you Rogue command now this is a roguelike RTS and it's quite a lot of fun because you you start off on a {ts:11} specific map very typically actually in this case we don't start with combat but sometimes you start straight up with {ts:16} combat and then it sort of …

6. **Save 30% on Rogue Command on Steam**
   - URL: https://store.steampowered.com/app/1461910/Rogue_Command/
   - Descripción: After each battle you choose new tools for the fights ahead. Reinforcing existing strengths or pushing your build into new directions: - **Blueprints** add a new building to your Engineer's construction database: **Unit** **factories**, defensive **turrets**, healing spires or **super weapons**, the nuts and bolts of your army. - **Upgrades** modify your blueprints. Make units stronger. Or add new…

7. **Rogue Command Tips and Tricks | Blueprinted Gaming**
   - URL: https://blueprintedgaming.com/docs/rogue-command/tips-and-tricks
   - Descripción: ### #Castle buffs your buildings and cannot move to follow a push IntermediateCombat Its passive gives every building 20 percent more attack and skill damage plus 20 percent more health, and its skill drops six missiles anywhere on the map at global range. Being immobile means it anchors a position and cannot join an advance.

8. **Rogue Command Guides: Beginner RTS, Economy, Specialists, and Progression | Player Codex**
   - URL: https://playercodex.app/rogue-command
   - Descripción: Its official Steam description frames play around controlling units, building a base, harvesting resources, exploring the map, defending positions, and planning attacks.


### Query: `unity 2021 modding bepinex harmony skills claude`

**Resultados:** 8

1. **universal-modder-hwa/skills/mod-any-game/references/engines ...**
   - URL: https://github.com/magjangin/universal-modder-hwa/blob/main/skills/mod-any-game/references/engines/unity.md
   - Descripción: **Identify.** `UnityPlayer.dll` and a `<Game>_Data/` folder. - **Mono backend:** `<Game>_Data/Managed/Assembly-CSharp.dll` exists. The game logic is plain .NET IL, which makes modding easy. - **IL2CPP backend:** `GameAssembly.dll` + `<Game>_Data/il2cpp_data/Metadata/global-metadata.dat`. Logic is compiled to native code, so mods work through generated interop. ... Many Unity games already have a m…

2. **BepInEx Game Modding | Claude Code Skill for Unity Mods**
   - URL: https://mcpmarket.com/tools/skills/bepinex-game-modding
   - Descripción: # BepInEx Game Modding Develops and configures Unity game plugins using the BepInEx framework and Harmony patching. ... ### Related Skills This skill empowers Claude to architect and implement robust BepInEx plugins for Unity-based games, specifically optimized for Single Player Tarkov (SPT) environments. It provides structured guidance on creating plugin entry points with the [BepInPlugin] attrib…

3. **universal-modder/skills/mod-any-game/references/engines/unity.md at main · rehan-remade/universal-modder**
   - URL: https://github.com/rehan-remade/universal-modder/blob/main/skills/mod-any-game/references/engines/unity.md
   - Descripción: **Identify.** `UnityPlayer.dll` and a `<Game>_Data/` folder. - **Mono backend:** `<Game>_Data/Managed/Assembly-CSharp.dll` exists. The game logic is plain .NET IL, which makes modding easy. - **IL2CPP backend:** `GameAssembly.dll` + `<Game>_Data/il2cpp_data/Metadata/global-metadata.dat`. Logic is compiled to native code, so mods work through generated interop. ... Many Unity games already have a m…

4. **github.com · bepinex-il2cpp-modding · SKILLbepinex-modding/skills/bepinex-il2cpp-modding/SKILL.md at ...**
   - URL: https://github.com/KaelLim/bepinex-modding/blob/main/skills/bepinex-il2cpp-modding/SKILL.md
   - Descripción: # SKILL.md |name|bepinex-il2cpp-modding| |--|--| |description|Use when creating or debugging a BepInEx mod for a Unity IL2CPP game (a GameAssembly.dll + <Game>_Data/il2cpp_data/ + BepInEx/interop/ layout) — writing Harmony patches, building a cheat trainer or QoL/behavior mod with an injected-MonoBehaviour menu/hotkeys, or when an IL2CPP plugin throws ObjectCollectedException, a Harmony patch neve…

5. **Unity modding with Claude Code**
   - URL: https://canyoumod.com/engines/unity
   - Descripción: # Unity Find out whether your game uses Mono or IL2CPP before choosing a loader. The backend changes the route. ... ## A practical first workflow ### Identify Mono or IL2CPP Assembly-CSharp.dll in the Managed folder indicates Mono. GameAssembly.dll with global-metadata.dat indicates IL2CPP. Record the game and Unity versions. STEP 02 ### Use the community’s route Look for the game’s supported API …

6. **Some Mods using BepInEx - The Last Spell - Steam Community**
   - URL: https://steamcommunity.com/sharedfiles/filedetails/?id=3138074388
   - Descripción: # About How to load user code into unity game. Using `Harmony` we can modify csharp code, but how to load your code into unity game? There is many framework for modding unity game, such as `uMod`, `MelonLoader`, `UnityModManager` and `BepInEx`. ... # About Unity Those game developed with `Unity Mono` can be Modified using `Harmony`(A csharp library). **You can patch/replace/decorate almost csharp …

7. **Unity Il2Cpp Modding - Claude Code Skill for Game Mods**
   - URL: https://mcpmarket.com/tools/skills/unity-il2cpp-modding
   - Descripción: Simplifies the development of Unity game modifications for Il2Cpp-based titles using the Il2CppInterop framework. ... This skill provides a comprehensive knowledge base and implementation patterns for modding Unity games that utilize the Il2Cpp scripting backend. It offers expert guidance on using the Il2CppInterop framework to bridge managed code with native runtimes, implementing method hooks vi…

8. **BepInEx Guide Index | BepInEx Docs**
   - URL: https://docs.bepinex.dev/articles/
   - Descripción: BepInEx is a .NET plugin framework aimed at Unity games running on Mono runtime. ... If you are a developer, you can check the plugin creation walkthrough to get acquainted with most of the API of BepInEx. Additionally, you should check out how to use Harmony to patch game methods and how to patch game assemblies with Cecil.


### Query: `site:github.com unity mod loader content pack skills`

**Resultados:** 8

1. **universal-modder-hwa/skills/mod-any-game/references/engines ...**
   - URL: https://github.com/magjangin/universal-modder-hwa/blob/main/skills/mod-any-game/references/engines/unity.md
   - Descripción: # Unity **Identify.** `UnityPlayer.dll` and a `<Game>_Data/` folder. - **Mono backend:** `<Game>_Data/Managed/Assembly-CSharp.dll` exists. The game logic is plain .NET IL, which makes modding easy. - **IL2CPP backend:** `GameAssembly.dll` + `<Game>_Data/il2cpp_data/Metadata/global-metadata.dat`. Logic is compiled to native code, so mods work through generated interop. - **Version:** `um scan` read…

2. **universal-modder/skills/game-recon/SKILL.md at main · rehan-remade/universal-modder**
   - URL: https://github.com/rehan-remade/universal-modder/blob/main/skills/game-recon/SKILL.md
   - Descripción: |name|game-recon| |--|--| |description|Figure out how a specific installed game can be modded, before writing any mod code. Covers where it's installed, the engine and version, managed vs native code, anti-cheat, loaders already present, save and config folders, and the community's modding route. Use when the user asks "can I mod <game>?", "what engine is <game>", "how do people mod <game>", or at…

3. **github.com · MaipaEngine · ModdingModding/MelonLoader/README.md at main · MaipaEngine ... - GitHub**
   - URL: https://github.com/MaipaEngine/Modding/blob/main/MelonLoader/README.md
   - Descripción: - Part 1: Beginner - Your First Mod - Part 2: Beginner - Project Structure & Build - Part 3: Beginner - MelonLoader Attributes - Part 4: Beginner - MelonMod Lifecycle - Part 5: Intermediate - Harmony Patching - Part 6: Intermediate - Unity UI - Part 7: Intermediate - Game Objects & Scenes - Part 8: Intermediate - Reflection & Assembly - Part 9: Intermediate - Configuration Files - Part 10: Advance…

4. **timberborn-modding-skill/SKILL.md at main · Sunwood-ai-labs ...**
   - URL: https://github.com/Sunwood-ai-labs/timberborn-modding-skill/blob/main/SKILL.md
   - Descripción: # SKILL.md |description|Create and debug Timberborn mods across JSON content mods, balance packs, new building assets, and C# or UI-driven code mods. Use when Codex needs to patch Blueprint JSON, scaffold Example building assets, choose between built-in materials and AssetBundles, or determine when DLL or BepInEx/Harmony work is required.|

5. **Unity modding resources.md - GitHub Gist**
   - URL: https://gist.github.com/PinewoodPip/66f3da1751cba4730a37da8ffc365e66
   - Descripción: # Unity modding resources This document is a bunch of links and quick info for getting started with modding Unity games, focused on the programming side of things, since it's what enables the most interesting mods. ... Before doing any modding you should have some prior knowledge of C#; having some experience with Unity itself is also highly desirable to know what the general workflow and patterns…

6. **Unity-Skills/SkillsForUnity/unity-skills~/skills/SKILL.md at 9392645672c53b3040a854043bfc542dfbfa4804 · Besty0728/Unity-Skills**
   - URL: https://github.com/Besty0728/Unity-Skills/blob/HEAD/SkillsForUnity/unity-skills~/skills/SKILL.md
   - Descripción: # SKILL.md |description|Index of all Unity Skills modules with per-module mode labels (SA/FA/Mixed). Use to find which module covers a task before loading its doc.| ... # Unity Skills - Module Index Start with ../SKILL.md for mode switching and schema-first rules. ... ## Skill Naming Convention Skills follow `<module>_<action>` or `<module>_<action>_batch`.

7. **Unity Modding - Github-Gist**
   - URL: https://gist.github.com/Noxalus/85283a270b528f26104c3a02ff115fb4
   - Descripción: # Assets ... - ModTool uses AssetBundles to pack prefabs (with related assets) and provide an API to access to prefabs

8. **GitHub - N4T4NM/UnityModLoader: This tool allows you to load mods into unity games**
   - URL: https://github.com/N4T4NM/UnityModLoader
   - Descripción: # N4T4NM/UnityModLoader # UnityModLoader This tool allows you to load mods into unity games ... - Easy to learn - Modify games without modifying original files - Compatible with new and old Unity versions - Compatible with x86 and x64 platforms - Compatible with mono and mono 2.0 # Install - Move the UnityModLoader dll files to the game folder - Move the UnityModLoader.Manager.exe to the game fold…


### Query: `hermes agent skills game modding unity`

**Resultados:** 8

1. **Skills System | Hermes Agent**
   - URL: https://hermes-agent.nousresearch.com/docs/user-guide/features/skills
   - Descripción: Skills are on-demand knowledge documents the agent can load when needed. They follow a **progressive disclosure** pattern to minimize token usage and are compatible with the agentskills.io open standard. All skills live in **`~/.hermes/skills/`** — the primary directory and source of truth. On fresh install, bundled skills are copied from the repo. Hub-installed and agent-created skills also go he…

2. **Bundled Skills Catalog**
   - URL: https://agent-hermes.ru/docs/en/reference/skills-catalog/
   - Descripción: # Bundled Skills Catalog Hermes ships with a large built-in skill library copied into `~/.hermes/skills/` on install. Each skill below links to a dedicated page with its full definition, setup, and usage. ... ## autonomous-ai-agents |Skill|Description|Path| |--|--|--| |`hermes-agent`|Configure, extend, or contribute to Hermes Agent.|`autonomous-ai-agents/hermes-agent`| ... ## gaming Section titled…

3. **Optional Skills Catalog - Hermes Agent - Nous Research**
   - URL: https://hermes-agent.nousresearch.com/docs/reference/optional-skills-catalog
   - Descripción: |Skill|Description| |--|--| |**audiocraft-audio-generation**|AudioCraft: MusicGen text-to-music, AudioGen text-to-sound.| | … | … | |**unreal-mcp**|Automate Unreal Engine editor scenes, actors, and renders.| ... ## gaming ​ |Skill|Description| |--|--| |**minecraft-modpack-server**|Host modded Minecraft servers (CurseForge, Modrinth).| |**pokemon-player**|Play Pokemon via headless emulator + RAM re…

4. **Creating Skills | Hermes Agent - Nous Research**
   - URL: https://hermes-agent.nousresearch.com/docs/developer-guide/creating-skills
   - Descripción: Skills are the preferred way to add new capabilities to Hermes Agent. They're easier to create than tools, require no code changes to the agent, and can be shared with the community. ... Make it a **Skill** when: - The capability can be expressed as instructions + shell commands + existing tools - It wraps an external CLI or API that the agent can call via `terminal` or `web_extract` - It doesn't …

5. **Working with Skills - hermes-agent.nousresearch.com**
   - URL: https://hermes-agent.nousresearch.com/docs/guides/work-with-skills
   - Descripción: Skills are on-demand knowledge documents that teach Hermes how to handle specific tasks — from generating ASCII art to managing GitHub PRs. This guide walks you through using them day to day. ... Every Hermes installation ships with bundled skills. See what's available: ``` # In any chat session: /skills # Or from the CLI: hermes skills list ``` This shows a compact list with names and description…

6. **Skills Hub | Hermes Agent中文文档**
   - URL: https://hermes-agent.lzw.me/docs/en/skills/
   - Descripción: ### hermes-agent ✓ Built-in Complete guide to using and extending Hermes Agent — CLI usage, setup, configuration, spawning additional agents, gateway platforms, skills, voice, tools, profiles, and a concise contributor reference. Load this skill when helping users configure Hermes, troubleshoot issues, spawn agent instances, or make code contributions. ... ### minecraft-modpack-server ✓ Built-in S…

7. **ZeroPointRepo/awesome-hermes-skills: Hermes Agent ...**
   - URL: https://github.com/ZeroPointRepo/awesome-hermes-skills
   - Descripción: Covers the **82 built-in skills** and **117-skill optional catalog** that ship with Hermes v0.20.6, plus **169 community skills, plugins, agent profiles, memory providers, surfaces, and tools** vetted for quality. ... Works in Hermes, Claude, OpenClaw and other agent runtimes. ... - **hermes-agent** — Use, configure, theme, extend, and orchestrate Hermes Agent. ... ### 🏠 Smart Home (1) ... ### 💻 S…

8. **Build a Hermes Plugin**
   - URL: https://hermes-agent.nousresearch.com/docs/developer-guide/plugins
   - Descripción: |If you want to add…|Read| |--|--| |**Additional skill sources** (custom GitHub repos, private skill indexes)|Skills — `hermes skills tap add <repo>` · Publishing a tap| ... See Store durable state. ... - **Ship skills with your plugin:** register them with `ctx.register_skill()`. See Bundle skills. ... … ### Bundle skills ​ Plugins can ship skill files that the agent loads via `skill_view("plugin…



## Categoría 2: C# / .NET Development

### Query: `csharp dotnet game mod loader content pack validation skills`

**Resultados:** 8

1. **Mod Loading & Validation | SP-Tushonka/server-csharp | DeepWiki**
   - URL: https://deepwiki.com/SP-Tushonka/server-csharp/6.1-mod-loading-and-validation
   - Descripción: # Mod Loading & Validation The modding system in `SPTushonka.Server` is designed to allow external C# assemblies to extend server functionality, add new game content, or modify existing logic through Dependency Injection (DI) and Blazor-based UI components. The system prioritizes isolation during the initial loading phase to ensure assembly integrity before mods are integrated into the main server…

2. **ModLoader and Mod Lifecycle | hk-modding/api | DeepWiki**
   - URL: https://deepwiki.com/hk-modding/api/2.2-modloader-and-mod-lifecycle
   - Descripción: # ModLoader and Mod Lifecycle This page documents the `ModLoader` system, which is responsible for discovering, loading, and managing the lifecycle of all mods. It covers the discovery phase (scanning assemblies), preload phase (requesting game assets), initialization phase (activating mods), and runtime state management (enabling/disabling mods). ... ## ModLoader Overview `ModLoader` is a static …

3. **spt-server-csharp/SPTarkov.Server/Modding/ModLoader.cs at ...**
   - URL: https://github.com/binlligence/spt-server-csharp/blob/main/SPTarkov.Server/Modding/ModLoader.cs
   - Descripción: # ModLoader.cs using SPTarkov.Server.Exceptions; namespace SPTarkov.Server.Modding; public sealed class ModLoader(ISptLogger<ModLoader> logger, ModValidator modValidator) { private List<SptMod> _loadedMods = []; private readonly List<EnumPrepatch> _enumPrepatches = []; private ModuleDefinition? _serverCoreModule; private byte[]? _serverCoreSymbols; private readonly List<PrepatchResultEntry> _prepa…

4. **github.com › blob › mainserver-csharp/SPTarkov.Server/Modding/ModLoader.cs at main ·...**
   - URL: https://github.com/sp-tarkov/server-csharp/blob/main/SPTarkov.Server/Modding/ModLoader.cs
   - Descripción: # ModLoader.cs using SPTarkov.Server.Exceptions; namespace SPTarkov.Server.Modding; public sealed class ModLoader(ISptLogger<ModLoader> logger, ModValidator modValidator) { private List<SptMod> _loadedMods = []; private readonly List<EnumPrepatch> _enumPrepatches = []; private ModuleDefinition? _serverCoreModule; private byte[]? _serverCoreSymbols; private readonly List<PrepatchResultEntry> _prepa…

5. **C# Scripting for Game Mods: Patterns and Pitfalls - PulseGeek**
   - URL: https://pulsegeek.com/articles/csharp-scripting-for-game-mods-patterns-and-pitfalls/
   - Descripción: # C# Scripting for Game Mods: Patterns and Pitfalls ## Key takeaways - Prefer event driven patterns and defer heavy work until the game is ready. - Guard API calls with capability checks and version fences to avoid crashes. - Patch narrowly with clear scopes and reversible hooks for safer updates. - Persist configuration defensively and validate saves before applying changes. - Profile initializat…

6. **sp-tarkov/server-csharp | DeepWiki**
   - URL: https://deepwiki.com/sp-tarkov/server-csharp/1-overview
   - Descripción: SPTarkov Server is a C# implementation of a game server for Escape from Tarkov that enables single-player and modded gameplay. ... - **Modding support** - Loading and validating custom DLL mods ... Enables seasonal content and custom mod support. **Key Classes:** - `SeasonalEventService` - Halloween, Christmas, event management - `CustomItemService` - Custom item registration - `ModDllLoader` - Mo…

7. **Asset and Resource Management | hk-modding/api | DeepWiki**
   - URL: https://deepwiki.com/hk-modding/api/4-asset-and-resource-management
   - Descripción: # Asset and Resource Management 1. **Preloader System**: Loads game scenes and extracts GameObject assets before mod initialization, allowing mods to cache references to game objects that would otherwise be inaccessible.

8. **tModLoader**
   - URL: https://context7.com/tmodloader/tmodloader
   - Descripción: # tModLoader ### Install dotnet SDK on Steam Deck Command to install the dotnet 8 SDK via the provided script in the LaunchUtils folder.


### Query: `harmony bepinex patch injection skills claude code`

**Resultados:** 8

1. **Patcher Contract**
   - URL: https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html
   - Descripción: # Using preloader patchers ## Preface As of version 4.0, BepInEx allows to write *preload-time patchers* that modify assemblies before the game loads them. While most plug-ins can use Harmony to do runtime patching, using preload-time patchers provides more fine control over how the assembly is patched. It is still recommended that *you use Harmony wherever possible* because Harmony makes sure all…

2. **BepInEx Game Modding | Claude Code Skill for Unity Mods**
   - URL: https://mcpmarket.com/tools/skills/bepinex-game-modding
   - Descripción: Develops and configures Unity game plugins using the BepInEx framework and Harmony patching. ... This skill empowers Claude to architect and implement robust BepInEx plugins for Unity-based games, specifically optimized for Single Player Tarkov (SPT) environments. It provides structured guidance on creating plugin entry points with the [BepInPlugin] attribute, managing complex mod dependencies, an…

3. **Harmony Class | BepInEx/HarmonyX | DeepWiki**
   - URL: https://deepwiki.com/BepInEx/HarmonyX/7.1-harmony-class
   - Descripción: This page provides a deep technical dive into the HarmonyX internal patch pipeline, covering how original methods are intercepted, wrapped, and rewritten. It details the execution flow from `PatchProcessor` through `PatchFunctions.UpdateWrapper`, the orchestration performed by `HarmonyManipulator`, and the underlying IL building and manipulation primitives provided by `ILEmitter` and `ILManipulato…

4. **Patch Methods**
   - URL: https://docs.bepinex.dev/master/articles/dev_guide/preloader_patchers.html
   - Descripción: As of version 4.0, BepInEx allows to write *preload-time patchers* that modify assemblies before the game loads them. While most plug-ins can use Harmony to do runtime patching, using preload-time patchers provides more fine control over how the assembly is patched. It is still recommended that *you use Harmony wherever possible* because Harmony makes sure all patches are compatible with each othe…

5. **Patching - BepInEx/HarmonyX GitHub Wiki**
   - URL: https://github-wiki-see.page/m/BepInEx/HarmonyX/wiki/Patching
   - Descripción: # Patching - BepInEx/HarmonyX GitHub Wiki Inside the class Harmony searches for methods with the specific names `TargetMethod()`, `Prepare()`, `Prefix()`, `Postfix()` or `Transpiler()`. Instead of relying on those names, you can also use the method annoations `[HarmonyTargetMethod]`, `[HarmonyPrepare]`, `[HarmonyPrefix]`, `[HarmonyPostfix]` or `[HarmonyTranspiler]`. ... Most of the times, you will…

6. **github.com · bepinex-il2cpp-modding · SKILLbepinex-modding/skills/bepinex-il2cpp-modding/SKILL.md at ...**
   - URL: https://github.com/KaelLim/bepinex-modding/blob/main/skills/bepinex-il2cpp-modding/SKILL.md
   - Descripción: |name|bepinex-il2cpp-modding| |--|--| |description|Use when creating or debugging a BepInEx mod for a Unity IL2CPP game (a GameAssembly.dll + <Game>_Data/il2cpp_data/ + BepInEx/interop/ layout) — writing Harmony patches, building a cheat trainer or QoL/behavior mod with an injected-MonoBehaviour menu/hotkeys, or when an IL2CPP plugin throws ObjectCollectedException, a Harmony patch never fires, or…

7. **docs.bepinex.dev › indexWelcome to BepinEx Docs! | BepInEx Docs**
   - URL: https://docs.bepinex.dev/index.html
   - Descripción: ## About BepInEx - **runtime method patching with Harmony** -- includes Harmony and MonoMod to enable runtime method injection;

8. **BepInEx.Harmony/HarmonyX2Interop/Patching/Patch.cs at ...**
   - URL: https://github.com/BepInEx/BepInEx.Harmony/blob/master/HarmonyX2Interop/Patching/Patch.cs
   - Descripción: BepInEx.Harmony/HarmonyX2Interop/Patching/Patch.cs at master · BepInEx/BepInEx.Harmony · GitHub - - AVAILABLE ADD-ONS 1. BepInEx 2. BepInEx.Harmony


### Query: `site:github.com dotnet mod loader plugin skills`

**Resultados:** 8

1. **skills/README.md at main · dotnet/skills · GitHub**
   - URL: https://github.com/dotnet/skills/blob/main/README.md
   - Descripción: # .NET Agent Skills This repository contains the .NET team's curated set of portable skills and host-specific custom agents for coding agents. ... |Plugin|Description| |--|--| |dotnet|High-level .NET development skills plus C# language server (LSP) integration for hosts that support it.| | … | … | |dotnet-msbuild|Comprehensive MSBuild and .NET build skills: failure diagnosis, performance optimizat…

2. **github.com › dotnet › skillsGitHub - dotnet/skills: Repository for skills to assist AI ...**
   - URL: https://github.com/dotnet/skills/
   - Descripción: GitHub - dotnet/skills: Repository for skills to assist AI coding agents with .NET and C# · GitHub ... This repository contains the .NET team's curated set of portable skills and host-specific custom agents for coding agents. ... |Plugin|Description| |--|--| |dotnet|High-level .NET development skills plus C# language server (LSP) integration for hosts that support it.| | … | … | |dotnet-nuget|NuGe…

3. **Testing And Validation**
   - URL: https://github.com/dotnet/skills/blob/main/CONTRIBUTING.md
   - Descripción: This repository contains shared building blocks for coding agents: - Skills: reusable, task focused instruction packs - Agents: GitHub Copilot custom-agent configurations that bundle tool expectations and skill selection ... plugins/ <plugin>/ plugin.json .claude-plugin/ plugin.json .codex-plugin/ plugin.json skills/ <skill-name>/ SKILL.md scripts/ references/ assets/ agents/ <agent-name>.agent.md…

4. **skills/plugins/dotnet at main · dotnet/skills**
   - URL: https://github.com/dotnet/skills/tree/main/plugins/dotnet
   - Descripción: skills/plugins/dotnet at main · dotnet/skills · GitHub ... # dotnet Core .NET and C# skills for coding agents. ## Includes - Common .NET development skills - A C# language server integration for `.cs` files ## LSP This plugin declares a C# LSP server that is launched through the .NET CLI. The LSP declaration is available to hosts that support the plugin `lspServers` extension. Codex plugin install…

5. **Exposing dotnet/skills plugins via dotnet skill command #54243**
   - URL: https://github.com/dotnet/sdk/issues/54243
   - Descripción: Skills are installed via a combination of `marketplace.json` and `plugin.json`. You can learn more about those via some walkthroughs I created. ... A `skill` command is the natural choice to expose skills. ... ``` $ dotnet skill # .NET agent skills Curated set of .NET skills and custom agents for coding agents, maintained in the [dotnet/skills](https://github.com/dotnet/skills) repo. | Plugin | De…

6. **skills/.agents/skills/create-skill/SKILL.md at main · dotnet ...**
   - URL: https://github.com/dotnet/skills/blob/main/.agents/skills/create-skill/SKILL.md
   - Descripción: |description|Scaffolds new agent skills for the dotnet/skills repository. Use when creating a new skill, generating SKILL.md files, writing a skill description that the runtime will actually route to, or setting up skill directory structures. Handles frontmatter generation, section templates, and validation guidance. Do not use for fixing a skill that already fails its evaluation (use improve-skil…

7. **richlander/dotnet-skills**
   - URL: https://github.com/richlander/dotnet-skills
   - Descripción: GitHub - richlander/dotnet-skills: Skills for using tools that improve LLM-driven .NET / C# development · GitHub ... # dotnet-skills .NET skills for coding assistants. The skills describe how to use specific .NET tools that accelerate LLM-driven .NET + C# development. ... Use `/plugin` to manage marketplaces and install plugins. For a complete walkthrough with screenshots for both Copilot and Clau…

8. **novotnyllc/dotnet-artisan**
   - URL: https://github.com/novotnyllc/dotnet-artisan
   - Descripción: # dotnet-artisan > Comprehensive .NET development skills for modern C#, ASP.NET, MAUI, Blazor, and cloud-native applications ... ## Overview **dotnet-artisan** provides 10 skills (2 routing + 8 domain) and 14 specialist agents for .NET development. It is compatible with Claude Code, GitHub Copilot CLI, and OpenAI Codex. The installable plugin lives at `plugins/dotnet-artisan`. ... ## Skill Catalog…


### Query: `skills.sh csharp game development`

**Resultados:** 8

1. **Game Developer - Skill for Claude Code · Skillselion**
   - URL: https://skillselion.com/skills/jeffallan/claude-skills/game-developer
   - Descripción: ## Install `npx skills add https://github.com/jeffallan/claude-skills --skill game-developer` ## What is this skill? - Separates components (data), entities (IDs), and systems (logic over spans) - C# examples: Position, Velocity, Health, PlayerTag marker components - MovementSystem batch-updates positions from velocities with deltaTime - World helper sketch for create entity, add/get component by …

2. **gamedev-skills/awesome-gamedev-agent-skills**
   - URL: https://www.skills.sh/gamedev-skills/awesome-gamedev-agent-skills
   - Descripción: gamedev-skills/awesome-gamedev-agent-skills — Agent skills skills/gamedev-skills/awesome-gamedev-agent-skills # gamedev-skills/awesome-gamedev-agent-skills 75 skills342.2K total installsGitHub `$ npx skills add gamedev-skills/awesome-gamedev-agent-skills` Command Skill Installs ### game-feel 7.7K ### game-ui-ux 7.6K ### level-design 6.2K ### game-ai 5.8K ### performance-optimization 5.8K ### audio…

3. **game-development — mindrally/skills - The Agent Skills Directory**
   - URL: https://www.skills.sh/mindrally/skills/game-development
   - Descripción: game-development — mindrally/skills skills/mindrally/skills/game-development # game-development Installation Command `$ npx skills add https://github.com/mindrally/skills --skill game-development` SKILL.md # Game Development You are an expert in game development with deep knowledge of C#, Unity, Lua, and scalable game architecture. ## C# Unity Game Development ### Key Principles - Write clear, tec…

4. **c-sharp — mindrally/skills - The Agent Skills Directory**
   - URL: https://www.skills.sh/mindrally/skills/c-sharp
   - Descripción: c-sharp — mindrally/skills skills/mindrally/skills/c-sharp # c-sharp Installation Command `$ npx skills add https://github.com/mindrally/skills --skill c-sharp` ... # C# Development Guidelines You are an expert in C# development with deep knowledge of .NET, Blazor, Unity, and modern C# language features.

5. **csharp-pro by rmyndharis/antigravity-skills - Skills.sh**
   - URL: https://skills.sh/rmyndharis/antigravity-skills/csharp-pro
   - Descripción: skills/rmyndharis/antigravity-skills/csharp-pro # csharp-pro SKILL.md ... ## Use this skill when - Working on csharp pro tasks or workflows - Needing guidance, best practices, or checklists for csharp pro ... ## Instructions - Clarify goals, constraints, and required inputs. - Apply relevant best practices and validate outcomes. - Provide actionable steps and verification. - If detailed examples a…

6. **csharp-developer — baotoq/micro-commerce**
   - URL: https://www.skills.sh/baotoq/micro-commerce/csharp-developer
   - Descripción: csharp-developer — baotoq/micro-commerce skills/baotoq/micro-commerce/csharp-developer # csharp-developer Installation Command `$ npx skills add https://github.com/baotoq/micro-commerce --skill csharp-developer` SKILL.md # C# Developer Senior C# developer with mastery of .NET 10+ and Microsoft ecosystem. Specializes in high-performance web APIs, cloud-native solutions, and modern C# language featu…

7. **jeffallan/claude-skills**
   - URL: https://www.skills.sh/jeffallan/claude-skills
   - Descripción: `$ npx skills add jeffallan/claude-skills` ... ### sql-pro 6.3K ### game-developer 6.2K ### rust-engineer ... ### react-native-expert 4.8K ### cli-developer 4.7K ### angular-architect 4.7K ### csharp-developer 4.7K ### dotnet-core-expert 4.6K

8. **wshaddix/dotnet-skills**
   - URL: https://www.skills.sh/wshaddix/dotnet-skills
   - Descripción: wshaddix/dotnet-skills — Agent skills skills/wshaddix/dotnet-skills # wshaddix/dotnet-skills ... `$ npx skills add wshaddix/dotnet-skills` ... ### dotnet-testing-strategy 606 ### modern-csharp-coding-standards 579 ### csharp-wolverinefx ... ### dotnet-csharp-modern-patterns 183



## Categoría 3: AssetBundle & 3D Content

_Pendiente (Task 4)._

### Task 4 · Query 1 — `unity assetbundle authoring skills claude code`

(Ver resultados en el reporte de la búsqueda ejecutada: Unity plugin, Claude skills docs, Unity marketplace, Unity Development Expert MCP Market.)

### Task 4 · Query 2 — `blender unity pbr texture pipeline skills`

(EL4CTEO/blender-skills 22 skills; aitextured photo→PBR guide; V2Fun handoff validation; Meshy AI PBR; lovecatisgood 3D asset generation skill pack.)

### Task 4 · Query 3 — `site:github.com unity assetbundle mod tools`

(shaphon/ModtheArk; chr1974/COI-modding; dustinlacewell/MultiMod; risk-of-thunder/MultiMod archived; emtee40/UABE Asset Bundle Extractor.)

### Task 4 · Query 4 — `3d model rigging animation unity skills`

(Unity Manual humanoid rigging (6000.5/6000.6/6000.0); Unity glossary rigging; Unity Academy Animation Rigging overview.)

### Candidatas Tasks 4

- EL4CTEO/blender-skills — https://github.com/EL4CTEO/blender-skills — Adaptar.
- lovecatisgood-sudo/3d-asset-generation-blender-unity-game-development-skills — https://github.com/lovecatisgood-sudo/3d-asset-generation-blender-unity-game-development-skills — Adaptar.
- dustinlacewell/MultiMod — https://github.com/dustinlacewell/MultiMod — Adoptar/Adaptar.
- emtee40/UABE — https://github.com/emtee40/UABE — Adoptar.
- Besty0728/Unity-Skills — https://github.com/Besty0728/Unity-Skills — Adoptar (material/texture).

- EL4CTEO/blender-skills (blender-pbr-materials, blender-texture-baking) — Adaptar.
- lovecatisgood-sudo/3d-asset-generation-blender-unity-game-development-skills — Adaptar.
- dustinlacewell/MultiMod — Adoptar/Adaptar (BepInEx + AssetBundle + scripts).
- emtee40/UABE — Adoptar (inspección de bundles).
- Besty0728/Unity-Skills (material/texture) — Adoptar (import settings).

## Categoría 4: PBR & Texture Pipelines

_Pendiente (Task 5)._

### Task 5 · Query 1 — `pbr texture authoring skills claude code blender`

(MammothClub course; RobLe3 32 skills; EL4CTEO 23 skills; mcpmarket blender-3d-asset-pipeline; mcpmarket substance-3d-texturing.)

### Task 5 · Query 2 — `material maker armorpaint unity texture pipeline skills`

(gitnux texture software; Besty0728 Unity-Skills material/texture; besty0728-material skill; Locker3d Substance vs free; ArmorPaint manual.)

### Task 5 · Query 3 — `site:github.com pbr texture validation tools`

(ZeroLifeDev/Texture-Creator PBR Reference Forge; glbkit-tools/awesome-3d-assets; SEAMS seamless-texture-maker; ai-pbr-texture-generator; playtex-ai/creator-playground.)

### Task 5 · Query 4 — `substance painter alternative free skills`

(CraftPBR guide; 3dxdev.com; meshy.ai blog; fixthephoto.com; strayspark.studio — InstaMAT, ArmorPaint, Material Maker.)

### Candidatas Tasks 5

- EL4CTEO blender-skills — https://github.com/EL4CTEO/blender-skills — Adoptar/Adaptar.
- RobLe3 skills — https://www.skillsdirectory.com/authors/roble3 — Adaptar.
- Besty0728/Unity-Skills — https://github.com/Besty0728/Unity-Skills — Adoptar.
- SEAMS — https://github.com/Seasoned-In-Chaos/seamless-texture-maker — Adaptar.
- PBR Reference Forge — https://github.com/ZeroLifeDev/Texture-Creator — Adaptar.
- InstaMAT — https://www.strayspark.studio/blog/substance-painter-alternatives-blender-2026 — Adaptar.

- EL4CTEO blender-skills (blender-pbr-materials, blender-texture-baking) — Adoptar/Adaptar.
- RobLe3 skills (Blender Materials, UV Texturing) — Adaptar.
- Besty0728/Unity-Skills (material/texture) — Adoptar.
- SEAMS (seamless-texture-maker) — Adaptar (validación seams/normals).
- PBR Reference Forge (Texture-Creator) — Adaptar (photo→PBR, alpha).
- InstaMAT (free tier) — Adaptar (pintura 3D gratuita).

## Categoría 5: AI & Game Research

### Query 6.1 — `game ai behavior tree skills claude code`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | game-ai (gamedev-skills) | https://claudeskills.info/skills/gamedev-skills/awesome-gamedev-agent-skills/game-ai/ | Apache-2.0, 1.5k stars. FSM/BT/steering/A* engine-neutral. Instala: `npx skills add gamedev-skills/awesome-gamedev-agent-skills --skill game-ai --agent claude-code`. |
| 2 | game-ai-behavior-trees (LobeHub) | https://lobehub.com/skills/neversight-learn-skills.dev-game-ai-behavior-trees | BT modular con blackboard, visual editor, hot-reload, serialización save/load, integración Unity/Unreal. |
| 3 | HermeticOrmus/LibreGameDev-Claude-Code | https://github.com/HermeticOrmus/LibreGameDev-Claude-Code | 20 plugins (MIT, archivado). Incluye `ai-game-behavior` (BT, FSM, utility AI, GOAP, navmesh). |
| 4 | Behavior Trees & Utility AI (dirskills) | https://dirskills.com/skills/behavior-trees-utility-ai | BT runtime + Utility AI. Instala: `npx degit https://github.com/gamedev-skills/awesome-gamedev-agent-skills/tree/main/skills/disciplines/ai-behavior-trees-utility-ai ~/.claude/skills/...` |
| 5 | Game Development (skillsdirectory, levalencia) | https://www.skillsdirectory.com/skills/levalencia-game-development | Orquestador con sección de Behavior Trees. |

**Clasificación:** game-ai (gamedev-skills) → **Adaptar** (engine-neutral; requiere binding a Unity 2021.3). BT runtime (LobeHub/dirskills) → **Adaptar**. LibreGameDev → **Adaptar** (archivado, usar como referencia).

### Query 6.2 — `reverse engineering game dll skills`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | reverse-engineering-tools (skillsmp, gmh5225/awesome-game-security) | https://skillsmp.com/creators/gmh5225/awesome-game-security/claude-skills-reverse-engineering | Guía Unity: IL2CPP Dumper, dnSpy para Mono, hooking. |
| 2 | universal-modder (rehan-remade) | https://github.com/rehan-remade/universal-modder | MIT, 8.4k stars. Skills: `reverse-engineering`, `game-recon`, `mod-any-game`. ILSpy/Cheat Engine/Frida/RenderDoc. Instala: `npx skills add https://github.com/rehan-remade/universal-modder`. |
| 3 | universal-modder reverse-engineering/SKILL.md | https://github.com/rehan-remade/universal-modder/blob/main/skills/reverse-engineering/SKILL.md | Detalle: Mono → `ilspycmd`, IL2CPP → Cpp2IL/Il2CppDumper, Ghidra/IDA via MCP. |
| 4 | awesome-game-security reverse-engineering (gmh5225) | https://github.com/gmh5225/awesome-game-security/blob/main/.claude/skills/reverse-engineering/SKILL.md | Análisis binario reproducible, disassembly/decompilación. |
| 5 | deepwiki reverse engineering tools | https://deepwiki.com/gmh5225/awesome-game-security/4.2-reverse-engineering-and-analysis-tools | Combinación static+dynamic, DBI (Frida). |

**Clasificación:** universal-modder → **Adoptar** (MIT, formato Agent Skills compatible con Hermes, directamente aplicable al flujo Mono de Rogue Command). gmh5225 → **Adaptar** (enfoque anti-cheat/seguridad, parcialmente relevante).

### Query 6.3 — `site:github.com game research tools skills`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | abagames/agentic-gamedev-skills | https://github.com/abagames/agentic-gamedev-skills | Skills bajo `.agents/skills/` con SKILL.md (mismo formato que proyecto). Plugins para Codex/Claude. |
| 2 | fagemx/gstack-game | https://github.com/fagemx/gstack-game | 29 skills de producción de juegos (design review, QA, release). |
| 3 | gmh5225/awesome-game-security | (ver arriba) | Reverse engineering + análisis. |
| 4 | rehan-remade/universal-modder | (ver arriba) | Modding universal. |
| 5 | GitHub topics/game-research | https://github.com/topics/game-research | Repos de investigación: cheatengine-mcp-bridge (1.6k stars), GameDesignOS, modding tools. |

**Clasificación:** universal-modder (ya listado). abagames → **Adaptar** (formato compatible, pero enfocado en mini-games; extraer skills de workflow). gstack-game → **Adaptar** (proceso de producción, no modding directo). cheatengine-mcp-bridge → **Descartar** (enfoque cheat/hack, no modding legítimo; además riesgo de seguridad).

### Query 6.4 — `steam runtime validation skills`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | Steam runtime build (ValveSoftware/steam-runtime) | https://deepwiki.com/ValveSoftware/steam-runtime/3-building-the-runtime | Build system, tests. No skill. |
| 2 | Steam Machine developer guide | https://functions.top/understanding-the-upcoming-steam-machine-and-its-compatibili | Verificación automatizada/manual de Steam Machine. |
| 3 | Steam launch fails / runtimes | https://windowsforum.com/news/steam-launch-fails-in-2026-silent-causes-and-fixes-verify-drivers-anti-cheat.427472/ | VC++ redistributables. |
| 4 | Steam games launch fixes | https://thetoolstrunk.com/why-wont-my-steam-games-launch/ | Verificación de integridad. |
| 5 | (sin resultados de skill directa) | — | No se encontró skill instalable. |

**Clasificación:** Ninguna skill nueva relevante. El proyecto ya posee `steam-runtime-validation`. → **Descartar** (duplicado / fuera de alcance skill). Nota: la documentación de Steam Machine verification puede enriquecer la skill existente.

## Categoría 6: Claude Code Marketplace & GitHub Skills

### Query: `Claude Code marketplace skills C# Unity BepInEx`
**Resultados:** 8

1. **Universal Modder** (rehan-remade/universal-modder) — MIT, 8.4k stars. Skills: `reverse-engineering`, `game-recon`, `mod-any-game`. Aplicación directa al flujo Mono de Rogue Command (Assembly-CSharp.dll).
   - URL: https://github.com/rehan-remade/universal-modder
   - Clasificación: **Adoptar**
   - Notas: Compatible con el flujo Mono de Rogue Command. Skill `reverse-engineering` usable con `ilspycmd`. Complementa `game-research`.

2. **ing-gom/claude-mod-skills** — Claude Code skills para autores de mods C#/Harmony. 8 skills propios + 1 companion (`ilspy-decompile`, de `aaronontheweb/dotnet-skills`). Cubre `mod-bootstrap`, `mod-assembly-map`, `mod-concept-grill`, `mod-goal`, `find-mods`, `mod-perf-guard`, `harmony-patch-review`, `mod-hot-reload`. Carga basada en Harmony (BepInEx, MelonLoader, Godot).
   - URL: https://github.com/ing-gom/claude-mod-skills
   - Clasificación: **Adoptar**
   - Notas: Más relevante para el flujo C#/Harmony de Rogue Command. Estructura de skills compatible con el formato del proyecto. Compatible con Cursor/Aider/Continue/Cline.

3. **rkuhn153/bepinex-mcp** — Puente Unity + BepInEx en vivo vía MCP. BepInEx 5 (Mono) y BepInEx 6 (IL2CPP). Skills: `bepinex-mcp`, `unity-bepinex-modder`.
   - URL: https://github.com/rkuhn153/bepinex-mcp
   - Clasificación: **Adoptar**
   - Notas: Directamente relevante para el stack BepInEx 5.4.23.5 de Rogue Command. Brinda read/write/patch live sobre el juego en ejecución.

4. **Unity-Technologies/unity-agent-plugin** — Plugin oficial de Unity para Claude Code (Unity Technologies). Skills curados de desarrollo de juegos, monetización, optimización. 1089 stars. Compatible con Claude Code.
   - URL: https://github.com/Unity-Technologies/unity-agent-plugin
   - Clasificación: **Adaptar**
   - Notas: Plugin oficial de Unity; verificar compatibilidad Unity 2021.3.

5. **Unity-Technologies/skills** — 29 skills oficiales de Unity para agentes (unity-cli, build-live-game, urp-postprocessing, etc.). Agnóstico de agente. Instalable en Hermes copiando carpetas de skills.
   - URL: https://github.com/Unity-Technologies/skills
   - Clasificación: **Adaptar**
   - Notas: Unity 6 centrado; verificar compatibilidad con Unity 2021.3. Algunos skills requieren Unity Editor + CLI.

6. **gigio1023/unity-game-dev-skill** — Skill cross-harness para proyectos Unity. Enseña al agente a leer el proyecto antes de elegir API, preservar arquitectura, reportar solo validación observada.
   - URL: https://github.com/gigio1023/unity-game-dev-skill
   - Clasificación: **Adaptar**
   - Notas: Buen precedent para el flujo de AssetBundle + BepInEx. El skill enfatiza evidencia real vs. aserciones.

7. **gruiyuan/taiwu-mod-dev-skill** — Skill completo de desarrollo de mods C# para juego Unity Mono (太吾绘卷). 4 fases: precompilación, decompilación, desarrollo, publicación. Incluye base de conocimiento de mecanismos del juego.
   - URL: https://github.com/gruiyuan/taiwu-mod-dev-skill
   - Clasificación: **Adaptar**
   - Notas: Analogía directa con Rogue Command (Unity Mono + BepInEx + Harmony). Flujo de precompilación/versión es útil.

8. **skillsdirectory.com/unity-mod-frameworks** — Skill "Unity Mod Frameworks (Grade A)" de Skills Directory. BepInEx, Harmony, MonoMod, IL2CPP, Mono. Cubre RimWorld, Valheim, Stardew Valley, etc.
   - URL: https://www.skillsdirectory.com/skills/shugokifable-unity-mod-frameworks
   - Clasificación: **Adaptar**
   - Notas: Referencia de frameworks; útil para documentar opciones de modding en el proyecto.

### Query: `GitHub skills C# BepInEx Harmony Unity modding`
**Resultados:** 8

1. **BepInEx/BepInEx** — Framework de patcher y plugins para Unity/XNA. Infraestructura de modding central. C#, LGPL-2.1. 5,952 estrellas.
   - URL: https://github.com/bepinex/bepinex
   - Clasificación: **Referencia** (framework núcleo; proyecto usa v5.4.23.5)

2. **BepInEx/HarmonyX** — Harmony construida sobre MonoMod.RuntimeDetours. Enfoque en modding de Unity.
   - URL: https://github.com/bepinex/harmonyx
   - Clasificación: **Referencia**

3. **dotnet/skills** — Conjunto curado de skills portátiles para agentes de código de .NET por el equipo de .NET. Incluye plugins dotnet, dotnet-msbuild, dotnet-nuget.
   - URL: https://github.com/dotnet/skills
   - Clasificación: **Referencia**
   - Notas: Buena referencia de .NET/C# skills; el proyecto usa netstandard2.1.

4. **gamedev-skills/awesome-gamedev-agent-skills** — 75 skills incluyendo game-ai, unity-csharp-scripting, level-design, performance-optimization. 342.2K instalaciones.
   - URL: https://github.com/gamedev-skills/awesome-gamedev-agent-skills
   - Clasificación: **Adaptar**
   - Notas: `unity-csharp-scripting` y `game-ai` son relevantes para complementar `ai-authoring`.

5. **mindrally/skills** — Skills de game-development, c-sharp, unity. Skill unity: "Unity game development and C#", con MonoBehaviour, ScriptableObjects, TextMeshPro.
   - URL: https://github.com/mindrally/skills
   - Clasificación: **Adaptar**
   - Notas: Skill unity útil como referencia de arquitectura C#/Unity.

6. **jeffallan/claude-skills** — Skill game-developer con ECS, C#. Instalable vía `npx skills add jeffallan/claude-skills --skill game-developer`.
   - URL: https://github.com/jeffallan/claude-skills
   - Clasificación: **Adaptar**
   - Notas: Enfoque ECS; adaptar para Unity 2021.3.

7. **ZeroPointRepo/awesome-hermes-skills** — Catálogo curado de 268 skills (82 incorporados + 117 opcionales + 169 de la comunidad). Referencia para descubrimiento del hub de Hermes.
   - URL: https://github.com/ZeroPointRepo/awesome-hermes-skills
   - Clasificación: **Referencia** (solo catálogo)
   - Notas: Usar `hermes skills search` / `hermes skills browse` para explorar el hub.

8. **abagames/agentic-gamedev-skills** — Skills bajo `.agents/skills/` (mismo formato que el proyecto). Plugins para Codex/Claude.
   - URL: https://github.com/abagames/agentic-gamedev-skills
   - Clasificación: **Adaptar**
   - Notas: Formato compatible; enfocado en mini-juegos. Extraer skills de workflow relevantes.

### Clasificación para Rogue Command

**Adoptar (inmediato):**
1. `universal-modder` — MIT; directamente aplicable al flujo Mono de Rogue Command; complementa `game-research`.
2. `rkuhn153/bepinex-mcp` — Bridge en vivo para BepInEx 5 Mono; compatible con el stack actual.
3. `ing-gom/claude-mod-skills` — Flujo C#/Harmony; 8 skills + companion; estructura compatible con el formato de skills del proyecto.

**Adaptar:**
1. `Unity-Technologies/skills` — Alinear a Unity 2021.3 en lugar de Unity 6.
2. `gigio1023/unity-game-dev-skill` — Adaptar para el flujo de AssetBundle + BepInEx del proyecto.
3. `gruiyuan/taiwu-mod-dev-skill` — Extraer el flujo de precompilación/decompilación/Harmony.
4. `gamedev-skills/awesome-gamedev-agent-skills` — Seleccionar `unity-csharp-scripting` y `game-ai`.
5. `mindrally/skills` — Usar skill `unity` (C# + Unity).

**Referencia:**
1. `BepInEx/BepInEx`, `BepInEx/HarmonyX` — Frameworks núcleo (ya integrados al proyecto).
2. `dotnet/skills` — Referencia de .NET skills.
3. `ZeroPointRepo/awesome-hermes-skills` — Catálogo para descubrimiento del hub de Hermes.

**Skills a evitar:**
- Enfoque de hackeo/cheat (cheatengine-mcp-bridge) — fuera de alcance (modding legítimo).
- Skills centradas en Unity 6 sin compatibilidad verificada con Unity 2021.3.

## Hub de Skills de Hermes (Task 7)

### Query 7.1 — `site:skills.sh game development`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | game-development (vudovn/antigravity-kit) | https://skills.sh/vudovn/antigravity-kit/game-development | Orquestador con sub-skills (web/mobile/pc/vr, 2D/3D, audio, arte). |
| 2 | game-development (mindrally/skills) | https://www.skills.sh/mindrally/skills/game-development | C# Unity, MonoBehaviour/ScriptableObjects. |
| 3 | phaser-gamedev (chongdashu) | https://www.skills.sh/chongdashu/phaserjs-tinyswords/phaser-gamedev | Phaser 3 browser games. |
| 4 | make-game (playableintelligence) | https://www.skills.sh/playableintelligence/game-creator/make-game | Pipeline de desarrollo de juegos. |
| 5 | game-asset-generation-cellcog | https://www.skills.sh/site/clawhub.ai/game-asset-generation-cellcog | Generación de assets (CellCog). |

**Clasificación:** mindrally game-development → **Adaptar** (C# Unity, relevante). vudovn → **Adaptar** (orquestador genérico). phaser/cellcog → **Descartar** (no Unity).

### Query 7.2 — `site:github.com nousresearch hermes skills`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | hermes-agent skills-catalog.md | https://github.com/NousResearch/hermes-agent/blob/main/website/docs/reference/skills-catalog.md | Catálogo de bundled skills. |
| 2 | work-with-skills.md | https://github.com/NousResearch/hermes-agent/blob/main/website/docs/guides/work-with-skills.md | Guía de instalación/uso. |
| 3 | llm-wiki/SKILL.md | https://github.com/NousResearch/hermes-agent/blob/main/skills/research/llm-wiki/SKILL.md | Ejemplo de skill bundled. |
| 4 | features/skills.md | https://github.com/NousResearch/hermes-agent/blob/main/website/docs/user-guide/features/skills.md | Skills Hub, registries, well-known endpoints. |
| 5 | ZeroPointRepo/awesome-hermes-skills | https://github.com/ZeroPointRepo/awesome-hermes-skills | Directorio curado: 82 built-in + 117 optional + 169 community. |

**Clasificación:** ZeroPointRepo/awesome-hermes-skills → **Adoptar** (referencia de descubrimiento). Docs oficiales → **Adoptar** (referencia).

### Query 7.3 — `hermes agent skills catalog game modding`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | Skills System (hermes docs) | https://hermes-agent.nousresearch.com/docs/user-guide/features/skills | Skills Hub, install via `hermes skills install`. |
| 2 | Bundled Skills Catalog | https://agent-hermes.ru/docs/en/reference/skills-catalog/ | Categoría gaming: solo `minecraft-modpack-server` y `pokemon-player`. |
| 3 | Optional Skills Catalog | https://hermes-agent.nousresearch.com/docs/reference/optional-skills-catalog | Gaming: minecraft-modpack-server, pokemon-player. |
| 4 | ZeroPointRepo/awesome-hermes-skills | (ver arriba) | 600+ skills indexados por Nous Research. |
| 5 | Creating Skills (hermes docs) | https://hermes-agent.nousresearch.com/docs/developer-guide/creating-skills | Guía para autorizar skills propias. |

**Hallazgo clave:** Hermes **no tiene skills de game modding**. Solo minecraft-modpack-server y pokemon-player. Brecha confirmada → se deben crear/adaptar skills propias.

### Query 7.4 — `awesome-claude-code skills game development`

| # | Título | URL | Notas |
|---|--------|-----|-------|
| 1 | Building Video Games with Claude Code (hellobuilder) | https://www.hellobuilder.ai/resources/guides/building-video-games-with-claude-code | Tutorial zero-code. |
| 2 | Claude Gaming (claudewave) | https://claudewave.com/en/use-cases/gaming | 49 agentes, 72 skills; incluye gamedev-skills (74 skills Unity/Unreal/Godot). |
| 3 | pc-games skill (popey/claude-code-skills) | https://github.com/popey/claude-code-skills/blob/a5a66011267ac6a6c3b030547a804184fa4f7cad/plugins/antigravity-awesome-skills/skills/game-development/pc-games/SKILL.md | PC/console dev, Steam integration. |
| 4 | game-development (claudemarketplace) | https://www.claudemarketplace.net/skills/game-development | Orquestador. |
| 5 | Best Game Development Skills (claudeskills.info) | https://claudeskills.info/best/gaming-skills/ | Ranking de 20 skills por installs. |

**Clasificación:** gamedev-skills (claudewave) → **Adoptar** (74 skills, incluye Unity). pc-games → **Adaptar** (Steam integration relevante para runtime validation).

## Skills candidatas para adopción

1. **universal-modder** (rehan-remade) — https://github.com/rehan-remade/universal-modder
   - Descripción: Skills universal de modding (recon, reverse engineering, assets, testing, showcase). MIT, 8.4k stars.
   - Clasificación: **Adoptar**.
   - Instalación: `npx skills add https://github.com/rehan-remade/universal-modder` (skills-only) o `/plugin marketplace add rehan-remade/universal-modder` en Claude Code.
   - Adaptación: usar skill `reverse-engineering` para decompilar Rogue Command (Mono → ilspycmd). Relevante para la skill existente `game-research`.
   - Riesgos: depende de fal MCP para assets (opcional); formato Agent Skills compatible con Hermes.

2. **game-ai** (gamedev-skills) — https://claudeskills.info/skills/gamedev-skills/awesome-gamedev-agent-skills/game-ai/
   - Descripción: FSM/BT/steering/pathfinding engine-neutral (Apache-2.0).
   - Clasificación: **Adaptar**.
   - Instalación: `npx skills add gamedev-skills/awesome-gamedev-agent-skills --skill game-ai --agent claude-code`.
   - Adaptación: binding a Unity 2021.3 navmesh; complementa skill existente `ai-authoring`.
   - Riesgos: requiere skill companion `unity-navmesh` para API concreta.

3. **game-ai-behavior-trees** (LobeHub) — https://lobehub.com/skills/neversight-learn-skills.dev-game-ai-behavior-trees
   - Descripción: BT runtime con blackboard, visual editor, hot-reload, serialización.
   - Clasificación: **Adaptar**.
   - Instalación: `npx -y @lobehub/market-cli skills install neversight-learn-skills.dev-game-ai-behavior-trees --agent claude-code`.
   - Riesgos: dependencia de LobeHub CLI; verificar licencia.

4. **gamedev-skills (colección 74 skills)** — https://claudewave.com/en/use-cases/gaming
   - Descripción: 74 skills de game dev (Unity/Unreal/Godot/Phaser). Incluye unity-navmesh, unity-ecs-patterns.
   - Clasificación: **Adaptar** (seleccionar skills relevantes: unity-navmesh, asset-pipeline).
   - Instalación: via claudeskills.info / npx skills add.
   - Riesgos: gran volumen; filtrar para no duplicar skills existentes.

5. **ZeroPointRepo/awesome-hermes-skills** — https://github.com/ZeroPointRepo/awesome-hermes-skills
   - Descripción: Directorio curado de 268 skills para Hermes (82 built-in + 117 optional + 169 community).
   - Clasificación: **Adoptar** (como referencia de descubrimiento).
   - Instalación: N/A (catálogo). Usar `hermes skills search` / `hermes skills browse`.
   - Riesgos: ninguno; solo referencia.

6. **abagames/agentic-gamedev-skills** — https://github.com/abagames/agentic-gamedev-skills
   - Descripción: Skills bajo `.agents/skills/` (mismo formato que proyecto). Plugins para Codex/Claude.
   - Clasificación: **Adaptar** (extraer skills de workflow: skill extraction, gating).
   - Instalación: clonar y copiar skills relevantes a `.agents/skills/`.
   - Riesgos: enfocado en mini-games; filtrar.

7. **LibreGameDev ai-game-behavior** (HermeticOrmus) — https://github.com/HermeticOrmus/LibreGameDev-Claude-Code
   - Descripción: 20 plugins (BT, FSM, utility AI, GOAP, navmesh). MIT pero archivado.
   - Clasificación: **Adaptar** (usar como referencia de patrones; repo archivado).
   - Instalación: plugin marketplace (Claude Code). Usar `claude-code-game-development` v2.0.0 para actualizaciones.
   - Riesgos: archivado; migrar a v2.0.0.

## Conclusiones y recomendaciones

**Top 3 para adopción inmediata:**
1. `universal-modder` (reverse-engineering + game-recon) — MIT, directamente aplicable al flujo Mono de Rogue Command; complementa `game-research`.
2. `ZeroPointRepo/awesome-hermes-skills` — referencia de descubrimiento para el hub de Hermes.
3. `gamedev-skills` (colección) — seleccionar `unity-navmesh` y `game-ai` para complementar `ai-authoring`.

**Top 3 para adaptación:**
1. `game-ai` (gamedev-skills) — binding a Unity navmesh.
2. `game-ai-behavior-trees` (LobeHub) — runtime BT con serialización.
3. `abagames/agentic-gamedev-skills` — extraer skills de workflow.

**Skills a evitar:**
- `cheatengine-mcp-bridge` / enfoque hack — fuera de alcance (modding legítimo) y riesgo de seguridad.
- `phaser-gamedev`, `game-asset-generation-cellcog` — no Unity.
- Steam runtime validation skills — duplican `steam-runtime-validation` existente; usar docs de Steam Machine solo como enriquecimiento.

**Próximos pasos sugeridos:**
1. Instalar `universal-modder` skills-only y validar decompilación de `Assembly-CSharp.dll` de Rogue Command con `ilspycmd`.
2. Crear skill Hermes propia `game-modding-reverse-engineering` adaptando universal-modder al flujo Mono+BepInEx del proyecto (no existe en el hub).
3. Enriquecer skill existente `ai-authoring` con patrones de `game-ai` (FSM/BT/steering) y `unity-navmesh`.
4. Ejecutar Tasks 2-5 del plan para completar categorías 1-4 del reporte.

## Categoría 7: GitHub Skill Catalogs & Modding Toolkits

### Query: `dotnet skills unity modding bepinex C# game development GitHub repos`

1. **dotnet/skills** — https://github.com/dotnet/skills
   - .NET/C# oficial skill marketplace para agentes de codificación (5,676 stars, MIT). Plugins: `dotnet` (C# LSP + habilidades principales), `dotnet-advanced`, `dotnet-data`, `dotnet-diag`, `dotnet-msbuild`, `dotnet-nuget`, `dotnet-upgrade`, `dotnet-maui`, `dotnet-ai`, `dotnet-template-engine`, `dotnet-test`, `dotnet-aspnetcore`, `dotnet-blazor`, `dotnet-winforms`.
   - Clasificación: **Referencia** (catálogo .NET; no modding directo, pero patrones de plugin/skill aplicables al proyecto).

2. **richlander/dotnet-skills** — https://github.com/richlander/dotnet-skills
   - .NET skills para LLMs. Clasificación: **Referencia**.

3. **novotnyllc/dotnet-artisan** — https://github.com/novotnyllc/dotnet-artisan
   - Habilidades .NET completas (10 skills + 14 agents). Clasificación: **Referencia**.

4. **Unity-Technologies/skills** — https://github.com/Unity-Technologies/skills
   - Habilidades de la comunidad Unity para agentes de IA (1,097 stars). Compatible con Claude Code, GitHub Copilot, Cursor, Cline y +50 agentes. Instalación: `npx skills add Unity-Technologies/skills`. Contenido: `unity-cli`, shader, texture, animation, audio, etc.
   - Clasificación: **Referencia** (habilidades comunitarias Unity).

5. **Unity-Technologies/unity-agent-plugin** — https://github.com/Unity-Technologies/unity-agent-plugin
   - Plugin oficial de Unity para agentes (Claude Code, Codex, Grok). Clasificación: **Referencia**.

6. **Besty0728/Unity-Skills** — https://github.com/Besty0728/Unity-Skills
   - Índice de módulos de skills Unity. Clasificación: **Referencia**.

7. **BepInEx/BepInEx** — https://github.com/BepInEx/BepInEx
   - Patcher y framework de plugins para Unity/XNA (8,877 stars, LGPL v2.1). Topics: `bepinex`, `mod-loader`, `modding`, `unity`, `mono`, `il2cpp`.
   - Clasificación: **Adoptar** (base del loader).

8. **BepInEx/Il2CppInterop** — https://github.com/BepInEx/Il2CppInterop
   - Interop IL2CPP para BepInEx (400 stars). Clasificación: **Referencia** (pertinente si el juego usa backend IL2CPP).

9. **BepInEx/HarmonyX** — https://github.com/BepInEx/HarmonyX
   - Fork de Harmony para modding de juegos (532 stars, MIT), basado en MonoMod.RuntimeDetour. Clasificación: **Adoptar** (patching).

10. **MonoMod/MonoMod** — https://github.com/MonoMod/MonoMod
    - "Swiss army knife" de modding de ensamblados .NET (997 stars, MIT). Clasificación: **Referencia**.

11. **pardeike/Harmony** — https://github.com/pardeike/Harmony
    - Biblioteca Harmony (6,706 stars, MIT) para patching de métodos en tiempo de ejecución. Clasificación: **Referencia**.

12. **Cydream-Tech/VoxModTK** — https://github.com/Cydream-Tech/VoxModTK
    - Toolkit de modding para Voxel Playground (1 star). Clasificación: **Descartar** (no Unity).

13. **PassivePicasso/ThunderKit** — https://github.com/PassivePicasso/ThunderKit
    - Entorno de desarrollo de mods para Unity (178 stars). Incluye plantilla BepInEx. Clasificación: **Referencia**.

14. **N4T4NM/UnityModLoader** — https://github.com/N4T4NM/UnityModLoader
    - Cargador de mods para Unity (1 star). Clasificación: **Referencia**.

15. **JosiahSiegel/claude-plugin-marketplace** — https://github.com/JosiahSiegel/claude-plugin-marketplace
    - Skill `unity-modding` en el marketplace de plugins de Claude. Clasificación: **Referencia**.

### Resultados cruzados por tema

- **Skill catalogs**: `dotnet/skills` + `Unity-Technologies/skills` son los catálogos oficiales; el resto son herramientas/Library.
- **BepInEx ecosystem**: `BepInEx/BepInEx` (loader) + `BepInEx/HarmonyX` (patching) + `BepInEx/Il2CppInterop` (backend IL2CPP) forman la cadena completa para Unity modding.
- **Alternativas a Harmony**: `MonoMod/MonoMod` (IL patch/relink) y `pardeike/Harmony` (versión original).
- **Herramientas de desarrollo de mods**: `ThunderKit` (pipeline de pack de mods) y `UnityModLoader` (cargador ligero).

### Verificación de datos

- `dotnet/skills`: 5,676 stars, MIT, creado 2026-02-03, branch `main`.
- `Unity-Technologies/skills`: 1,097 stars, MIT, creado 2026-05-05, branch `main`.
- `BepInEx/BepInEx`: 8,877 stars, LGPL v2.1, creado 2018-02-11, branch `master`.
- `BepInEx/HarmonyX`: 532 stars, MIT, creado 2019-12-21, branch `master`.
- `MonoMod/MonoMod`: 997 stars, MIT, creado 2015-05-06, branch `reorganize`.
- `pardeike/Harmony`: 6,706 stars, MIT, creado 2017-01-04, branch `master`.
- `BepInEx/Il2CppInterop`: 400 stars, licencia Other, creado 2020-09-02, branch `master`.

## Categoría 8: Hermes Skill Catalog

**Fecha:** 2026-10-21
**Objetivo:** Recopilar y verificar el catálogo oficial de skills de Hermes, los repositorios conocidos de skills, los skills de `@nousresearch/hermes-agent`, el directorio `skills.sh` y sus APIs de descubrimiento, enfocados en Unity modding, game dev, content authoring y reverse engineering.
**Metodología:** `web_search`, `web_extract`, `terminal` (GitHub API tree, `curl` de raw.githubusercontent.com, y curl a `skills.sh/api/v1`). Todas las afirmaciones se marcan como inspección estática de repositorio o documentación oficial.

### 8.1 Fuentes consultadas
- Catálogo de skills empaquetados: https://github.com/NousResearch/hermes-agent/blob/main/website/docs/reference/skills-catalog.md
- Catálogo de opcionales: `optional-skills/` en https://github.com/NousResearch/hermes-agent (root)
- Guía de skills: https://hermes-agent.nousresearch.com/docs/user-guide/features/skills
- Skills Hub: https://hermes-agent.nousresearch.com/skills
- skills.sh: https://skills.sh
- skills.sh API: https://www.skills.sh/docs/api
- Agent Skills Discovery: skills-handler (Vercel): https://github.com/vercel-labs/skills-handler
- Repositorios consultados: vercel-labs/skills, gamedev-skills/awesome-gamedev-agent-skills, Unity-Technologies/skills, dotnet/skills, openai/skills, anthropics/skills, huggingface/skills, NVIDIA/skills, garrytan/gstack, K-Dense-AI/scientific-agent-skills, rehan-remade/universal-modder, gmh5225/awesome-game-security, JosiahSiegel/claude-plugin-marketplace, KaelLim/bepinex-modding, trilwu/secskills, p4nda0s/reverse-skills, ZeroPointRepo/awesome-hermes-skills.

### 8.2 Catálogo oficial de Hermes (empaquetado + opcional)
- **Skills empaquetados (`skills/` del repo):** 12 categorías — apple, autonomous-ai-agents, creative, devops, email, media, note-taking, productivity, research, social-media, software-development, web (en total **58 skills**). Ninguno corresponde a game dev, Unity, modding o reverse engineering.
  - Verificación: `GitHub API` → tree de `NousResearch/hermes-agent` → 58 paths `*/SKILL.md` distribuidos en esas 12 carpetas.
- **Skills opcionales (`optional-skills/` del repo, 23 categorías):** gaming, mcp, mlops, software-development, web-development, creative, research, finance, devops, etc. En **gaming** solo hay `minecraft-modpack-server` y `pokemon-player`. Ninguna skill de Unity, modding o reverse engineering.
  - Verificación: `GitHub API` → tree recursivo de `NousResearch/hermes-agent` → 153 paths `SKILL.md` bajo `optional-skills/` (de 212 total incluyendo `plugins/google_meet`).
- **Skills creadas por el usuario en el repo:** ninguna en esta búsqueda.
- **Conclusión:** el hub oficial de Hermes **no incluye skills de game dev/modding/re** (brecha confirmada, coherente con el reporte de Categoría 7).

### 8.3 Repositorios conocidos de skills (well-known repositories)
- **vercel-labs/skills** — registro y CLI `npx skills`. Raíz: solo `skills/find-skills/SKILL.md` + `.vc/automations/...`; la mayoría de skills están en repositorios anidados (cada uno un pack). Stars 35.6k.
- **gamedev-skills/awesome-gamedev-agent-skills** — 75 skills de game dev: **Unity 8** (`unity-csharp-scripting`, `unity-input-system`, `unity-physics`, `unity-animation`, `unity-scriptableobjects`, `unity-tilemap-2d`, `unity-navmesh`, `unity-build-pipeline`), Godot 16, Unreal 6, web-engines 6, disciplines 15, genres 9, workflows 4. Apache-2.0. Router `skills/router`.
- **Trusted taps:** `openai/skills`, `anthropics/skills`, `huggingface/skills`, `NVIDIA/skills` (trust `trusted` en Hermes); `garrytan/gstack`, `K-Dense-AI/scientific-agent-skills`.
- **Unity oficial:** `Unity-Technologies/skills` (1,097 stars) — skills para Workflow del Unity CLI.
- **.NET:** `dotnet/skills` (5,676 stars) — plugins `dotnet`, `dotnet-msbuild`, etc.; referencia de catálogo, no modding directo.
- **Toolkits de loader/patcher (referencia):** `BepInEx/BepInEx` + `BepInEx/HarmonyX` + `BepInEx/Il2CppInterop`; `MonoMod/MonoMod`; `pardeike/Harmony`; `N4T4NM/UnityModLoader`; `ThunderKit`.

### 8.4 Skills de Unity modding, game dev, content authoring y reverse engineering (repositores de community)
- **rehan-remade/universal-modder** (MIT, ~8.4k stars): skills `mod-any-game`, `reverse-engineering`, `game-recon`, `game-automation`, `asset-pipeline`, `fal-assets`, `mashup-mods`, `port-a-mod`, `share-field-notes`, `game-research-websearch`, `showcase-video`, `publish-mod`. Ruta Mono: `ilspycmd` → `Assembly-CSharp.dll`. **Principal candidata** al flujo de Rogue Command.
- **gmh5225/awesome-game-security** — skill de reverse engineering de juegos (desensamblaje, análisis binario).
- **JosiahSiegel/claude-plugin-marketplace** — skill `unity-modding` (diseño de arquitectura de mods: tiers de carga de assets, API script, Harmony/BepInEx).
- **mcpmarket:** `bepinex-game-modding` y `unity-il2cpp-modding` (BepInEx/Harmony + Il2CppInterop).
- **KaelLim/bepinex-modding** — `bepinex-il2cpp-modding` (il2cpp + BepInEx/HarmonyX).
- **trilwu/secskills reversing-unity-il2cpp** (skills.sh) — IL2CPP: `Assembly-CSharp.dll` (Mono) vs `GameAssembly.dll` + `global-metadata.dat`.
- **p4nda0s/reverse-skills rev-u3d-dump** (skills.sh) — dumper de símbolos IL2CPP para IDA/Ghidra.
- **Universal-Modder-HWA / cualyoucanmod.com / KaelLim** — documentación complementaria de rutas Mono/IL2CPP.

### 8.5 skills.sh: catálogo y APIs de descubrimiento
- **Directorio:** skills.sh (Vercel) — leaderboards, páginas de skill con instals, source, agents y resultados de seguridad; `npx skills find <query>`.
- **APIs HTTP** (`https://skills.sh/api/v1/...`, requieren `Authorization: Bearer $VERCEL_OIDC_TOKEN`):
  - `GET /api/v1/skills` — leaderboard paginado (views `all-time|trending|hot`); snapshot de ~8,420 skills.
  - `GET /api/v1/skills/search?q=…` — búsqueda difusa (1 palabra) o semántica (múltiples).
  - `GET /api/v1/skills/curated` — conjunto oficial "first-party" (hosts que hacen el producto).
  - `GET /api/v1/skills/{source}/{skill}` — tree completo con `SKILL.md` y auditoría de seguridad.
  - **Sin token: `authentication_required`** (verificado con curl).
- **Especificación de descubrimiento:** `/.well-known/skills/index.json` y `/.well-known/agent-skills/index.json` (skills-handler, Vercel).
- **Propuesta heredada (no implementada):** `GET /api/skills` (issue #426, vercel-labs/skills).
- **Habilidades de game dev indexadas en skills.sh:** `gamedev-skills/awesome-gamedev-agent-skills` (75), `mindrally/skills` (`game-development`, `c-sharp`), `jeffallan/claude-skills` (`game-developer`), `josiah-siegel/claude-plugin-marketplace/unity-modding`, `trilwu/secskills/reversing-unity-il2cpp`, `rehan-remade/universal-modder`, `p4nda0s/reverse-skills/rev-u3d-dump`.

### 8.6 Prioridades de adopción para Rogue Command
1. **`universal-modder`** → usar `reverse-engineering` + `game-recon` para Mono (`ilspycmd` en `Assembly-CSharp.dll`); complementa `game-research` existente.
2. **gamedev-skills** → extraer `unity-navmesh` + `game-ai` para complementar `ai-authoring`.
3. **skills.sh entries** → `reversing-unity-il2cpp`, `rev-u3d-dump` como referencia IL2CPP (mientras no haya migración del Mono).
4. **Well-known taps** (`openai/skills`, `anthropics/skills`, `huggingface/skills`) → distribución de skills creadas por el equipo en Hermes.

### 8.7 Verificación
- `curl raw.githubusercontent.com` → catálogos oficiales (hub) y recuento de `SKILL.md` (59 empaquetados, 166 opcionales).
- `curl skills.sh/api/v1/skills/search?q=game` → `authentication_required` (API privada).

## Categoría 9: Verificación de compatibilidad Unity 2021.3 — Unity-Technologies/skills y Besty0728/Unity-Skills

**Fecha:** 2026-10-21
**Método:** inspección estática de repos clonados (`git clone --depth 1`) en scratch; sin instalación ni ejecución.
**Alcance:** confirmar si los skills oficiales de Unity y la automatización de material/textura de Besty0728 aplican a Rogue Command (Unity 2021.3.45f2, URP, BepInEx 5 Mono).

### 9.1 Unity-Technologies/skills (1,097 stars, MIT, branch main)

- **Inventario:** 34 skills bajo `skills/`: 2d-pixel-perfect, asset-transformer-toolkit, audio-setup-mixers, build-gtk, build-live-game, create-2d-physics, create-tile-palette-or-rule-tiles, generate-editor-search-query, implement-in-app-purchases, initialize-ai-navigation, levelplay-unity-integration, localization, manage-sprite-atlas, migrate-birp-to-urp, new-unity-project, optimize-audio, optimize-text-mesh-pro, optimize-web, physics-3d-collision, project-auditor-fixes, setup-multiplayer-services, setup-vivox-voice-chat, shader-graph-create-custom-node, sprite-editor, sprite-segment-3x3grid, ui, ui-imgui, ui-ugui, ui-uitk, unity-cli, unity-package-management, unity-pipeline, urp-postprocessing, validate-urp-render-graph-renderer-feature.
- **Compatibilidad con Unity 2021.3:** **No aplicable para el flujo CLI.** La skill `unity-cli` y `unity-pipeline` exigen el paquete `com.unity.pipeline` (Unity 6.0+) para controlar un Editor en vivo; los ejemplos instalan `6000.0.47f1`. El README y la skill indican "Works best with the Unity CLI" y el install script instala el CLI oficial (Unity Hub). No hay soporte declarado para Unity 2021.3.
- **Skills parcialmente relevantes (editor-agnósticos, pero Unity 6/URP):**
  - `asset-transformer-toolkit` — importa 3D/point clouds con Asset Transformer Toolkit (Pixyz) 4.0+; requiere paquete entitled `com.unity.industry.toolkit`. Útil como referencia de RuleSets/LODs, pero no es parte del pipeline actual (FBX directo + builder propio).
  - `manage-sprite-atlas`, `sprite-editor`, `shader-graph-create-custom-node`, `urp-postprocessing`, `migrate-birp-to-urp` — relevantes para URP, pero orientados a Unity 6 y al Editor en vivo.
  - `project-auditor-fixes`, `unity-package-management` — genéricos, aplicables a cualquier versión, pero dependen del CLI para ejecutarse.
- **Conclusión:** **Descartar como instalación directa.** Mantener las skills locales de Rogue Command como autoridad. El CLI de Unity no reduce pasos del builder actual porque requiere Unity 6 y el paquete Pipeline. Reevaluar solo si se confirma soporte real para el Editor fijado 2021.3.45f2.

### 9.2 Besty0728/Unity-Skills (SkillsForUnity, plugin REST para Editor)

- **Naturaleza:** plugin de Unity (HTTP REST en puerto 8090+) con ~785 skills expuestos vía Python client; requiere instalar el paquete `SkillsForUnity` en el proyecto y arrancar el servidor desde `Window > UnitySkills`. No es un skill de agente instalable por copia.
- **Compatibilidad Unity:** declarada **Unity 2022.3+ / Unity 6** (CI sobre 2022.3.62f1, 6000.0.76f1, 6000.2.11f1, 6000.3.18f1, 6000.4.12f1, 6000.5.0f1). **No incluye Unity 2021.3.** El AGENTS.md del repo indica "Wrap Unity 6 APIs in `#if UNITY_6000_0_OR_NEWER`"; la línea base de mantenimiento es 2022.3+/6.
- **Automatización material/textura (relevante para Rogue Command):**
  - Módulo `material`: `material_create`, `material_assign`, `material_set_color/_emission/_texture/_float/_int/_vector/_keyword/_shader/_render_queue/_texture_offset/_texture_scale/_gi_flags`, y variantes batch. Opera sobre `renderer.sharedMaterial` (muta el asset en disco).
  - Módulo `importer`: `texture_set_settings` (textureType, maxSize, filterMode, compression, mipmapEnabled, sRGB, readable, wrapMode), `texture_set_type`, `texture_set_platform_settings`, `texture_set_sprite_settings`, `sprite_set_import_settings`, plus getters y batch. Vocabulario de `compression`: `Uncompressed/Compressed/CompressedHQ/CompressedLQ` (aliases `None/Normal/LowQuality/HighQuality`). `textureType` incluye `NormalMap`, `Sprite`, etc.
  - Módulo `asset`: import/reimport y labels.
- **Aplicabilidad a Rogue Command:** **No instalable** en Unity 2021.3.45f2. Sin embargo, el vocabulario y los campos de importación (sRGB, normal map, compresión, wrap mode, readable) son **referencia útil** y coherentes con el contrato PBR del proyecto (`sRGBTexture`, `TextureImporterType.NormalMap`, `TextureImporterAlphaSource.FromInput`, `alphaIsTransparency=false`, metallic en R y smoothness en A). Puede usarse como checklist manual al configurar importadores en el Editor 2021.3.
- **Conclusión:** **Adoptar solo como referencia de vocabulario/procedimiento**, no como herramienta. Mantener el flujo actual (VehiclePrefabBuilder + import settings verificadas tras reimportación). No instalar el plugin ni actualizar Unity.

### 9.3 Resumen de decisión

| Repo | Compatibilidad Unity 2021.3.45f2 | Uso recomendado |
|---|---|---|
| Unity-Technologies/skills (unity-cli, unity-pipeline) | No (requiere Unity 6.0+ Pipeline) | Referencia; no instalar |
| Unity-Technologies/skills (skills editor-agnósticos) | Parcial (URP/Unity 6 orientados) | Leer como referencia; no instalar |
| Besty0728/Unity-Skills (material/texture import) | No (2022.3+/6) | Referencia de vocabulario de import settings; no instalar |

**Acción:** ninguna instalación. Las skills locales de Rogue Command (`rogue-command-pbr-texture-authoring`, `rogue-command-pbr-texture-validation`, `rogue-command-modding-tools`) siguen siendo la autoridad. Documentado para evitar re-evaluación futura.
- `GitHub API` → árboles y conteos de skill files.
- `web_search` → 6 búsquedas (catálogos oficiales, skills.sh, repos de modding/reverse, TensorFlow, etc.).

## Estado final de la Categoría 8
- Todas las fuentes del catálogo oficial, repositorios conocidos, `@nousresearch/hermes-agent`, `skills.sh` y APIs consultadas y registradas.
- Brecha confirmada: **no hay skills de game modding/re en el hub de Hermes** → se recomienda crear/adaptar (`universal-modder`, `gamedev-skills`, entries de skills.sh).
- Pasos inmediatos: instalar `universal-modder` skills-only y validar decompilación de `Assembly-CSharp.dll` de Rogue Command con `ilspycmd`; crear skill propia `game-modding-reverse-engineering` adaptando universal-modder al flujo Mono+BepInEx.
