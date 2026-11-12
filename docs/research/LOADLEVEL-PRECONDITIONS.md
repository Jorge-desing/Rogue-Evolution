# Precondiciones de `LoadLevel()` / `LoadNextLevel()` — matriz estática

> **Evidencia:** inspección ESTÁTICA del `Assembly-CSharp.dll` fijado (Rogue Command 1.0.9, Unity 2021.3.45f2, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`). Cuerpos vía decompiles previos conservados en `artifacts/rea-tools/decompiled/` (`LoadScene.cs`, `StageMap.cs`, `LevelMap.cs`, `LevelProgression.cs`) + anclajes REA frescos del mismo artefacto para `InitMap`/`GuiController` (`inspect_managed_members` evidencia `ev_2cc52737a76e60f78e91146f76ff471a6d855c2e401d9710ec16777cc887b47d`; resumen `ev_544e59f9a621c76954dee64dc521294adf0256ee733d982436f228a15130bb80`). **No** es evidencia de runtime: nada de lo afirmado aquí prueba que una cadena viva funcione. Sesiones vivas relacionadas: `docs/research/AGENT-HANDOFF.md` entradas 56–57 y `artifacts/runtime-smoke/live-reads-20261108-game/runtime-evidence-game-attempt.json`.

## 1. `LoadLevel()` elige rama según el nodo objetivo, no siempre carga la escena

`LoadScene.LoadLevel()` (`artifacts/rea-tools/decompiled/LoadScene.cs:72-201`):

1. Si `_nextLevelIsLoading` ya es `true`, retorna sin hacer nada (flag de guarda).
2. Lee `Game.StageMap.CurrentLevelMap` y calcula:
   - `num = IsCurrentLevelFinished ? CurrentLevel + 1 : CurrentLevel`
   - `chosenField = ChosenField`
3. Si `(num, chosenField)` es un **nodo de recompensa** (`IsRestSite` / `IsShop` / `IsCardReward` / `IsUpgradeReward` / `IsRelicReward` / `IsResearchReward` / `IsDropReward` / `IsCoinReward` / `IsStartCrystalReward` / `IsResearchPointsReward`), aplica la recompensa, redibuja el mapa y restaura `_nextLevelIsLoading = false`. **No hay transición de escena.**
4. Solo la rama `else` (nodo de combate) llama `GuiController.OnPressedEnemyLaunchButton()`, activa `loadingBar` y lanza `InitMapAndLoadNextLevelAfterOneFrame()`, que tras un frame ejecuta `LevelProgression.InitMapIfBossComesNext()` y `StartCoroutine("LoadNextLevel")`.

Cada predicado de recompensa es `level == LevelCount ? false : lista[level].Contains(field)` (`LevelMap.cs:302-390`); con `field = -1` todos devuelven `false`.

## 2. Tras `JumpTo(4)`, `LoadLevel()` entra en la rama de combate — pero espera un mapa que nadie generó

- `StageMap.JumpTo(int)` (`StageMap.cs:118-122`) solo hace `CurrentStage = stage; CurrentLevelMap.Reset();`. No existe overload `(bool, int)` en el decompile (coherente con el seam del loader, que reportó `JumpTo(int)`).
- `LevelMap.Reset()` (`LevelMap.cs:436-443`): `CurrentLevel = -1`, `CurrentField = -1`, `ChosenField = -1`, `ChosenPath` vacía, `IsCurrentLevelFinished = true`.
- Por tanto, tras el jump: `num = -1 + 1 = 0`, `chosenField = -1` → todos los predicados falsos → rama combate (apartado 1.4).
- `LoadScene.LoadNextLevel()` (`LoadScene.cs:269-366`) espera **sin timeout**:
  ```csharp
  while (!InitMap.MapIsReady_Static()) { ...; yield return null; }
  ```
  y solo después carga aditivamente la escena de juego, mueve objetos (incl. `LoadNextLevel`), instancia la IA (`AI/` + `AiPrefabName`), `Navigator.SetSceneState(Game)` y `SetupPlayerStart`.

**Cadena causal del stall:** `JumpTo` no genera el mundo. La única llamada conservada a `InitMap.StartInit_Static(...)` está en `LevelProgression.InitMapIfBossComesNext()` (`LevelProgression.cs:61-94`), que **solo actúa si `IsLastLevelBeforeBoss`** — falso con `CurrentLevel = -1`. Por tanto, para la etapa 4 nadie inicia `InitMap`, `MapIsReady_Static()` puede quedar en `false` para siempre, y la corrutina queda esperando indefinidamente (barra vacía, `LoadingHider` nunca poblado, sin errores en log — exactamente lo observado).

Evidencia REA fresca sobre los miembros implicados (solo metadatos: firmas, tokens build-local, call edges; sin cuerpos):
- `InitMap` (24 métodos): `Awake`, `StartInit`, `CancelInit`, `RunInit`, `SaveDataToMapLog`, `PlaceMapBorderGameObjects`, `HasMapDanglingCells`, `OnDestroy`, `CreateWorldGrid`, `CreatePlane`, `AddRampsToGrid`, `ChooseMusic`, `GetPlane_Static`, `ChooseMusic_Static`, `StartInit_Static`, `CancelInit_Static`, `MapIsReady_Static`, `MapGenerationFinishedRatio_Static`, `MeshGenerationFinishedRatio_Static`, `UsedSeed_Static`, `NameLocaId_Static`, `LandscapeGeneratorNameLocaId_Static`, `.ctor`, `.cctor`.
- `GuiController` (15 métodos): `OnPressedEnemyLaunchButton`, `OnRewardMapStep`, `OnCoinsChanged`, `OnCurrentResearchPointsChanged`, `OnDropsChanged`, `OnDropPodsChanged`, `OnStartCrystalsChanged`, `OnActivateAssignUpgradeToCardView`, `OnDeactivateAssignUpgradeToCardView`, `OnActivateDeckOverview`, `OnDeactivateDeckOverview`, `OnInGameGuiButtonClicked`, `Subscribe`, `Unsubscribe`, `.cctor`.

## 3. `IsChoosable` explica el fail-closed natural del director

`LevelMap.IsChoosable(level, field)` (`LevelMap.cs:413-425`): si `CurrentLevel == -1 && level == 0`, choosable = campo presente; si no, exige `level == CurrentLevel + 1` y conexión `(CurrentField, field)` en `Connections(level)`. La semántica del `RuntimeIntermissionDirector` (fail-closed con "No choosable intermission node … (stop point: node pick)") es fiel a ésta — evidencia viva de la sesión 56/57.

## 4. Hipótesis ordenada para el siguiente intento (inferencia, NO runtime)

El stall tras jump se explica por listo-no-iniciado (`MapIsReady`). Candidatos, de mayor a menor plausibilidad:

1. **Llamar `LoadLevel()` sin `JumpTo` previo**, sobre el estado conservado del mapa (stage 0, `CurrentLevel = 3`, `IsCurrentLevelFinished = true` → `num = 4`, `chosenField = -1` → rama combate contra un mundo **ya generado** para la etapa actual, `MapIsReady = true` → transición inmediata). *Desconocido:* cómo se generó `SetCurrent(4, -1)` y si eso deja `ChosenPath`/recompensas consistentes; además el efecto de `OnPressedEnemyLaunchButton()` (cuerpo no decompilado).
2. **Estimular `InitMap.StartInit_Static`** para la etapa destino antes de `LoadNextLevel` — exige resolver `LandscapeGenerator`, fallback y `DefaultAiBehaviour` de la etapa; camino no verificado y con más superficie de fallo.
3. Repetir la cadena idéntica tras jump — **descartado**: el modo de fallo (espera infinita en `MapIsReady`) se reproduce determinísticamente por el trazado; repetirlo sin cambiar la precondición no agrega información (y viola la regla do-not-loop).

## 5. Desconocidos explícitos

- Cuerpo de `GuiController.OnPressedEnemyLaunchButton()` y estado UI que exige (p. ej. botón Launch activo) — sin decompile de `GuiController` en el repo.
- Quién invoca normalmente `InitMap.StartInit_Static` para la etapa actual (fuera de la vía boss) — la búsqueda en decompiles previos solo halló el caller de boss.
- Contenido de `InitMap.MapIsReady_Static()` / ratios (métodos REA solo metadatos; cuerpos no presentes).
- Ventana exacta de tiempo entre frames/coroutines en la única transición observada (`c75d54d6`).

## Próximo paso sugerido (no ejecutado)

En una sesión futura con checkpoint cerrado → Steam: (1) llegar al mapa conservado; (2) **sin JumpTo**, llamar una sola vez `LoadLevel()` sobre `LoadNextLevel` y observar si el censo sale de `stage=4/level=-1`-equivalente y aparece `LoadingHider`; (3) si aparece, clic en su botón `INICIAR` y snapshot con entidades. **No reintentar la cadena con jump idéntica.** Registrar como `synthetic` solo si se usa; si se usa la vía natural (`TryEnterLevelNodeWorkflow`) primero, seguir documentándolo como natural.
