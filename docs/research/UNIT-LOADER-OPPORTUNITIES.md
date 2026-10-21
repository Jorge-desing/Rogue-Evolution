# Auditoría de oportunidades del cargador de unidades

**Fecha:** 2026-10-21
**Resultado de la auditoría de mappings:** **No-Go** para añadir mappings nuevos a subdefiniciones. Un seguimiento posterior encontró un fallo distinto en la escritura de caches ya existente; el diagnóstico y la corrección quedan registrados al final.
**Evidencia:** inspección estática REA, validación local y smoke de runtime Steam mediante BepInEx MCP en `MainMenu`; no se inició una partida ni se verificó el consumo por una unidad en movimiento o combate.

## Identidad verificada

Antes de usar firmas o metadata, se comprobó en la instalación local:

- `version.txt`: `9= Version 1.0.9`.
- `Player.log`: Unity `2021.3.45f2`; `Mono path[0]` apunta a la instalación indicada debajo.
- Instalación: `C:/Program Files (x86)/Steam/steamapps/common/Rogue Command`.
- `Assembly-CSharp.dll` SHA-256: `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`.
- MVID: `db0e9462-128d-4296-9900-de18b4bd0c00`.
- Evidencia REA de artefacto/metadata: `ev_b923e23cfec30be565c58056206d348d2714b1d81204fa26364cbbcce0bd6463`; membresía seleccionada: `ev_214e7b4b37510329ed5bd4200780c140c7404955c0587fde76a57de2c935d5dc` y `ev_051f7b6ad1d784d56a99b0b56e05f61d358f9f87b69ca581d3cc24ec70274e85`.

REA clasificó el ensamblado como PE32/CIL para Unity Mono y reportó metadata completa. La inspección no cargó ni ejecutó el ensamblado. La cobertura de miembros es completa **solo dentro de los tipos seleccionados**, no de todo el juego.

## Pipeline actual del loader

- `ContentUnitDefinition` y sus subdefiniciones están en `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs:110-272`; `ContentPackCopy.CloneUnit` está en `:537-582`, con clones específicos de subdefiniciones a continuación.
- `UnitBalanceOverrideMapper.CreateCustomBalance` en `ContentPackDefinition.cs:275-324` copia primero `customBalance` y solo deriva `movement.moveSpeed` si es positivo y finito, `MoveSpeed` no está explícito en `customBalance` y no existe un override crudo `moveSpeed`.
- `ContentPackRuntimeLoader.GetBalanceOverrides` en `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs:1714-1757` entrega las filas derivadas al mutador. Los `balanceOverrides` crudos se aplican a la fila al construirla; por ejemplo, el loader los aplica antes de registrar la fila (`ContentPackRuntimeLoader.cs:2958` y `:2995`).
- `RuntimeContentMutator.ParamMap` en `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs:17-48` ya incluye los nombres/tipos admitidos por el cache. `ApplyBalanceOverrides` (`:348-432`) resuelve esos nombres a `ChangeableValue` y los escribe en el cache entero o flotante.
- El registro aditivo, validación de IDs y relaciones fábrica/producto, índices, caches y rollback se ejecutan desde `RuntimeContentMutator.RegisterEntityRows` (`RuntimeContentMutator.cs:269-345`) hacia `TryRegisterEntityRows` (`:475`); la suite tiene pruebas para registro, colisiones, rollback y caches (`tests/RogueCommandExpansionLoader.ValidationTests/Program.cs:4114-4266).
- Los validadores de cada subdefinición están en `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`; validación de paquete/schema por sí sola no implica efecto nativo.

Las pruebas locales ya comprueban las keys del `ParamMap`, la precedencia del movimiento estructurado frente a `customBalance` y `balanceOverrides`, y que valores de movimiento no finitos no se mapeen (`tests/RogueCommandExpansionLoader.ValidationTests/Program.cs:759-835`).

## Matriz de candidatos considerados

Un campo nativo existente no prueba que el DTO lo seleccione ni que su semántica coincida. Los candidatos siguientes quedan como **No-Go** porque falta al menos uno de esos eslabones; no se afirma que los miembros nativos carezcan de consumidores.

| Campo estructurado | Candidato nativo / incertidumbre | Decisión y motivo |
|---|---|---|
| `movement.turnRate`, `movement.acceleration` | No se probó una entrada de balance ni lector nativo que reciba estos DTOs. | No-Go: no inferir velocidad angular o aceleración desde nombres del DTO. |
| `detection.detectionRange` | `sightRadius` / `SightRadius` existe, pero no se estableció equivalencia de semántica o conversión de `float` a valor nativo entero. | No-Go: alcance de detección y radio de visión no se consideran intercambiables sin lector demostrado. |
| `weapons.damage` | Existen `damage1`/`damage2`; el DTO no prueba selección de slot. | No-Go: ambiguo entre slots y sin ruta DTO→consumidor trazada. |
| `weapons.range` | `weaponRange` es un candidato por nombre, sin mapping probado para cada elemento `weapons[]`. | No-Go: faltan selección, precedencia y efecto observado. |
| `weapons.fireRate` | `attackCooldown`/`attack2Cooldown` son cooldowns, no una tasa equivalente demostrada. | No-Go: no introducir una conversión/inversa sin contrato. |
| `armor.armorValue`, `armor.shieldValue`, `armor.damageReduction` | `maxArmor` y `armorProtection` son campos nativos distintos; no se demostró una correspondencia para shield o reducción. | No-Go vigente: no inferir equivalencias por parecido de nombre. La validación finita/rangos existente no implica mapping. |
| `healing.healAmount`, `healRange`, `healRate`, `repairAmount` | Hay miembros nativos como `healAmount1/2` y `skillRange`, pero los slots y los conceptos de rango/tasa/reparación no están ligados al DTO. | No-Go: falta el contrato y el lector por campo. |
| `stealth.stealthLevel`, `cloakDuration`, `detectionReduction` | No se probó un miembro/caller nativo que consuma estos valores desde el DTO. | No-Go: sin mapping de balance ni contrato compilado. |
| `experienceReward.experienceAmount`, `experienceType`, `bonusMultiplier` | `neededExperienceLevel` es un nivel requerido, no una recompensa; no se trazó un consumidor de recompensa para este DTO. | No-Go: no confundir nivel requerido con XP otorgada. |
| `aura.auraRadius`, `auraType`, `buffAmount`, `affectsAllies`, `affectsEnemies` | `effectRadius1/2` existe para efectos/slots, pero no se demostró que describa un aura. | No-Go: sin sistema nativo aura↔DTO probado. |
| `resourceGathering.resourceType`, `gatherRate`, `carryCapacity`, `gatherRange` | `gainCreditsAmount` es solo un candidato parcial; no se demostró equivalencia con tasa, capacidad, alcance o tipo. | No-Go: semántica y lector faltantes. |
| `aiBehavior.unitCap`, `combatUnitCap`, `skillActivationCheckInterval`, `aiTag` | No se probó una lectura de esos DTOs por el roster o comportamiento AI nativo. | No-Go: no añadir aliases sin caller y efecto concretos. |
| `command`, `spawn`, `productionQueue`, `resourceCosts`, `statusEffects`, `synergies`, `upgrades`, `experience`, `visualVariants` y otras subdefiniciones | Están declaradas/validadas o cubiertas por caminos propios del loader; no se trazó en esta auditoría un mapping nuevo desde ellas a un consumidor nativo. | No-Go para nuevos efectos en este ciclo; no cambiar su contrato. |

**Evidencia REA complementaria:** `ev_c041b3a37e950e2225a1d7b1fc95844ef2e0103d1c1a8a8476dd18c7a3cd1b63` registra la selección de `EntityBalancingStore`, `EntityBalancingParameters`, `EntityController`, `EntityAttack`, `EntityFlockingMovement`, `ShootProjectile` y `ChangeableValue` (no coincidió `EntityDetection`). Páginas usadas: campos `ev_eaf8219cf1c4a617cde04a327f6ee27adaaaeb73ba90b4a7523ba1be15afc0b7`, field accesses `ev_52ce9140084ee392fe728c7e9f82f8b43774c56d8d9988d030df7ea7648071dc`, call edges `ev_2b0bf8ae4b0fe9e6eb9f0bdfed47e6c0e8067202f223f414aca538a975f7184a`. El pageo de métodos se limitó a las filas solicitadas y no se interpreta como ejecución.

Las auditorías paralelas de miembros/registro aportaron además Evidence IDs `ev_2a918437ee73772fae6720e06526aa25b2fb141590c699a3458157f2ddde9b8a`, `ev_c15d96fdbe0799f2424b5ce0b760f8697c82ccf0663397790c3031b47a5e5429`, `ev_158715c303704543e6c376174aee38fd71f58ba284711b3b8d6566f675851eba`, `ev_92cbc1ce0113226fb5c0ee5ed0d30b95c3128fa4c61937115980a4ccd43ee370`, `ev_e20bb8e9c8af312eee909c596ca2e4ca2213c7deef3d52dc87bd4676c9b5cd6e`, `ev_d6388cdf4432b4275355bea15ddc4f51d624437fdd772fcb40811ed023d16d1b`, `ev_af3dc2fdfcff4304f116d4565ccf2c48cce6928281d967218614cd2b572997d0` y `ev_d48bde024fbea4e3ad397ceab2e2eb793420ec12e1f201b6eb8843f0a205ca40`. Son evidencia estática acotada a la selección registrada por cada operación.

## Decisión y validación

- **Decisión de implementación:** No-Go. El mapping existente `movement.moveSpeed → MoveSpeed` y las rutas genéricas `customBalance`/`balanceOverrides` ya cubren los efectos verificables. No apareció una extensión nueva que justificara código, DTO, schema, fixture o cambio de balance sin especular.
- **Auditoría inicial y seguimiento:** la auditoría no halló una salida incorrecta porque no trazaba un valor no vacío hasta el cache final. El seguimiento reprodujo y corrigió el lookup del enum; añadió pruebas provider→mutator→cache para un float y un int mediante TDD, y luego verificó el camino concreto del loader con BepInEx MCP.
- **Validación local de baseline Debug:** `dotnet build -c Debug --no-incremental --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj` compiló con 0 advertencias y 0 errores. El DLL Debug exacto pasó **189/190**, con un skip: `reparse-point bundle paths are rejected`, por privilegios de symlink de Windows.
- **Cierre local Release de la auditoría inicial:** las builds Release del plugin y del harness terminaron con 0 advertencias y 0 errores. El DLL Release exacto pasó **189/190**, con el mismo skip de symlink; `git diff --cached --check` pasó. No cambiaron schema/manifiestos, así que no se ejecutó una validación JSON adicional en esa fase.
- **TDD del seguimiento:** la auditoría inicial no añadía comportamiento; el fix posterior sí produjo RED en `MoveSpeed`, añadió la cobertura de `MaxHealth` y comprobó ambos caches tras el cambio, según el seguimiento de abajo.
- **Smoke runtime Steam/MCP (2026-10-21):** se desplegaron temporalmente el Release DLL y un manifiesto de prueba para `sample-native:scout-tank` (`MoveSpeed=14.5`, `MaxHealth=130`). Con Rogue Command `1.0.9` / Unity `2021.3.45f2` y la identidad completa revalidada, se invocó por BepInEx MCP el callback `RuntimeHooks.BeforeEntityFactory()` desde `MainMenu`; la primera llamada inició la carga asíncrona y la siguiente completó el registro. La sonda devolvió `ProbeStatus=ok`, ambas filas de cache presentes, `MoveSpeedValue=14.5` y `MaxHealthValue=130` (resultado guardado en `artifacts/runtime-smoke/bepinex-mcp-run/runtime-cache-probe-result.json`). Esto verifica la escritura en los caches nativos dentro del proceso del juego, no el spawn ni el efecto de velocidad/vida durante una partida.
- **Perfiles y rollback:** no se inició un run ni se cambió el selector (permaneció en `5`); los cinco perfiles y sus 18 archivos conservaron sus hashes. Plugin, manifiesto del pack y logs de BepInEx/Unity se restauraron a sus SHA-256 previos al smoke; el proceso del juego y el servidor MCP local quedaron cerrados.
- `pygount` no está disponible; no se instaló. `fal-assets` y la conversión de AssetBundles no se usaron porque no se aprobó ninguna oportunidad visual ni un nuevo asset.

## Seguimiento: defecto en escritura del cache de balance

- **Inspección estática REA:** con la identidad de juego de arriba, el enum `ChangeableValue` de los caches pertenece a `EntityBalancingStore` (Evidence IDs `ev_84b3d3c96544a9abf4da45c12a4809714f93ac1bc09737a07fdd2a9b0757bf06`, `ev_52c24c4076eaeaf60eb68915ace74021416811bef8b69c5da85e1e841c71ffaf`, `ev_e6d7439461311f158a6ad6dc1fdb0704dbc142d58d83341e93590a15fc5cadce`). `RuntimeContentMutator` ya validaba el enum de `EntityBalancingStore` al construir, pero `ApplyBalanceOverrides` buscaba otro nested type desde `GameBalancingScriptableObject`; al fallar el lookup, el catch registraba el problema y omitía los overrides.
- **Fix local:** `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs` conserva el `Type` validado en `_changeableValueType` y lo reutiliza al parsear keys para los caches. No se añadieron mappings, DTO ni stat values.
- **TDD:** la regresión de `movement.moveSpeed → MoveSpeed` falló antes del fix con `Structured MoveSpeed was not written...`; después pasó. Se añadió además una regresión para `MaxHealth` y el cache entero. Una mutación temporal que desviaba `MaxHealth` del branch `Int` produjo el fallo esperado de la aserción; se restauró antes de la validación final.
- **Validación local final:** Debug y Release del harness pasaron **191/192**, con un skip conocido de symlink por privilegios de Windows. Plugin Release y harness Release compilaron con 0 advertencias y 0 errores. El test verifica el provider fixture → mutator → ambos caches, no el `ContentPackRuntimeLoader` concreto, ya que ese source no forma parte del proyecto de tests.
- **Límite de evidencia:** el harness local solo recorre un provider fixture. El smoke MCP ejercitó el `ContentPackRuntimeLoader` real y leyó los caches nativos, pero no disparó una creación natural de entidad ni observó spawn, velocidad de movimiento, HP o combate en una partida. El No-Go de nuevos mappings de subdefiniciones se mantiene.
