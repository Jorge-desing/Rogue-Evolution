# Plan histórico y seguimiento: overrides de balance por filas nativas

> **Estado actual:** la solución intermedia que sembraba las cachés quedó supersedida por la escritura de overrides en filas base antes del registro. La implementación actual aplica 25 valores per-unit a filas antes del registro. Un smoke Steam/BepInEx MCP en `Game` leyó los valores custom desde la fila y los getters nativos tras invalidar cachés. En la misma partida se produjo una factoría StunWalker nativa y se observó movimiento/ataque de un StunWalker; esto no prueba que `sample-native:scout-tank` se haya ofrecido, producido ni consumido sus valores custom en combate. Un intento posterior en stage 0 no dio evidencia interpretable de apertura del factory menu ni de producción del objetivo. La instalación y el selector se restauraron/verificaron y los logs se preservaron. Profile2 conserva los datos QA creados (un `savegame.dat` nuevo y `metaSavegame.dat` cambiado); su copia original coincide con el baseline, los demás perfiles no cambiaron y no se editó `tutorialState.txt`. Falta la aceptación end-to-end.

## Goal

Mejorar el cargador de unidades aplicando overrides de balance por unidad a filas nativas antes del registro, con mapping explícito, validación numérica segura y pruebas locales; verificar el loader real con BepInEx MCP sin confundir MainMenu/fila/getter con gameplay.

## Current context / assumptions

- Contexto de Git: rama de trabajo `codex/rogue-unit-balance-20261018`; cualquier HEAD/upstream citado en el historial es un snapshot y debe revalidarse antes de operar. Git reporta `?? .worktrees/`; preservarlo sin listar, abrir, modificar ni stagear y no inspeccionar `.worktrees/cargador/`.
- La auditoría anterior concluyó No-Go para **añadir mappings nuevos a subdefiniciones**; esa decisión sigue vigente. El lookup de enum descrito en el plan original quedó corregido. El estado actual aplica overrides per-unit a filas base antes del registro.
- La identidad completa se revalidó en esta sesión: Rogue Command `1.0.9`; Unity `2021.3.45f2`; SHA-256 de `Assembly-CSharp.dll` `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. `version.txt` y `Player.log` pertenecen a la misma instalación Steam. REA Evidence de identidad: `ev_b923e23cfec30be565c58056206d348d2714b1d81204fa26364cbbcce0bd6463`.
- REA estático seleccionó `EntityBalancingStore`, `GameBalancingScriptableObject` y tipos anidados `ChangeableValue`, con metadata completa dentro de esa selección: `ev_84b3d3c96544a9abf4da45c12a4809714f93ac1bc09737a07fdd2a9b0757bf06`. La página de tipos `ev_52c24c4076eaeaf60eb68915ace74021416811bef8b69c5da85e1e841c71ffaf` identifica `EntityBalancingStore` como TypeDef `0x02000073`, `GameBalancingScriptableObject` como `0x02000074`, y el `ChangeableValue` del cache como TypeDef `0x02000475`, anidado bajo `0x02000073`. La página de campos `ev_e6d7439461311f158a6ad6dc1fdb0704dbc142d58d83341e93590a15fc5cadce` confirma `ChangeableIntValueCache` y `ChangeableFloatValueCache` en `EntityBalancingStore`. La selección completa no muestra un `ChangeableValue` anidado en `GameBalancingScriptableObject`.
- **Implementación actual:** `ContentPackRuntimeLoader.GetBalanceOverrides()` (`:1714-1750`) filtra las definiciones candidatas con los IDs de unidades cargadas y llama a `CreateBalanceOverridesForLoadedUnits`. `RuntimeContentMutator.RegisterEntityRows()` aplica los valores a las filas boxed antes de registrarlas; las cachés nativas quedan bajo control del juego.
- **Cobertura actual:** el harness prueba mapping/precedencia, 25 campos, filtrado por ID registrado, filas `MoveSpeed`/`MaxHealth` y validación numérica. En el harness, `ContentPackRuntimeLoader.cs` no está compilado y los fixtures limpian los caches con `Clear()` sin recalcular un getter nativo; la integración provider se inspeccionó estáticamente y los getters se comprobaron aparte mediante MCP.
- Baseline histórico del harness Debug y Release (antes de las ampliaciones de esta tarea): `189/190 validation checks passed; 1 skipped`; el único skip fue `reparse-point bundle paths are rejected` por privilegios de symlink de Windows. La validación actual se registra abajo.
- Alcance del plan original: corregir el lookup del enum y cubrirlo con un test, sin añadir valores a las unidades de muestra, perfiles AI, manifiestos ni assets. La auditoría posterior amplió el cambio del cargador para aplicar únicamente los 25 miembros per-unit respaldados por filas nativas, cerrar su validación/schema y mantener fuera de alcance los mappings semánticos no demostrados. No se crearon recursos visuales ni se llamaron servicios de generación.

## Architecture / proposed approach

Preservar la identidad CLR del `EntityBalancingStore.ChangeableValue` validado y mapear explícitamente solo sus 25 miembros per-unit a campos/tipos verificados de `EntityBalancingParameters`. Aplicar los overrides a la fila entrante antes del registro; dejar que el cache nativo se regenere desde esa fila tras invalidación y componga sus modificadores. Las pruebas locales cubren `MoveSpeed` y `MaxHealth` en la fila del provider/registro, además de claves inválidas y precedencia; no presentar fixtures como evidencia runtime.

## Diseño intermedio archivado (no ejecutable)

> **ARCHIVO HISTÓRICO — NO EJECUTAR:** Todas las instrucciones, comandos, expectativas de test, límites y pasos de entrega del bloque siguiente describen el diseño intermedio que sembraba valores finales en cachés. Están supersedidos por la implementación por filas y el contrato `customBalance` actual. No copiar ni seguir este bloque; el estado vigente y sus validaciones están en `## Seguimiento: corrección a fila base`.

### 1. Reconfirmar estado y preservar el worktree ajeno (2–3 min)

Desde la raíz, ejecutar:

```bash
git status --short --branch --untracked-files=all
git rev-parse HEAD
git rev-parse --abbrev-ref --symbolic-full-name '@{u}'
```

Esperado: HEAD `2e35449e208776788c65c327a0f1ba60d462b22c`, rama/upstream `codex/rogue-unit-balance-20261018` / `origin/codex/rogue-unit-balance-20261018`, y `?? .worktrees/cargador/`. Si hay cambios task-external, preservarlos; detenerse si hay modificaciones en los archivos que se necesitan tocar. No usar `git add .`, `git add -A`, `git clean`, reset ni cambiar de rama.

### 2. Revalidar el target antes de usar metadata (2–4 min)

Aunque el harness usa stubs locales, la hipótesis se apoya en un tipo del juego: vuelve a comprobar el tuple completo en la instalación antes de reutilizar el hallazgo REA. Comando local de solo lectura:

```bash
python -B -c 'from pathlib import Path; import hashlib; root=Path("C:/Program Files (x86)/Steam/steamapps/common/Rogue Command"); log=Path("C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Player.log"); lines=log.read_text(encoding="utf-8",errors="replace").splitlines(); print("version.txt="+(root/"version.txt").read_text(encoding="utf-8").strip()); print(next((x.strip() for x in lines if x.startswith("Initialize engine version:")), "Unity marker missing")); print(next((x.strip() for x in lines if "Mono path[0]" in x), "Mono path[0] missing")); print("assembly_sha256="+hashlib.sha256((root/"Rogue Command_Data/Managed/Assembly-CSharp.dll").read_bytes()).hexdigest())'
```

Esperado: `1.0.9`, Unity `2021.3.45f2`, `Mono path[0]` bajo la misma raíz y SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; la llamada REA `inspect_managed_artifact(detail="summary")` debe mantener MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Si difiere cualquier valor, detener el uso de firmas/evidence IDs y repetir la inspección con el binario actual antes de implementar.

### 3. Añadir primero la regresión de escritura al cache (3–5 min)

En `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`, junto al test de `movement.moveSpeed` (`:781`), insertar este caso en la colección `tests`. No modificar `RuntimeMutatorTestFixtures.cs`: ya contiene el provider, la fila y el enum/cache con la forma que queremos probar.

```csharp
("runtime entity registration applies structured MoveSpeed to the native float cache", () =>
{
    EntityBalancingStore.Reset();
    try
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample:move-cache",
            movement = new ContentUnitMovementDefinition { moveSpeed = 12.5f }
        };
        var provider = new RuntimeMutatorTestProvider();
        provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.id));
        provider.BalanceOverrides.Add(new BalanceOverride
        {
            EntityId = unit.id,
            Parameters = UnitBalanceOverrideMapper.CreateCustomBalance(unit)
        });
        var mutator = new RuntimeContentMutator(
            typeof(EntityBalancingStore).Assembly, provider, null);

        if (!mutator.RegisterEntityRows())
        {
            throw new InvalidOperationException("Entity registration failed.");
        }

        if (!EntityBalancingStore.ChangeableFloatValueCache.TryGetValue(unit.id, out var values) ||
            !values.TryGetValue(EntityBalancingStore.ChangeableValue.MoveSpeed, out float actual) ||
            actual != 12.5f)
        {
            throw new InvalidOperationException(
                "Structured MoveSpeed was not written to EntityBalancingStore.ChangeableFloatValueCache.");
        }
    }
    finally
    {
        EntityBalancingStore.Reset();
    }
}),
```

### 4. Observar RED con el binario recién generado (2–4 min)

La regresión debe fallar en el código actual: el constructor valida el enum de `EntityBalancingStore`, pero `ApplyBalanceOverrides` busca otro enum desde `GameBalancingScriptableObject`; el `catch` omite la escritura. Ejecutar el harness completo (el programa no ofrece filtro de test):

```bash
dotnet build -c Debug --no-incremental --artifacts-path artifacts/unit-cache-red tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/unit-cache-red/bin/RogueCommandExpansionLoader.ValidationTests/debug/RogueCommandExpansionLoader.ValidationTests.dll .
```

Esperado: aparece `FAIL runtime entity registration applies structured MoveSpeed to the native float cache: Structured MoveSpeed was not written to EntityBalancingStore.ChangeableFloatValueCache.`; el proceso termina con código distinto de cero. Los demás casos deben conservar su resultado, incluido el skip de symlink. Si hay error de compilación o el test pasa, detenerse y revisar fixture/assertion antes de producción; no debilitar el test.

### 5. Implementar el fix mínimo (2–3 min)

En `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs`:

1. Añadir junto a los campos `Type`:

```csharp
private readonly Type _changeableValueType;
```

2. En el constructor, justo después de validar `changeableValueType` obtenido de `entityStoreType.GetNestedType("ChangeableValue", ...)` (`:191-197`), guardar el tipo ya comprobado:

```csharp
_changeableValueType = changeableValueType;
```

3. En `ApplyBalanceOverrides` (`:403`), reemplazar únicamente:

```csharp
Type enumType = _balancingType.GetNestedType("ChangeableValue");
```

por:

```csharp
Type enumType = _changeableValueType;
```

No cambiar `ParamMap`, conversions, precedencia, defaults, catch/logging, balance values ni el tipo de los caches. No añadir Harmony ni nuevos bindings reflectivos: se reutiliza el tipo ya validado en el constructor.

### 6. Verificar GREEN y regresiones en Debug (2–4 min)

Rebuild con una carpeta distinta y ejecutar el DLL de esa build, no una salida anterior:

```bash
dotnet build -c Debug --no-incremental --artifacts-path artifacts/unit-cache-green tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/unit-cache-green/bin/RogueCommandExpansionLoader.ValidationTests/debug/RogueCommandExpansionLoader.ValidationTests.dll .
```

Esperado: `PASS runtime entity registration applies structured MoveSpeed to the native float cache`; al añadir la cobertura del cache entero, el harness final pasa **191/192 validation checks; 1 skipped**, sin fallos. Si no pasa, arreglar producción o la forma CLR del fixture; no aceptar un test que solo demuestra que el mapper devuelve un diccionario.

### 7. Build y verificación Release (4–5 min)

Desde la raíz:

```bash
dotnet build -c Release --no-incremental --artifacts-path artifacts/release src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj
dotnet build -c Release --no-incremental --artifacts-path artifacts/unit-cache-release tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/unit-cache-release/bin/RogueCommandExpansionLoader.ValidationTests/release/RogueCommandExpansionLoader.ValidationTests.dll .
git diff --check
```

Esperado: plugin y harness con `0 Warning(s)`/`0 Error(s)`; los tests nuevos aparecen como `PASS`; **191/192 validation checks passed; 1 skipped**. No cambiar schema/manifiestos, por lo que no se ejecuta una validación JSON ni build de AssetBundles.

### 8. Actualizar la evidencia existente (2–4 min)

En `docs/research/UNIT-LOADER-OPPORTUNITIES.md`, conservar el No-Go sobre campos nuevos y registrar el lookup incorrecto, el fix, REA, TDD y smoke Steam/MCP en `MainMenu`. En `docs/research/AGENT-HANDOFF.md`, distinguir el cache runtime comprobado de la aceptación pendiente de spawn/movimiento/combate. La validación local final es 191/192 con un skip. No editar `content/schema/README.md`: no cambió el contrato de packs.

### 9. Revisión, commit y publicación (4–5 min)

Revisar el diff staged con `requesting-code-review`, hacer security scan de líneas añadidas y exigir `passed=true`, `security_concerns=[]`, `logic_errors=[]`. Ejecutar staging explícito y checks:

```bash
git add -- src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs tests/RogueCommandExpansionLoader.ValidationTests/Program.cs docs/research/UNIT-LOADER-OPPORTUNITIES.md docs/research/AGENT-HANDOFF.md .hermes/plans/2026-10-21_105104-unit-balance-cache-enum-fix.md
git diff --cached --check
git diff --cached --name-only
git diff --cached
git commit -m "fix(loader): resolve balance cache enum from entity store"
```

No usar `git add .`/`git add -A` ni incluir `.worktrees/cargador/`. Inmediatamente antes de push, comprobar que el upstream es `origin/codex/rogue-unit-balance-20261018`, que `git ls-remote` todavía apunta al padre del commit y que éste es ancestro de HEAD; empujar sin force y volver a leer `git ls-remote`, exigiendo igualdad exacta con `git rev-parse HEAD`.

## Validación vigente y límites

- El harness Debug y Release ejecuta las regresiones de mapping, precedencia, filtrado por unidades registradas, persistencia en filas y validación de los 25 campos. Los logs preservados bajo `artifacts/final-loader-registered-balance-overrides-20261022/` terminan en **208/209 checks; 1 skipped** por privilegios de symlink en Windows; el build Release del plugin tiene 0 advertencias y 0 errores.
- Los fixtures del harness invalidan con `Clear()` y verifican las filas; no vuelven a calcular un getter nativo. Por separado, el smoke MainMenu con BepInEx MCP verificó en el juego los getters antes/después de invalidar cachés y el rechazo de `MaxHealth=16777217`; fue una sonda que invocó el hook, no una llamada natural de fábrica.
- El historial de trabajo registra TDD RED→GREEN durante el cambio de cache a fila, pero los artefactos revisados no conservan una salida RED reproducible. Las aserciones actuales y logs preservados respaldan la ruta GREEN, no reconstruyen por sí solos el RED histórico.
- La prueba actual del helper filtra overrides a unidades registradas, y `ContentPackRuntimeLoader.GetBalanceOverrides()` lo invoca con los IDs de unidades cargadas. El harness no compila ese loader; la integración se verificó por inspección estática y el camino real de carga se ejercitó en MainMenu con MCP.
- La ejecución local de schema/packs y el simulador de balance son validaciones locales. Ninguna demuestra que `sample-native:scout-tank` aparezca en una oferta, se produzca naturalmente o consuma los stats durante movimiento/combate.

## Risks, tradeoffs, and open questions

- Mantener el mapping limitado a los 25 miembros per-unit respaldados por filas nativas; `ManaRechargePerSecond` es global y las nuevas equivalencias semánticas siguen No-Go sin evidencia de consumidor.
- `customBalance` conserva los números JSON como `double` hasta validar. Los campos enteros se limitan a `2^24` porque el juego los convierte a `Single`; los campos float deben caber en `Single`.
- Los fixtures locales no demuestran recálculo nativo de getter: la evidencia para ello procede del smoke MCP MainMenu. Ninguna evidencia disponible demuestra todavía oferta, producción natural ni consumo de stats de `sample-native:scout-tank` en gameplay.
- La revisión independiente de los cuatro archivos de mapping/validación/runtime no encontró hallazgos accionables; la revisión de documentación identificó inconsistencias históricas que se corrigieron en este plan y en el handoff.

## Resultado histórico del plan original (2026-10-21; diseño de caché supersedido)

- **RED inicial:** el caso de `MoveSpeed` compiló y falló con el código original porque el valor no aparecía en `EntityBalancingStore.ChangeableFloatValueCache`.
- **GREEN:** se guardó el `ChangeableValue` validado desde `EntityBalancingStore` y se reutilizó en `ApplyBalanceOverrides`; el caso de `MoveSpeed` pasó.
- **Cobertura adicional del cache entero:** se agregó una prueba de `MaxHealth`; una mutación temporal que impedía su branch `Int` produjo el fallo esperado, se restauró el código correcto y la prueba pasó.
- **Validación local final:** Debug y Release del harness: `191/192 validation checks passed; 1 skipped` (symlink, privilegio Windows). Plugin Release y harness Release: 0 advertencias/0 errores.
- **Runtime Steam/BepInEx MCP:** el juego cargó el Release plugin y el manifiesto de prueba desde una instalación temporal reversible. En `MainMenu`, una llamada MCP a `RuntimeHooks.BeforeEntityFactory()` completó la preparación del pack y el registro. La sonda devolvió `ProbeStatus=ok`, `HookBound=True`, `HookInvoked=true`, filas float/int presentes, `MoveSpeedValue=14.5` y `MaxHealthValue=130` para `sample-native:scout-tank`; salida preservada en `artifacts/runtime-smoke/bepinex-mcp-run/runtime-cache-probe-result.json`. Esto es evidencia de runtime de los caches nativos, no de un run, una creación natural de entidad ni del efecto visible en gameplay.
- **Restauración y perfiles:** el proceso del juego y el servidor MCP local se cerraron; los cuatro archivos alterados de instalación se restauraron y verificaron contra sus SHA-256 de baseline. El selector siguió en `5`; los cinco perfiles y sus 18 archivos no cambiaron. No se seleccionó QA ni se modificó ningún tutorial.
- **Resultado intermedio:** overrides del mutator escribieron a los caches tipados flotante/entero con el enum correcto. La revisión P1 posterior determinó que este seed de valor final era transitorio y saltaba los modificadores nativos; este comportamiento ya no describe el código actual.
- **Límite:** el harness usa un provider fixture; el smoke MCP complementó esa prueba con el `ContentPackRuntimeLoader` real y los caches de `EntityBalancingStore`. El efecto sobre movimiento, salud, spawn, UI o combate sigue pendiente de una partida observada.

## Seguimiento: corrección a fila base

- **Inspección estática:** con la identidad fijada arriba, `EntityBalancingStore.InvalidateCache()` limpia ambos diccionarios; los getters recalculan desde `GetOriginalIntValue`/`GetOriginalValueAsFloat` cuando falta una clave y luego aplican los modificadores de relics, upgrades, cartas en partida y meta-progresión. Evidencia REA methods view: `ev_671140f1dcea0100d52ade0f05cde8c76a7abb274e843de41d04c946d8d3a515` (padre `ev_dfd6ab4bf3404f0f9b71951a456f083de2f39f283b98d21c7d5243ae08dc5f73`); decompilación ILSpy local bajo `artifacts/rea-tools/decompiled/cache-contract-20261021/EntityBalancingStore.cs`.
- **Mapeo actual:** 25 `ChangeableValue` per-unit se mapean explícitamente a `EntityBalancingParameters` con su storage type; `Attack1Cooldown → attackCooldown`, `SpawnTtl → duration2`. `ManaRechargePerSecond` se rechaza en unidades porque el getter usa `GameBalancingStore.ManaRechargePerSecond` global.
- **Implementación actual:** `RuntimeContentMutator` valida y aplica valores al boxed row del provider antes de añadirlo al registro. No escribe valores finales en `ChangeableIntValueCache` ni `ChangeableFloatValueCache`; la caché nativa queda libre para recalcular desde la fila y componer los modificadores del juego. La precedencia es `customBalance` > raw `balanceOverrides` > `movement.moveSpeed` derivado.
- **TDD local (pasada inicial del refactor):** se observaron RED en la expectativa de valores de fila después de invalidar el cache y al rechazar `ManaRechargePerSecond`; después pasaron con la migración a filas. Esa pasada del harness Release dio `199/200 validation checks passed; 1 skipped` (symlink, privilegio de Windows), sin warnings/errors.
- **Validación local final (2026-10-21):** harness Debug y Release: builds con 0 advertencias/0 errores; ambos DLL exactos pasaron `200/201 validation checks`, con el único skip de symlink por privilegios Windows. Plugin Release: 0 advertencias/0 errores. Draft 2020-12 aceptó los dos packs y `customBalance.MoveSpeed`; `--all-mod-units` ejecutó sin error. El simulador reportó CEI 97.6 para Sandlance Scout y victoria sobre `PCXBaseTank` en 3.85s/78.5% HP; es una observación estática de balance y no motivó un cambio de stats en este trabajo.
- **Smoke Steam/BepInEx MCP del camino actual:** en `Game`, `sample-native:scout-tank` devolvió `MoveSpeed=15.75`, `MaxHealth=137`, `Damage1=7.25` y `Attack1Cooldown=1.15` desde la fila y getters nativos, incluso tras invalidación. Evidencia: `artifacts/runtime-smoke/bepinex-mcp-row-based/evidence/runtime-row-probe-game-scene.json`. Esto prueba la fila/cache en el proceso del juego, no la instancia ni el consumo por una entidad.
- **Producción/movimiento/combate nativos:** se colocaron `RoboCrystalMill` y dos `StunWalkerFactory` mediante `PlaceBuildings.InstantiateBuildingWithEffect` y el comando nativo del engineer; una factoría produjo StunWalker. Un StunWalker nativo se observó moviéndose y atacando `PCXEnergyHunterFactory`; otro recibió un AttackMove hacia `PCXBaseControl`, movió su posición en la primera muestra y quedó sin cambio en la siguiente. Evidencias en `artifacts/runtime-smoke/bepinex-mcp-row-based/evidence/`. No corresponde a la unidad custom ni a sus stats.
- **Pendiente de aceptación de la partida anterior:** no se observó una oferta ni instancia de `sample-native:scout-tank` en el run `Default`, stage 0, ni consumo de sus valores durante movimiento/combate. La evidencia de StunWalker es solo baseline nativa; en ese cierre anterior Profile2 coincidía con su snapshot.
- **Corrección de precisión (2026-10-22):** la revisión independiente detectó que el DTO `float` convertía `MaxHealth: 16777217` en `16777216` antes de validar. Una regresión TDD falló primero por esa pérdida; `customBalance` se mantiene como `double` hasta el mutator para preservar la precisión JSON. Una revisión estática posterior de `CalculateResultingIntValue` mostró que el juego también convierte cada `Int32` de fila a `Single` antes de aplicar deltas y redondear. Por ello, `customBalance` y los campos enteros ChangeableValue de `balanceOverrides` quedan limitados a `2^24` (`16777216`); el schema y ambos validadores aplican el límite. Las regresiones verifican que `16777217` se conserva al deserializar y se rechaza, mientras `16777216` llega intacto a la fila.
- **Intento adicional Steam/MCP (2026-10-22):** abrir `ShowBuildingMenu` para `sample-native:scout-tank-factory` no produjo evidencia interpretable del menú, oferta, colocación ni producción natural. Sigue pendiente el consumo gameplay sin atribuir ese intento al loader.
- **Revalidación local final 2026-10-22:** builds no incrementales del harness Debug/Release y plugin Release finalizaron con 0 advertencias/0 errores; los DLL recién compilados pasaron `208/209 validation checks` en ambas configuraciones, con un solo skip conocido por privilegio de symlink de Windows. Draft 2020-12 aceptó los tres packs de muestra y el manifiesto válido `MaxHealth=16777216`, y rechazó `MaxHealth=16777217` y alias `Damage`. El simulador `--matchup sample-native:scout-tank PCXBaseTank` terminó correctamente (estimación CEI `97.6`, victoria en 3.85s con 78.5% HP restante); no se modificaron los stats. Esta evidencia es local y no prueba gameplay.
- **Rollback del intento anterior de fábrica (2026-10-22):** el juego se cerró normalmente antes de restaurar. Plugin, manifiesto y logs de instalación coinciden con hashes baseline; selector `currentProfile.txt` restaurado a `4`. Los archivos preexistentes de otros perfiles no cambiaron; el test creó y conserva `Profile2\\savegame.dat` y cambió `Profile2\\metaSavegame.dat`, cuya copia original coincide con el hash baseline. No se editó `tutorialState.txt`. Logs cerrados y deltas exactos están bajo `artifacts/runtime-smoke/bepinex-mcp-unit-balance-gameplay-20261021/`, en `restoration-result-20261022.json`.
- **Smoke runtime final de frontera — Steam/BepInEx MCP (2026-10-22, MainMenu sin run):** se revalidó conjuntamente Rogue Command `1.0.9`, Unity `2021.3.45f2`, SHA-256 de `Assembly-CSharp.dll` `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` y MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Con deployment reversible, la sonda MCP invocó `RuntimeHooks.BeforeEntityFactory()` y leyó en la fila provider y getters nativos de `sample-native:scout-tank`: `MoveSpeed=14.5`, `MaxHealth=16777216`, `Damage1=8`, `Attack1Cooldown=0.9`, antes y después de dos invalidaciones. El loader informó 2 packs y 18 entidades nuevas. Una carga separada con `MaxHealth=16777217` desactivó el pack en `units[0].customBalance.MaxHealth` con `unit-custom-balance.value-invalid`; el provider quedó con cero filas. Esto confirma el límite positivo y el rechazo en el loader real dentro del proceso del juego, pero el hook fue invocado por una sonda (no por una factoría natural), no se inició partida y no se observó instancia, movimiento ni combate de `scout-tank`. `restoration-valid-boundary.json` y `restoration-invalid-overflow.json` confirman hashes de instalación baseline, selector `4` y cero cambios de perfiles.
