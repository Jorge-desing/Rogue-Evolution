# Content Loader Tools — guía de agente

**Updated:** 2026-11-10
**Scope:** herramientas offline que mantienen `content.json` (content loader). No es diagnóstico clean-room ni BepInEx runtime: es validación local y edición de manifiestos.

> **Regla de rutas:** la CLI se ejecuta como módulo desde la raíz del repo con el venv del proyecto:
> ```text
> PYTHONPATH="$PWD/.venv/Lib/site-packages:." .venv/Scripts/python.exe -B -m tools.<nombre> ...
> ```
> Los tests Python cargan los módulos con `load_tool_module()` (`tests/python/helpers.py`). `tools/` es un namespace package; su CLI debe invocarse con `python -m tools.<nombre>` desde la raíz. Ejecutar directamente `python tools/<nombre>.py` puede ocultar el namespace y romper imports `tools.*`.

## Catálogo de herramientas

| Herramienta | Función pura | Uso |
|---|---|---|
| `tools/merge_content_packs.py` | `merge_packs` | Fusión aditiva por ID estable; conflictos reportados, gana la izquierda. |
| `tools/diff_content_packs.py` | `diff_packs` | added/removed/changed por sección. |
| `tools/audit_content_references.py` | `audit_pack` | Refs colgantes, mapeo 1:1, localización en-US, `nativeTemplateId`+`assetBundleId`. |
| `tools/clone_content_pack.py` | `clone_pack` | Clona remapeando IDs `ns:*` (plantillas nativas intactas). |
| `tools/migrate_content_pack.py` | `migrate_pack` | Normaliza legacy (schemaVersion, alias id, localizaciones objeto). |
| `tools/scaffold_pack.py` | `generate_scaffold` | Pack esqueleto mínimo válido. |
| `tools/inspect_content_pack.py` | `inspect_pack` | Resumen: conteos, fábrica→unidad, pools, módulos. |
| `tools/validate_ai_modules.py` | `validate_ai_profiles` | IDs leídos desde `AiModuleRegistry.CreateDefault()` (23 módulos), parámetros y bounds sincronizados con sus parsers. |
| `tools/analyze_blueprint_pools.py` | `analyze_stage_pools` | Scoping de pools por etapa, unidades huérfanas de fallback. |
| `tools/analyze_relic_synergy.py` | `analyze_relic_synergy` | `neededSystemTags` vs `offeredSystemTags`, 26 `ChangeableValue`. |
| `tools/validate_card_changes.py` | `validate_card_changes` | 26 `ChangeableValue` + operación/side finitos en unidades, reliquias y mejoras. |
| `tools/analyze_factory_product_parity.py` | `analyze_factory_product_parity` | Mapeo 1:1 fábrica↔producto, rareza/nivel, huérfanas. |
| `tools/analyze_upgrade_chains.py` | `analyze_upgrade_chains` | targetUnitId, nivel 0–100, costo ≥0, cardChanges válidos. |
| `tools/analyze_ai_profile_coverage.py` | `analyze_ai_profile_coverage` | Producto por `unitId` en `unitEntries`/`productionBuildingEntries`, 23 módulos y refs cross-pack. |
| `tools/export_unit_catalog.py` | `export_unit_catalog` | Catálogo compacto de unidades (rol, rareza, tech, costo, HP, velocidad, fábrica, etapas). |
| `tools/check_stage_blueprint_filters.py` | `check_stage_blueprint_filters` | poolId, unitIds no vacíos/sin duplicados, resolución. |
| `tools/validate_native_templates.py` | `validate_native_templates` | Allowlists de plantillas nativas (unidades/fábricas/etapas/paisajes/AI). |
| `tools/export_balance_table.py` | `export_balance_table` | Balance efectivo con precedencia `customBalance` > `balanceOverrides` > estructurados. |
| `tools/lint_content_pack.py` | `lint_pack` | Avisos: sin es-XL, fábrica en `unitEntries`, `mapSizeMin>Max`. |
| `tools/validate_balance_overrides.py` | `validate_balance` | 26 `ChangeableValue` REA + cap Single + rarity enums; `ManaRechargePerSecond` es global. |
| `tools/export_registry.py` | `export_registry` | Inventario global de IDs de todos los packs. |
| `tools/check_stage_reachability.py` | `analyze_reachability` | Unidades ofrecidas por etapa vs definidas (contenido muerto). |
| `tools/content_pack_stats.py` | `pack_stats` | Conteos, histogramas de plantillas, ops de cardChanges, cobertura bilingüe. |
| `tools/validate_pack_contract.py` | `validate_contract` | Contrato cruzado: `packId`, paridad unidad↔fábrica, guards de stage/bundle. |
| `tools/normalize_content_pack.py` | `normalize_pack` | Copia canónica (sección vacías, sort, dedupe); `--check`/`--write`. |
| `tools/query_content_packs.py` | `query_units`, `unit_stages`, `unit_ai_profiles`, `stage_profiles`, `find_units_without_stage`, `query_stages`, `query_ai_profiles` | Consultas por rol/tag/tech/rarity/costo/texto. |
| `tools/economy_report.py` | `economy_report`, `stage_economy` | Costo/EHP/DPS y economía de etapa (estimación local). |
| `tools/check_localization.py` | `validate_localization` | Idiomas soportados (10), duplicados, `en-US` no vacío. |
| `tools/analyze_unit_mechanics.py` | `analyze_mechanics` | Mapa de 30 subdefiniciones de unidad + oportunidad de uso. |
| `tools/check_pack_dependencies.py` | `defined_ids`/`external_references`/`analyze_dependencies` | Grafo de dependencias cross-pack (refs no resueltas). |
| `tools/fingerprint_content_pack.py` | `fingerprint_pack`, `verify_fingerprint` | SHA-256 del JSON canónico; verificación de identidad. |
| `tools/edit_content_pack.py` | ver editor abajo | Editor único de `content.json` (42 ops + batch). |

AI `unitEntries` y `productionBuildingEntries` referencian ambos el ID de la unidad producida con `unitId`. Solo `sample:scheduled-base-build.parameters[].entityId` nombra una fábrica. `tools/ai_module_contracts.py` obtiene los IDs de la tabla C#; al añadir un módulo también hay que actualizar los nombres de parámetros y límites espejados en `validate_ai_modules.py`.

## Editor: `tools/edit_content_pack.py`

`edit_content_pack.py` es **el único punto de edición** de `content.json`. Toda operación es una función pura `(pack, ...) -> (pack, diagnostics)`; el CLI solo parsea flags y escribe.

### Invocación

```text
PYTHONPATH=.venv/Lib/site-packages:. .venv/Scripts/python.exe -B -m tools.edit_content_pack --pack <pack|content.json> --operation <op> ... --out-dir <dir>
```

Descubrimiento: `--operations-json` imprime el catálogo completo (JSON) y sale — úsalo antes de adivinar flags.

### Operaciones

**Unidades** · `add` (unit, stageIds) · `remove` (unitId) · `duplicate-unit` (unitId, newId) · `rename-unit` (unitId, newId) · `set-flag` (unitId, flag, value) · `set-balance`/`remove-balance` (unitId, name, value, factory) · `set-template` (unitId, templateId, factory — validado contra allowlist) · `set-unit-assets` (unitId, assetBundleId, cardImage, factory)

**Etapas** · `add-stage` (stage) · `remove-stage` (stageId) · `rename-stage` · `clone-stage` · `set-map-size` (stageId, min, max — 1..128) · `add-pool`/`remove-pool` (stageId, poolId, unitIds) · `set-stage-param` (stageId, name, value) · `set-landscape` (stageId, seed, mapSize impar, nativeTemplateId)

**Perfiles AI** · `add-ai-profile`/`remove-ai-profile`/`rename-ai-profile`/`clone-ai-profile` · `set-ai-field` (profileId, field, value) · `set-ai-param` (profileId, moduleId, name, value) · `add-ai-module`/`remove-ai-module`

**Relics** · `add-relic`/`remove-relic` · `set-relic-card-change` (relicId, valueToChange, operation Add/Multiply/Set, value)

**Upgrades** · `add-upgrade`/`remove-upgrade` · `set-upgrade-field` · `set-upgrade-card-change`

**Bundles** · `add-bundle`/`remove-bundle` (bundleId)

**Composición** · `extract-units` (unitIds) · `import-units` (source, unitIds) · `purge-dangling` (elimina refs colgantes)

**Identity** · `set-pack-id` (newId) · `set-schema-version` (version — solo 1)

**Batch** · `--operation batch --script ops.json`: lista de `{"op": <nombre>, ...args}`. Las ops con error no detienen el batch; el CLI reporta los diagnósticos con índice.

### Vocabulario y trampas (ver skill de content loader tooling)

- **IDs namespaced `ns:slug`**; remapear solo strings que empiecen con el prefijo (las plantillas nativas `PCXBaseTank` nunca se tocan).
- **`apply_edits` usa claves declarativas** (`unitId`, `stageId`, `profileId`, `name`, `value`, `min`/`max`, `unitIds`), no los flags del CLI — misma lista en ambas capas.
- **`remove_unit` purga también** `unitEntries`/`productionBuildingEntries` escalares y parámetros de módulos (`entityId` del scheduled-base-build) que referencian la unidad/fábrica eliminada.
- **`add_pool_entry` valida contra el pack local** — en tests usa unidades definidas localmente.
- **`extract_units_to_pack`/`import_units` transportan solo** las etapas/perfiles que referencian las unidades movidas.
- **mapSize (etapa) 1..128**, no el bound de `startCountRange` (1..64): los packs reales usan hasta 80.
- **`--dry-run`** imprime el pack resultante sin escribir; **`--validate-after`** corre `validate_balance` + `audit_pack` tras editar y falla si hay issues.
- Las ops que leen JSON (`--unit-json`, `--stage-json`, `--profile-json`, `--module-json`, `--upgrade-json`, `--relic-json`, `--bundle-json`, `--script`) comparten `_read_json_file`.

### Ejemplos

```text
# Catálogo de operaciones (sin --pack):
-m tools.edit_content_pack --operations-json

# Batch declarativo:
-m tools.edit_content_pack --pack content/packs/sample-native-expansion \
  --operation batch --script ops.json --out-dir /tmp/out --validate-after

# Un upsert de balance en seco:
-m tools.edit_content_pack --pack content/packs/sample-native-expansion \
  --operation set-balance --unit-id sample-native:scout-tank \
  --balance-name rarity --balance-value Rare --out-dir /tmp/out --dry-run
```

## Intermission y selección del nodo jefe

El grafo de `LevelMap` incluye un nodo jefe especial en `(LevelMap.LevelCount, 0)`, fuera de `Fields(level)`. `GetIntermissionNodeJson(level, field)` debe devolver `found=true`, `isBoss=true`, sin recompensas y con una sola casilla para esa coordenada; `isChoosable` refleja `IsLastLevelBeforeBoss` y las conexiones entrantes cuentan los campos del nivel anterior. La corrección del nodo no implica seleccionar ni cargarlo.

`GetAiModulesDiagnosticsJson` informa módulos registrados, no el perfil asociado a la etapa actual ni su ejecución. Confirma el perfil de la etapa, el censo del director/roster y la telemetría propia del módulo antes de afirmar comportamiento AI en vivo.

## Reglas de evidencia

Tests, validaciones de schema y registry: evidencia **local** únicamente. No prueban registro en runtime, ofertas, producción, render ni aceptación Steam.
