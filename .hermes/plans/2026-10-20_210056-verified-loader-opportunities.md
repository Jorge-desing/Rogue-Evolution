# Plan: oportunidades verificadas para el loader de Rogue Command

**Estado:** ejecución local y estática completada; no se desplegó ni inició el juego.

## Objetivo

Retomar la ingeniería inversa desde el checkout vigente, eliminar solo los fragmentos de documentación solicitados, corregir primero cualquier bloqueo del loader y añadir mejoras útiles para unidades, etapas y AI solo cuando el contrato nativo esté probado. Cada cambio de comportamiento seguirá RED→GREEN→REFACTOR y tendrá revisión independiente.

## Estado comprobado

- Checkout limpio en `codex/rogue-unit-balance-20261018`, HEAD `07cd2250a7484bd101b6357c17eba3588391f019` (`feat: extend unit stage and AI modding`). No se reutilizarán los resultados o el estado de otros worktrees sin verificarlos.
- La identidad instalada se comprobó en esta sesión: Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.
- Ya existen el mapeo estructurado `movement.moveSpeed`, los overrides escalares/booleanos de etapas, `startCountRange`, el scoping de pools de blueprint y el módulo `patrol-and-intercept`. No repetirlos como trabajo nuevo.
- `ContentUnitArmorDefinition.armorValue` existe, pero `ContentPackDefinition` solo deriva actualmente `MoveSpeed`; la validación de armadura rechaza valores negativos, pero no comprueba explícitamente NaN/infinito. En el assembly se observaron `maxArmor` y `armorProtection` como campos distintos: no elegir uno por parecido de nombre; trazar su consumidor antes de mapear.
- **Bloqueo de etapa confirmado por inspección estática:** `ContentPackRuntimeLoader` resuelve `OpenReward` y exige que `StageParametersScriptableObject.cardReward` tenga ese tipo. En el assembly fijado, `cardReward` es un tipo valor (`valuetype:1368`), mientras `OpenReward` es una clase distinta con campos `idsToChooseFrom`, `skippable`, `specialistId`, `blueprintPoolId`, `rarity`, `rareProbability` y `ultraRareProbability`; no tiene los campos `poolId/count/weight` que el constructor exige. Las referencias del loader a ese binding aparecen solo en la declaración y la asignación. El fixture de tests reproduce la forma incorrecta como clase, por lo que no detecta el desajuste.
- El supuesto override `cardReward.count/weight` es **No-Go**: ni el tipo nativo ni la ruta de lectura respaldan esa forma. No reintroducirlo. Los pesos escalares existentes (`cardRewardWeight`, etc.) ya son otra cosa y están implementados.
- `sample:reactive-base-defense` solo reconoce hoy `unitId`, `baseIndex`, `cooldownSeconds`, `defenseRadiusCells` y `maxResponders`; su selección de amenazas requiere que el atacante apunte a un edificio AI. No hay opción para proteger Harvesters AI.
- La limpieza solicitada no tiene archivos objetivo en el checkout activo. `MODDING_PLAN.md` y `MODLOG.md` están en el worktree existente de `main`. Retirar solo el texto de restricción pedido y conservar la recon técnica; modificar memorias de agentes.
- Investigación comunitaria de esta pasada: la guía de Steam muestra la ruta TSV nativa; Nexus lista `Unit Stat Buff`, que requiere BepInEx. Eso es precedente de configuración de estadísticas, no prueba del contrato del juego ni de una API de etapas/AI. Fuentes: https://steamcommunity.com/app/1461910/guides/ y https://www.nexusmods.com/roguecommand/mods/1 .
- OpenCode `1.19.3` pudo ejecutarse con `npx`, pero `auth list` devolvió cero credenciales; se usó `delegate_task` para implementar y revisar. `um` y `pygount` siguen ausentes y no se instalaron. Para etapas se siguió `rogue-command-stage-authoring` y la inspección de código/metadata de Rogue Command.

## Plan de ejecución

### 0. Resolver el objetivo documental sin tocar otra rama

- Hacer la limpieza solicitada únicamente en `MODDING_PLAN.md` y `MODLOG.md` del worktree existente de `main`, retirando el texto específico solicitado y sin afirmar que existe permiso o autorización. No editar memorias de Hermes ni de otros agentes.
- Si se mantiene el checkout actual, registrar que no hay cambios documentales que aplicar ahí; no crear copias nuevas de esos archivos ni borrar investigación no relacionada.

### 1. Reparar el binding de etapa que impide inicializar el loader

**Archivos principales:** `ContentPackRuntimeLoader.cs`, `RuntimeMutatorTestFixtures.cs`, `Program.cs`.

1. Crear primero una regresión de forma/reflexión con un `cardReward` de tipo valor y `OpenReward` como clase distinta; observar el fallo esperado con el binding actual.
2. Verificar que los cuatro `FieldInfo`/tipo de recompensa no tienen consumidores. Eliminar únicamente el binding inválido y los campos muertos, o rediseñar la seam si el uso real requiere otro campo nativo verificado.
3. Cambiar el fixture para que no imite la forma incorrecta del juego. No añadir soporte de override de recompensa durante esta corrección.
4. Compilar y ejecutar el test específico; después, ejecutar el harness completo y el build del plugin.

### 2. Investigar y, si procede, activar un campo de armadura

**Archivos candidatos:** `ContentPackDefinition.cs`, `ContentPackValidation.cs`, `content/schema/README.md`, tests.

1. Con la identidad del juego revalidada, seguir por REA/ILSpy los campos `maxArmor` y `armorProtection` hasta sus usos en balance y aplicación de daño. Confirmar si `armor.armorValue` corresponde exactamente a uno de ellos.
2. Si la semántica no queda probada, dejar el campo descriptivo y registrar No-Go; no mapearlo por intuición.
3. Si queda probada, añadir un test que falle antes del cambio y luego mapear el valor estructurado mediante el pipeline de balance ya existente. Preservar precedencia de `customBalance` y `balanceOverrides`; definir explícitamente el significado de cero (DTOs de `JsonUtility` no distinguen cero omitido de cero explícito).
4. Validar NaN/infinito/negativo y agregar documentación del comportamiento observable. No mapear `shieldValue` ni `damageReduction` sin contratos independientes.

### 3. Buscar una mejora de etapas que tenga consumidor nativo inequívoco

**Archivos candidatos:** `ContentPackValidation.cs`, `ContentPackRuntimeLoader.cs`, `StageParameterOverrideParser.cs`, `content/schema/content.schema.json`, `content/schema/README.md`, tests.

1. Comparar todos los campos aún no admitidos de `StageParametersScriptableObject` con `LevelProgression.CreateLevelMap` y sus callers; distinguir campos escalares ya soportados de structs/referencias Unity.
2. Examinar un candidato por vertical slice: campo exacto → tipo → punto de lectura → efecto limitado → test RED.
3. Implementar solo si el significado, clonado y validación de límites son inequívocos. Si no queda un candidato seguro, cerrar la investigación como No-Go, sin inventar `cardReward.count/weight` ni convertir una referencia de asset en un scalar.

### 4. Añadir defensa reactiva opcional de Harvesters AI

**Archivos candidatos:** `ReactiveBaseDefenseParameters.cs`, `ReactiveBaseDefenseAiModule.cs`, `Program.cs`, `ReactiveBaseDefenseRuntimeTestStubs.cs`, `content/schema/README.md`.

1. Usar REA sobre la identidad verificada para localizar el indicador/colección nativa que distingue Harvester AI, ownership y `aiBaseIndex`; no inferirlo del nombre de una unidad.
2. TDD del parser para `protectedTargetType`: default `Buildings` (sin cambio de comportamiento), más `Harvesters` y `BuildingsAndHarvesters`; rechazar vacío, desconocido y duplicados.
3. Añadir el test de comportamiento antes de implementar: los Harvesters objetivo deben estar vivos, ser AI y pertenecer a la base configurada. Mantener sin cambios la elegibilidad de atacantes, radio, cooldown y límite de defensores.
4. Implementar el selector mínimo y documentar que la prueba local no demuestra decisiones AI dentro de una partida.

### 5. Verificación y revisión final

- Cada slice: RED observado por razón esperada → implementación mínima → GREEN → suite completa; no acumular pruebas imaginadas antes de implementar.
- Build Release del plugin y build Release del proyecto de tests. Ejecutar directamente el DLL producido por ese build con la ruta de `artifacts/validation-tests`; no confiar en `dotnet run` con outputs potencialmente obsoletos.
- Validar JSON Schema Draft 2020-12 y los manifiestos afectados; ejecutar `git diff --check` y revisar el diff exacto.
- Revisión independiente con `requesting-code-review`. OpenCode no está instalado; no instalarlo automáticamente. Si sigue ausente al ejecutar, usar un reviewer independiente con `delegate_task` y comprobar el resultado en el checkout padre.
- Mantener los niveles de evidencia separados: inspección estática REA, build/harness local y runtime. Este plan no incluye despliegue ni lanzamiento del juego.
- En la fase de ejecución, aplicar la política vigente de staging/commit/push de `AGENTS.md`, conservando cualquier trabajo ajeno.

## Criterios de aceptación

- La inicialización del loader ya no exige el tipo/clase equivocado para `cardReward` y la regresión está cubierta.
- `armor.armorValue` solo se aplica si la inspección estática prueba su correspondencia nativa; de lo contrario queda explícitamente sin mapear.
- La investigación de etapas termina con una extensión probada o un No-Go documentado; no se añade una forma de recompensa inventada.
- La protección Harvester es opt-in, conserva `Buildings` como default y cubre selección: el default ignora Harvesters y `BuildingsAndHarvesters` responde a ambos tipos.
- Build/tests/revisión pasan salvo skips heredados claramente reportados. No cambió el JSON Schema ni los manifiestos. No se afirma funcionalidad in-game sin evidencia de runtime.

## Estado de ejecución

- La limpieza documental se aplicó localmente en `.worktrees/cargador/` (`main`), retirando solo los fragmentos solicitados de `MODDING_PLAN.md` y `MODLOG.md`. `git grep` ya no encuentra esas menciones en esos dos archivos locales. Ese worktree está desfasado respecto a `origin/main`; los cambios permanecen locales y sin commit/push.
- El bloqueo de inicialización se corrigió: se eliminaron los bindings muertos a `OpenReward`/`cardReward` y se extrajo `StageParameterBindingContract`, compartido entre el loader y el test para probar el shape nativo de tipo valor. RED fue observado antes de añadir el seam; GREEN verificado después.
- REA concluyó No-Go para mapear `armor.armorValue` a `MaxArmor` o `ArmorProtection`: son estadísticas nativas distintas. Se dejó el campo descriptivo y se añadió validación finita para `armorValue`, `shieldValue` y `damageReduction`.
- REA no encontró otro campo de etapa con consumidor y contrato suficientemente probados; no se añadió un override de recompensa.
- La defensa reactiva acepta opcionalmente `protectedTargetType` (`Buildings` por defecto, `Harvesters`, `BuildingsAndHarvesters`). La identificación se basa en ownership AI, `IsUnit`, rol nativo Harvester, vida y base configurada; solo tiene evidencia estática y de harness local.
- TDD para las dos expectativas de selección: ambos tests fallaron bajo mutaciones dirigidas (Harvester incluido por default; Harvester excluido del modo combinado) y pasaron al restaurar la implementación.
- Build Release del plugin y de tests: 0 advertencias/errores. DLL Release exacta: 183/184 pruebas pasaron; 1 omitida por privilegios de symlink de Windows. Revisión independiente: aprobada, sin errores de lógica ni alertas de seguridad. Sin pruebas de runtime in-game; queda pendiente observar la respuesta en una partida aislada.
