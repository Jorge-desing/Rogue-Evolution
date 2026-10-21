# Plan: siguiente auditoría de oportunidades del loader Rogue Command

## Goal

Descubrir oportunidades **nuevas y no duplicadas** para el cargador de unidades, etapas y AI, e implementar una mejora por dominio únicamente cuando la ruta nativa y su efecto estén probados con evidencia.

## Current context / assumptions

- Workspace: `C:/Users/Alex/Documents/Mod Rogue Command`; rama activa `codex/rogue-unit-balance-20261018`, HEAD `d2f39b9f95906b72990287e2de5eb0b3144070aa` (`feat(loader): add opt-in Harvester defense and harden validation`). El status observado no muestra cambios fuente; sí muestra `?? .worktrees/cargador/`.
- **No repetir** lo ya entregado en `d2f39b9`: `StageParameterBindingContract` para la forma de tipo valor de `StageParametersScriptableObject.cardReward`, validación de NaN/infinito en armadura, y `protectedTargetType` opcional (`Buildings` por defecto, `Harvesters`, `BuildingsAndHarvesters`) en `sample:reactive-base-defense`, con pruebas del default y del modo combinado.
- La evaluación anterior documentó No-Go para mapear `armor.armorValue` por parecido de nombre con `MaxArmor`/`ArmorProtection`, y para inventar `cardReward.count/weight`. Reabrir esos puntos solo si aparece evidencia nativa nueva; no deshacer el No-Go por conveniencia.
- La auditoría anterior concluyó que los overrides de etapa existentes ya cubren los escalares/booleanos probados y `startCountRange`; las referencias/structs sin consumidor exacto no son oportunidades automáticas.
- El último resultado local registrado en `docs/research/AGENT-HANDOFF.md` fue Release: plugin y harness con 0 warnings/errors, 183/184 checks y 1 skip de symlink por privilegios de Windows. Es un baseline histórico, no reemplaza una ejecución fresca al comenzar.
- La identidad registrada es Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. **Debe revalidarse completa antes de usar firmas/reflection/tokens.**
- El worktree anidado `.worktrees/cargador/` es otro checkout en `main`, está desfasado respecto a `origin/main` y tiene modificaciones locales a `MODDING_PLAN.md`/`MODLOG.md`. No tocarlo, limpiarlo ni incluirlo en staging; no pertenece a este plan.
- `pygount` y `um` no estaban disponibles en el último chequeo; no instalarlos para esta tarea. `jsonschema` 4.26.0 sí está disponible en `python`, y el comando de Draft 2020-12 de abajo validó los tres manifiestos.
- No hay código/API/field nuevo que pueda fijarse de antemano: la selección de una mejora es el resultado de la inspección, no una suposición. Si un dominio no tiene candidato Go, documentar No-Go y no escribir producción especulativa.

## Architecture / proposed approach

Usar REA para trazar un candidato desde el DTO/manifest hasta el campo nativo y su consumidor real; la inspección estática no se presentará como comportamiento en juego. Para cada candidato aprobado, hacer un vertical slice TDD independiente: test RED, cambio mínimo, GREEN, luego el siguiente dominio; preservar defaults, clone paths, precedencia y registros nativos. Mantener la investigación de unidades, etapas y AI en agentes de solo lectura con responsabilidades separadas; el implementador integra y realiza los cambios secuencialmente para evitar conflictos en `Program.cs`.

## Step-by-step tasks

### 1. Confirmar el checkout y preservar trabajo ajeno (3 min)

Desde la raíz, ejecutar:

```bash
git status --short --branch --untracked-files=all
git rev-parse HEAD
git log -1 --oneline
```

Esperado al redactar este plan: rama `codex/rogue-unit-balance-20261018`, HEAD `d2f39b9f95906b72990287e2de5eb0b3144070aa`, y ninguna edición fuente en el checkout activo. Confirmar que `.worktrees/cargador/` es un worktree anidado; no usar `git add .`, `git add -A`, `git clean`, ni cambiar de rama.

### 2. Leer los contratos y resultados previos (4 min)

Leer, en este orden, `AGENTS.md`, `README.md`, `docs/research/AGENT-HANDOFF.md`, `docs/research/BASELINE.md`, `docs/research/ASSET-CATALOG.md`, `content/schema/README.md` y `.hermes/plans/2026-10-20_210056-verified-loader-opportunities.md`. Registrar qué está implementado en HEAD y separar cada afirmación como inspección estática, validación local o runtime. No usar un reporte antiguo como sustituto del código actual.

### 3. Revalidar el target antes de REA (3–5 min)

Usar `mcp__rea__inspect_managed_artifact` en:

`C:/Program Files (x86)/Steam/steamapps/common/Rogue Command/Rogue Command_Data/Managed/Assembly-CSharp.dll`

Verificar la versión del juego, Unity, SHA-256 y MVID como una sola identidad. El resultado debe coincidir con los cuatro valores de Current context; registrar el Evidence ID y su cobertura. Si difiere o REA no puede identificar el ensamblado, detener la investigación de firmas y refrescar `docs/research/BASELINE.md` mediante un plan separado; no reciclar tokens.

### 4. Construir el mapa real de los tres subsistemas (4 min)

Leer estas rutas, no crear abstracciones todavía:

- Unidad: `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`, `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs`, y el punto de consumo en `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs`.
- Etapa: `src/RogueCommandExpansionLoader/Content/StageParameterOverrideParser.cs`, `src/RogueCommandExpansionLoader/Content/StageParameterBindingContract.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs` y `content/schema/content.schema.json`.
- AI: `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs`, `src/RogueCommandExpansionLoader/Contracts/IRogueCommandAiModule.cs`, todos los `*AiModule.cs`/`*Parameters.cs` en `src/RogueCommandExpansionLoader/Modules/`, `content/schema/README.md` y los tests en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`.

Anotar para cada cambio propuesto: DTO/parameter → validación → clone/precedencia → mutador/loader → consumidor nativo → observable esperado. Si falta un eslabón, no es todavía candidato Go.

### 5. Inspeccionar en paralelo, solo lectura, una pregunta por dominio (5 min)

Después de `delegate_task(action="list")`, lanzar como máximo tres agentes de investigación, cada uno con identidad, rutas y formato de salida completos:

1. **Unidad:** candidatos en `ContentUnitDefinition` que puedan mapearse a una entrada ya existente de `RuntimeContentMutator.ParamMap`/`ChangeableValue`, sin duplicar `movement.moveSpeed` ni el No-Go de armadura. Devolver caller/campo exacto, tipo CLR, precedencia y posible test.
2. **Etapas:** campos `StageParametersScriptableObject` aún sin override; encontrar consumidor en `LevelProgression`/llamadores y explicar si es clone-safe. No proponer objetos Unity ni `cardReward.count/weight` sin lectura/caller demostrado.
3. **AI:** gaps no cubiertos por `TraceDefaultAiModule`, `ScheduledBaseBuildAiModule`, `ReactiveBaseDefenseAiModule`, `HarvesterRaiderAiModule`, `SiegeAssaultAiModule`, `RetreatAndRegroupAiModule` y `PatrolAndInterceptAiModule`. Para cada target/acción, demostrar rol/ownership/base/vida y límite por tick.

Cada agente devuelve: archivos, firmas, Evidence IDs, evidencia estática, efecto esperado, riesgo y Go/No-Go. No permitir que estos agentes editen, compilen, alteren la instalación o pisen `Program.cs`.

### 6. Decidir Go/No-Go con una matriz corta (3 min)

En el plan de ejecución, comparar candidatos por dominio usando: (a) campo/parameter exacto; (b) consumidor nativo trazado; (c) seam existente; (d) default y precedencia; (e) test observacional; (f) coste/riesgo. Elegir como máximo una mejora acotada por dominio. Rechazar candidatos sustentados solo en nombres parecidos, campos sin lectores, resultados de parsers sin comportamiento, o stubs que no reproduzcan el tipo CLR real.

### 7. Unidad: un vertical slice únicamente si el candidato quedó Go (2–5 min por subpaso)

7.1. Elegir un único campo estructurado existente y anotar su key exacta en `RuntimeContentMutator.ParamMap`, tipo nativo (`Int`/`Float`) y caller. No reutilizar `movement.moveSpeed`; no mapear `armor.armorValue` sin prueba nueva.

7.2. Escribir primero en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs` un test con nombre que describa el efecto observado y cubra además colisión con `customBalance`/`balanceOverrides` cuando aplique. Si se incorpora un nuevo DTO, escribir también la prueba de deep clone y añadir el source al `<Compile Include>` de `tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj`.

7.3. RED: compilar y ejecutar el binario exacto de Debug:

```bash
dotnet build -c Debug --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/debug/RogueCommandExpansionLoader.ValidationTests.dll .
```

Esperado: el nuevo test aparece como `FAIL` por el mapping ausente, no por compilación/fixture. Si el test pasa de inmediato, corregirlo antes de escribir producción.

7.4. GREEN: cambiar solo lo necesario en `ContentPackDefinition.cs`/`RuntimeContentMutator.cs`/`ContentPackRuntimeLoader.cs`/`ContentPackValidation.cs` y `content.schema.json` si el contrato realmente cambia. Volver a compilar y ejecutar el mismo DLL; esperado: nuevo test `PASS`, ningún `FAIL`, sin nuevas advertencias. Mantener el caso en No-Go si la semántica no es inequívoca.

### 8. Etapas: un vertical slice únicamente si hay lector nativo probado (2–5 min por subpaso)

8.1. Elegir un campo allowlistable de `StageParametersScriptableObject` y trazar una lectura concreta, clonación, tipo y efecto. La corrección del binding `cardReward` ya está hecha; no volver a exigir `OpenReward` ni añadir `count/weight`.

8.2. Escribir en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs` un test RED para validación y aplicación/clonado. Solo después actualizar `ValidateStageParameterOverrides` en `ContentPackValidation.cs`, `ApplyStageParameterOverrides` en `ContentPackRuntimeLoader.cs`, `StageParameterOverrideParser.cs` si el valor requiere parseo, y la condición correspondiente en `content/schema/content.schema.json`.

8.3. Ejecutar la compilación y el DLL Debug exactos del paso 7.3. Esperado: test nuevo falla por la capacidad ausente, luego pasa tras la implementación mínima; no cambiar el objeto original ni los pools/filtros nativos.

### 9. AI: una ampliación no duplicada y acotada, si el análisis la avala (2–5 min por subpaso)

9.1. Seleccionar un target o decisión no cubierta por los módulos existentes. Inspeccionar el tipo/rol y caller exactos con REA; comprobar ownership, base, vida, selección de defensores, cooldown, máximo de órdenes y lifecycle. No repetir `protectedTargetType` ni romper el default `Buildings` de `sample:reactive-base-defense`.

9.2. Escribir el test RED primero en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`; para un módulo nuevo, aislar parser/configuración en `src/RogueCommandExpansionLoader/Modules/<Nombre>Parameters.cs` y comportamiento en `src/RogueCommandExpansionLoader/Modules/<Nombre>AiModule.cs`. Tocar `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs` y el validador de módulos solo si es una clase nueva registrada.

9.3. Confirmar RED con el DLL Debug exacto, implementar lo mínimo, confirmar GREEN con el mismo comando. Cubrir default/exclusiones, base incorrecta, target inválido y límite de dispatch que correspondan al comportamiento elegido. No atribuir eficacia de combate en juego al harness.

### 10. Validar schema y los tres manifests (2–3 min)

Si ningún schema/manifest cambió, registrar que el schema quedó intacto. Si hubo cambios de contrato, desde la raíz ejecutar este check Draft 2020-12 (requiere el `jsonschema` ya disponible en `python`):

```bash
python -B -c 'import json; from pathlib import Path; from jsonschema import Draft202012Validator; schema=json.loads(Path("content/schema/content.schema.json").read_text(encoding="utf-8")); Draft202012Validator.check_schema(schema); validator=Draft202012Validator(schema); paths=sorted(Path("content/packs").glob("*/content.json")); errors=[(str(path), error.message) for path in paths for error in validator.iter_errors(json.loads(path.read_text(encoding="utf-8")))]; print(f"PASS: {len(paths)} manifests" if not errors else f"FAIL: {len(errors)} schema errors"); [print(f"{path}: {message}") for path, message in errors]; raise SystemExit(bool(errors))'
```

Esperado con el checkout actual: `PASS: 3 manifests`, exit code 0. No instalar paquetes si el entorno futuro no dispone del validador; informar el blocker y mantener el change sin schema no validado.

### 11. Verificación integrada Debug/Release (4–5 min)

Tras cada slice, ejecutar el harness completo Debug. Al cerrar todos los Go-candidates, ejecutar:

```bash
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj -c Release -v:minimal
dotnet build -c Release --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/release/RogueCommandExpansionLoader.ValidationTests.dll .
git diff --check
```

Esperado: ambos builds con 0 warnings/0 errors; el DLL Release exacto lista todos los nuevos tests como `PASS`, no muestra `FAIL`, y conserva solo el skip conocido de symlink si Windows no concede el privilegio. Reportar el conteo real; no mantener fijo el total histórico 183/184 después de añadir pruebas.

### 12. Actualizar documentación de contrato y handoff (3 min)

Si se añade una capacidad, actualizar solo lo pertinente en `content/schema/README.md` y registrar en `docs/research/AGENT-HANDOFF.md` el ID/semántica, consumidor, Evidence IDs, tests y límites de evidencia. Añadir explícitamente No-Go de cada dominio que no tenga consumidor probado. No tocar `MODDING_PLAN.md` ni `MODLOG.md` en el worktree `main` anidado.

### 13. Revisión independiente y cierre (4 min)

Antes del commit, revisar `git diff --name-only`, `git diff --check` y el diff exacto. Ejecutar la revisión independiente de `requesting-code-review` con el diff y resultados del security scan; tras `delegate_task(action="list")`, mandar un reviewer de solo lectura, sin solaparlo con editores. Debe responder `passed=true`, `security_concerns=[]`, `logic_errors=[]`; corregir bloqueos y volver a ejecutar Release/tests. Solo entonces preparar commits task-owned explícitos y seguir la política vigente de `AGENTS.md`; preservar `.worktrees/cargador/`. Antes de cualquier push, comprobar upstream y remote, y verificar el SHA remoto después. No crear PR ni iniciar el juego en esta fase.

## Tests / validation

- Cada comportamiento nuevo: **RED** (el test nuevo falla por ausencia funcional) → **GREEN** (cambio mínimo y test pasa) → **REFACTOR** (solo si mejora legibilidad, tests siguen verdes). Repetir el build Debug + DLL exacta por slice.
- Harness exacto: `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs` compilado a `artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/<debug|release>/RogueCommandExpansionLoader.ValidationTests.dll`; no ejecutar una salida stale de otro directorio.
- Schema: validar `content/schema/content.schema.json` y los tres `content/packs/*/content.json` con el one-liner del paso 10 cuando haya cambios de contrato.
- Release: build del plugin y del harness, DLL de Release exacta, `git diff --check`, revisión independiente y verificación de file list antes de commit.
- Runtime: fuera de alcance de este plan. Una prueba Steam posterior requiere autorización/alcance específico y seguir `docs/research/STEAM-RUNTIME-CONTROL.md`, `AGENTS.md` y `rogue-command-steam-runtime-validation`; no editar `tutorialState.txt`, no reutilizar Profile 1, y no eludir `FirstRun` sin perfil QA aislado.

## Risks, tradeoffs, and open questions

- Puede no existir una mejora nativa, acotada y no duplicada para una o más áreas; No-Go documentado es un resultado válido y preferible a inventar semantics.
- Los DTO `JsonUtility` no distinguen siempre omitido de cero/false explícito; una nueva opción debe definir default/precedencia y no cambiar comportamiento anterior.
- `RuntimeContentMutator.ParamMap` y los cachés nativos tienen tipos exactos; cambiar una key sin identificar el consumidor puede truncar valores o dejar caches stale.
- El harness usa fixtures/stubs y prueba lógica del plugin, no la carga real de AssetBundles ni la estrategia AI durante una partida.
- No instalar `um`, `pygount`, herramientas, dependencias ni cambiar configuración Hermes solo para esta exploración.
- Pregunta abierta para quien ejecute: ¿hay autorización separada para aceptación Steam? Hasta recibirla, solo inspección estática y validación local; no desplegar ni modificar perfiles.

## Resultado de ejecución (2026-10-21)

- **Unidades:** No-Go; ningún mapeo nuevo de balance estructurado tuvo una cadena verificada hasta un consumidor nativo. **Etapas:** No-Go; no se demostró un nuevo contrato de consumidor compatible con la clonación. No se añadieron mapeos especulativos ni overrides de etapa.
- **AI:** Go para una ruta de patrulla nativa optativa en el módulo existente `sample:patrol-and-intercept`. Se añadió `patrolMode=Native`; `AttackMove` sigue siendo el valor predeterminado. Las rutas nativas se limitan a ocho celdas distintas más cercanas dentro del radio y solo se asignan con comando actual `DoNothing` y cola nativa verificada vacía. La cola privada `_commandChain` se lee por reflexión con tipo exacto y fallo cerrado si no se puede comprobar; así no se dejan órdenes `Move` ni otras órdenes encoladas dormidas. Las intercepciones attack-move conservan el límite compartido `maxPatrolUnits`.
- **TDD:** Se observó RED/GREEN para el parser del modo nativo, modos inválidos/duplicados, despacho nativo, selección/capacidad de rutas, comportamiento AttackMove predeterminado, preservación de intercepciones, límite compartido y una orden `Move` encolada detrás de `DoNothing`. Los fixtures ahora modelan `EntityCommand` como el tipo valor nativo y la cola privada como `LinkedList<EntityCommand>`; las aserciones de identidad de órdenes anteriores usan contadores de ejecución.
- **Validación:** Las builds Release del plugin y del harness terminaron con 0 advertencias/errores. Los DLL exactos de pruebas Debug y Release pasaron 189/190 checks cada uno, con un skip de symlink de Windows por privilegios. La validación Draft 2020-12 pasó los 3 manifiestos y `git diff --check` pasó. Firmas y evidencia estática: `docs/research/AI-NATIVE-PATROL.md`.
- **Límite de evidencia:** No se inició una partida de Steam ni se modificaron la instalación o perfiles. El pathfinding, la reflexión contra el ensamblado cargado y la interrupción/reanudación de la ruta nativa siguen sin validar en juego. La revisión independiente final no encontró bloqueos de lógica o seguridad; esto no eleva la validación local a evidencia de runtime.
