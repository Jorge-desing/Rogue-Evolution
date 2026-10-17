# Catálogo inicial de assets — Rogue Command

**Fecha:** 2026-10-16
**Juego observado:** versión 1.0.9
**Unity detectado:** 2021.3.45f2

## Método e identidad

El inventario se generó con `tools/asset_inventory.py`, UnityPy 1.25.4 y Python 3.12, leyendo los archivos de `Rogue Command_Data`. Se analizaron los archivos serializados reconocidos (`.assets` y escenas/administradores sin extensión); se omitieron los almacenes de datos externos `.resource` y `.resS`. No se exportaron recursos ni se escribió en la instalación.

| Dato | Valor |
|---|---|
| Archivos serializados leídos | 16 de 16 |
| Objetos indexados | 562.122 |
| Objetos `MonoBehaviour` | 84.141 |
| Objetos `MonoScript` | 2.662 |
| Errores de lectura de archivos | 0 |
| `resources.assets` | 277.421.448 bytes |
| SHA-256 de `resources.assets` | `c751bdeb3c1190b8f9fe80d58dd3b99e5a28130e37df41b93f9d8698a49c6490` |

El índice detallado está disponible localmente en `docs/research/generated/` y se excluye de Git por su tamaño. Los IDs de objeto citados abajo pertenecen al archivo y hash indicados; deben volver a obtenerse si el juego o sus assets cambian.

## Estructuras relevantes observadas

### Progresión

El objeto `1_Rogue Command Progression Pools FinalAscensions` (path ID `384970` en `resources.assets`) se decodificó con los ensamblados locales y contiene 10 entradas en `stages`, además de referencias a listas predeterminadas de 34 AI, 34 generadores de paisaje y 17 mundos.

| Etapa | Tamaño de mapa serializado | ScriptableObject de parámetros | `isPartOfDemo` |
|---:|---:|---|---:|
| 1 | 29–35 | `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` | sí |
| 2 | 35–41 | `StageParameters_Pools_Level3` | sí |
| 3 | 55–64 | `StageParameters_Pools_Level3` | sí |
| 4 | 55–61 | `StageParameters_Pools_Level4` | sí |
| 5 | 59–71 | `StageParameters_Pools_Level5` | sí |
| 6 | 75–83 | `StageParameters_Pools_Level5to6` | sí |
| 7 | 71–81 | `StageParameters_Pools_Level7` | no |
| 8 | 75–89 | `StageParameters_Pools_Level8to9` | no |
| 9 | 91–117 | `StageParameters_Pools_Level9` | no |
| 10 | 96–120 | `StageParameters_Pools_Level9` | no |

El perfil `StageParameters_Pools_Level1` (path ID `384981`) expone campos como `levelCount=4`, `maxWidth=5`, `twoWayBranchingProbability=1.0`, recompensas y pesos para seleccionar recompensas. El perfil `StageParametersShortHardRuns` (path ID `384977`) es otra configuración serializada: `levelCount=6`, `maxWidth=5`, `twoWayBranchingProbability=0.5`, además de recompensas distintas. Se conservaron los valores serializados tal cual; no se infiere el significado del orden de los componentes `x/y` en `startCountRange`.

La lista `WorldListBossLevels` (path ID `384864`) referencia tres objetos de mundo. En esta pasada se obtuvieron sus IDs, pero no nombres legibles para esas tres referencias.

### Generación de mapas

Los nombres de objetos incluyen decenas de configuraciones de paisaje, por ejemplo `27 Circles`, `29 Hill in the middle`, `41 BaseVsBase small`, `43 Hillrange in the middle 2 Base 1 POI`, `69_SurviveL2` y `101 Giant Volcanoes`. Esto muestra variedad de assets existentes y aporta puntos de comparación para investigar el esquema de generación.

### Extracción puntual con AssetRipper

El 2026-10-16, AssetRipper 2.0.0 en modo headless cargó `Rogue Command_Data/level1` y extrajo el `Sprite` `PCXBaseTank` (path ID `14721`), cuya referencia resolvió a `Texture2D` path ID `1606`. El PNG resultante mide 256 × 256 y su SHA-256 local fue `b609724b1b5a048505807df58edbe047ecaf7c02d5aa3fb9047100f25e479643`; AssetRipper también produjo YAML y JSON con rectángulo, pivot y pixels-per-unit. El informe reproducible de esta prueba quedó en `%TEMP%/RogueCommand-AssetRipper-c92b5b7dfa3049c08e18049fe93157c0/assetripper-sprite-report.md`, fuera del repositorio.

La lectura del archivo activó la resolución de dependencias del juego, incluyendo `resources.assets` y `resources.assets.resS`. Se detuvo la exportación en ese punto y no se escribió un proyecto Unity. La extracción confirma lectura de imagen y metadata, pero no reconstrucción/importación del Sprite en Unity, ni compatibilidad de un prefab, etapa o AssetBundle con el loader.

### Viabilidad de un paquete de muestra

La authoring project importa ahora copias locales de DLL bajo `Assets/Plugins/GameAssemblies/` con compatibilidad de Editor solamente. La herramienta fija `Assembly-CSharp.dll` al SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` y, al resolver tipos del juego, compara el MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. La exclusión Git se verificó; `ignore.conf` contiene reglas equivalentes para Unity Version Control, pero no hay un CLI de Plastic local para comprobar cómo las evalúa. Los assemblies no se asignan a AssetBundles.

**Resultado del experimento de authoring de prefabs completos, 2026-10-17:** se clonó el proyecto bajo `%TEMP%` sin copiar `.plastic`, UserSettings ni configuración Cloud. Se probaron los tres DLLs inicialmente seleccionados, después todas las dependencias administradas no pertenecientes al framework de Unity y, finalmente, compatibilidad Any Platform. La importación por lotes de Unity 2021.3.45f2 compiló el script editor, pero el probe no pudo añadir `EntityController` al GameObject de prueba: `Unity could not attach game component EntityController`. Los intentos anteriores registraron además `script missing` para `EntityController`, `DefaultAiBehaviour` e `InitMap` al guardar prefabs. El scaffolder de prefabs completos termina con exit code 1 cuando falla el preflight; no fabrica scripts de juego. Logs: `artifacts/unity-authoring-fail-closed-no-upm-3.log`, `artifacts/unity-authoring-final-no-upm-4.log` y `artifacts/unity-authoring-check-any-platform.log`.

Los outputs anteriores que parecían éxito eran inválidos: el `sample-expansion.bundle` de 6.459 bytes y los prefabs de 914/3.829 bytes se guardaron sin componentes de juego funcionales. Se movieron con sus `.meta` y manifiestos a `artifacts/unverified-authoring-output/2026-10-17/`, fuera del proyecto Unity y de `content/packs/`. No son muestras instalables.

AssetRipper exportó para `PCXBaseTank` únicamente PNG (256 × 256), YAML y JSON. No creó `.asset`, prefab, `.meta` ni un proyecto Unity importable, y no se intentó importar el Sprite exportado. Resolver dependencias encontró `resources.assets` (~277 MB), `resources.assets.resS` (~560 MB) y `resources.resource` (~343 MB); la prueba se detuvo sin exportar el proyecto completo. El informe temporal está en `%TEMP%/RogueCommand-AssetRipper-c92b5b7dfa3049c08e18049fe93157c0/assetripper-sprite-report.md`.

La ruta de carga exige una fila `EntityBalancingScriptableObject`, un prefab con `EntityController` raíz y Renderer, y un Sprite; para una etapa exige `StageParametersScriptableObject`, al menos un `BlueprintPool` con filtro y un `InitMap` con `WorldGenerator`; para AI exige un prefab activo con exactamente un `DefaultAiBehaviour`, deck no vacío con IDs de unidades disponibles, y `aiBases` configurado. El `_UnitEntityPrefab` inspeccionado tiene siete componentes raíz y muchas referencias; tampoco es una plantilla lista para reutilizar. `BP_Turrets` (path ID `384895`) sigue sin decodificarse porque el árbol de tipos necesitaba `ShapesEditor.dll`, ausente del generador local.

**Comprobación estática de las plantillas del paquete nativo, 2026-10-17:** después de volver a verificar versión 1.0.9, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` y MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, los nombres declarados en `sample-native-expansion/content.json` se contrastaron con los assets del mismo juego. `PCXBaseTank` aparece en `Mod/exampleEntityBalancing.tsv` y como GameObject en `level1` (path ID `845`); el parámetro de etapa aparece como MonoBehaviour en `resources.assets` (path ID `384984`); y el perfil Quick Hunter aparece como componente AI (path ID `421336`) en un GameObject del mismo nombre (path ID `99366`). Los inventarios `docs/research/generated/asset-objects.csv`, `stage-template-candidates.json` y `native-ai-template-candidates.json` registran estas lecturas. Esto confirma presencia y nombres serializados, no unicidad entre los objetos que Unity cargue ni éxito de `Resources.Load`/`Resources.FindObjectsOfTypeAll` en partida.

**Jerarquía estática del prefab nativo PCXBaseTank, 2026-10-17:** el objeto en `resources.assets` (path ID `95696`) tiene `EntityController` y componentes de juego en la raíz; su `Transform` contiene la jerarquía `Vehicle` y objetos descendientes con `MeshFilter`/`MeshRenderer`. El informe completo `artifacts/research/pcx-base-tank-hierarchy.json` se genera localmente y está ignorado por Git. Esto indica que la validación recursiva de renderers del cargador coincide con el asset serializado, pero no prueba que `Resources.Load<GameObject>` resuelva esa ruta en runtime.

**Overlay visual nativo, 2026-10-17:** para evitar la limitación de Unity Editor con `EntityController`, `sample-native-expansion` conserva ese prefab y sustituye solo los Renderer bajo su ruta `Vehicle`. El asset `ScoutTankVisual` es un GameObject Unity normal, con 73 MeshFilters/MeshRenderers; SkirmishBikeVisual tiene 53. Ambos incluyen materiales asignados y no contienen MonoBehaviour ni scripts faltantes. `RogueCommandVisualSampleScaffolder` generó el bundle de 346,164 bytes en Unity 2021.3.45f2; el Editor volvió a abrirlo con `AssetBundle.LoadFromFile` y cargó los prefabs. UnityPy corroboró los objetos serializados. SHA-256 del bundle final `f1c1b0cb1b6034820d1d7fa3c27153d26b42102a4480aad7ada6b58c2e11baee`; SHA-256 del manifest final `30d0e73dbaa880f55538797df1e82f2f6323955dbc4758dd2646e4d78f499254`. El plugin conserva la jerarquía nativa de gameplay, desactiva los Renderer originales en el clon y agrega el visual al nodo de destino; al no incluir material, hereda el primero disponible en el subtree nativo. Esto demuestra build e importación en Editor, no renderizado del modelo en combate.

**Transición estática de arranque, 2026-10-17:** `BuildSettings` lista `Assets/_Scenes/EmptyBootUpScene.unity` como primera escena y `MainMenu.unity` como segunda. El objeto serializado `ImageLOGO` en `level0` tiene un `ImageFading` configurado con `enableBehaviour = FadeIn`; `onFadeInEnd` contiene un callback persistente a `LoadScene.LoadMainMenu` en el GameObject `Loader`. La carga del menú registrada por el clon concuerda con esa ruta del bootstrap. La lectura de la escena confirma el callback configurado, pero no que se haya ejecutado durante el smoke ni por qué el clon no termina la transición a `MainMenu`. Otros botones tienen callbacks a métodos del menú en escenas posteriores; no son necesarios para explicar la solicitud inicial.

**Runtime controlado mediante Steam (2026-10-17):** Rogue Command 1.0.9 llegó a `RunSetup` después de cargar el plugin con 12 hooks verificados. El log confirma el registro de ambos packs: cinco unidades, cuatro etapas totales y cuatro perfiles AI totales. El bridge encontró el clon de Sandlance en `DontDestroyOnLoad`, pero estaba inactivo; por tanto el runtime confirma carga y preparación del clon, no que `EntityFactory` lo instancie, que se genere un mapa de combate, que el módulo AI se ejecute o que el modelo se renderice. La sesión usó el bundle anterior `dcb8a28554959f813e97a7f6d0dc93b718138dda1ed3b161c641d805989c471d`; el bundle final con ancho de moto ajustado a 1.196 m tiene SHA-256 `f1c1b0cb1b6034820d1d7fa3c27153d26b42102a4480aad7ada6b58c2e11baee` y todavía no se ha probado en juego.

**Siguiente paso:** iniciar desde Steam una partida de prueba con perfil reversible y verificar la creación del mapa, la oferta aleatoria de una unidad del pack, su instanciación/renderizado y al menos una decisión del módulo AI. Para `sample-expansion` con contenido propio, aún se necesitan prefabs y ScriptableObjects serializados con los componentes y referencias completos; no se debe producir un bundle placeholder. Evidencia REA de tipos estáticos: `ev_eea143512c436bb6579559b439d25e7356d59246194b33404955c85560424097` (identidad) y `ev_b8839dc6512b0dfb2313f88fa47529e3f1794fb2433cfc934bd1aa31c5f4cd0e` (miembros).

**Skirmish Strike Bike, 2026-10-17:** `sample-native:skirmish-bike` añade una variante namespaced basada en `BombBike`; no cambia el registro de la moto nativa. La inspección estática de `resources.assets` encontró que `GameObject/BombBike` contiene el subtree de renderizado y deja fuera el root, `EntityController` y los componentes de gameplay. El bundle final de muestra incluye `SkirmishBikeVisual` (modelo de dos ruedas con dos cargas explosivas externas, bandas, rieles, aros de rueda, abrazaderas de horquilla y barra frontal) y `SkirmishBikeCard`; el cargador clona el prefab nativo, desactiva solo los Renderer de ese subtree en el clon y agrega ahí el visual propio. El Scout recibió su actualización de 73 partes, con zapatos de oruga, collares de boca de cañón, mástil arriostrado y sensores montados. El ancho de la moto se midió en 1.196 m, dentro del objetivo de 1.2 m para su collider nativo. El bundle final mide 346,164 bytes y tiene SHA-256 `f1c1b0cb1b6034820d1d7fa3c27153d26b42102a4480aad7ada6b58c2e11baee`; su manifest mide 1,588 bytes y tiene SHA-256 `30d0e73dbaa880f55538797df1e82f2f6323955dbc4758dd2646e4d78f499254`. Unity Editor volvió a abrir ambos prefabs y Sprites; UnityPy corroboró el inventario serializado. Build/importación estática no demuestra carga, movimiento, combate ni renderizado en una partida.

## Límites y lectura

- Los datos muestran que progresión, parámetros de etapa, generadores y mundos están representados por objetos serializados Unity.
- El catálogo no demuestra que el directorio `Mod` pueda registrar ScriptableObjects nuevos ni que Rogue Command descubra bundles externos.
- El formato integrado sigue confirmado solo para sobrescribir parámetros de entidades existentes.
- El decodificador logró leer los cuatro objetos seleccionados del informe local `progression-objects.json`. Una prueba de lectura de `BP_Turrets` (path ID `384895`) falló porque el árbol de tipos referenciaba `ShapesEditor.dll`, que no quedó disponible para el generador local; ese objeto se mantiene como pendiente, no vacío.
- La extracción de imagen no autoriza a inferir que un prefab completo sea reconstruible. El Sprite exportado no se añadió al proyecto Unity ni a ningún paquete para evitar incorporar assets comerciales del juego al repositorio.
- El bundle visual del sample sí se generó y reabrió en Unity Editor. En la prueba Steam se demostró que el loader descubre los packs, registra cinco unidades y prepara clones dormidos, pero no se ha verificado una instancia de unidad en combate ni el uso de un mapa/AI nuevo. Tampoco se ha importado a Unity ningún `ScriptableObject`/prefab de gameplay extraído; las etapas y perfiles de muestra siguen siendo clones de plantillas nativas.
- Un smoke anterior en un clon temporal cargó BepInEx y 11 hooks de una compilación anterior; el modo `-nographics` produjo excepciones repetidas de TextMeshPro. Las pruebas corregidas usaron la build actual y un `steam_appid.txt` temporal en el clon: el loader instaló 10 hooks y completó chainloader, pero ni ese clon ni el control con la DLL del cargador desactivada emitieron `sceneLoaded`, `Start` o el heartbeat del probe después de solicitar `MainMenu`. No apareció el mensaje `SteamAPI_Init() failed` en esos dos logs, pero no hay una traza afirmativa de éxito de Steam. Tampoco se observó un mensaje de carga del paquete ni actividad de unidad, etapa o AI. La decompilación estática muestra que `StartUp.Awake` no pide `MainMenu`; sus tres callers directos son métodos de botones o retorno al menú, y no se identificó cuál se ejecutó. No se identificó la causa del bloqueo ni se ejecutó la instalación real de Steam; ver evidencia, limpieza y límites en `docs/research/AGENT-HANDOFF.md`.

## Siguiente investigación

1. Seguir el registro de `LevelProgressionParametersScriptableObject` y quién selecciona estos perfiles durante la carga del juego.
2. Decodificar `BlueprintPool`, `UnitBalancingScriptableObject` y assets de entidades; corregir o suplir la dependencia del árbol de tipos si hace falta.
3. Localizar el punto que descubre archivos de mods y comprobar si acepta bundles o referencias externas.
4. Solo después de hallar un camino de carga, elegir una primera unidad o nivel nuevo y separar una prueba controlada de las modificaciones al contenido instalado.
