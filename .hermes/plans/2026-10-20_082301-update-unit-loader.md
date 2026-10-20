# Plan: Update Unit Loader & Expand Capabilities

## Goal
Update the mod version and extend the unit loader to support new unit types, additional balance parameters, and optimized AssetBundle handling.

## Current Context / Assumptions
- **Project Root:** `C:\Users\Alex\Documents\Mod Rogue Command`
- **Active Branch:** `codex/rogue-unit-balance-20261018`
- **Target DLL:** `Assembly-CSharp.dll` (managed)
- **Existing Framework:** Uses a JSON schema for content packs, a validator for pre-flight checks, and a `RuntimeContentMutator` to apply changes to the game's memory.
- **Assumption:** "New unit types" refers to the ability to categorize units (e.g., Ground, Air, Support) within the content packs to allow for type-specific balance overrides or logic.

## Architecture / Proposed Approach
1. **Schema-Driven Extension:** Modify the JSON schema to include `unitType` and a generic `customBalanceParameters` map.
2. **Validator Synchronization:** Update the `ContentPackValidation` logic to ensure new types and parameters are valid.
3. **Dynamic Parameter Mapping:** Implement a mapping system in `RuntimeContentMutator` that translates schema-defined parameter names into the corresponding `EntityBalancingStore` value calls.
4. **Bundle Resilience:** Enhance `ContentPackRuntimeLoader` to handle missing bundles gracefully with descriptive logging and optional fallback mechanisms.

## Step-by-Step Tasks

### Phase 1: Versioning & Schema
- [ ] **Task 1.1: Bump Mod Version**
  - File: `src\RogueCommandExpansionLoader\RogueCommandExpansionLoader.csproj`
  - Action: Increment `<Version>` (e.g., `1.0.9` $\rightarrow$ `1.1.0`).
  - Verification: `grep "Version" src\RogueCommandExpansionLoader\RogueCommandExpansionLoader.csproj`

- [ ] **Task 1.2: Extend JSON Schema for Unit Types**
  - File: `content\schema\content.json` (or `content.schema.json`)
  - Action: Add `"unitType": { "type": "string", "enum": ["Ground", "Air", "Support", "Stationary"] }` to the unit definition.
  - Verification: `dotnet run .` (should pass for existing packs, but fail if we add a malformed type).

- [ ] **Task 1.3: Add Custom Balance Parameters to Schema**
  - File: `content\schema\content.json`
  - Action: Add `"customBalance": { "type": "object", "additionalProperties": { "type": "number" } }` to unit definitions.
  - Verification: Validate a sample JSON with these fields.

### Phase 2: Validator Updates (TDD)
- [ ] **Task 2.1: Test New Unit Type Validation**
  - File: `tests\RogueCommandExpansionLoader.ValidationTests\Program.cs`
  - Action: Add a test case that provides an invalid `unitType` and asserts that `ContentPackValidation` returns a failure.
  - Command: `dotnet run -- "C:\Users\Alex\Documents\Mod Rogue Command"`
  - Expected: `FAIL unitType validation rejects unsupported types`

- [ ] **Task 2.2: Implement Unit Type Validation**
  - File: `src\RogueCommandExpansionLoader\Content\ContentPackValidation.cs`
  - Action: Add logic to check if `unitType` is within the allowed enum.
  - Verification: `dotnet run -- "C:\Users\Alex\Documents\Mod Rogue Command"` $\rightarrow$ `PASS unitType validation rejects unsupported types`

### Phase 3: Balance Parameter Integration (TDD)
- [ ] **Task 3.1: Define Parameter Mapping in Mutator**
  - File: `src\RogueCommandExpansionLoader\Runtime\RuntimeContentMutator.cs`
  - Action: Create a `private static readonly Dictionary<string, ParameterType> ParamMap` where `ParameterType` is an enum (`Int`, `Float`).
  - Code:
    ```csharp
    private enum ParameterType { Int, Float }
    private static readonly Dictionary<string, ParameterType> ParamMap = new Dictionary<string, ParameterType> {
        { "AttackSpeed", ParameterType.Float },
        { "ArmorValue", ParameterType.Int },
        // Add other identified parameters from REA
    };
    ```

- [ ] **Task 3.2: Implement Dynamic Value Application**
  - File: `src\RogueCommandExpansionLoader\Runtime\RuntimeContentMutator.cs`
  - Action: In the loop where balance overrides are applied, check `ParamMap`. If the key exists, call `EntityBalancingStore.GetIntValue` or `GetFloatValue` (via reflection or existing helper methods) and update the value.
  - Verification: Add a test in `RuntimeMutatorTestFixtures.cs` that mocks a custom balance parameter and verifies the value is written to the store.

### Phase 4: AssetBundle Enhancements
- [ ] **Task 4.1: Implement Bundle Existence Check**
  - File: `src\RogueCommandExpansionLoader\Content\ContentPackRuntimeLoader.cs`
  - Action: Before calling `AssetBundle.LoadFromFile`, verify the file exists on disk using `File.Exists`. If missing, log a `Warning` instead of allowing the engine to throw a generic error.
  - Verification: Create a content pack with a missing bundle file and verify the log output.

- [ ] **Task 4.2: Implement Bundle Dependency Resolver**
  - File: `src\RogueCommandExpansionLoader\Content\ContentPackRuntimeLoader.cs`
  - Action: If a bundle is missing, check if a "global-fallback" bundle is defined in the mod settings and attempt to load the asset from there.
  - Verification: Test with a missing bundle and a valid fallback bundle.

### Phase 5: Final Validation & Release
- [ ] **Task 5.1: Full Suite Execution**
  - Command: `dotnet run -- "C:\Users\Alex\Documents\Mod Rogue Command"`
  - Expected: `146/146 validation checks passed` (or updated count).

- [ ] **Task 5.2: Clean & Commit**
  - Command: `git add . && git commit -m "feat(loader): update to v1.1.0 with extended unit types and dynamic balance parameters" && git push origin codex/rogue-unit-balance-20261018`
