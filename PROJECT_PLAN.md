# Plan de Implementación: Aparición Universal de Unidades Modeadas en Selección Aleatoria de Cartas

## 1. Contexto y Diagnóstico (Reverse Engineering)
A través de la descompilación con `ILSpyCmd` e inspección estática del ensamblado nativo `Assembly-CSharp.dll` y del cargador `RogueCommandExpansionLoader`:
- **Flujo de Recompensas de Cartas (`ChooseCard.cs:560-642`)**:
  - **Recompensa Genérica (sin pool)**: Consulta `EntityBalancingStore.AllEntityIdsAllowedAsBlueprints()`. El cargador intercepta esto con `RuntimeHooks.FilterGlobalBlueprintRewardCandidates()` llamando a `BlueprintRewardFactoryScope.RemoveFactoriesOutsideStage()`. En etapas nativas (`StageParameters_Level1..3`), `_stageFactoryIds` estaba vacío, por lo que **el cargador purgaba el 100% de las fábricas de mods** de la lista de candidatos.
  - **Recompensa con Pool Temático (`BlueprintPool.cs:50-89`)**: Consulta `IsInPool(entityId)`, el cual comprueba `EntityBalancingStore.IsAllowedAsBlueprint(factoryId)`. En `ContentPackRuntimeLoader.cs:4767`, la fila de la fábrica se inicializaba con `isAllowedAsBlueprint = false`, y solo se activaba si una etapa de mod personalizada declaraba la unidad. Por ello, los pools nativos (Vehículos, Mechs, etc.) rechazaban inmediatamente las unidades modeadas.
  - **Selección de Carta Inicial (`isStartingBlueprintSelection`)**: Requiere `isAllowedAsStartingBlueprint == true` y `isAllowedAsBlueprint == true` en la fábrica.

## 2. Solución Arquitectónica
1. **Habilitación de Blueprint en Fábricas (`ContentPackRuntimeLoader.cs`)**:
   - Marcar `isAllowedAsBlueprint = true` en las fábricas de producción por defecto.
   - Registrar todas las fábricas de unidades modeadas y edificios independientes en `_blueprintUnitIds`.
   - Propagar `isAllowedAsStartingBlueprint` tanto al producto como a la fábrica.
   - Fallback de herencia para `neededExperienceLevel`, `rarity` y `tech` desde la unidad hacia la fábrica si no se definen explícitamente en `productionBuilding`.
2. **Alcance de Recompensas Universal en Etapas Nativas (`TryGetBlueprintRewardFactoryScope`)**:
   - En etapas nativas (`!activeStageIsPackOwned`):
     - Antes del primer nivel (`isStartingBlueprintSelection == true`): poblar `stageFactoryIds` con las unidades que tengan `isAllowedAsStartingBlueprint == true`.
     - Durante la campaña (`isStartingBlueprintSelection == false`): incluir todas las fábricas modeadas elegibles (`_blueprintUnitIds`) en `stageFactoryIds`, permitiendo que aparezcan en todas las recompensas aleatorias genéricas y pools temáticos nativos.
   - En etapas de mod personalizadas (`activeStageIsPackOwned == true`): preservar el filtrado estricto definido por el creador del mapa.

## 3. Fases de Ejecución
- **Fase 1**: Modificación de `ContentPackRuntimeLoader.cs` (registro de elegibilidad de fábricas y resolución de alcance en etapas nativas).
- **Fase 2**: Actualización de pruebas unitarias en `tests/RogueCommandExpansionLoader.ValidationTests/` para verificar el nuevo comportamiento universal y evitar regresiones.
- **Fase 3**: Ejecución y validación de suites:
  - Compilación Release de `RogueCommandExpansionLoader.csproj` (0 advertencias, 0 errores).
  - Ejecución del arnés de pruebas C# (`dotnet run --project tests/... -- .`).
  - Validación de manifiestos y schemas en Python (`python -m unittest`).
- **Fase 4**: Empaquetado y Despliegue:
  - Generación de `artifacts/packages/RogueCommandExpansionLoader-1.19.0.zip`.
  - Instalación de DLL y PDB actualizados en `C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\BepInEx\plugins\`.
  - Verificación de Git, commit y push a `origin/codex/rogue-unit-balance-20261018`.
