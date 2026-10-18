# Native CardChange & Relic Architecture in Rogue Command

This document details the reverse-engineered schema and execution contracts for roguelike mutators, relics, and unit stat modifiers in Rogue Command 1.0.9 (`Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`).

---

## 1. Architectural Philosophy: Native ScriptableObjects vs Mod Injections

Rogue Command incorporates a complete, data-driven card modification and relic pipeline built on Unity `ScriptableObject` assets:
- **Zero-Harmony Requirement:** Modded mutators and relic effects that modify unit attributes, production economies, or combat parameters can be defined additively through `CardChangeScriptableObject` and registered into `EntityBalancingStore` or `RelicBalancingStore`.
- **Pre-computed Stat Caching:** When a run begins or stage initializes, `ManageStartCardChanges` iterates over active card changes and applies them directly into `EntityBalancingStore`, guaranteeing $O(1)$ lookups during combat without per-frame calculation overhead.

---

## 2. CardChangeScriptableObject Schema

The native mutator asset `CardChangeScriptableObject` (token `0x020000bd`) contains the following serialized fields:

| Field | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `valueToChange` | `ChangeableValue` | `Health` | Target unit/building attribute to modify. |
| `operation` | `Operation` | `Add` | Mathematical operation (`Add` = 0, `Multiply` = 1). |
| `value` | `float` | `0.0f` | Magnitude of the modifier. |
| `multiplyValueWithCurrentHeatLevel` | `bool` | `false` | When true, scales `value * heatLevel` dynamically. |
| `multiplyValueWithMetaProgressionMultiplier` | `bool` | `false` | When true, scales with unlocked meta-progression tiers. |
| `side` | `Side` | `False` | Target faction: `False` (Player), `True` (Enemy/AI), `Irrelevant` (Global). |
| `cardMustHaveAllOfTheseRoles` | `UnitRole[]` | `[]` | Conjunction filter: unit must possess all listed roles. |
| `cardMustHaveOneOfTheseRoles` | `UnitRole[]` | `[]` | Disjunction filter: unit must possess at least one listed role. |
| `cardMustNotHaveOneOfTheseRoles` | `UnitRole[]` | `[]` | Exclusion filter: units with any of these roles are bypassed. |
| `cardMustNotHaveOneOfTheseTechs` | `Tech[]` | `[]` | Exclusion filter: units with these tech dependencies are bypassed. |
| `onlyForTheseEntityIds` | `string[]` | `[]` | Explicit whitelist of unit IDs (e.g. `["rc_tank", "rc_mech"]`). |
| `entityIdsNotAllowed` | `string[]` | `[]` | Explicit blacklist of unit IDs. |

### Canonical `ChangeableValue` Enumeration (26 Values)

Enum `EntityBalancingStore.ChangeableValue` (token `0x020000bc`) defines all mutable parameters:

```csharp
public enum ChangeableValue
{
    None = 0,
    Health = 1,
    AttackRange = 2,
    MinAttackRange = 3,
    MovementSpeed = 4,
    AttackDamage = 5,
    DamagePerSecond = 6,
    TimeBetweenAttacks = 7,
    ProductionTime = 8,
    VisionRange = 9,
    ProductionCostMoney = 10,
    CoinsReceivedOnDestroy = 11,
    BonusDamageAgainstShields = 12,
    ExtraHarvestAmount = 13,
    TimeBetweenDamageDealt = 14,
    SpawnDamage = 15,
    UnitCount = 16,
    SpawnHeal = 17,
    ExplosionRadius = 18,
    Accuracy = 19,
    Shield = 20,
    ShieldRegen = 21,
    ShieldRegenDelay = 22,
    MaxCapacity = 23,
    TransferRate = 24,
    Armor = 25
}
```

### `Operation` & `Side` Enumerations

- **`Operation` (token `0x020000ba`):**
  - `Add` ($0$): Sums `value` to the baseline attribute ($Stat_{final} = Stat_{base} + V$).
  - `Multiply` ($1$): Scales baseline attribute ($Stat_{final} = Stat_{base} \times V$). A 20% buff is authored as `1.20f`; a 15% debuff is authored as `0.85f`.
- **`Side` (token `0x020000bb`):**
  - `False` ($0$): Applies exclusively to Player units and buildings.
  - `True` ($1$): Applies exclusively to Enemy / AI units and buildings.
  - `Irrelevant` ($2$): Applies symmetrically to all entities on the map.

---

## 3. ManageStartCardChanges Structure

`ManageStartCardChanges` controls difficulty profiles, ascension penalties, and heat level mutators:

```csharp
public class ManageStartCardChanges
{
    public CardChangeType cardChangeType;             // Ascension (0), Heat (1), Difficulty (2)
    public int startingOnAscensionLevel;              // Minimum Ascension level to activate (e.g. 1..10)
    public int startingOnHeat;                        // Minimum Heat level to activate (e.g. 1..20)
    public Difficulty difficulty;                     // Engaged (0), Relaxed (1), Meditative (2)
    public string customOriginGlobalLocaId;           // Localization string token for UI display
    public List<CardChangeScriptableObject> cardChanges; // Collection of atomic stat changes
}
```

---

## 4. RelicBalancingStore & RelicScriptableObject

Relics provide persistent run-long passives, granting one or more `CardChangeScriptableObject` modifications alongside custom trigger listeners:

### Relic Registration API
`RelicBalancingStore` (token `0x020000bf`) mirrors `EntityBalancingStore`:

```csharp
public static void SetScriptableObject(string relicId, RelicScriptableObject relicSO);
public static string[] RelicIds();
public static string[] AllActiveRelicIds();
public static RelicScriptableObject ScriptableObject(string relicId);
public static string ImageLocation(string relicId);
public static int CoinsAmount(string relicId);
public static RelicTech RelicTech(string relicId);
public static Rarity Rarity(string relicId);
public static int NeededExperienceLevel(string relicId);
public static bool IsAllowedForDemo(string relicId);
public static bool IsInactive(string relicId);
public static bool IsForSpecialists(string relicId);
```

### Relic JSON Manifest Structure for Mod Packs

In addition to compiled AssetBundle ScriptableObjects, mod expansion packs can declare data-driven mutators and relics in `mutators.json`:

```json
{
  "mutators": [
    {
      "id": "mut_overcharged_lasers",
      "displayName": "Overcharged Capacitors",
      "description": "Increases energy damage by 25%, but increases production cost by 15%.",
      "side": "Player",
      "rarity": "Rare",
      "heatRequirement": 0,
      "ascensionRequirement": 0,
      "changes": [
        {
          "valueToChange": "AttackDamage",
          "operation": "Multiply",
          "value": 1.25,
          "cardMustHaveOneOfTheseRoles": ["Sniper", "Supporter"]
        },
        {
          "valueToChange": "ProductionCostMoney",
          "operation": "Multiply",
          "value": 1.15,
          "cardMustHaveOneOfTheseRoles": ["Sniper", "Supporter"]
        }
      ]
    }
  ]
}
```
