# Plan: corregir la escritura de overrides en el cache de balance

## Goal

Corregir el lookup del enum usado por `RuntimeContentMutator.ApplyBalanceOverrides` para que los overrides admitidos se escriban usando el tipo real `EntityBalancingStore.ChangeableValue`, y cubrir la ruta con un test RED→GREEN.

## Current context / assumptions

- Checkout observado: `C:/Users/Alex/Documents/Mod Rogue Command`, rama `codex/rogue-unit-balance-20261018`, HEAD `2e35449e208776788c65c327a0f1ba60d462b22c`, upstream `origin/codex/rogue-unit-balance-20261018`. El único dato ajeno no rastreado es `?? .worktrees/cargador/`; conservarlo sin abrir, modificar ni stagear.
- La auditoría anterior concluyó No-Go para **añadir mappings nuevos a subdefiniciones**; esa decisión sigue vigente. Este plan atiende otro hallazgo: el camino ya existente de `BalanceOverride` parece buscar el enum cache dentro del tipo equivocado y descartar cada override al capturar la excepción.
- La identidad completa se revalidó en esta sesión: Rogue Command `1.0.9`; Unity `2021.3.45f2`; SHA-256 de `Assembly-CSharp.dll` `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. `version.txt` y `Player.log` pertenecen a la misma instalación Steam. REA Evidence de identidad: `ev_b923e23cfec30be565c58056206d348d2714b1d81204fa26364cbbcce0bd6463`.
- REA estático seleccionó `EntityBalancingStore`, `GameBalancingScriptableObject` y tipos anidados `ChangeableValue`, con metadata completa dentro de esa selección: `ev_84b3d3c96544a9abf4da45c12a4809714f93ac1bc09737a07fdd2a9b0757bf06`. La página de tipos `ev_52c24c4076eaeaf60eb68915ace74021416811bef8b69c5da85e1e841c71ffaf` identifica `EntityBalancingStore` como TypeDef `0x02000073`, `GameBalancingScriptableObject` como `0x02000074`, y el `ChangeableValue` del cache como TypeDef `0x02000475`, anidado bajo `0x02000073`. La página de campos `ev_e6d7439461311f158a6ad6dc1fdb0704dbc142d58d83341e93590a15fc5cadce` confirma `ChangeableIntValueCache` y `ChangeableFloatValueCache` en `EntityBalancingStore`. La selección completa no muestra un `ChangeableValue` anidado en `GameBalancingScriptableObject`.
- Código a corregir: `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs`. El constructor asigna `_balancingType` a `GameBalancingScriptableObject` (`:112-113`), pero descubre y valida el enum en `entityStoreType.GetNestedType("ChangeableValue")` (`:191-197`). `ApplyBalanceOverrides` vuelve a buscarlo desde `_balancingType` (`:403`) antes de `Enum.Parse`; las excepciones se capturan y solo se registran (`:425-428`), por lo que la escritura del cache se omite.
- Estado del harness: `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs:781-835` prueba el mapping estructurado a diccionario y su precedencia; `:4114-4150` prueba registro de filas con overrides vacíos. `RuntimeMutatorTestProvider` ya expone `BalanceOverrides` y `EntityRows` (`tests/RogueCommandExpansionLoader.ValidationTests/RuntimeMutatorTestFixtures.cs:244-261`), y `RuntimeMutatorTestData.Row` crea la fila mínima (`:292-304`). El proyecto de validación enlaza `RuntimeContentMutator.cs`, pero **no** `ContentPackRuntimeLoader.cs`; por tanto el test propuesto comprueba mapper → provider de prueba → mutator → cache, no el loader concreto ni una partida real.
- Baseline del harness Debug y Release: `189/190 validation checks passed; 1 skipped`; el único skip es `reparse-point bundle paths are rejected` por privilegios de symlink de Windows. Builds Release previas del plugin y del harness terminaron con cero advertencias y errores.
- Alcance exclusivo: fix de lookup + test de cache + actualización de la evidencia/handoff. No añadir campos, balance values, unidades, perfiles AI, schema, manifiestos, assets, bundles ni llamadas a FAL. La skill de simulación de balance no se aplica porque no se están calibrando stats; AssetBundle authoring y asset pipeline no se aplican porque no se crean recursos visuales.

## Architecture / proposed approach

Guardar una sola vez en un campo `readonly Type` la instancia de `EntityBalancingStore.ChangeableValue` descubierta y validada por el constructor; usar esa misma identidad CLR para `Enum.Parse` y las claves de los caches. Añadir una prueba de integración local en el harness existente que derive `MoveSpeed` con el mapper, lo entregue mediante `IRuntimeContentProvider` y compruebe que `RegisterEntityRows` deja exactamente `12.5f` en el cache flotante; no cambiar ningún mapping ni presentar el fixture como evidencia runtime.

## Step-by-step tasks

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

## Tests / validation

- Comportamiento nuevo cubierto por regresiones observacionales: DTO `movement.moveSpeed` → `CreateCustomBalance` → `IRuntimeContentProvider.GetBalanceOverrides()` del test double → `RuntimeContentMutator.RegisterEntityRows()`/`ApplyBalanceOverrides()` → caches float (`MoveSpeed`) e int (`MaxHealth`) de `EntityBalancingStore`.
- RED se observa con el código actual porque el lookup de enum apunta a `GameBalancingScriptableObject`; GREEN se observa al reutilizar `_changeableValueType`, previamente validado como nested enum de `EntityBalancingStore`.
- Suite Debug y Release: ejecutar directamente cada DLL del `--artifacts-path` recién construido. Baseline antes del primer caso: 189/190 con un skip; tras las regresiones float e int: 191/192 con el mismo skip.
- La validación local con fixtures no equivale a runtime: `ContentPackRuntimeLoader.cs` no está compilado en el harness. La invocación MCP descrita abajo ejercitó el loader real en `MainMenu`, pero no equivale a una llamada natural de fábrica ni a una partida.
- Sin modificación de esquema/manifest/asset: no JSON Schema, fal.ai, conversión de assets ni Unity AssetBundle build.

## Risks, tradeoffs, and open questions

- `GameBalancingScriptableObject` se usa legítimamente para datos de progresión, pero su nombre no autoriza a obtener de ahí el enum de caches. Preservar el `Type` exacto que ya fue validado desde `EntityBalancingStore`.
- El harness local demuestra escritura a un cache fixture con la misma forma CLR y enum. El smoke Steam/MCP descrito abajo verificó las filas y valores en los caches nativos, pero no su consumo para movimiento/combate durante una partida.
- La auditoría previa no probó un mapping nuevo para `turnRate`, `detectionRange`, weapons, armor, aura u otros DTO. No convertir este fix en autorización para mapearlos.
- Las pruebas cubren un campo float (`MoveSpeed`) y uno int (`MaxHealth`); todos los keys pasan por el mismo `enumType`, pero no se añade una matriz exhaustiva de enum members ni se cambian conversions.
- Si el test requiere añadir `ContentPackRuntimeLoader.cs`, reflexión sobre sus privados, más stubs o una seam de producción para arrancar el loader, detenerse: esa expansión no es necesaria para demostrar el bug del mutator y merece un plan aparte.
- El smoke autorizado de Steam/MCP llega hasta la escritura real de caches desde `MainMenu`; no inició una partida, no invocó naturalmente la fábrica y no prueba que la unidad use los stats en movimiento/combate.

## Resultado de ejecución (2026-10-21)

- **RED inicial:** el caso de `MoveSpeed` compiló y falló con el código original porque el valor no aparecía en `EntityBalancingStore.ChangeableFloatValueCache`.
- **GREEN:** se guardó el `ChangeableValue` validado desde `EntityBalancingStore` y se reutilizó en `ApplyBalanceOverrides`; el caso de `MoveSpeed` pasó.
- **Cobertura adicional del cache entero:** se agregó una prueba de `MaxHealth`; una mutación temporal que impedía su branch `Int` produjo el fallo esperado, se restauró el código correcto y la prueba pasó.
- **Validación local final:** Debug y Release del harness: `191/192 validation checks passed; 1 skipped` (symlink, privilegio Windows). Plugin Release y harness Release: 0 advertencias/0 errores.
- **Runtime Steam/BepInEx MCP:** el juego cargó el Release plugin y el manifiesto de prueba desde una instalación temporal reversible. En `MainMenu`, una llamada MCP a `RuntimeHooks.BeforeEntityFactory()` completó la preparación del pack y el registro. La sonda devolvió `ProbeStatus=ok`, `HookBound=True`, `HookInvoked=true`, filas float/int presentes, `MoveSpeedValue=14.5` y `MaxHealthValue=130` para `sample-native:scout-tank`; salida preservada en `artifacts/runtime-smoke/bepinex-mcp-run/runtime-cache-probe-result.json`. Esto es evidencia de runtime de los caches nativos, no de un run, una creación natural de entidad ni del efecto visible en gameplay.
- **Restauración y perfiles:** el proceso del juego y el servidor MCP local se cerraron; los cuatro archivos alterados de instalación se restauraron y verificaron contra sus SHA-256 de baseline. El selector siguió en `5`; los cinco perfiles y sus 18 archivos no cambiaron. No se seleccionó QA ni se modificó ningún tutorial.
- **Mejora lograda:** overrides aplicados por el mutator escriben a los caches tipados flotante/entero con el enum correcto; no se amplió el conjunto de mappings ni se cambió el schema.
- **Límite:** el harness usa un provider fixture; el smoke MCP complementó esa prueba con el `ContentPackRuntimeLoader` real y los caches de `EntityBalancingStore`. El efecto sobre movimiento, salud, spawn, UI o combate sigue pendiente de una partida observada.
