# Runtime evidence contract

Use this contract for `artifacts/runtime-smoke/<session-id>/runtime-evidence.json`. It is a reporting contract, not a claim that a session passed.

## Required top-level shape

```json
{
  "schema": "rogue-command/runtime-evidence/v1",
  "sessionId": "<stable-session-id>",
  "claim": "<one behavior under test>",
  "status": "PASS | FAIL | BLOCKED | UNKNOWN | NOT_TESTED",
  "evidenceLevel": "static | local | steam-runtime",
  "source": {
    "repository": "<repository root>",
    "commit": "<40-hex commit>",
    "branch": "<branch>",
    "artifactHashes": [{"path": "<ignored artifact>", "sha256": "<64-hex>"}]
  },
  "gameIdentity": {
    "gameVersion": "<observed version>",
    "unityVersion": "<observed Unity version>",
    "assemblyCSharpSha256": "<64-hex>",
    "assemblyCSharpMvid": "<GUID>",
    "runtime": "Mono",
    "architecture": "x64"
  },
  "process": {
    "launchedThroughSteam": true,
    "pid": 0,
    "bridgeEndpoint": "<verified endpoint>",
    "sceneObservations": []
  },
  "profile": {
    "qaProfile": "Profile2",
    "selectorPath": "<authoritative selector>",
    "selectorBefore": "<exact selector text>",
    "selectorAfter": "<exact selector text>",
    "checkpoint": "<relative artifact path>",
    "verification": {},
    "allowedChanges": ["Profile2/metaSavegame.dat"]
  },
  "deployment": {
    "manifest": "<relative artifact path>",
    "targets": [],
    "restored": true
  },
  "assertions": [
    {
      "id": "<stable assertion id>",
      "expected": "<observable condition>",
      "observed": "<exact observation>",
      "status": "PASS | FAIL | BLOCKED | UNKNOWN | NOT_TESTED",
      "evidence": ["<relative artifact path>"]
    }
  ],
  "cleanup": {
    "processExited": true,
    "temporaryComponentsRemoved": true,
    "deploymentRestored": true,
    "profileVerificationPassed": true
  },
  "unknowns": ["<unverified behavior>"],
  "conclusion": "<one sentence bounded conclusion>"
}
```

Do not store passwords, access tokens, card numbers, CVCs, 2FA codes, or raw credential-bearing configuration in this artifact. Sanitize user names and unrelated local paths when the artifact will leave the machine. Keep the unsanitized local evidence under ignored `artifacts/` only when it is needed for forensic correlation.

## Path contract

- `profilesRoot` and `selector.path` are captured as absolute, lexically normalized paths.
- Symlink and reparse-point endpoints are rejected before path comparison or traversal; paths are never resolved through links.
- Verification fails when operator-supplied paths do not normalize to the captured `profilesRoot` and `selector.path` (unless they are the same lexical targets after normalization).
- Runtime file enumeration aborts on `os.walk` errors and rejects symlinks, reparse points, and non-regular entries.
- These checkpoint checks are local safety validation only; they do not establish Steam runtime success.

## State and evidence rules

- `PASS` requires the exact claim-specific postcondition and matching deployed artifact hash in the live process.
- `FAIL` means the target was reached and contradicted the expected condition.
- `BLOCKED` means a prerequisite prevented a valid observation: identity mismatch, stale DLL, missing bridge, unsafe profile, process still alive during restore, or failed cleanup.
- `UNKNOWN` means the run was valid but stopped before the target behavior or produced inconclusive evidence, such as one random offer without the unit.
- `NOT_TESTED` is reserved for assertions intentionally not attempted. It is never a success.
- `steam-runtime` requires a Steam-launched process, a live bridge read, and the exact identity/deployment/profile binding. A local probe or forced diagnostic spawn must be marked with a narrower assertion and must not be promoted to natural gameplay evidence.

## Claim ladder

Record assertions in this order when applicable:

1. process and bridge live;
2. plugin loaded with the tested hash;
3. pack discovered and accepted;
4. runtime registration/census contains expected IDs;
5. randomized offer names the target in the correct stage/pool;
6. native selection and placement produce the target factory;
7. factory produces the target product;
8. target instance renders with expected visual/rig evidence;
9. stage/map identity is generated in the run;
10. compatible AI profile ticks and produces the intended bounded decision;
11. target stats/mechanics are consumed by the target instance.

A later assertion may be `NOT_TESTED` or `UNKNOWN` even when earlier assertions pass. Never collapse the ladder into a single “runtime successful” boolean.

## Artifact layout

```text
artifacts/runtime-smoke/<session-id>/
  runtime-evidence.json
  profile-checkpoint.json
  profile-verification.json
  predeploy-manifest.json
  restoration-result.json
  logs/
  bridge/
  screenshots/
  probes/
```

Use relative paths in the evidence JSON for files below the session directory. Include raw bridge payloads before filtering when they are needed to prove an observation, and preserve the command arguments alongside a mutating response. Do not delete evidence merely because cleanup succeeded.

## Restoration acceptance

`cleanup.profileVerificationPassed` is true only when:

- the authoritative selector equals the pre-run value and hash;
- there are no added or missing profile files;
- no profile file changed unexpectedly;
- `Profile2/metaSavegame.dat` is the only allowed changed file, and only when the run actually advanced Profile2;
- `savegame.dat`, tutorial state, and all non-QA profiles match their checkpoint;
- every declared installation target matches its predeploy backup or its declared post-cleanup state;
- the game process has exited before any restoration write.

If any condition fails, set the session status to `BLOCKED`, retain the reports, and do not claim that the environment was restored safely.
