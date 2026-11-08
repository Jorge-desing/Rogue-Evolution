# Auditoría REA: mappings nuevos de `ChangeableValue` (2026-11-08)

**Resultado: sin candidatos verificables.** No se añaden mapeos nuevos; no cambian schema, C# ni validación. Este plan cierra en el gate del paso 2.

**Evidencia:** inspección estática REA sobre `Assembly-CSharp.dll` verificado en instalación local.

## Identidad verificada

| Capa | Valor observado | Verificado con |
|---|---|---|
| Versión del juego | `9= Version 1.0.9` | `version.txt` |
| Unity | `2021.3.45f2 (88f88f591b2e)` | `Player.log` |
| SHA-256 `Assembly-CSharp.dll` | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` | `sha256sum` + REA `inspect_managed_artifact` |
| MVID | `db0e9462-128d-4296-9900-de18b4bd0c00` | `dnfile` + REA |

REA evidencias (deterministas):
- Artefacto: `ev_11c78815073f2f14f265757e353c82be01ebd3916e13d051a6ccd709019446af` (padre `ev_60923f6387af179a422c15a78ce2f428f13b0c9ad03101c76dfdf44f12df84fe`).
- Miembros: `ev_0741060f2e0026e94206ea97c923b9021fab1ac20f25d0f70cf9941f26d736e3` (padre `ev_7e50c973ca7fc5dcd0c3e28fa7c8f72a21a347a8b9daa98c23c91439728a4c8b`).
- Vistas: tipos `ev_60f8bb8c6e296244ff97cbc8b43df0c35cd97d116d713afb313c00f363cfd62e`, métodos `ev_638eec0f9b42cc6bead90ac912c61ca560f0feff7c7fc9a3a3807af8945fee27`, campos `ev_00dd6a273d8ed4f1eff0d4a1f0710954378e6543838fe948c916f30c63cc5a33`, call_edges `ev_685248cf7a20f80f59e688ecd219b06572000688b52a6a97d161d726e2f93275`, field_accesses `ev_08533ba9979c57512435804947fe821ec39db9c5ab2aca9e2afd1edf9a794e81`.

## Hallazgos

### 1. `EntityBalancingStore.ChangeableValue` (26 miembros) — completo

Enum anidado `EntityBalancingStore` (`0x02000475`, contenedor `0x02000073`), 27 campos con `value__` (26 miembros):

`Cost`, `ProductionDuration`, `MaxCapacity`, `MaxHealth`, `MaxShield`, `MaxMana`, `MaxRank`, `MoveSpeed`, `SightRadius`, `Damage1`, `Damage2`, `WeaponRange`, `Attack1Cooldown`, `Attack2Cooldown`, `EffectRadius1`, `EffectRadius2`, `SkillRange`, `HealAmount1`, `HealAmount2`, `GainCreditsAmount`, `SkillManaCost`, `Duration1`, `SpawnTtl`, `ManaRechargePerSecond`, `MaxArmor`, `ArmorProtection`.

El loader ya mapea los 25 per-unit en `UnitBalanceOverrideMapper.BalanceParameters` (`ContentPackDefinition.cs`); el 26º (`ManaRechargePerSecond`) es global y su getter lee `GameBalancingStore`. **No queda ningún miembro per-unit sin mapear.**

Lectores confirmados en `EntityBalancingStore`: `GetOriginalValueAsFloat` (`0x060002f0`), `GetOriginalIntValue` (`0x060002f1`), `GetOriginalFloatValue` (`0x060002f2`).

### 2. `EntityController.ChangeableValue` (19 miembros) — No-Go

Enum anidado `EntityController` (`0x020004e6`, contenedor `0x020000FF` = `EntityController`), 20 campos con `value__`: `CostOfProduct`, `ProductionDurationOfProduct`, `MaxHealth`, `MoveSpeed`, `SightRadius`, `Damage`, `WeaponRange`, `AttackCooldown`, `EffectRadius`, `SkillRange`, `HealAmount`, `GainCreditsAmount`, `MaxShield`, `MaxMana`, `SkillManaCost`, `Duration`, `MaxRank`, `MaxArmor`, `ArmorProtection`.

Consumidores: `ScaleByChangeableValue` (MonoBehaviour que escala objetos en `Update`) y `ChangeableFillAmountStruct` / `SomeOriginalEntityControllerValues`. Ninguno lee de `EntityBalancingParameters` vía `GetOriginal*`; no existe cadena manifest → fila base → consumidor para el loader. Mapearlo equivaldría a inventar una equivalencia semántica por nombre (p. ej. `Damage` genérico vs `Damage1/2`). **No-Go.**

### 3. Campos de fila no mapeables sin inventar nombres

`EntityBalancingParameters` (48 campos) confirma los 25 campos de balance del loader. Campos estructurales extra (`combatValue`, `coinsAmount`, `firePointCount`, `neededExperienceLevel`, `gridSize`, `cardModelScalingFactor`, booleanos `isAllowed*`, etc.) **no** son miembros de `EntityBalancingStore.ChangeableValue`. Añadirlos a `customBalance` exigiría crear claves que no existen en el enum y no tienen lector `GetOriginal*`; viola el gate y la regla No-Go. `firePointCount`/`gridSize`/`cardModelScalingFactor` ya se manejan como campos estructurados propios, no como overrides de balance.

## Veredicto

| Candidato | Lector | Campo de fila | Per-unit | Veredicto |
|---|---|---|---|---|
| `ManaRechargePerSecond` | `GetOriginal*` (global) | — | No (`GameBalancingStore`) | No-Go (global) |
| `EntityController.ChangeableValue` (19) | No hay lector `GetOriginal*` a fila | — | N/A | No-Go (sin cadena) |
| `combatValue`, `coinsAmount` | — (no son `ChangeableValue`) | Sí (i4) | Sí | No-Go (fuera del enum) |

Sin acción sobre schema, C# ni validación. La cobertura actual 25/26 es completa para el pipeline `customBalance` → fila base.
