# Heat and Ascension Scaling

Use this guide for balance budgets and validation steps. The native Heat flag alone does not establish the exact scaling formula. Before relying on reflected members or calculating Heat deltas, verify the game version, `Assembly-CSharp.dll` SHA-256, and MVID together, then inspect the current `CardChangeScriptableObject` calculation path.

## Current authoring boundary

- Native card changes expose `multiplyValueWithCurrentHeatLevel` and `multiplyValueWithMetaProgressionMultiplier`. Confirm their behavior against the current game assembly before authoring dynamic values.
- The content-pack relic contract currently requires Heat scaling to be `false`. The loader rejects relic changes that set it to `true` until the per-level balance budget is validated.
- The native meta-progression member is a string key. Omit it when a change must not scale with meta progression.
- `ManageStartCardChanges` uses threshold fields such as `startingOnHeat` and `startingOnAscensionLevel` for stage-start changes. Confirm the exact class and lifecycle before registering new changes there.

## Balance budgets

- Keep each major combat stat change at or below 3% per Heat level.
- Keep total `AttackRange` growth at or below 15%; verify how that author-facing budget maps to the native `WeaponRange` attribute.
- Evaluate combined changes together. Multiplying health, damage, cadence, armor, and range can compound combat power even when each individual change seems small.
- For `Add` operations, compare the absolute delta with every targeted unit's baseline. A single flat delta can be a small percentage for a heavy unit and a large percentage for a light unit.
- Do not assume one target's range, health, or damage baseline represents all units selected by a role filter.

## Validation procedure

1. Record the current game compatibility identity and inspect the native calculation path for the exact Heat and meta scaling behavior.
2. Calculate the effective stat at Heat 0, 1, and the highest supported Heat value for every target entity.
3. Reject changes that exceed the per-level or total range budgets, or that combine to create disproportionate combat power under Lanchester square-law conditions.
4. Run the pack validator and the mutator audit. The audit flags Heat-scaled modifiers for review; it does not emulate the native formula or prove live balance.
5. Verify any enabled scaling in a controlled in-game run before shipping it.

## Native attribute names

Relevant native `ChangeableValue` names include `MaxHealth`, `MaxArmor`, `MoveSpeed`, `Damage1`, `Damage2`, `WeaponRange`, `Attack1Cooldown`, `Attack2Cooldown`, and `ProductionDuration`. The data-only relic contract currently accepts only its `Health` alias for native `MaxHealth` and does not allow dynamic Heat scaling.
