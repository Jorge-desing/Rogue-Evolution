# Verified native blueprint pools by stage template

Use this when assigning `blueprintPoolEntries` in a content pack. A listed `poolId` must exist in the native stage template; otherwise runtime registration disables the entire pack.

Verified on Rogue Command 1.0.9 (Assembly-CSharp.dll SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`):

| Native template | Available pool IDs |
|---|---|
| `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` | `BP_CheaperVehicles`, `BP_CheaperRobos`, `BP_CheaperMechs` |
| `StageParameters_Pools_Level3` | `BP_CheaperMechs` |

Guidance:

- Do not assume a template contains `BP_CheaperVehicles`, `BP_CheaperTanks`, or `BP_CheaperRobos`; only the verified templates above expose them. `StageParameters_Pools_Level3` exposes only `BP_CheaperMechs`.
- If a product has no matching pool in a stage template, omit that pool entry. The product may still remain in the broad `stage.unitIds` set, but explicit pool scoping cannot add it to a pool the native template does not contain.
- Recheck these IDs after any game update or change to the pinned compatibility identity.
