# Auditoría de unidades heredadas frente al loader 1.19.0

**Fecha de cierre:** 2026-10-24
**Checkout de la auditoría original:** rama `codex/rogue-unit-balance-20261018`; inventario inicial en `bfcacea6ab3382d101dd71b25ed656375e4c8bd2` y verificaciones de aquella auditoría en `0bfa7ccb283960b745aed06d77f05b5b3ee48620`.
**Alcance original:** el informe inicial solo añadió el análisis; los manifests, schema, código y assets no se editaron entonces. El addendum posterior documenta los cambios de seguimiento, separados de los resultados históricos.

## Resumen ejecutivo

- Los tres manifests declaran 10 productos, 10 fábricas, 5 etapas y 5 perfiles AI: nueve productos nativos en `sample-native-expansion`, un producto custom declarado en `sample-expansion` cuyo bundle está ausente, y ningún producto en `sample-reactive-ai`.
- En las nueve unidades nativas, 23–24 overrides numéricos crudos por unidad corresponden a campos de la tabla canónica de 25 parámetros por fila. `ApplyBalanceOverrides` sigue siendo una ruta activa para esos campos y también para enums/flags no incluidos en ese mapa; su uso no es por sí solo un defecto. Scout, Ironclad y Atlas ya usan `customBalance.Damage1`, sin repetirlo como raw `damage1`. En las nueve, `duration2` está declarado como `SpawnTtl`; ocho fijan el valor en `0` y Skirmish Bike en `120`.
- La migración hipotética de los overrides respaldados por fila a `customBalance` conservó los perfiles observables del simulador para nueve unidades (`parity=PASS`, cero diferencias). **La prueba cubre solo los campos que el simulador modela**: no verifica las 25 claves del mapper ni la equivalencia final del registro nativo. Por ello es un candidato para una migración con tests de paridad por campo, no una autorización para editar manifests ni una prueba de gameplay.
- No hay subdefiniciones estructuradas ni `variants` en las unidades auditadas. En 1.19.0 las variantes AP/HE siguen siendo metadata sin selección ni aplicación. No se debe inferir que los demás DTO producen efectos porque pasen schema y validación.
- `sample-native-expansion` declara nueve overlays de producto, dos overlays de fábrica y siete tarjetas propias de producto; Arachnid y Overlord conservan sus tarjetas nativas. Las fábricas de esos dos productos tienen overlay pero no tarjeta propia. En `sample-expansion`, el producto declara una tarjeta y el factory no; su bundle falta.
- `sample-expansion` declara su bundle y su manifest de AssetBundle, pero **ambos archivos faltan**. Su manifest pasa schema/registry; no pasa la validación de referencias y el pack **no es distribuible**.
- La documentación de presentación mantenía un estado antiguo para Ironclad y los assets visuales; se actualiza para alinearla con el manifest/bundle actual, sin cambiar el contenido del juego.
- Las diferencias de balance que resaltó el simulador se registran como hipótesis de pruebas comparativas. No hubo evidencia nueva de oferta, producción, movimiento, combate ni render en una partida de Steam.

## 1. Qué consume el loader 1.19.0

`UnitBalanceOverrideMapper` contiene los 25 bindings explícitos `ChangeableValue → campo de fila` (`src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs:303-342`). Entre ellos están `Attack1Cooldown → attackCooldown` y `SpawnTtl → duration2`; `ManaRechargePerSecond` se excluye porque no es un campo per-unit. `customBalance` valida y convierte esos nombres canónicos, y `RuntimeContentMutator` escribe sus valores en las filas antes del registro (`ContentPackDefinition.cs:377-441`; `RuntimeContentMutator.cs:240-299, 328-431`; `ContentPackRuntimeLoader.cs:1837-1874`). La precedencia documentada es `customBalance` > raw `balanceOverrides` > `movement.moveSpeed` (`content/schema/README.md:22`).

`balanceOverrides` no se limita al mapa de 25 claves: el loader también aplica directamente `roles`, `offeredSystemTags`, `rarity`, `neededExperienceLevel`, `attackType`, `skillType`, `firePointCount` y otros campos allowlistados (`ContentPackValidation.cs:1483-1532, 2808-2895`; `ContentPackRuntimeLoader.cs:4495-4579`). No conviene migrar esas claves a `customBalance`. La validación y aplicación rechazan nombres raw repetidos, y la validación señala como redundante un campo de fila declarado tanto en raw como en `customBalance` (`ContentPackValidation.cs:1483-1532`; runtime: `ContentPackRuntimeLoader.cs:4508-4517`). No se encontraron duplicados raw ni colisiones raw/custom en estos manifests.

`movement.moveSpeed` es el único campo de una subdefinición con mapping estructurado a fila; las otras subdefiniciones están validadas pero carecen de un mapping por defecto (`ContentPackDefinition.cs:430-440`; `content/schema/README.md:22`). `variants` se valida/copia y cuenta para diagnóstico, pero no selecciona un loadout ni aplica cambios a filas, instancias, AI o VFX (`ContentPackValidation.cs:1998-2039`; `ContentPackRuntimeLoader.cs:1647-1653`; `content/schema/README.md:22`).

## 2. Matriz por producto

**R** enumera exactamente los nombres raw presentes que corresponden a campos respaldados por el mapa de 25 claves. **D** son los nombres raw fuera de ese mapa que el loader trata por su ruta directa. Todas las unidades de esta matriz tienen `structured=ninguna` y `variants=0`.

| Producto | Template | Factory 1:1 | R: `balanceOverrides` respaldados por fila | `customBalance` | D: otros overrides raw |
|---|---|---|---|---|---|
| `sample-native:scout-tank` (`content.json:13-159`) | `PCXBaseTank` | `sample-native:scout-tank-factory` / `PCXBaseTankFactory` | `cost`, `maxHealth`, `moveSpeed`, `sightRadius`, `weaponRange`, `attackCooldown`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `armorProtection`, `damage2`, `attack2Cooldown`, `effectRadius1`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | `Damage1=4.0` | `roles`, `offeredSystemTags`, `rarity`, `neededExperienceLevel`, `attackType`, `skillType`, `firePointCount` |
| `sample-native:heavy-artillery` (`:198-343`) | `ArtilleryTank` | `sample-native:heavy-artillery-factory` / `ArtilleryTankFactory` | `cost`, `maxHealth`, `moveSpeed`, `damage1`, `weaponRange`, `attackCooldown`, `effectRadius1`, `armorProtection`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `sightRadius`, `damage2`, `attack2Cooldown`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | — | Los siete campos D comunes |
| `sample-native:assault-walker` (`:379-519`) | `GatlingWalker` | `sample-native:assault-walker-factory` / `GatlingWalkerFactory` | `cost`, `maxHealth`, `moveSpeed`, `attackCooldown`, `weaponRange`, `armorProtection`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `sightRadius`, `damage2`, `attack2Cooldown`, `effectRadius1`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | `Damage1=2.8` | Los siete campos D comunes |
| `sample-native:atlas-crane-mech` (`:557-701`) | `GatlingWalker` | `sample-native:atlas-crane-mech-factory` / `GatlingWalkerFactory` | Igual que Assault Walker | `Damage1=2.8` | Los siete campos D comunes |
| `sample-native:bastion-siege-mech` (`:737-880`) | `GatlingWalker` | `sample-native:bastion-siege-mech-factory` / `GatlingWalkerFactory` | `cost`, `maxHealth`, `moveSpeed`, `damage1`, `attackCooldown`, `weaponRange`, `armorProtection`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `sightRadius`, `damage2`, `attack2Cooldown`, `effectRadius1`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | — | Los siete campos D comunes |
| `sample-native:arachnid-hexapod` (`:916-1063`) | `GatlingWalker` | `sample-native:arachnid-hexapod-factory` / `GatlingWalkerFactory` | Igual que Bastion | — | Los siete campos D comunes |
| `sample-native:overlord-command-hq` (`:1099-1246`) | `PCXBaseTank` | `sample-native:overlord-command-hq-factory` / `PCXBaseTankFactory` | `cost`, `maxHealth`, `moveSpeed`, `sightRadius`, `weaponRange`, `attackCooldown`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `armorProtection`, `damage1`, `damage2`, `attack2Cooldown`, `effectRadius1`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | — | Los siete campos D comunes |
| `sample-native:skirmish-bike` (`:1282-1430`) | `BombBike` | `sample-native:skirmish-bike-factory` / `BombBikeFactory` | `cost`, `maxHealth`, `moveSpeed`, `damage1`, `effectRadius1`, `productionDuration`, `maxCapacity`, `maxShield`, `maxMana`, `maxArmor`, `armorProtection`, `sightRadius`, `damage2`, `weaponRange`, `attackCooldown`, `attack2Cooldown`, `effectRadius2`, `skillRange`, `healAmount1`, `healAmount2`, `gainCreditsAmount`, `skillManaCost`, `duration1`, `duration2` | — | `roles`, `offeredSystemTags`, `attackType`, `rarity`, `neededExperienceLevel`, `skillType`, `firePointCount` |
| `sample-native:surge-tank` (`:1465-1608`) | `LightningTank` | `sample-native:surge-tank-factory` / `LightningTankFactory` | Igual que Skirmish Bike | — | Los siete campos D comunes |
| `sample:scout-tank` (`sample-expansion/content.json:13-47`) | Prefab custom declarado; bundle ausente | `sample:scout-tank-factory` / `PCXBaseTankFactory` | Ninguno en el producto | — | Producto: `rarity`, `neededExperienceLevel` |

En el factory de `sample:scout-tank`, sus raw overrides son `maxCapacity` (row-backed) y `neededExperienceLevel`, `rarity` (campos directos). El factory no declara `cardImageAssetName`; el pack completo carece de bundle, así que no se puede probar esa presentación.

Hay nueve factory IDs distintos para las nueve unidades nativas. En el custom sample también hay un factory ID propio. El pack reactivo no añade productos; su etapa/perfil referencia Scout y Bike (`sample-reactive-ai/content.json:20-48`).

## 3. Presentación, localización y flags

- Los nueve productos nativos tienen `en-US` y `es-XL`; sus nueve factories también tienen ambos idiomas. Los flags AI son explícitos: ocho productos `true`, Overlord `false`. `isAllowedForDemo` y `isAllowedAsStartingBlueprint` son selectivos; los omitidos conservan el default `false` del DTO.
- Siete productos declaran Sprite propio, repetido en su factory: Scout, Heavy Artillery, Assault Walker, Atlas, Bastion, Bike y Surge. Arachnid y Overlord omiten `cardImageAssetName` en producto y factory, por lo que usan la imagen nativa del template; no es por sí sola una falla. Los nueve productos declaran visual overlay; las factories Arachnid y Overlord también declaran visual overlay, mientras que las otras siete conservan el visual de su factory nativa.
- El proyecto Unity contiene siete tarjetas PNG únicas. El preflight local resolvió 16 referencias de producto más 9 de fábricas (25 en total) y nueve rutas visuales nativas; esto no demuestra presentación en partida.
- El custom product `sample:scout-tank` declara una tarjeta propia, pero su factory no declara tarjeta propia. El loader solo cambia la imagen de factory cuando existe `productionBuilding.cardImageAssetName` (`ContentPackRuntimeLoader.cs:4361-4374`); sin ese campo queda la imagen nativa del factory. Decidir después si se pretendía igualar la tarjeta del producto.
- El custom factory y su producto declaran nombres y descripciones tanto `en-US` como `es-XL` (`sample-expansion/content.json:30-44`); no se observa una brecha de localización en ese ejemplo.

## 4. Hallazgos y mejoras defendibles

### A. Normalizar numéricos raw a `customBalance`: candidato de paridad, no migración recomendada aún

Las nueve unidades nativas contienen 23–24 claves numéricas row-backed crudas cada una. La conversión a nombres canónicos PascalCase puede mejorar tipado/legibilidad, manteniendo en raw roles, tags, rarity, unlock, `attackType`, `skillType` y `firePointCount`. La sonda local convirtió temporalmente esos overrides y observó igualdad de perfiles simulados en las nueve unidades.

**Límite importante:** `SIMULATED_CUSTOM_BALANCE_FIELDS` solo modela una fracción de las 25 claves, así que `parity=PASS` prueba únicamente los campos que entran en el simulador, no los demás bindings ni la igualdad final de todas las filas. Antes de migrar, escribir una regresión C# por binding/raw field, comprobar la fila final tras el mutator y verificar que cada valor conserva tipo y precisión. No mover campos por nombre parecido; `duration2` corresponde a `SpawnTtl`, y `ManaRechargePerSecond` no es per-unit.

### B. Revisar paridad de combate mediante casos concretos

El simulador local produce hipótesis comparables, no decisiones de balance:

| Caso simulado | Resultado observado | Pregunta para una fase de balance |
|---|---|---|
| Scout vs `PCXBaseTank` | CEI 36.9; Scout gana en 26.35 s con 3.1% HP | `weaponRange=3.5` queda bajo la banda de scout 5–7 pese a `sightRadius=8`; ¿es una vulnerabilidad intencional? |
| Colossus vs `ArtilleryTank` | CEI 16.0; en las 10 ejecuciones locales observadas gana en 4.3–4.8 s con 73.1% HP | Si se busca paridad con el template, validar con inversión equivalente y condiciones de artillería. |
| Ironclad/Atlas vs `GatlingWalker` | Ambos usan la misma fila; Ironclad gana en 32.15 s con 30% HP; en tres ejecuciones de 100 trials de 5 vs 4, Side A obtuvo 100% wins | Revisar si la misma fila debe sustentar dos papeles con tags distintos; el resultado de squad es aproximado. |
| Bastion vs `GatlingWalker` | Bastion pierde en 45.05 s; Gatling queda con 10% HP | Resultado cercano a una derrota; considerar junto al rol/producción antes de cualquier ajuste. |
| Surge vs `GatlingWalker` | Señal de armor lockout (`armorProtection=2`, daño Gatling=2) y kiting en el modelo | Confirmar si es counterplay buscado; el resultado depende de simplificaciones de rango/velocidad. |
| Skirmish Bike vs `BombBike` | Empate en 0.15 s; CEI 83.1 | El CEI alto coincide con la banda de detonador/suicide bike 75–95; no indica automáticamente un problema. |

La simulación no reproduce por completo la entrega secundaria, targeting, orden geométrico, colisiones, movimiento real ni decisiones AI (`rogue-command-balance-simulation/SKILL.md:30-31`; referencia Lanchester: secciones 2 y 4). No cambiar stats a partir de esta tabla sin una hipótesis explícita y una prueba apropiada.

### C. Sincronizar documentación de presentación

En la auditoría original, `content/schema/README.md:9` describía Ironclad como usuario del visual nativo y como un prototipo estático; el manifest actual declara su overlay `RoboMarine_Mesh` (`sample-native-expansion/content.json:552-553`), y el prefab serializa referencias a Animator, Avatar y controller. El builder de authoring valida el rig fuente del FBX y los clips de animación requeridos. `unity/RogueCommandAssetAuthoring/README.md:16,28` también contenía conteos anteriores. Estos datos estáticos/locales superseden la decisión histórica de omitir el overlay, pero no prueban compatibilidad de animación ni render en runtime.

### D. Completar `sample-expansion` solo con assets completos

`sample-expansion/content.json:4-9` declara `bundles/assetbundle-manifest.bundle` y `bundles/sample-expansion.bundle`; la carpeta solo contiene el manifest JSON. La validación de bundle references termina con exit 2 por el bundle ausente, aunque schema y registry pasen. El pack queda fuera del ZIP hasta que exista un bundle Unity real con su manifest y se validen assets/tipos; no crear placeholders ni tratarlo como contenido instalable.

### E. Alinear la tarjeta custom del factory, si se desea

La factory de `sample:scout-tank` no declara `cardImageAssetName`, por lo que conserva la imagen nativa, aunque el producto tiene una tarjeta propia. Si se quiere que coincidan, añadir la referencia solo tras disponer del bundle real; actualmente el bundle ausente impide validarla.

## 5. No-Go: no inventar mappings ni capacidad AP/HE

- Los DTO `weapons`, `armor`, `healing`, `stealth`, `aura`, `resourceGathering`, `aiBehavior`, `detection`, `experienceReward`, `spawn`, `resourceCosts`, `statusEffects`, `synergies`, `upgrades`, `experience` y `visualVariants` pueden validar sin que exista un consumidor runtime general. No recomendarlos como cambios de gameplay sin trazar un caller/mapping específico. En el seguimiento se corrigió la resolución de targets upgrade/synergy contra unidades nativas y cross-pack registradas; los análisis iterativos de ciclo/profundidad siguen limitados a las unidades declaradas en un mismo pack, y estos subcampos siguen sin consumidor runtime general.
- `unitType=Ground` de Arachnid/Overlord es aceptado por DTO/validador, pero no se halló una lectura en la ruta runtime de unidades auditada. No inferir efecto.
- `variants` no está declarado por estas unidades auditadas. Cuando aparezca, el cargador lo registra en `RuntimeUnitVariantRegistry` y `diagnostics_order_apply_unit_loadout` puede seleccionar el ID en una instancia viva; los overrides soportados se convierten a modificadores relativos y los campos row-only no soportados fallan cerrados. Esto sigue sin demostrar selección natural de cartas, decisión AI, visuales o VFX.
- `MaxRank` es parte del mapa de 25, pero no aparece en los overrides actuales.

## 6. Validaciones y evidencia del informe original

> Esta matriz conserva los resultados de la auditoría inicial en su checkout/fecha de cierre. La verificación posterior y los fixes implementados están anotados en el addendum al final; no mezclar sus contadores.

| Check local/static | Resultado observado |
|---|---|
| `python -B -m unittest discover -s tests -v` | 47/47 |
| JSON Schema Draft 2020-12 | 3 manifests, 0 errores |
| `tools/validate_loader_registry.py --json` | 3 manifests, 0 issues, registry válido |
| Referencias AssetBundle `sample-native-expansion` | 16 referencias, 0 errores |
| Referencias AssetBundle `sample-reactive-ai` | 0 referencias, 0 errores |
| Referencias AssetBundle `sample-expansion` | Exit 2: bundle declarado ausente |
| Raíces visuales nativas | 9 rutas, 0 errores; identidad de versión/SHA/MVID aceptada por el validator |
| Simulador `--all-mod-units` | Cargó 655 perfiles de balance, 9 de ellos del pack nativo; exit 0 |
| Conversión hipotética raw→custom en el simulador | 9 unidades; igualdad de perfiles **solo para los campos modelados** |
| Build Release plugin + harness | 0 advertencias, 0 errores |
| Harness Release DLL recién compilado | 263/264; un skip de symlink por privilegios de Windows |

**Límite de evidencia:** no se inició Rogue Command ni se observó una partida. Estos datos acreditan inspección estática, builds, tests y validación local; no acreditan oferta, producción, movimiento, combate o renderizado de una unidad heredada.

### Verificación y correcciones de seguimiento (2026-10-24)

Esta verificación se ejecutó sobre el checkout actual y no reemplaza ni modifica la matriz histórica de arriba.

| Check local/static | Resultado actual |
|---|---|
| Python `unittest discover -s tests -v` | 52/52 OK |
| C# harness recién compilado | 276/277; 1 skip por privilegio de symlink en Windows; regresiones nuevas de targets cross-pack y overrides duplicados pasaron |
| Release build del plugin | 0 advertencias, 0 errores |
| Registry validator | 3 manifests válidos, 0 issues |
| Referencias de AssetBundle `sample-native-expansion` | 25 referencias (producto/fábrica), 0 errores |
| Raíces visuales nativas | 9 rutas, 0 errores con identidad 1.0.9 / Unity 2021.3.45f2 / SHA-256 / MVID verificada |
| Bundle serializado inspeccionado | 7 Sprites; prefabs referenciados hallados. No fue reconstruido en Unity en esta verificación |

- Se añadió validación para rechazar que una unidad declare uno de los cinco campos estructurados y el mismo campo en `balanceOverrides`; la regresión falló antes de implementar la regla y pasó después. La prueba conserva y permite los otros overrides existentes, incluidos enum/flags.
- Los targets de upgrades y synergies ahora se validan como unidades y pueden resolver contra unidades nativas o IDs cross-pack únicos del snapshot; se rechazan IDs de fábrica. Las pruebas de regresión cubren targets nativos, cross-pack y factory. El análisis iterativo de ciclos/profundidad continúa siendo local al pack.
- Se ajustó el límite JSON de `cardModelScalingFactor` para que Schema y validación C# coincidan después del redondeo binary32. Un test Python cubre los puntos de redondeo inferior/superior.
- `validate_content_bundle_references.py` ahora comprueba también card/visual assets de cada `productionBuilding`, usando su propio `assetBundleId`; dos pruebas cubren referencias de fábrica y producto.
- Se sincronizó la documentación del roster/visuals con el manifest y bundle existentes, y el handoff histórico se marcó como supersedido. El bundle `sample-expansion` sigue ausente y no es distribuible.
- Se verificó estáticamente la identidad completa del juego y los tipos de los campos estructurados, pero su semántica de gameplay y máximos no están demostrados. No se cambiaron stats ni manifests. No hubo prueba en Steam de registro, producción, combate, animación o render.

## 7. Pendientes

1. No migrar overrides raw a `customBalance` hasta cubrir paridad para las 25 claves en la fila native resultante; la igualdad del simulador solo aplica a sus campos modelados.
2. Tratar los matchups de Colossus, Ironclad/Atlas, Bastion y Surge como hipótesis; no ajustar balance sin objetivo de diseño y pruebas comparativas adecuadas.
3. No distribuir `sample-expansion` hasta authorar su bundle real y validar todas sus referencias.
4. Si se solicita aceptación de gameplay, planearla separadamente con el procedimiento y seguridad de perfiles descritos en `AGENTS.md` y el runbook Steam.
