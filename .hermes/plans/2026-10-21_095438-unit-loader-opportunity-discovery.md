# Plan: auditoría e implementación de oportunidades del cargador de unidades

## Goal

Encontrar y añadir mejoras nuevas y no redundantes al cargador de unidades de Rogue Command, y dejar un No-Go documentado en vez de código especulativo cuando no exista una oportunidad segura.

## Current context / assumptions

- Checkout inspeccionado: `C:/Users/Alex/Documents/Mod Rogue Command`; rama `codex/rogue-unit-balance-20261018`, HEAD `2514c556d8077b37cf301e55241d86d747daeef6` (`feat(ai): add opt-in native patrol mode`), upstream configurado `origin/codex/rogue-unit-balance-20261018`. El estado actual muestra únicamente `?? .worktrees/`; es un worktree/dato ajeno y debe conservarse sin staging, limpieza ni modificación.
- La última revisión de unidades, registrada en `.hermes/plans/2026-10-20_231518-rogue-loader-next-opportunity-audit.md`, concluyó No-Go para mapeos nuevos: no se había trazado otro campo estructurado hasta un consumidor nativo. Reabrir un candidato solo con evidencia estática nueva; no repetir esa conclusión como si fuera una implementación ni duplicar lo ya entregado.
- Estado actual del pipeline: `ContentUnitDefinition` y sus subdefiniciones están en `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs`; `ContentPackCopy.CloneUnit` realiza el deep clone; `ContentPackValidation` valida todos los subtipos; `UnitBalanceOverrideMapper.CreateCustomBalance` actualmente deriva solo `movement.moveSpeed` positivo/finito a `MoveSpeed`. `customBalance` y `balanceOverrides` ya alimentan rutas genéricas existentes. `RuntimeContentMutator.ParamMap` define los valores nativos permitidos y `ApplyBalanceOverrides` escribe en los caches tipados; `ContentPackRuntimeLoader.GetBalanceOverrides` conecta las unidades con esa ruta.
- En `content/schema/README.md` se documenta que `movement.moveSpeed` es el único subcampo estructurado con mapping runtime. Las subdefiniciones restantes son descriptivas/validadas hasta que exista un binding con consumidor probado. No mapear `armor.armorValue` por semejanza con `MaxArmor` o `ArmorProtection`: sus semánticas nativas son distintas. No añadir una alternativa a las rutas genéricas `customBalance`/`balanceOverrides` salvo que ofrezca semántica o validación distinta y verificable.
- La identidad histórica del target es Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Es un baseline, no prueba de la identidad instalada al empezar la futura ejecución: volver a verificar los cuatro valores juntos antes de usar REA, firmas, tokens o reflexión.
- El harness Release más reciente registrado en el handoff pasó `189/190`, con un test omitido por privilegios de symlink de Windows. Es un baseline histórico; ejecutar una build limpia y el DLL exacto antes de atribuir fallos o aciertos.
- `pygount` no está instalado; no instalarlo solo para esta auditoría.
- Alcance: unidad/registro/balance y validación de sus datos. No volver a implementar `patrolMode=Native`, cambios de etapas, AI reactiva, bundles visuales ni producción ya existente. No desplegar al juego ni iniciar Steam en este plan; cualquier aceptación in-game será una solicitud separada, con perfil QA reversible si corresponde.

## Architecture / proposed approach

Trazar cada candidato de extremo a extremo: campo del manifiesto → JSON Schema/DTO → deep clone → validación y precedencia → proveedor/runtime loader → mutador/cache → miembro nativo y caller que lo lee → efecto observable. Priorizar REA managed inspection con `detail: "summary"` y páginas pequeñas de la colección exacta; usar ILSpy fijado solo si REA no puede responder, guardando decompilados fuera del repositorio. Para cada candidato Go, escribir un test de regresión que ejercite el efecto y la precedencia antes de tocar producción, implementar un vertical slice mínimo y mantener las categorías de evidencia estática, validación local y runtime separadas.

## Step-by-step tasks

### 1. Confirmar el checkout y preservar datos ajenos (2–3 min)

Desde la raíz, volver a ejecutar:

```bash
git status --short --branch --untracked-files=all
git rev-parse HEAD
git log -1 --oneline
git rev-parse --abbrev-ref --symbolic-full-name '@{u}'
```

Esperado para el plan redactado: rama `codex/rogue-unit-balance-20261018`, HEAD `2514c556d8077b37cf301e55241d86d747daeef6`, upstream `origin/codex/rogue-unit-balance-20261018` y `?? .worktrees/`. Si el estado difiere, parar, actualizar el contexto del plan y conservar cualquier cambio ajeno. Nunca usar `git add .`, `git add -A`, `git clean`, reset ni cambiar de rama.

### 2. Releer contratos y resultados previos (3–4 min)

Leer, en este orden: `AGENTS.md`, `README.md`, `docs/research/AGENT-HANDOFF.md`, `docs/research/BASELINE.md`, `docs/research/ASSET-CATALOG.md`, `content/schema/README.md`, `.hermes/plans/2026-10-20_231518-rogue-loader-next-opportunity-audit.md` y `.hermes/plans/2026-10-20_210056-verified-loader-opportunities.md`.

Anotar qué existe en el HEAD actual y clasificar cada afirmación como inspección estática, validación local o runtime. Tratar `movement.moveSpeed`, `customBalance`, `balanceOverrides`, la fábrica por unidad, caché/rollback, y el No-Go previo de armadura como baseline; no volver a proponerlos sin una hipótesis nueva y evidencia concreta.

### 3. Capturar baseline local antes de editar (3–5 min)

Compilar y ejecutar el harness Debug exacto desde la raíz:

```bash
dotnet build -c Debug --no-incremental --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/debug/RogueCommandExpansionLoader.ValidationTests.dll .
```

Esperado histórico: `189/190 validation checks passed; 1 skipped.` El skip conocido es `reparse-point bundle paths are rejected` por falta de privilegios de symlink en Windows. Guardar el total real; si el comando no compila o falla antes de las pruebas, no atribuirlo al nuevo cambio y resolver primero el baseline.

### 4. Revalidar la identidad completa antes de inspeccionar firmas (3–5 min)

Comprobar versión, Unity, ruta del proceso y hash desde la instalación local, sin escribir en ella:

```bash
python -B -c 'from pathlib import Path; import hashlib; root=Path("C:/Program Files (x86)/Steam/steamapps/common/Rogue Command"); log=Path("C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Player.log"); lines=log.read_text(encoding="utf-8",errors="replace").splitlines(); print("version.txt="+(root/"version.txt").read_text(encoding="utf-8").strip()); print(next((x.strip() for x in lines if x.startswith("Initialize engine version:")), "Unity marker missing")); print(next((x.strip() for x in lines if "Mono path[0]" in x), "Mono path[0] missing")); print("assembly_sha256="+hashlib.sha256((root/"Rogue Command_Data/Managed/Assembly-CSharp.dll").read_bytes()).hexdigest())'
```

Luego llamar `mcp__rea__inspect_managed_artifact` con `detail: "summary"` y `path` igual a `C:/Program Files (x86)/Steam/steamapps/common/Rogue Command/Rogue Command_Data/Managed/Assembly-CSharp.dll`. Confirmar que el `Mono path[0]` corresponde a esa misma instalación y que REA devuelve SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` y MVID `db0e9462-128d-4296-9900-de18b4bd0c00`; `version.txt` debe identificar `1.0.9` y `Player.log` debe indicar Unity `2021.3.45f2`. Si alguno difiere, detener el análisis de firmas y no reciclar tokens.

### 5. Mapear la superficie actual del cargador sin inventar APIs (4 min)

Leer estos símbolos concretos:

- `ContentUnitDefinition`, todas las `ContentUnit*Definition`, `UnitBalanceOverrideMapper.CreateCustomBalance`, `HasExplicitRowOverride` y `ContentPackCopy.CloneUnit` en `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs`.
- La llamada a `ValidateUnit*` y sus validadores en `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`.
- `RuntimeContentMutator.ParamMap`, `RegisterEntityRows`, `TryRegisterEntityRows` y `ApplyBalanceOverrides` en `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs`.
- `GetBalanceOverrides`, preparación de `LoadedUnit` y generación de balance/prefab en `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs`.
- Las reglas de unidad en `content/schema/content.schema.json`, el contrato narrativo en `content/schema/README.md` y las pruebas pertinentes en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`.

Crear una tabla de trabajo con una fila por candidato y columnas: subcampo, ya cubierto por `customBalance`/`balanceOverrides`, clone, validator, camino runtime, `ChangeableValue` o consumidor, tipo CLR, precedence/default y prueba de efecto. Considerar para investigación —no como mapping aceptado— `movement.turnRate/acceleration`, `detection.detectionRange`, `weapons.damage/range/fireRate`, `healing`, `stealth`, `experienceReward`, `aura`, `resourceGathering` y `aiBehavior`. Los nombres similares no constituyen evidencia.

### 6. Repartir investigación estática acotada, sin ediciones paralelas (5 min)

Antes de delegar, llamar `delegate_task(action="list")`. Si no hay otro trabajo activo, usar como máximo dos agentes de solo lectura con la identidad completa y el commit actual en su contexto:

1. **Subdefiniciones y balance:** trazar cada subcampo candidato hasta lectores nativos de `EntityBalancingParameters`, `EntityBalancingStore.ChangeableValue`, `EntityController` y sus callers. Por cada candidato, devolver firma exacta, clase vs struct, static vs instance, tipo/semántica del valor, Evidence IDs y resultado Go/No-Go. No sugerir ratios, inversas ni slots de arma por intuición.
2. **Registro/runtime de unidades:** auditar la ruta `GetEntityParameterRows` → `RuntimeContentMutator.RegisterEntityRows` → index/factory map/image/stat caches → `GetBalanceOverrides`, y las rutas de asset/template ya implementadas. Buscar un hueco funcional no cubierto por el audit previo; devolver archivo/método/caller, fallo reproducible, prueba propuesta y evidencia.

Los agentes no editan, compilan, cargan bundles, escriben en la instalación ni tocan `.worktrees/`. Sus informes deben separar evidencia estática, validación local y desconocidos.

### 7. Confirmar el contrato con REA y cerrar Go/No-Go (3–5 min por candidato)

Con la identidad ya igualada, llamar `mcp__rea__inspect_managed_members` con `detail: "summary"` y nombres exactos de tipos seleccionados, empezando por `EntityBalancingParameters`, `EntityBalancingStore` y `EntityController`. Registrar `Evidence ID`, `unmatched_type_names` y cobertura. Después usar `mcp__rea__inspect_analysis_view` solo para `fields`, `methods`, `field_accesses` o `call_edges` necesarios, con `limit` pequeño; seguir el caller hasta la lectura observable. Si la respuesta REA es incompleta o `resource-limit`, usar `tools/Decompile-ManagedType.ps1` con sus parámetros reales (`AssemblyPath`, `TypeName`, `ReferenceDirectory`, `OutputDirectory`), guardando salida en `C:/Users/Alex/AppData/Local/hermes/cache/scratch/rc-unit-loader/`; no publicar decompilados. Cerrar la sesión REA al finalizar.

Aceptar Go solo si se prueba la cadena completa campo → clon/validación → mapping → caller nativo/compilado → efecto, se conoce la precedencia con `customBalance`/`balanceOverrides`, se preserva el tipo CLR real y se puede probar el efecto localmente. Si es solo un alias para el mapping genérico, una referencia no leída, un nombre parecido o un campo sin caller, marcar No-Go. No alterar etapas ni AI como efecto lateral.

### 8. Escribir la primera regresión antes del código (2–4 min)

Para cada candidato Go, añadir un único caso observacional en `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`. Si el candidato solo cambia una derivación estructurada, el test debe comprobar tanto la key/value nativa esperada como la precedencia: `customBalance` explícito y `balanceOverrides` explícito deben ganar según el contrato vigente. Si el cambio alcanza el registro, probar además que `RuntimeContentMutator.RegisterEntityRows`/`ApplyBalanceOverrides` deja el valor en el cache tipado correspondiente, no solo que el mapper devuelve un diccionario. Si se añade DTO, preservar en `ContentPackCopy.CloneUnit` deep clone independiente, validar null/rango/finito y agregar el campo al JSON Schema; incluir pruebas de clone y rechazo. No crear un helper o subproyecto sin necesitarlo.

### 9. Verificar RED y la ruta de test exacta (2–4 min)

Recompilar y ejecutar el DLL Debug recién producido:

```bash
dotnet build -c Debug --no-incremental --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/debug/RogueCommandExpansionLoader.ValidationTests.dll .
```

El test nuevo debe aparecer como `FAIL` por la capacidad ausente, no por typo, fixture inválido o error de compilación. Si pasa inmediatamente, fortalecer la aserción para observar el efecto faltante antes de producción.

### 10. Implementar el mínimo y comprobar GREEN (2–5 min por slice)

Cambiar solo la ruta probada. Para una mapping de subdefinición existente, el seam esperado es `UnitBalanceOverrideMapper.CreateCustomBalance` en `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs`, el proveedor `ContentPackRuntimeLoader.GetBalanceOverrides` y el `ParamMap`/cache validado en `RuntimeContentMutator.cs`; solo tocar `ContentPackValidation.cs` y el schema si el contrato de entrada realmente cambia. Si aparece una necesidad diferente, parar y actualizar la matriz antes de editar. No añadir Harmony para un stat que cabe en el pipeline de balance existente, no reescribir filas nativas, no cambiar defaults sin test y no borrar cache de otros IDs. Ejecutar de nuevo el comando Debug exacto del paso 9: el nuevo caso debe ser `PASS`, sin regresiones ni advertencias nuevas. Repetir RED→GREEN de a una oportunidad.

### 11. Validar manifiestos, plugin y Release exacto (4–5 min)

Si cambió el schema o algún manifiesto, ejecutar desde la raíz:

```bash
python -B -c 'import json; from pathlib import Path; from jsonschema import Draft202012Validator; schema=json.loads(Path("content/schema/content.schema.json").read_text(encoding="utf-8")); Draft202012Validator.check_schema(schema); validator=Draft202012Validator(schema); paths=sorted(Path("content/packs").glob("*/content.json")); errors=[(str(path), error.message) for path in paths for error in validator.iter_errors(json.loads(path.read_text(encoding="utf-8")))]; print(f"PASS: {len(paths)} manifests" if not errors else f"FAIL: {len(errors)} schema errors"); [print(f"{path}: {message}") for path, message in errors]; raise SystemExit(bool(errors))'
```

Esperado con los tres manifiestos actuales y sin cambios incompatibles: `PASS: 3 manifests`. Si `jsonschema` falta, no instalarlo sin aprobación; marcar el check bloqueado. A continuación ejecutar:

```bash
dotnet build -c Release --no-incremental --artifacts-path artifacts/release src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj
dotnet build -c Release --no-incremental --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/release/RogueCommandExpansionLoader.ValidationTests.dll .
git diff --check
```

Esperado: ambos builds con `0 Warning(s)`/`0 Error(s)`; el DLL Release exacto no presenta `FAIL` y muestra los nuevos tests como `PASS`. Reportar el número real y el skip de symlink; no copiar el total histórico a una ejecución nueva. Todo generado permanece bajo `artifacts/` ignorado.

### 12. Registrar resultado y límites de evidencia (3 min)

Crear `docs/research/UNIT-LOADER-OPPORTUNITIES.md` (nuevo) con identidad, matriz candidata, REA Evidence IDs, Go/No-Go y límites; actualizar `docs/research/AGENT-HANDOFF.md` y `content/schema/README.md` solo para el contrato que realmente cambie. Si no hay Go, documentar el No-Go y no tocar producción. Dejar explícito: REA = estático; build/harness = validación local; sin Steam match no afirmar que el stat afecta al combate o a la UI. No modificar perfiles ni instalar el plugin durante este plan.

### 13. Revisión, commit y push en la futura ejecución (4–5 min)

Ejecutar `requesting-code-review` sobre el diff staged final y el security scan de líneas añadidas. El revisor debe devolver `passed=true`, `security_concerns=[]` y `logic_errors=[]`. Para staging, nombrar cada ruta task-owned explícitamente: según el slice pueden ser `ContentPackDefinition.cs`, `ContentPackValidation.cs`, `RuntimeContentMutator.cs`, `ContentPackRuntimeLoader.cs`, `content/schema/content.schema.json`, `content/schema/README.md`, `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`, y las notas research/handoff. No usar staging global ni incluir `.worktrees/`.

Usar un mensaje Conventional Commit acorde al resultado, por ejemplo `feat(loader): add verified unit stat mapping`; si todo termina No-Go y solo cambia research, `docs(research): record unit loader audit`. Antes de cada push, verificar `git status`, rama/upstream y SHA real con `git ls-remote`; exigir que el remoto sea fast-forward del commit local y detenerse si cambió, sin rebase, force push o cambio de rama automático. Después del push, volver a leer `git ls-remote` y exigir igualdad exacta con `git rev-parse HEAD`.

## Tests / validation

- Por cada comportamiento: test nuevo primero → fallo esperado observado con el DLL Debug recién compilado → cambio mínimo → el mismo test y harness completo en verde → revisión. No sustituir el resultado del DLL exacto por una ejecución stale ni por una compilación con stubs presentada como runtime.
- Baseline documentado antes de la tarea: exacto Debug/Release `189/190`, un skip por privilegio de symlink. Repetirlo antes de implementar y anotar el total real.
- Para cambios de esquema/manifiestos: Draft 2020-12 con el comando del paso 11, además del harness completo.
- Cierre local: Release plugin + Release harness, DLL exacto, `git diff --check`, seguridad/review y archivo staged exacto.
- Runtime: fuera de alcance. Cualquier prueba real posterior debe iniciar desde Steam y seguir `AGENTS.md`/`docs/research/STEAM-RUNTIME-CONTROL.md`; no modificar la instalación o perfil en esta tarea.

## Risks, tradeoffs, and open questions

- `customBalance` y `balanceOverrides` ya cubren una ruta genérica de estadísticas; una subdefinición nueva puede ser duplicada sin añadir semántica validada.
- Los DTO de `JsonUtility` no distinguen campo omitido de `0`/`false`; cualquier mapping nuevo necesita default, precedencia y tratamiento de cero explícitos.
- `MaxArmor` y `ArmorProtection` son valores distintos con consumidores distintos; el No-Go previo continúa hasta que aparezca una cadena nativa nueva. No deducir multiplicadores, inversas de fireRate ni selección `Damage1`/`Damage2` por nombres.
- Todos los campos de subdefiniciones existentes validan, pero validación no implica efecto runtime. Puede que el resultado correcto sea No-Go y documentación, no código.
- El harness usa fixtures; no prueba carga de Unity AssetBundles ni comportamiento dentro del juego. La reflexión/metadata pertenece al build fijado y requiere revalidación tras cualquier actualización.
- `pygount` no está disponible y no es un gate funcional; no instalar dependencias solo para producir métricas LOC.

## Resultado de ejecución (2026-10-21)

- **No-Go para comportamiento nuevo:** las auditorías de subdefiniciones y del registro/runtime no demostraron una ruta nueva, no redundante y observable hasta un consumidor nativo. No se modificaron código, DTO, tests, schema ni manifiestos. El detalle de candidatos, límites y Evidence IDs está en `docs/research/UNIT-LOADER-OPPORTUNITIES.md` y se añadió una referencia al handoff.
- **Identidad revalidada:** Rogue Command 1.0.9; Unity 2021.3.45f2; SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.
- **Validación local:** baseline Debug y Release harnesses pasaron 189/190, cada uno con un skip por privilegio de symlink de Windows. Plugin y harness Release compilaron con 0 advertencias/0 errores; el DLL Release exacto terminó en 189/190 con el mismo skip. `git diff --check` pasó. No se hizo prueba runtime ni se desplegó el mod.
- **Trabajo ajeno:** `.worktrees/cargador/` quedó sin tocar ni staged.
