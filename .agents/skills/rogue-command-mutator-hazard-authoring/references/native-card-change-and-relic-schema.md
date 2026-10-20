# Native Card Changes and Relics

This reference is tied to Rogue Command 1.0.9 and the compatibility identity used for this implementation: `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Recheck the game version, DLL hash, and MVID together before relying on reflected members or metadata tokens.

## Native card changes

`CardChangeScriptableObject` is the game's data-driven modifier object. Its `valueToChange` field uses the native `ChangeableValue` enum. The current 1.0.9 enum contains these 26 members:

```text
Cost, ProductionDuration, MaxCapacity, MaxHealth, MaxShield, MaxMana, MaxRank,
MoveSpeed, SightRadius, Damage1, Damage2, WeaponRange, Attack1Cooldown,
Attack2Cooldown, EffectRadius1, EffectRadius2, SkillRange, HealAmount1,
HealAmount2, GainCreditsAmount, SkillManaCost, Duration1, SpawnTtl,
ManaRechargePerSecond, MaxArmor, ArmorProtection
```

The relevant serialized fields are `valueToChange`, `operation`, `value`, `multiplyValueWithCurrentHeatLevel`, `multiplyValueWithMetaProgressionMultiplier`, `side`, `onlyForTheseEntityIds`, and `entityIdsNotAllowed`. The meta progression member is a `string` key, not a boolean. `operation` is `Add` or `Multiply`; a 10% increase uses `Multiply` with `1.10`. `side` is an enum: `False` targets player entities, `True` targets enemy entities, and `Irrelevant` applies without a faction restriction. The ID lists are native `List<string>` fields.

The pack schema currently exposes only `valueToChange: "Health"`, an author-friendly alias that the loader maps to native `MaxHealth`. Other native enum members are not yet accepted by the data-only relic contract. Every pack card change must explicitly whitelist namespaced IDs in `onlyForTheseEntityIds`.

Pack-authored relic changes must set `multiplyValueWithCurrentHeatLevel` to `false`. The loader rejects Heat-scaled relic changes until their per-level balance limits can be validated. To avoid meta scaling, omit `multiplyValueWithMetaProgressionMultiplier`; if present, its native contract is a non-empty string key.

## Native relic registration

`RelicScriptableObject` holds its `cardChanges` list. The game's `RelicBalancingParameters` row supplies the ID, ScriptableObject, image location, coin amount, tech, rarity, offered and needed system tags, required experience level, inactive flag, demo eligibility, and specialist flag.

`RelicBalancingStore.SetScriptableObject` is a static initializer that accepts one `RelicBalancingScriptableObject`; it is not an `(id, relic)` registration overload. After the store has initialized, the runtime loader appends a pack relic to its native parameter list and updates the matching ID-index and image cache. Modded images are cached as bundle-loaded Sprites because the native Resources lookup cannot resolve an AssetBundle path.

For content packs, declare relics in `content.json` under `relics`, with `localizations` and `cardChanges`. `en-US` localization is required and other supported language IDs are optional. The JSON schema and `ContentPackValidation` are the authoring contract; there is no separate `mutators.json` contract in this loader.

## Evidence and implementation boundary

The enum names, serialized field types, relic store shape, and registration signature above were checked against the stated 1.0.9 compatibility identity. The runtime loader uses the native relic balance store and `CardChangeScriptableObject` pipeline; simple stat modifiers do not need a Harmony patch. Local pack validation and a successful AssetBundle import prove structural compatibility only. They do not prove that a relic is offered or applied in a live run.
