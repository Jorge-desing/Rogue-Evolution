# Rogue Evolution — expansión de contenido para Rogue Command

Este repositorio desarrolla un **mod de expansión para añadir unidades, etapas y AI a Rogue Command**. El cargador externo BepInEx 5 con Harmony es la infraestructura que permite registrar y ejecutar ese contenido; no es el objetivo final del mod. El paquete de muestra actual demuestra una unidad visual basada en una plantilla, una variante de etapa y un módulo compilado de AI, pero aún no es contenido aceptado dentro de una partida.

El trabajo prioriza contenido original que se pueda jugar: unidades con identidad visual y balance propios, etapas con una experiencia de mapa reconocible y perfiles de AI con decisiones nuevas. Cada entrega debe separar la inspección estática, la compilación local y la comprobación en una partida iniciada por Steam. Conserva `mod/manifest.json` para los ajustes TSV de balance que admite el juego de forma nativa.

Para retomar el trabajo en otro chat o con otro agente, comienza por [docs/research/AGENT-HANDOFF.md](docs/research/AGENT-HANDOFF.md). Resume el estado del contenido, el cargador de soporte, comandos, proyecto Unity y límites de la evidencia. La búsqueda de herramientas y sus usos recomendados está en [docs/research/CONTENT-AUTHORING-TOOLS.md](docs/research/CONTENT-AUTHORING-TOOLS.md).

## Compatibilidad fijada

El plugin solo instala patches cuando coinciden estas condiciones:

- Rogue Command `1.0.9` y Unity `2021.3.45f2`.
- Proceso Mono de 64 bits.
- SHA-256 de `Assembly-CSharp.dll`: `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`.
- MVID: `db0e9462-128d-4296-9900-de18b4bd0c00`.

Una actualización del juego, del ensamblado o de Unity desactiva el loader hasta que se vuelvan a verificar las firmas y los campos observados. La inspección previa de esos puntos fue estática; la aceptación dentro de una partida aún requiere una prueba en el runtime Mono del juego.

## Compilar

Requisitos: .NET SDK, la instalación local de Rogue Command 1.0.9 y las referencias de BepInEx `5.4.23.5` en `.tools/bepinex-dist-5.4.23.5/`.

```powershell
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj -v:minimal
```

El ensamblado se genera en `artifacts/plugin/RogueCommandExpansionLoader.dll`. El proyecto apunta a `netstandard2.1`: el `Assembly-CSharp.dll` de esta instalación referencia esa versión y la compilación `netstandard2.0` fallaba con `CS1705`. El plugin usa BepInEx y Harmony del paquete local, y no copia esas bibliotecas ni las ensambladas del juego al directorio de salida.

## Herramientas de contenido

Las skills del repositorio en `.agents/skills/` orientan a agentes por área: `rogue-command-unit-authoring`, `rogue-command-assetbundle-authoring`, `rogue-command-stage-authoring`, `rogue-command-ai-authoring`, `rogue-command-game-research`, `rogue-command-pack-validation` y `rogue-command-steam-runtime-validation`. Se basan en el contrato y la evidencia del handoff; no elevan una validación local a prueba de juego.

Unity 2021.3.45f2 ya está instalado para compilar AssetBundles. Blender 4.5.14 LTS y los inspectores de runtime UnityExplorer 4.8.2 y Runtime Unity Editor 6.3.2.1 se guardan bajo `.tools/`; esta carpeta está ignorada por Git. Para volver a preparar las copias locales en otro checkout:

```powershell
.\tools\Setup-ContentAuthoringTools.ps1 -Blender -UnityExplorer
```

El script verifica SHA-256 y solo prepara Blender y UnityExplorer bajo `.tools/`. Runtime Unity Editor se prepara por separado con `./tools/Setup-BackendTools.ps1 -RuntimeUnityEditor`; también verifica SHA-256. Ninguno de estos scripts copia archivos al juego ni lo inicia. Los inspectores requieren BepInEx 5 Mono y se añaden a la instalación de Steam solo durante una prueba reversible, usando uno por perfil de prueba.

Para revisar por CLI el contenido serializado de los bundles ya creados, prepara UnityDataTool 2.4.0 con su instalador fijado a una release y hash:

```powershell
.\tools\Setup-UnityDataTool.ps1
& .\.tools\UnityDataTool-2.4.0\UnityDataTool.exe archive list content\packs\sample-native-expansion\bundles\sample-native-expansion.bundle
& .\.tools\UnityDataTool-2.4.0\UnityDataTool.exe sf objectlist content\packs\sample-native-expansion\bundles\sample-native-expansion.bundle -f json
```

Esto enumera archivos y tipos serializados offline; no sustituye la carga del bundle en Unity ni una comprobación del juego.

El validador de paquetes y guardas de compatibilidad se puede ejecutar sin iniciar el juego ni cargar AssetBundles:

```powershell
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .
```

El ejecutable enlaza los DTO y validadores del plugin. Comprueba directamente ambos manifiestos de muestra, crea fixtures temporales para los casos de rutas, referencias, IDs y DLLs, y verifica el rechazo de versión de Rogue Command/Unity, SHA-256 y MVID incompatibles. Usa un stub local para `Application.unityVersion`; no carga el juego ni comprueba tipos Unity serializados, AssetBundles reales, Harmony o decisiones AI durante una partida.

Para crear un ZIP de instalación sin escribir en la carpeta del juego:

```powershell
.\tools\Package-Loader.ps1
```

El paquete aparece en `artifacts/packages/` y conserva la estructura `BepInEx/plugins/`. Incluye el plugin, el esquema, `sample-native-expansion` con un modelo visual sin scripts compilado como AssetBundle de Unity 2021.3.45f2 y `sample-reactive-ai`, un paquete de datos con una variante de etapa y un módulo táctico compilado. `sample-expansion` queda fuera porque todavía necesita sus propios assets completos de balance, etapa, AI y mapa. Se puede cambiar la instalación usada para compilar con `-GameRoot` y la ruta local de BepInEx con `-BepInExCorePath`.

## Instalar

1. Instala BepInEx `5.4.23.5` **win-x64 Mono** en el directorio de Rogue Command usando la distribución oficial.
2. Copia `artifacts/plugin/RogueCommandExpansionLoader.dll` a `BepInEx/plugins/`.
3. Coloca cada paquete en `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/<carpeta-del-pack>/` con su `content.json` y, cuando los use, sus AssetBundles.
4. Conserva `Rogue Command/Mod/` para las sobrescrituras TSV nativas; el loader externo no reemplaza ese formato.

`content/packs/sample-native-expansion/content.json` crea variantes de balance basadas en unidades nativas, sustituye la rama visual `Vehicle` del explorador con un prefab Mesh del bundle, deriva etapas de plantillas nativas y añade un módulo de construcción programada al AI. `content/packs/sample-reactive-ai/content.json` referencia a `sample-native:scout-tank` desde su etapa `counterstrike-lane` y muestra un perfil de contraataque que detecta unidades del jugador atacando un edificio AI y envía hasta dos defensores con ataque y movimiento habilitados, dentro de 12 celdas, a la última posición del atacante. Solo interrumpe órdenes inactivas o de movimiento; después de llegar, el juego reanuda la orden guardada. Los perfiles nativos de muestra usan `scopeToReferencedStages`: el loader crea un alias clonado con el rango calculado de las etapas que lo referencian, sin modificar la plantilla global. El cálculo pasa validación local y preflight estático; ninguna unidad, etapa o AI se ha confirmado en una partida. El overlay visual conserva la jerarquía de gameplay, hereda el primer material nativo si hace falta y no remapea animaciones o rigs. La elegibilidad blueprint sigue siendo global a la unidad. `sample-expansion` muestra además la ruta para paquetes con prefabs Unity completos. Los AssetBundles no pueden distribuir código ejecutable.

## Diagnóstico e Inspector en el Juego (F8 / F10)

El loader incluye un inspector visual integrado accesible mediante **F8** o **F10** (o haciendo clic en la marca de agua superior derecha):

- **Marca de agua:** Muestra el estado del loader y versión (`RCL v0.1.0 [OK]`).
- **6 pestañas interactivas:**
  1. **Resumen:** Estado del registro nativo, compatibilidad, recuento de paquetes y memoria en AssetBundles.
  2. **Paquetes:** Lista de paquetes descubiertos, estado (Activo/Error), tamaño y desglose de contenido.
  3. **Unidades:** Unidades agregadas al registro con sus claves de recursos prefab/imagen y estado de blueprint.
  4. **Etapas:** Etapas registradas con sus rangos de tamaño de mapa y unidades/perfiles asociados.
  5. **Perfiles IA:** Perfiles de IA cargados con sus módulos compilados y recuento de instancias activas.
  6. **Diagnóstico:** Registro de logs del loader en tiempo real con campo de filtrado por texto.
- **Aislamiento defensivo:** Las excepciones de interfaz están acotadas y no interfieren con el hilo principal del juego. Emplea texturas y estilos cacheados para minimizar recolección de basura (GC).
- **Modo seguro por compatibilidad:** Si la versión del juego o hash de ensamblado no coinciden, la interfaz muestra el error de compatibilidad exacto sin crashear.
- **Traza de escenas:** el log de BepInEx registra las escenas que Unity completa y los cambios de escena activa; una solicitud `LoadScene` sin callback aparecerá como transición incompleta.

## Contrato de contenido

La versión 1 está documentada en [content/schema/README.md](content/schema/README.md) y [content/schema/content.schema.json](content/schema/content.schema.json). Cada pack se valida por completo antes de incorporarse; IDs duplicados, módulos desconocidos, referencias ausentes, archivos faltantes, DLLs o rutas que salgan de la carpeta lo desactivan con un diagnóstico localizado.

Los manifiestos se leen antes de incorporar contenido, por lo que las etapas pueden referenciar unidades o perfiles de otro pack. Los paquetes solo nativos declaran `assetBundles: []` y omiten `assetBundleManifestPath`; los demás declaran de 1 a 128 bundles. El loader calcula dependencias entre packs y entre bundles, carga primero los requisitos y rechaza ciclos o dependencias que falten. Cada perfil puede componer hasta ocho módulos compilados; el manifiesto solo elige IDs y pasa parámetros de texto. Los bundles tienen límites de tamaño: 64 MiB para el bundle de manifiesto, 512 MiB por bundle de contenido y 2 GiB en total por sesión. `sample:scheduled-base-build` solicita construir edificios que ya están en el deck AI; `sample:reactive-base-defense` aporta una decisión táctica de respuesta acotada. Ambos añaden comportamiento después de la actualización AI nativa y no distribuyen código en los paquetes.

Las unidades reciben claves de recurso namespaced para que Harmony pueda resolver sus prefabs e imágenes. En modo `nativeTemplateId`, se clona el prefab del juego y se le asigna la identidad nueva; si se indica `visualAssetName` junto con `visualRootPath`, el loader desactiva los Renderer nativos de esa rama y agrega un prefab visual del bundle al mismo nodo. Los componentes de gameplay nativos quedan en su jerarquía. Los prefabs visuales no pueden incluir componentes `MonoBehaviour`; los Renderer sin materiales heredan el primer material `MeshRenderer` de la rama nativa. Esta integración no reasigna animaciones o rigs de la unidad. Una etapa de plantilla clona `StageParametersScriptableObject` y comparte sus referencias existentes de mapa y pool; un AI de plantilla conserva su resource name nativo y aplica su módulo a cada aparición de ese AI. Los modelos completos, mapas o prefabs AI aún necesitan referencias serializadas válidas en bundles creados con Unity.

## Authoring de AssetBundles en Unity

En `unity/RogueCommandAssetAuthoring/`:
- **Editor Builder:** `Assets/Editor/RogueCommandAssetBundleBuilder.cs` provee el menú `Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...`.
- **Modelo de muestra:** `tools/Build-SampleScoutModel.py` ejecuta Blender 4.5.14 LTS para generar el `.blend` fuente y exportar `SandlanceScout.fbx`; el FBX contiene 31 piezas mesh de baja poligonización. Para regenerarlo: `.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-SampleScoutModel.py`. El Editor importa ese modelo, crea siete materiales y lo guarda como prefab visual sin scripts. El pack lo superpone al subárbol visual nativo `PCXBaseTank/Vehicle`, por lo que conserva la jerarquía de gameplay del juego.
- **Scaffolding:** `Assets/Editor/RogueCommandVisualSampleScaffolder.cs` ofrece el menú `Rogue Command > Asset Bundles > Build Sample Native Visual Pack`. Requiere el paquete integrado `com.unity.modules.assetbundle` `1.0.0` en `Packages/manifest.json`, fuerza una reconstrucción limpia y escribe el producto intermediario bajo `artifacts/asset-bundles/`, fuera de `Assets/`. Copia el bundle y su manifiesto al pack. El `RogueCommandSamplePackScaffolder.cs` original sigue cubriendo prefabs completos, valida SHA-256/MVID y exige las referencias de gameplay serializadas, que Unity Editor no puede crear aquí desde el `Assembly-CSharp.dll` precompilado.
- **Estado del sample:** Unity 2021.3.45f2 salió con código 0, reabrió el bundle con `AssetBundle.LoadFromFile` y cargó `ScoutTankVisual`. El validador del Editor comprobó 31 MeshRenderers, 31 MeshFilters, 31 meshes, siete materiales y ningún `MonoBehaviour` o componente faltante. UnityPy confirmó el encabezado de Unity 2021.3.45f2 y el inventario serializado. SHA-256 del bundle: `c418a547e600922378731ca8781bb32c06c784eda27b3b5796ddbb62c98b89ce`; manifiesto: `554304831deff815d9dde89d21e668a04fcae8a641b1a143c139278e3f9e70d4`. Esto valida el producto en el Editor, no que Rogue Command lo cargue o renderice durante una partida. Los placeholders anteriores se conservan fuera del proyecto en `artifacts/unverified-authoring-output/2026-10-17/`; no son instalables.
- **Ejecución headless del flujo visual:** `Unity.exe -batchmode -nographics -projectPath <ruta> -executeMethod RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.BuildSampleNativeExpansion -quit`. Si Unity Package Manager vuelve a rechazar la cadena TLS de `packages.unity.com`, se puede pasar la raíz validada por Windows con `NODE_EXTRA_CA_CERTS` solo al proceso del Editor; no desactivar la validación TLS.

`unity/RogueCommandAssetAuthoring/` es el proyecto Unity de autoría de este repositorio y se conserva separado del proyecto `Rogue Evolution` que el usuario añadió a Unity Hub. Se mantienen en Git `ProjectSettings`, `Packages/manifest.json`, `Packages/packages-lock.json` y los ajustes compartidos de Version Control/Unity Cloud. `.plastic/` contiene estado local del workspace Plastic, por lo que permanece en disco pero se excluye del push. Las salidas regenerables bajo `/artifacts/` y el antiguo `Assets/RogueCommandExpansionSample/BundleOutput/` se ignoran; en cambio, los bundles declarados por un pack bajo `content/packs/<id>/bundles/` sí son archivos distribuibles y quedan versionados.

## Análisis de assets

El inventario y el inspector de Unity son herramientas de solo lectura; sus informes regenerables quedan fuera de Git:

```powershell
.venv\Scripts\python.exe tools\asset_inventory.py `
  --asset-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data" `
  --output-dir docs\research\generated
.venv\Scripts\python.exe tools\inspect_unity_objects.py `
  --game-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command" `
  --asset-file resources.assets `
  --path-id 384970 --path-id 384977 --path-id 384981 `
  --output docs\research\generated\progression-objects.json
```

La identidad y cobertura de la investigación están en [docs/research/BASELINE.md](docs/research/BASELINE.md) y [docs/research/ASSET-CATALOG.md](docs/research/ASSET-CATALOG.md). La línea base registra el estado original del repositorio; la implementación posterior se resume aquí y en el esquema.

## Evidencia local actual

- `dotnet build -c Release` del plugin completó con 0 errores y 0 advertencias. SHA-256 de la DLL incluida en el ZIP: `78e826044389a835f089c36f208eb215d1afe1af0995529b55951c759a2980a4`.
- El validador local reporta 64/65 comprobaciones; Windows omitió el caso de symlink porque este entorno no concede el privilegio necesario. Ambos manifiestos pasan la validación del paquete. El harness confirma que el registro añade las cachés enteras y flotantes de stats, las revierte junto con filas e índices ante un fallo inducido y conserva una fila indexada con ambas cachés si una limpieza compartida falla; también comprueba los aliases AI, referencias entre packs, progresiones y preflight de mapas. La etapa reactiva comprueba su referencia a Sandlance con y sin el snapshot del pack de unidad.
- El ZIP de `artifacts/packages/RogueCommandExpansionLoader-0.1.0.zip` se leyó íntegro: nueve entradas, rutas seguras y solo la DLL del loader; incluye ambos manifiestos, esquema, bundle visual y pack de AI táctico. La auditoría confirmó que no contiene bibliotecas de BepInEx, Harmony, ensamblados del juego, PDB ni `Library/`. SHA-256 del ZIP: `860a190e245737b23dae41c6c452896dbd6d3b00609a1e296d7aec7284d0f51d`.
- Unity 2021.3.45f2 generó un bundle de 111,153 bytes con `ScoutTankVisual`, 31 MeshRenderers, 31 MeshFilters, 31 meshes y siete materiales; no hay MonoBehaviours ni componentes faltantes. SHA-256: `c418a547e600922378731ca8781bb32c06c784eda27b3b5796ddbb62c98b89ce`; manifiesto: `554304831deff815d9dde89d21e668a04fcae8a641b1a143c139278e3f9e70d4`.
- La inspección estática de `EntityBalancingStore` volvió a verificar la identidad 1.0.9, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, y confirmó las dos cachés anidadas por ID (`ChangeableIntValueCache` y `ChangeableFloatValueCache`), evidencia REA `ev_671c16715af8c93c83b40e0532e0fcce836f8c296f8efb851416635861119e67`. El mutator ahora añade entradas de caché vacías para cada fila nueva y las incluye en el rollback transaccional. Es inspección estática más validación local; no hay confirmación de instanciación en una partida.
- **Revisión local de código:** revisión manual de carga y retención de bundles, validación de assets, commit por paquete, rollback de filas, índices, mappings de fábricas y cachés de stats, integración idempotente de progresión, ciclo de vida de módulos AI y resolución de aliases de etapa. Es revisión estática local; no sustituye ejecución en una partida. La evidencia estática del overload tipado `Resources.Load(string, Type)` es `ev_07fa217587c15e7c71aefa972647c15f656180a8cb767b957aad078352b51fa8`.
- **Límite de ejecución:** no hay evidencia de que un mod de Rogue Command cargue unidades, etapas o AI en una partida. Esta versión solo tiene evidencia de inspección estática, compilación y validación offline. Una prueba de integración debe iniciarse mediante Steam y usar un perfil reversible.
- **Runtime histórico, antes del cambio de carga tipada:** un clon inició con 11 hooks, pero registró 39 excepciones de `TMP_Settings.defaultStyleSheet`; no llegó a crear mapa ni entidad. Evidencia conservada en `artifacts/runtime-smoke/native-data-smoke-7-evidence/` y `artifacts/runtime-smoke/native-data-smoke-7-window.png`.
- **Runtime histórico en clon visible:** BepInEx 5.4.23.5 cargó el plugin e instaló 10 hooks; el probe llegó a `Awake`, pero el clon arrancado directamente registró `SteamAPI_Init() failed` y no emitió `sceneLoaded`. Esa prueba no aisló el origen de la falta de escena. Evidencia: `artifacts/runtime-smoke/typed-load-probe-visible-1/`.
- **Runtime más reciente en clones con perfil:** al clon se le copiaron seis archivos de perfil locales que faltaban; se excluyeron `steam_autocloud.vdf` y cualquier override de AppID. Tanto la ejecución con loader como el control vanilla solicitaron `MainMenu`, permanecieron vivos 120 s / 30 s respectivamente, y no emitieron el primer `Start`/heartbeat ni un evento `sceneLoaded`. En la ejecución con loader, BepInEx completó el chainloader e instaló 10 hooks, el probe alcanzó `Awake`/`OnEnable`, pero no se detectó el pack ni se llamó a sus puntos de activación (`CreateStageMap`/`EntityFactory`). El control vanilla se comportó igual sin BepInEx; este resultado no atribuye el bloqueo al loader y tampoco demuestra carga de unidades, mapas o AI. Evidencia: `artifacts/runtime-smoke/profile-clone-corrected-long-1/` y `artifacts/runtime-smoke/profile-clone-control-vanilla-1/`.
- El análisis estático del `Assembly-CSharp.dll` fijado también encontró que `ProfileManager` compone `.\Profiles\Profile<n>\savegame.dat`; `LevelProgression.Awake` carga un guardado existente y, si no existe, crea el mapa de etapas. Referencia REA: `ev_387e08b970a05b9e05a88e8a2800b4e0a77b52b5b4bbaa01e352fd33c9fa1d30`. Esto es inspección estática, no prueba de runtime.
- **Aislamiento de las pruebas:** el clon recuperó el plugin original (`ABD8B65A90C0D4EED9CDF090C401C50CB932A0BDF425590D9DB4C3E3271A5648`) y el log original de BepInEx (`76A390463EC153896E02A911511641B8FF34CEAAB287C57E84FE014260813963`). Los perfiles copiados, probe y pack se retiraron; los archivos generados de LocalLow/registro quedaron archivados bajo las carpetas de evidencia de cada smoke. El postflight confirma que no siguen activos el proceso, la ruta original de preferencias ni la clave original de registro. La instalación de Steam no se modificó ni se lanzó y el proyecto de Unity Hub se conservó.
