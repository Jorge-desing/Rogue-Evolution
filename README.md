# Rogue Evolution — expansión de contenido para Rogue Command

Este repositorio desarrolla un **mod de expansión para añadir unidades, etapas y AI a Rogue Command**. El cargador externo BepInEx 5 con Harmony es la infraestructura que permite registrar y ejecutar ese contenido; no es el objetivo final del mod. Los paquetes de muestra incluyen cinco unidades, sus edificios de producción, cuatro etapas y cuatro perfiles AI. La sección de evidencia distingue lo observado en Steam de las rutas pendientes de aceptación.

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

Las skills del repositorio en `.agents/skills/` orientan a agentes por área: `rogue-command-unit-authoring`, `rogue-command-3d-model-authoring`, `rogue-command-assetbundle-authoring`, `rogue-command-stage-authoring`, `rogue-command-ai-authoring`, `rogue-command-game-research`, `rogue-command-pack-validation` y `rogue-command-steam-runtime-validation`. Se basan en el contrato y la evidencia del handoff; no elevan una validación local a prueba de juego.

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
dotnet run --artifacts-path artifacts/validation-tests --project tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj -- .
```

El ejecutable enlaza los DTO y validadores del plugin. `--artifacts-path` deja los binarios e intermediarios en `artifacts/` y ejecuta la salida que acaba de compilarse. Comprueba directamente ambos manifiestos de muestra, crea fixtures temporales para los casos de rutas, referencias, IDs y DLLs, y verifica el rechazo de versión de Rogue Command/Unity, SHA-256 y MVID incompatibles. Usa un stub local para `Application.unityVersion`; no carga el juego ni comprueba tipos Unity serializados, AssetBundles reales, Harmony o decisiones AI durante una partida.

Para comprobar que los nombres de assets declarados existen en los bundles y que sus tipos serializados coinciden con lo esperado:

```powershell
.\.venv\Scripts\python.exe .\tools\validate_content_bundle_references.py .\content\packs\sample-native-expansion
.\.venv\Scripts\python.exe .\tools\validate_native_visual_roots.py .\content\packs\sample-native-expansion
```

Este chequeo lee los AssetBundles con UnityPy y no modifica el pack ni la instalación del juego. Revisa las entradas de bundle, no valida que el contenido funcione dentro de una partida.

El segundo comando requiere Python 3.11 o posterior y las dependencias fijadas en `requirements.txt`. Verifica versión, SHA-256 y MVID del juego, además del hash de `resources.assets`, y comprueba que cada ruta visual nativa sea única y contenga renderers. La selección del recurso y su aspecto dentro de una partida se comprueban por separado.

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

`content/packs/sample-native-expansion/content.json` añade cinco unidades con balance y modelos propios sobre controladores nativos: Sandlance en `PCXBaseTank/Vehicle`, Bike en `BombBike/GameObject/BombBike`, Colossus en `ArtilleryTank/Tank`, Ironclad en `GatlingWalker/RoboMarine_Mesh` y Surge en `LightningTank/PCX TankHolder`. El overlay conserva la jerarquía de gameplay y no remapea animaciones o rigs. Cada unidad tiene una nueva fábrica, según la tabla siguiente.

La etapa `training-variant` añade una variante procedural del LandscapeGenerator nativo, con semilla fija, tamaño 31 y nombre localizado. El loader la expone durante la generación de esa etapa y restaura las listas temporales mediante un finalizer de Harmony. Los perfiles AI nativos se clonan con un rango calculado a partir de sus etapas, preservando sus plantillas globales. Las nuevas etapas reciben también un fallback nativo validado en un snapshot que conserva los nueve slots originales.

Los pools clonados conservan los filtros nativos y admiten los planos de las fábricas correspondientes a los productos declarados por cada etapa. El fallback genérico sin pool conserva la selección aleatoria del juego, pero filtra las fábricas del loader según la etapa activa. Los mapas nuevos guardan el orden de etapas en una propiedad JSON adicional de `StageMap`; al reanudar se valida contra el contenido cargado. Las partidas antiguas sin esa marca, con marcadores inválidos o con etapas propias ausentes omiten de forma segura las fábricas del loader en ese fallback. `sample-expansion` documenta la ruta de prefabs Unity completos y permanece fuera del instalador hasta disponer de esos assets. Los AssetBundles contienen recursos serializados; los módulos AI se compilan en el plugin.

El perfil `sample-reactive-ai:base-defense` añade la Moto de Asalto Rápido al roster de la base 0 de una copia AI aislada. En la prueba dirigida por Steam del 17 de octubre, anterior a la integración de edificios propios, esa unidad apareció activa con tag `AI` en la etapa añadida 12; también se observaron 13 mapas de etapa, la AI inicializada y los tooltips localizados de sus 20 pools. Ese acceso usó un salto de diagnóstico a la etapa. La producción desde los edificios y una decisión de contraataque efectiva requieren su propia comprobación.

## Unidades y edificios de producción

Cada unidad del mod declara un edificio propio en `productionBuilding`. El edificio y la unidad tienen IDs nuevos y se registran como pareja: el edificio produce exclusivamente esa unidad. Las recompensas aleatorias ofrecen el plano del edificio y aplican al producto los filtros nativos del pool; los manifiestos de etapa y los rosters AI siguen usando el ID de la unidad.

| Unidad | Edificio | ID del edificio |
|---|---|---|
| Sandlance Scout | Planta de ensamblaje Sandlance | `sample-native:scout-tank-factory` |
| Colossus Siege Platform | Fábrica de asedio Coloso | `sample-native:heavy-artillery-factory` |
| Ironclad Gatling Walker | Fundición de caminantes Ironclad | `sample-native:assault-walker-factory` |
| Skirmish Strike Bike | Taller de motos de asalto | `sample-native:skirmish-bike-factory` |
| Surge Arc Tank | Planta de tanques Surge Arc | `sample-native:surge-tank-factory` |

Estos edificios parten de los prefabs de sus fábricas nativas, con identidad, balance y localización propios. El registro conserva las relaciones originales del juego. El campo es obligatorio para todas las unidades añadidas, incluidas las de prefab completo; el contrato detallado está en `content/schema/README.md`. Los cambios de producción deben verificarse colocando el edificio y observando la unidad producida durante una partida por Steam.

**Comprobación por Steam (2026-10-17):** las cinco parejas se registraron, conservaron las relaciones de las fábricas originales y resolvieron sus nombres en español. La prueba colocó los cinco edificios mediante la API nativa y ordenó producción no instantánea sin coste: las cinco colas terminaron y aparecieron sus cinco productos activos con tag `Player`. Los pools de las cuatro etapas devolvieron los planos de fábrica correspondientes a sus productos. La selección aleatoria y compra/colocación completas por interfaz siguen pendientes de una partida ordinaria. Detalles, migración y evidencia en [PRODUCTION-BUILDINGS.md](docs/research/PRODUCTION-BUILDINGS.md).

El paquete final también adapta la distancia de edificios AI para etapas propias añadidas: en Steam, el índice 12 usó la franja nativa configurada (29 celdas) y dejó de emitir `Unknown stage`. La lista de fallbacks conserva nueve entradas nativas y devuelve un snapshot de 13 para la progresión ampliada.

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

Los manifiestos se leen antes de incorporar contenido, por lo que las etapas pueden referenciar unidades o perfiles de otro pack. Los paquetes solo nativos declaran `assetBundles: []` y omiten `assetBundleManifestPath`; los demás declaran de 1 a 128 bundles. El loader calcula dependencias entre packs y entre bundles, carga primero los requisitos y rechaza ciclos o dependencias que falten. Cada perfil puede componer hasta ocho módulos compilados; el manifiesto solo elige IDs y pasa parámetros de texto. Los bundles tienen límites de tamaño: 64 MiB para el bundle de manifiesto, 512 MiB por bundle de contenido y 2 GiB en total por sesión. `sample:scheduled-base-build` solicita construir edificios que ya están en el deck AI; `sample:reactive-base-defense` aporta una decisión táctica de respuesta acotada y admite unitId para limitar el envío a la unidad configurada. Ambos añaden comportamiento después de la actualización AI nativa y no distribuyen código en los paquetes.

Las unidades reciben claves de recurso namespaced para que Harmony pueda resolver sus prefabs e imágenes. En modo `nativeTemplateId`, el loader copia la fila de balance y clona el prefab del juego; la identidad, balance y cambios visuales se aplican solo a esas copias. Si se indica `visualAssetName` junto con `visualRootPath`, se desactivan los Renderer nativos de esa rama en el clon y se agrega el modelo del bundle al mismo nodo. Los componentes de gameplay quedan en la jerarquía clonada, pero las animaciones o rigs no se reasignan. Una etapa de plantilla clona `StageParametersScriptableObject` antes de sus overrides y la nueva etapa se agrega a las progresiones. Los perfiles AI nativos deben usar `scopeToReferencedStages: true`, que crea un alias `RCL_` aislado para sus módulos; el validador rechaza perfiles que afectarían el comportamiento nativo compartido. Las unidades, etapas y perfiles usan IDs namespaced y los conflictos desactivan el pack. Los modelos completos, mapas o prefabs AI aún necesitan referencias serializadas válidas en bundles creados con Unity.

## Authoring de AssetBundles en Unity

Las referencias de bundle deben coincidir con las entradas que Unity generó. Usa la ruta relativa completa del asset siempre que el contrato lo permita, especialmente para Sprites; la carga tipada del plugin evita seleccionar la textura cuando el archivo también expone un Sprite.

En `unity/RogueCommandAssetAuthoring/`:
- **Editor Builder:** `Assets/Editor/RogueCommandAssetBundleBuilder.cs` provee el menú `Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...`.
- **Modelos 3D:** La guía [UNIT-VISUAL-AUTHORING](docs/research/UNIT-VISUAL-AUTHORING.md) registra los modelos, comandos y evidencia. Sandlance conserva 78 piezas editables y exporta 7 lotes FBX (2.17 × 2.52 × 1.57 m); Skirmish Bike conserva 59 y exporta 7 lotes (1.20 × 2.20 × 1.37 m); Colossus conserva 104 y exporta 8 lotes (2.58 × 4.74 × 1.86 m); Ironclad conserva 74 y exporta 5 lotes (1.83 × 1.98 × 2.45 m); Surge Arc Tank conserva 94 y exporta 6 lotes (1.99 × 2.78 × 1.52 m). El cañón del Colossus rebasa la longitud aproximada del prefab ArtilleryTank; su espaciado debe verificarse en partida.
- **Tarjetas 2D:** las cinco unidades tienen una tarjeta PNG generada con ImageGen a partir de sus renders Blender. El scaffolder importa cada archivo como Sprite de 128 × 128, sin dibujarlo por código, y falla si falta una imagen o la importación no coincide.
- **Scaffolding:** Assets/Editor/RogueCommandVisualSampleScaffolder.cs crea cinco prefabs visuales sin scripts, asigna materiales por lote, construye el bundle y lo vuelve a abrir en Unity para validar renderers, materiales, geometría y Sprites. Los componentes de gameplay siguen en los prefabs nativos; no se remapean animaciones ni rigs.
- **Bundle visual actual:** Unity 2021.3.45f2 reconstruyó `sample-native-expansion.bundle` con los cinco prefabs visuales y las cinco tarjetas. UnityPy encontró cinco Sprites de 128 × 128, y el validador offline resolvió las diez referencias de modelo/tarjeta. El bundle mide 840,628 bytes (SHA-256 `273ff7abbd6f8ccbd52cfbf79972e004f5b9c450519111b266a995dd00ad292d`); el manifiesto mide 1,588 bytes (SHA-256 `668b51f6c8947e9e9441d2bb74a6e92667f3d3e2b5008add374c70441ea1b137`). Es evidencia de build/importación local, no de carga o renderizado durante una partida.

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

- `dotnet build -c Release` del plugin completó con 0 errores y 0 advertencias. El DLL actual mide 318,976 bytes; SHA-256: `81272d043bf05f24040cd61d164be9aff82e495a4a6357123bd2a34c9fc1742a`.
- El harness local pasó 113/114 comprobaciones; Windows omitió una prueba de symlink porque este entorno no concede el privilegio necesario. Los casos cubren registro y rollback de filas, índices, cachés y mappings de fábricas; aliases AI aislados, referencias entre packs, progresiones, preflight de mapas, localizaciones de las cinco unidades, y parsers AI con IDs nativos y namespaced.
- El ZIP de `artifacts/packages/RogueCommandExpansionLoader-0.1.0.zip` se leyó íntegro: nueve entradas y rutas seguras. Incluye los dos packs distribuibles, el esquema y el bundle visual; su única DLL compilada es el loader. No contiene bibliotecas de BepInEx, Harmony, ensamblados del juego, PDB ni `Library/`. Mide 663,412 bytes (SHA-256 `1eb72144a21027733cc4c3f4b441f97cc9ae784f76eccc52ffb67ef98ddd39a4`); el DLL incluido coincide con el hash local anterior.
- Unity 2021.3.45f2 generó el bundle actual de 840,628 bytes. UnityPy encontró cinco Sprites de tarjeta de 128 × 128 píxeles y el validador offline resolvió las diez referencias de modelo/tarjeta. SHA-256 del bundle: `273ff7abbd6f8ccbd52cfbf79972e004f5b9c450519111b266a995dd00ad292d`; manifiesto AssetBundle: `668b51f6c8947e9e9441d2bb74a6e92667f3d3e2b5008add374c70441ea1b137`. Esto es evidencia local de Unity/UnityPy, no de renderizado en partida.
- La inspección estática de `EntityBalancingStore` volvió a verificar la identidad 1.0.9, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, y confirmó las dos cachés anidadas por ID (`ChangeableIntValueCache` y `ChangeableFloatValueCache`), evidencia REA `ev_671c16715af8c93c83b40e0532e0fcce836f8c296f8efb851416635861119e67`. El mutator añade entradas de caché vacías para cada fila nueva y las incluye en el rollback transaccional. En la prueba diagnóstica de stage 12 se observó un Skirmish Bike activo en el roster AI; eso no verificó su oferta roguelike ni su producción por edificio. En la ruta aleatoria posterior no se observó una instancia activa de unidad del mod.
- **Revisión local de código:** revisión manual de carga y retención de bundles, validación de assets, commit por paquete, rollback de filas, índices, mappings de fábricas y cachés de stats, integración idempotente de progresión, ciclo de vida de módulos AI y resolución de aliases de etapa. Es revisión estática local; no sustituye ejecución en una partida. La evidencia estática del overload tipado `Resources.Load(string, Type)` es `ev_07fa217587c15e7c71aefa972647c15f656180a8cb767b957aad078352b51fa8`.
- **Runtime controlado mediante Steam, 2026-10-17:** con Rogue Command 1.0.9 iniciado por Steam AppID `1461910`, el bridge confirmó Unity `2021.3.45f2`, BepInEx `5.4.23.5` y 12 hooks verificados. El log registra `sample-native:expansion` con 5 unidades, 3 etapas y 3 perfiles AI, `sample-reactive-ai:expansion` con 1 etapa y 1 perfil, y la escena `RunSetup`. La búsqueda del bridge encontró el clon `RCL_Native_sample-native_scout-tank` bajo `DontDestroyOnLoad`, pero inactivo: esto confirma carga/registro de contenido y creación del clon dormido, no instanciación mediante `EntityFactory`, generación de mapa, callback AI ni renderizado del modelo en combate. Esa sesión usó el bundle anterior de 336,503 bytes (`dcb8a28554959f813e97a7f6d0dc93b718138dda1ed3b161c641d805989c471d`), antes del ajuste final de ancho; el bundle actual de 346,164 bytes tiene evidencia local de Blender/Unity/UnityPy, no de renderizado en juego. Los tres archivos del perfil siguen coincidiendo con el respaldo. El log final y el manifiesto con hashes están en `artifacts/runtime-smoke/steam-loader-proof-20261017-87c448ca/`. La retirada del loader temporal y del log instalado fue bloqueada por la política de comandos; siguen en `BepInEx/plugins/RogueCommandExpansionLoader/`, `BepInEx/plugins/RogueCommandExpansionLoader.dll` y `BepInEx/LogOutput.log`.
- **Seguimiento de partida roguelike por Steam, 2026-10-17:** una nueva ruta aleatoria llegó a `Game` tras pasar por nodos de recompensa. La única oferta de cartas observada contenía `LittleLighter`, `FireDrag` y `JeepWithMachineGun`; no demuestra ausencia global de productos del mod porque cada oferta es una muestra aleatoria. En `Game` inició el `DefaultAiBehaviour` nativo (`TickHasStarted=True`, `ActualAiStartBaseCount=1`), pero en esta partida no se observó una instancia activa de unidad del mod ni una decisión del módulo reactivo. Las plantillas `RCL_Native_sample-native_*` quedaron inactivas en `DontDestroyOnLoad`. Algunos callbacks de recompensa se invocaron mediante el bridge. Evidencia en partida; alcance y siguiente prueba en [docs/research/AGENT-HANDOFF.md](docs/research/AGENT-HANDOFF.md).
- **Límite de ejecución:** el stage 12 diagnóstico confirmó generación de mapa, attachment del perfil reactivo y entrada de Skirmish Bike al roster AI; la decisión de contraataque sigue sin verificarse. La ruta aleatoria confirmó el arranque de AI nativa, pero su única oferta observada fue nativa. Aún falta seguir una oferta elegible modded a través de colocación de edificio y producción, comprobar el renderizado de la unidad y observar una decisión del módulo reactivo.
- **Runtime histórico, antes del cambio de carga tipada:** un clon inició con 11 hooks, pero registró 39 excepciones de `TMP_Settings.defaultStyleSheet`; no llegó a crear mapa ni entidad. Evidencia conservada en `artifacts/runtime-smoke/native-data-smoke-7-evidence/` y `artifacts/runtime-smoke/native-data-smoke-7-window.png`.
- **Runtime histórico en clon visible:** BepInEx 5.4.23.5 cargó el plugin e instaló 10 hooks; el probe llegó a `Awake`, pero el clon arrancado directamente registró `SteamAPI_Init() failed` y no emitió `sceneLoaded`. Esa prueba no aisló el origen de la falta de escena. Evidencia: `artifacts/runtime-smoke/typed-load-probe-visible-1/`.
- **Runtime más reciente en clones con perfil:** al clon se le copiaron seis archivos de perfil locales que faltaban; se excluyeron `steam_autocloud.vdf` y cualquier override de AppID. Tanto la ejecución con loader como el control vanilla solicitaron `MainMenu`, permanecieron vivos 120 s / 30 s respectivamente, y no emitieron el primer `Start`/heartbeat ni un evento `sceneLoaded`. En la ejecución con loader, BepInEx completó el chainloader e instaló 10 hooks, el probe alcanzó `Awake`/`OnEnable`, pero no se detectó el pack ni se llamó a sus puntos de activación (`CreateStageMap`/`EntityFactory`). El control vanilla se comportó igual sin BepInEx; este resultado no atribuye el bloqueo al loader y tampoco demuestra carga de unidades, mapas o AI. Evidencia: `artifacts/runtime-smoke/profile-clone-corrected-long-1/` y `artifacts/runtime-smoke/profile-clone-control-vanilla-1/`.
- El análisis estático del `Assembly-CSharp.dll` fijado también encontró que `ProfileManager` compone `.\Profiles\Profile<n>\savegame.dat`; `LevelProgression.Awake` carga un guardado existente y, si no existe, crea el mapa de etapas. Referencia REA: `ev_387e08b970a05b9e05a88e8a2800b4e0a77b52b5b4bbaa01e352fd33c9fa1d30`. Esto es inspección estática, no prueba de runtime.
- **Aislamiento de los smoke en clones:** en esas pruebas históricas el clon recuperó el plugin original (`ABD8B65A90C0D4EED9CDF090C401C50CB932A0BDF425590D9DB4C3E3271A5648`) y el log original de BepInEx (`76A390463EC153896E02A911511641B8FF34CEAAB287C57E84FE014260813963`). Los perfiles copiados, probe y pack se retiraron; los archivos generados de LocalLow/registro quedaron archivados bajo las carpetas de evidencia de cada smoke. Ese lote no modificó ni inició la instalación de Steam; se conserva separado de la prueba Steam descrita arriba y del proyecto Unity Hub del usuario.
