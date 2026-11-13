# Rogue Evolution — expansión de contenido para Rogue Command

Este repositorio desarrolla un **mod de expansión para añadir unidades, etapas y AI a Rogue Command**. El cargador externo BepInEx 5 con Harmony es la infraestructura que permite registrar y ejecutar ese contenido; no es el objetivo final del mod. Los manifests del repositorio declaran diez unidades y diez edificios de producción, cinco etapas y cinco perfiles AI. El ZIP instalable distribuye nueve unidades en `sample-native-expansion` y la etapa/perfil de `sample-reactive-ai`; `sample-expansion` permanece fuera hasta contar con sus assets completos.

El trabajo prioriza contenido original que se pueda jugar: unidades con identidad visual y balance propios, etapas con una experiencia de mapa reconocible y perfiles de AI con decisiones nuevas. Cada entrega debe separar la inspección estática, la compilación local y la comprobación en una partida iniciada por Steam. Conserva `mod/manifest.json` para los ajustes TSV de balance que admite el juego de forma nativa.

Para retomar el trabajo en otro chat o con otro agente, comienza por [docs/research/AGENT-HANDOFF.md](docs/research/AGENT-HANDOFF.md). Resume el estado del contenido, el cargador de soporte, comandos, proyecto Unity y límites de la evidencia. La búsqueda de herramientas y sus usos recomendados está en [docs/research/CONTENT-AUTHORING-TOOLS.md](docs/research/CONTENT-AUTHORING-TOOLS.md).

Para ejecutar todas las pruebas Python: `python -m tools.run_python_tests --output artifacts/python-tests/report.json`. El ejecutor usa CPython y el Blender fijado según los imports de cada módulo y devuelve un código distinto de cero ante fallos, timeouts o entornos requeridos ausentes. `--mode unit` y `--mode blender` permiten comprobaciones parciales; el modo predeterminado `all` ejecuta dos workers CPython aislados y un worker exclusivo para integraciones que abren Blender; los módulos que requieren `bpy` se ejecutan después, en serie. `--jobs 1` conserva la ejecución totalmente serial de referencia y `--jobs 2..4` fija los workers unitarios. El JSON registra inventario, resultados y tiempos; `--blender` y `--timeout` permiten fijar el ejecutable y el límite por proceso. La preparación mide `setUp` por caso; los fixtures de clase/módulo quedan incluidos en ejecución.

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

Las skills del repositorio en `.agents/skills/` orientan a agentes por área: `rogue-command-unit-authoring`, `rogue-command-3d-model-authoring`, `rogue-command-pbr-texture-authoring`, `rogue-command-pbr-texture-validation`, `rogue-command-armorpaint-texture-authoring`, `rogue-command-material-maker-texture-authoring`, `rogue-command-blender-pbr-45`, `blendsmith`, `rogue-command-mech-rigging-authoring`, `rogue-command-assetbundle-authoring`, `rogue-command-stage-authoring`, `rogue-command-ai-authoring`, `rogue-command-game-research`, `rogue-command-pack-validation`, `rogue-command-steam-runtime-validation`, `rogue-command-audio-authoring`, `rogue-command-animation-authoring`, `rogue-command-balance-simulation`, `rogue-command-mutator-hazard-authoring` y `rogue-command-vfx-authoring`. La guía de [flujo PBR](docs/research/PBR-TEXTURE-WORKFLOW.md) conecta Blender, las herramientas opcionales y el importador de Unity. Las skills se basan en el contrato y la evidencia del handoff; no elevan una validación local a prueba de juego.

Unity 2021.3.45f2 ya está instalado para compilar AssetBundles. Blender 4.5.14 LTS y los inspectores de runtime UnityExplorer 4.8.2 y Runtime Unity Editor 6.3.2.1 se guardan bajo `.tools/`; esta carpeta está ignorada por Git. Para volver a preparar las copias locales en otro checkout:

```powershell
.\tools\Setup-ContentAuthoringTools.ps1 -Blender -UnityExplorer
```

Hardflow 1.23.0 es una herramienta opcional para modelado geométrico manual de vehículos. Instálala con el Blender fijado y su perfil aislado del proyecto:

```powershell
.\tools\Setup-Hardflow.ps1
.\tools\Start-BlenderWithHardflow.ps1
```

El instalador verifica el SHA-256 de la release oficial y guarda la extensión, configuración y preferencias bajo `.tools/`; no modifica el perfil global de Blender. Usa Hardflow para cortes de placas, rejillas, pernos, cordones y conducciones. Blender y sus exportadores siguen siendo la autoridad del mesh/FBX; conserva la jerarquía, pivotes, sockets, `Color RGBA` y el presupuesto geométrico dinámico. Los UVs y las texturas finales siguen el flujo existente con Blender y Material Maker; Hardflow no es requisito para los generadores headless ni para compilar packs.

Los detalles reutilizables se guardan como recetas geométricas en `tools/hard_surface_geometry_presets.py`, `tools/hard_surface_geometry_presets_v2.py`, `tools/hard_surface_geometry_presets_functional.py` y `tools/hard_surface_geometry_presets_detailing.py`. El registro `canonical-visual-module-roles.json` cubre las 47 fábricas con su slot, función mecánica y roles visuales permitidos. Las declaraciones activas enlazan 57 módulos visuales, sin cambios de gameplay, en 35 variantes y usan las 47 recetas geométricas al menos una vez; cada enlace selecciona explícitamente una receta compatible. El builder ejecuta esa fábrica (con rutas específicas para ERA, hatch de servicio y conector de Gravitas, y dispatch genérico para el resto), mientras el piloto Hardflow cubre el catálogo completo. El build completo ya generó y sincronizó los FBX LOD, prefabs y bundles Level 2 bajo la autorización acotada del usuario; todos siguen marcados `UNCALIBRATED_USER_AUTHORIZED`, sin considerarse calibrados ni evidencia de rendimiento.

`service_hatch_recessed_v1` se aplica al hatch de la sponson Level 2 de Salvage Hauler y valida el hatch existente del Bulwark. `era_brick_cassette_v1` crea casetes con placa de apoyo, juntas cruzadas rebajadas, retenedores laterales y pernos. El Bulwark conserva sus seis casetes frontales en Level 1; la variante Battleship Tungsten recibe diez laterales, cinco por costado, como módulo ERA Level 2 mediante `bulwark_side_skirt_array_v2`. Las placas de apoyo se ajustan al plano lateral del FBX actual en X=±1,312 m. `coaxial_service_connector_recessed_v1` añade un conector al panel del módulo de contención de Gravitas Breaker. En el Gravitas Breaker principal, el builder también reproduce conectores de alimentación rebajados sobre la recámara y fijaciones countersunk en los retenedores de trunnion. Los manifiestos mantienen `geometryPreset` y `geometryLayout` separados del preset PBR de Material Maker. El replay de producción usa operaciones estándar de Blender, sin requerir Hardflow.

El arreglo lateral ERA del Bulwark está modelado en espacio local de unidad. Su builder normaliza `Socket_Hardpoint_ERA` al origen únicamente en el prefab anfitrión de Unity. El FBX Level 1 actualizado, que es el host importado por el prefab, tiene planos de apoyo en X=±1,312 m; la variante L2 monta cinco casetes por costado y conserva las seis ERA frontales de Level 1. La revisión local combina el FBX de host actual con el módulo, además de vistas cercanas de ambas filas. Estas comprobaciones no demuestran apariencia en una partida ni despeje de piezas móviles.

El builder del Command Relay integra la cabina y el shelter de electrónica en una carcasa continua con el piso adaptado a los dos coamings existentes; deja una junta de 1 mm entre ambos collares. Adapta los paneles C2 al perfil faceteado, asienta la bandeja del dron sobre un nivelador y mueve el mástil si su collar invade la bandeja. Conserva los pernos de buje y conecta los seis arcos de rueda sin invadir neumáticos ni suspensión; elimina el periscopio y el hatch base que quedaban enterrados bajo la nueva carcasa, además de gussets y parrillas sin soporte visible confirmado.

El Sentinel Relay ahora usa dos tapas compactas con franja y fijaciones sobre el cab cowl y tirantes que conectan la bahía de electrónica con el mástil. El builder elimina los paneles laterales, racks y gussets desconectados o en conflicto con ruedas y hatches. `tests/python/authoring/test_command_relay_surface_fit.py` y `tests/python/authoring/test_sentinel_relay_surface_fit.py` comprueban estos contactos en Blender 4.5.14. La validación es local de autoría: el perfil de geometría sigue sin calibrar, así que no se regeneran FBX, prefabs ni bundles.

El Arachnid Hexapod conserva su launcher fijo, las seis celdas y la tapa superior de servicio. Su builder descarta la tapa heredada que quedaba dentro del carapace y ya no genera herrajes de torreta, el eje transversal que cruzaba las celdas ni gussets sin apoyo claro. Las varillas hidráulicas quedan incluidas en el mapeo de huesos de pata; se conservan las juntas articuladas de las patas medias. Sus outputs no se promueven mientras el perfil de geometría siga sin calibrar.

Para repetir el piloto Hardflow del Bulwark, ejecuta `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-BulwarkHardflowPilot.ps1`. Genera candidatos aislados, comprueba que las seis piezas frontales no cambien ni se dupliquen, valida los soportes y el despeje estático, compara las superficies de los casetes ERA y captura cuatro vistas. Valida el asiento contra el FBX Level 1 exacto que importa Unity. El arreglo Level 2 coloca diez respaldos laterales —cinco por costado— en `x=±1.312 m`; la regresión comprueba 90 muestras de contacto, error máximo de apoyo de 1 mm, al menos 35 mm de despeje a la correa y cero solapamientos con ella. El refresco Level 1 se hizo con una autorización puntual del usuario; el perfil de presupuesto continúa sin calibrar y esa excepción no se extiende a otros builders.

## Presupuesto dinámico de geometría

Los vehículos, módulos visuales y estructuras no tienen límites de compilación derivados de la calibración. [`content/source-art/model-budget-profile.json`](content/source-art/model-budget-profile.json) conserva mediciones y diagnósticos opcionales; los LOD siguen configurándose por tamaño en pantalla. Los reportes conservan por separado las caras de Blender, los triángulos de Unity, los umbrales de LOD y el veredicto.

El objetivo local es 60 FPS (16,67 ms por frame). Prefabs y paquetes no exigen un perfil calibrado ni rechazan assets por una asignación de geometría. La calibración requiere al menos tres muestras válidas, dos cargas de triángulos, una escena de juego y una pareja baja/alta con población controlada. El compilador acepta un bundle de evidencia `rogue-command/runtime-geometry-evidence/v1`; cada informe conserva su SHA-256 y se enlaza a la escena, nivel, mezcla y cámara exactas:

```powershell
python -m tools.profile_model_budgets --profile content/source-art/model-budget-profile.json --samples docs/research/model-budget-evidence/2026-11-12-level4/runtime-geometry-evidence.json --output content/source-art/model-budget-profile.json
```

El comando escribe las muestras y el veredicto en el perfil incluso cuando sigue `uncalibrated`; en ese caso termina con código 2 para indicar que no se ha demostrado la calibración; esto no bloquea builds.

El medidor temporal `tools/RuntimeModelBudgetProbe.cs` se inyecta de forma temporal con BepInEx MCP; no forma parte del loader. Requiere una cámara `Game` que renderice a pantalla, cuenta triángulos de mallas habilitadas en el frustum y recoge al menos 120 intervalos de `Time.unscaledDeltaTime`. Cada 30 frames comprueba que la cámara y el conjunto de renderers/LOD sigan iguales; la captura queda inelegible si cambian o si el juego se pausa. El JSON v3 incluye `geometryStabilityTrace` con el conteo, delta, firma y contexto en cada comprobación; sirve para diagnosticar y no afloja la aceptación exacta. El bundle de calibración verifica SHA-256 y liga esos informes a la misma escena, pose de cámara, versión de Unity y resolución. El intervalo observado es pacing real, no un desglose CPU/GPU; el conteo sigue siendo una estimación que no incluye oclusión, partículas, rutas de terreno, sombras ni draws instanciados por GPU.

Cada muestra del perfil registra `triangles` y `p95UnscaledFrameIntervalMs`. Si el ajuste lineal no identifica un costo geométrico estable, el modelo `conservative-measured-incremental-envelope-v1` calcula un candidato finito sobre el rango observado, reserva 25% y nunca extrapola. Ese candidato no valida por sí solo la calibración: las demandas deben coincidir con el inventario de caras, triángulos y hash, y sus instancias máximas sumadas no pueden superar el máximo de escena. Una pareja baja/alta con una población que cambió por causas ajenas al set medido permanece solo como evidencia.

El perfil incluye ahora tres capturas de juego del nivel 4, todas con 120/120 intervalos válidos, la misma cámara a 1920×1080 y p95 de 12,093–12,160 ms; abarcan 110.175 y 401.462 triángulos visibles. El candidato de capacidad incremental es 218.465 triángulos con la reserva del 25%. Aun así el perfil permanece `uncalibrated`: entre las cargas cambiaron las unidades y edificios de IA, así que la diferencia no aísla el costo del set añadido; además faltan demandas verificadas de escena. Los builds no dependen de esa calibración; no presentes números sintéticos ni `calibrationReady` de una muestra aislada como evidencia de rendimiento. Unity cambia de LOD por tamaño en pantalla con las mallas ya exportadas; no genera geometría durante la partida. Para repetir el piloto Gravitas aislado, ejecuta `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-GravitasBreakerHardflowPilot.ps1`. Para la fábrica HQ, ejecuta Blender 4.5.14 con `tools/Run-OverlordCommandHQFactoryHardflowPilot.py`; prepara vistas y compara el candidato Hardflow con el replay nativo bajo `artifacts/`. `tools/Build-OverlordCommandHQFactoryModel.py` aplica el mismo replay y registra el FBX LOD sin exigir calibración.

La generación Level 2 conserva `geometryPreset`, `geometryLayout` y el SHA-256 del registro compartido. Python valida que el registro cubra cada fábrica y que slot, función geométrica, rol visual y variante autorizada coincidan; el builder genérico invoca la receta seleccionada. Unity valida los mismos bindings y layouts contra el manifiesto schema v3, en vez de mantener una lista aparte de presets. El `preset` PBR sigue identificando únicamente los mapas de material. La fuente ahora declara 57 módulos en 35 variantes y utiliza las 47 recetas. El manifiesto y los outputs generados se reconstruyen con `tools/Build-CanonicalVisualModules.ps1`. Por instrucción explícita del usuario, la calibración no limita la generación de ningún modelo; el manifiesto y los logs deben marcar el build `UNCALIBRATED_USER_AUTHORIZED`, y no se deben presentar como calibración ni como prueba de rendimiento. El permiso sigue activo hasta que el usuario lo revoque. El perfil de presupuesto no se modifica con este bypass.

El script verifica SHA-256 y solo prepara Blender y UnityExplorer bajo `.tools/`. Runtime Unity Editor se prepara por separado con `./tools/Setup-BackendTools.ps1 -RuntimeUnityEditor`; también verifica SHA-256. Ninguno de estos scripts copia archivos al juego ni lo inicia. Los inspectores requieren BepInEx 5 Mono y se añaden a la instalación de Steam solo durante una prueba reversible, usando uno por perfil de prueba.

Para preparar Material Maker 1.7 como herramienta PBR obligatoria, ejecuta desde la raíz del repositorio `powershell -File tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker`. El script verifica el SHA-256 publicado para la release y deja la aplicación en `.tools/MaterialMaker-1.7/`. El contrato de workflows está en `tools/material-maker/rogue-command-material-maker.json` y cubre `urp`, `blender`, `three_d` y `procedural`; valida su integridad con `python tools/validate_material_maker_config.py`. El bake automático se ejecuta con `powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow urp` (o el workflow correspondiente); crea temporalmente el target y workaround de exportación, espera el dispositivo de render y restaura la configuración local al terminar. ArmorPaint se distribuye como binario portable con acceso de compra y permanece opcional; si ya tienes acceso, usa `-ArmorPaintArchivePath <ZIP-oficial>`. Ambos flujos se mantienen fuera del proyecto Unity y de la instalación del juego. Sigue [PBR-TEXTURE-WORKFLOW.md](docs/research/PBR-TEXTURE-WORKFLOW.md) y las dos skills PBR antes de integrar mapas.

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

Tras editar la matriz visual o las fuentes de arte, reconstruye la cadena canónica completa:

```powershell
.\tools\Build-CanonicalVisualModules.ps1
```

El comando regenera los FBX de nivel 2 con Blender 4.5.14, hornea los 28 sets PBR con Material Maker 1.7 en modo estricto y luego actualiza con Unity 2021.3.45f2 las cuatro prefabs de nivel 1 que alimentan variantes modulares. Las posiciones de sus hardpoints salen de `tools/CanonicalUnitHardpoints.py`, compartido por los constructores de fuente y los constructores rigged; la actualización conserva los sockets de los FBX y completa los cuatro hardpoints canónicos si faltan. La compilación de nivel 2 verifica que cada prefab tenga uno de cada tipo antes de enlazar los módulos. Después construye ambos packs, prepara el staging y ejecuta los validadores. `content/source-art/canonical-visual-module-roles.json` declara la función mecánica visible de cada módulo por variante y hardpoint; los presets PBR solo definen superficie. También guarda una huella de las fuentes y hashes de cada bundle para que un cambio posterior en el proyecto Unity, en los manifiestos o en los roles invalide el staging. Para repetir la auditoría de referencias en el staging recién creado:

```powershell
.\.venv\Scripts\python.exe .\tools\validate_content_bundle_references.py .\artifacts\canonical-two-level\validation-packs\sample-native-expansion
.\.venv\Scripts\python.exe .\tools\validate_content_bundle_references.py .\artifacts\canonical-two-level\validation-packs\tactical-operations
.\.venv\Scripts\python.exe .\tools\validate_native_visual_roots.py .\content\packs\sample-native-expansion
```

El chequeo de referencias lee los AssetBundles con UnityPy y no modifica el pack ni la instalación del juego. Revisa las entradas de bundle, no valida que el contenido funcione dentro de una partida. Los bundles declarados bajo `content/packs/` son salidas regenerables locales e ignoradas; genera y coloca los bundles actuales antes de validar o empaquetar. Para QA, usa el staging de la ejecución más reciente `artifacts/canonical-level2-<UTC>/validation-packs/` después de reconstruirlos.

El validador de rutas nativas requiere Python 3.11 o posterior y las dependencias fijadas en `requirements.txt`. Verifica versión, SHA-256 y MVID del juego, además del hash de `resources.assets`, y comprueba que cada ruta visual nativa sea única y contenga renderers. La selección del recurso y su aspecto dentro de una partida se comprueban por separado.

Para crear un ZIP de instalación sin escribir en la carpeta del juego:

```powershell
.\tools\Build-CanonicalVisualModules.ps1
.\tools\Package-Loader.ps1
```

El primer comando reconstruye y valida los módulos visuales canónicos y prepara los packs actuales en un directorio nuevo `artifacts/canonical-level2-<UTC>/validation-packs/`, sin reemplazar ejecuciones previas. El segundo selecciona el build validado más reciente, comprueba la huella de fuentes, los hashes de los bundles y el `content.json` vigente antes de crear el ZIP en `artifacts/packages/`, con el plugin, el esquema, `sample-native-expansion` y `sample-reactive-ai`. `sample-expansion` queda fuera porque todavía necesita sus propios assets completos de balance, etapa, AI y mapa. Se puede fijar una ejecución concreta con `-CanonicalVisualArtifactRoot <directorio>`; la instalación usada para compilar se cambia con `-GameRoot` y la ruta local de BepInEx con `-BepInExCorePath`.

## Instalar

1. Instala BepInEx `5.4.23.5` **win-x64 Mono** en el directorio de Rogue Command usando la distribución oficial.
2. Copia `artifacts/plugin/RogueCommandExpansionLoader.dll` a `BepInEx/plugins/`.
3. Coloca cada paquete en `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/<carpeta-del-pack>/` con su `content.json` y, cuando los use, sus AssetBundles.
4. Conserva `Rogue Command/Mod/` para las sobrescrituras TSV nativas; el loader externo no reemplaza ese formato.

`content/packs/sample-native-expansion/content.json` declara nueve productos con balance sobre filas nativas: Sandlance (`PCXBaseTank`), Bike (`BombBike`), Colossus (`ArtilleryTank`), Ironclad, Atlas, Bastion y Arachnid (`GatlingWalker`), Overlord (`PCXBaseTank`) y Surge (`LightningTank`). Cada producto tiene una fábrica namespaced propia, según la tabla siguiente.

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
| Atlas Crane Mech | Fábrica de Mechas Grúa Atlas | `sample-native:atlas-crane-mech-factory` |
| Bastion Siege Mech | Fundición del Bastión | `sample-native:bastion-siege-mech-factory` |
| Arachnid Hexapod | Pórtico de ensamblaje Arácnido | `sample-native:arachnid-hexapod-factory` |
| Overlord Command HQ | Parque de vehículos Overlord | `sample-native:overlord-command-hq-factory` |
| Skirmish Strike Bike | Taller de motos de asalto | `sample-native:skirmish-bike-factory` |
| Surge Arc Tank | Planta de tanques Surge Arc | `sample-native:surge-tank-factory` |

Estos edificios parten de los prefabs de sus fábricas nativas, con identidad, balance y localización propios. El registro conserva las relaciones originales del juego. El campo es obligatorio para todas las unidades añadidas, incluidas las de prefab completo; el contrato detallado está en `content/schema/README.md`. Los cambios de producción deben verificarse colocando el edificio y observando la unidad producida durante una partida por Steam.

**Comprobación por Steam (2026-10-17):** en aquella prueba de cinco parejas, las relaciones se conservaron y los nombres españoles se resolvieron; esto no amplía la cobertura de nombre a las nueve parejas que declara hoy el manifest. La prueba colocó los cinco edificios mediante la API nativa y ordenó producción no instantánea sin coste: las cinco colas terminaron y aparecieron sus cinco productos activos con tag `Player`. Los pools de las cuatro etapas devolvieron los planos de fábrica correspondientes a sus productos. La selección aleatoria y compra/colocación completas por interfaz siguen pendientes de una partida ordinaria. Detalles, migración y evidencia en [PRODUCTION-BUILDINGS.md](docs/research/PRODUCTION-BUILDINGS.md).

El paquete final también adapta la distancia de edificios AI para etapas propias añadidas: en Steam, el índice 12 usó la franja nativa configurada (29 celdas) y dejó de emitir `Unknown stage`. La lista de fallbacks conserva nueve entradas nativas y devuelve un snapshot de 13 para la progresión ampliada.

## Diagnóstico e Inspector en el Juego (atajo configurable)

Una función configurable de BepInEx (`Inspector.ToggleKey`, **F8 inicial**) alterna únicamente la ventana; la marca de agua/badge permanece disponible. La tecla o combinación actual se muestra en el badge y el pie. **F10 ya no es un atajo separado**, pero puede asignarse en esa configuración. Escape cierra el inspector.

- **Marca de agua:** Muestra el estado del loader y versión (`RCL v1.19.0 [OK]`).
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

Los manifiestos se leen antes de incorporar contenido, por lo que las etapas pueden referenciar unidades o perfiles de otro pack. Los paquetes solo nativos declaran `assetBundles: []` y omiten `assetBundleManifestPath`; los demás declaran de 1 a 128 bundles. El loader calcula dependencias entre packs y entre bundles, carga primero los requisitos y rechaza ciclos o dependencias que falten. Cada perfil puede componer hasta ocho módulos compilados; el manifiesto solo elige IDs y pasa parámetros de texto. Los bundles tienen límites de tamaño: 64 MiB para el bundle de manifiesto, 512 MiB por bundle de contenido y 2 GiB en total por sesión. Un perfil nativo aislado puede usar `productionBuildingEntries` para agregar al deck AI la fábrica asociada a una unidad del pack; esta entra con cantidad inicial cero y `sample:scheduled-base-build` puede construirla al apuntar su ID de fábrica. `sample:reactive-base-defense` aporta una decisión táctica de respuesta acotada y admite unitId para limitar el envío a la unidad configurada. Ambos añaden comportamiento después de la actualización AI nativa y no distribuyen código en los paquetes.

Las unidades reciben claves de recurso namespaced para que Harmony pueda resolver sus prefabs e imágenes. En modo `nativeTemplateId`, el loader copia la fila de balance y clona el prefab del juego; la identidad, balance y cambios visuales se aplican solo a esas copias. Si se indica `visualAssetName` junto con `visualRootPath`, se desactivan los Renderer nativos de esa rama en el clon y se agrega el modelo del bundle al mismo nodo. Los componentes de gameplay quedan en la jerarquía clonada. El overlay no recibe animaciones nativas automáticamente; opcionalmente, `nativeRigRetarget.mappings[]` define de 1 a 64 pares de rutas exactas (`sourcePath` desde la raíz nativa y `targetPath` desde la raíz del prefab visual) para copiar deltas de posición y rotación locales relativos a la pose bind. El retarget no copia escala ni root motion, se suspende al morir y restaura las poses bind al reutilizar la unidad. `diagnostics_inspect_entity_visual` muestra hasta 16 mapeos con las poses locales actuales de origen/destino y el destino calculado; `omittedMappingCount` precede la lista. El destino calculado sirve para comparar la matemática, no demuestra movimiento visible. La compatibilidad visual de locomoción, apuntado, disparo, muerte y reúso debe observarse en una partida. Una etapa de plantilla clona `StageParametersScriptableObject` antes de sus overrides y la nueva etapa se agrega a las progresiones. Los perfiles AI nativos deben usar `scopeToReferencedStages: true`, que crea un alias `RCL_` aislado para sus módulos; el validador rechaza perfiles que afectarían el comportamiento nativo compartido. Las unidades, etapas y perfiles usan IDs namespaced y los conflictos desactivan el pack. Los modelos completos, mapas o prefabs AI aún necesitan referencias serializadas válidas en bundles creados con Unity.

## Authoring de AssetBundles en Unity

Las referencias de bundle deben coincidir con las entradas que Unity generó. Usa la ruta relativa completa del asset siempre que el contrato lo permita, especialmente para Sprites; la carga tipada del plugin evita seleccionar la textura cuando el archivo también expone un Sprite.

En `unity/RogueCommandAssetAuthoring/`:
- **Editor Builder:** `Assets/Editor/RogueCommandAssetBundleBuilder.cs` provee el menú `Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...`.
- **Modelos 3D:** La guía [UNIT-VISUAL-AUTHORING](docs/research/UNIT-VISUAL-AUTHORING.md) registra los modelos, comandos y evidencia. Sandlance conserva 78 piezas editables y exporta 7 lotes FBX (2.17 × 2.52 × 1.57 m); Skirmish Bike conserva 59 y exporta 7 lotes (1.20 × 2.20 × 1.37 m); Colossus conserva 104 y exporta 8 lotes (2.58 × 4.74 × 1.86 m); Ironclad conserva 74 y exporta 5 lotes (1.83 × 1.98 × 2.45 m); Surge Arc Tank conserva 94 y exporta 6 lotes (1.99 × 2.78 × 1.52 m). El cañón del Colossus rebasa la longitud aproximada del prefab ArtilleryTank; su espaciado debe verificarse en partida.
- **Tarjetas 2D:** los nueve productos del pack tienen tarjeta PNG propia de 128 × 128 importada como Sprite; Arachnid Hexapod y Overlord Command HQ usan renders de tarjeta de Blender (1254 × 1254) de sus modelos. El scaffolder importa las nueve imágenes autoradas sin dibujar tarjetas por código.
- **Scaffolding:** `RogueCommandVisualSampleScaffolder.cs` genera siete visuales de producto a partir de FBX; el bundle incorpora además cuatro prefabs visual-only ya authorados (productos y fábricas de Arachnid/Overlord). El bundle distribuible declara 11 prefabs visuales y 9 tarjetas Sprite propias. El builder los valida localmente; preservar la jerarquía de gameplay nativa no prueba la animación ni el renderizado en partida.
- **Bundle visual actual:** El bundle versionado actual contiene los assets referenciados por 9 overlays de producto, 2 overlays de fábrica y 9 tarjetas Sprite propias. El preflight local resolvió las 29 referencias de assets del manifest. Bundle: 12,182,177 bytes, SHA-256 `deb6c9220167e943e12cbbff0307d1f41cd1d2c4e803a1c361e880a0a421b65c`; manifiesto: 1,587 bytes, SHA-256 `6210c55ff9dce54e9da21ff7d63849cf18f35771266cb6bcc8584cb00c4c41b7`. Esta inspección serializada y validación de referencias no son una prueba de carga, animación o render en una partida.

- **Ejecución headless del flujo visual:** `Unity.exe -batchmode -nographics -projectPath <ruta> -executeMethod RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.BuildSampleNativeExpansion -quit`. Si Unity Package Manager vuelve a rechazar la cadena TLS de `packages.unity.com`, se puede pasar la raíz validada por Windows con `NODE_EXTRA_CA_CERTS` solo al proceso del Editor; no desactivar la validación TLS.

`unity/RogueCommandAssetAuthoring/` es el proyecto Unity de autoría de este repositorio y se conserva separado del proyecto `Rogue Evolution` que el usuario añadió a Unity Hub. Se conservan en el proyecto sus `ProjectSettings`, `Packages/manifest.json`, `Packages/packages-lock.json` y los ajustes compartidos de Version Control/Unity Cloud. `PackageManagerSettings.asset` solo guarda preferencias locales del editor y se regenera, así que se excluye del push. `.plastic/` contiene estado local del workspace Plastic, por lo que permanece en disco pero se excluye del push. Se ignoran las salidas regenerables bajo `/artifacts/`, el antiguo `Assets/RogueCommandExpansionSample/BundleOutput/` y los AssetBundles bajo `content/packs/<id>/bundles/`. Antes de validar o empaquetar un checkout limpio, reconstruye y coloca allí los bundles declarados por cada pack.

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

## Evidencia local y snapshots de validación

> Los resultados de harness, DLL y ZIP que siguen documentan snapshots históricos de sus respectivas ejecuciones, no un estado de publicación actual. La verificación de seguimiento del 2026-10-24 está en [la auditoría de unidades heredadas](docs/research/LEGACY-UNITS-LOADER-1.19-AUDIT.md#verificacion-y-correcciones-de-seguimiento-2026-10-24); no se aceptó gameplay en esa revisión.

- `tools/Package-Loader.ps1` completó la compilación Release con 0 errores y 0 advertencias. Tras la corrección de la traza, el DLL mide 332,800 bytes; SHA-256: `05b8518a44abd7eb8fd0c904bd30078e0f9d4ba736e1799caf11ba1eb1db77a0`.
- El harness local pasó 127/128 comprobaciones; Windows omitió una prueba de symlink porque este entorno no concede el privilegio necesario. Los casos cubren registro y rollback de filas, índices, cachés y mappings de fábricas; elegibilidad de fábricas por etapa y demo; entradas AI de unidades y fábricas, referencias entre packs, progresiones, preflight de mapas, localizaciones y parsers AI. La regresión nueva fuerza una traza de pools que supera 4,096 caracteres y verifica que los contadores `omittedIdObservations` y `omittedPoolQueries` sigan visibles. El rastreo separa candidatas globales, fábricas elegibles por etapa, pools clonados y productos ofrecidos sin alterar el sorteo.
- El validador JSON Schema aceptó los tres manifiestos distribuidos y el ejemplo documentado. El ZIP de `artifacts/packages/RogueCommandExpansionLoader-0.1.0.zip` se leyó íntegro: nueve entradas y rutas seguras. Incluye los dos packs distribuibles, el esquema y el bundle visual; su única DLL compilada es el loader. No contiene bibliotecas de BepInEx, Harmony, ensamblados del juego, PDB ni `Library/`. Mide 659,155 bytes (SHA-256 `ed0b694869ec9565b72c83d820bd4c1c5bd03f708a0827cc4a4aaf62aaf1b49b`); el DLL incluido coincide con el hash local anterior. Una auditoría adicional encontró cero rutas inseguras y cero entradas prohibidas.
- **Rastreo de ofertas aleatorias:** REA inspeccionó estáticamente `ChooseCard.PresentCardsToChoose`, `BlueprintPool.BlueprintsInPool(int?, bool)` y `PresentCardAsRewardEvent.Record(...)` en la identidad fijada del juego. Los hooks nuevos registran candidatas antes/después del filtro de etapa, el pool clonado consultado con sus parámetros y los productos del mod seleccionados para la oferta. Solo observan; no cambian listas ni RNG. Las pruebas locales pasan. La sesión Steam abierta ahora está en `MainMenu` con el loader anterior (0.1.0, 19 hooks); la reflexión en vivo confirma las firmas, pero el build nuevo aún no está cargado y no se ha iniciado una partida ni capturado ofertas. Una oferta sin unidades del mod continúa siendo inconclusa. Consulta [docs/research/AGENT-HANDOFF.md](docs/research/AGENT-HANDOFF.md) para la evidencia y el siguiente paso.
- **Inventario estático del bundle visual (2026-10-24):** UnityPy encontró siete Sprites y el validador resolvió 25 referencias declaradas entre productos y fábricas: nueve overlays de producto y dos de fábrica. El bundle versionado mide 18,694,646 bytes (SHA-256 `41333dd54d63abdf87f8990c9c9ccfc2132c33d09828ea626aa2d48b60a3be37`); su manifiesto mide 1,588 bytes (SHA-256 `6878ce0b545466906851c9c75ecc21e32517e7601839325172fd7fb86b76eb4d`). Es inspección serializada/preflight local: no hubo reconstrucción Unity en esta verificación ni prueba de carga o render en partida.
- La inspección estática de `EntityBalancingStore` volvió a verificar la identidad 1.0.9, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, y confirmó las dos cachés anidadas por ID (`ChangeableIntValueCache` y `ChangeableFloatValueCache`), evidencia REA `ev_671c16715af8c93c83b40e0532e0fcce836f8c296f8efb851416635861119e67`. El mutator añade entradas de caché vacías para cada fila nueva y las incluye en el rollback transaccional. En la prueba diagnóstica de stage 12 se observó un Skirmish Bike activo en el roster AI; eso no verificó su oferta roguelike ni su producción por edificio. En la ruta aleatoria posterior no se observó una instancia activa de unidad del mod.
- **Revisión local de código:** revisión manual de carga y retención de bundles, validación de assets, commit por paquete, rollback de filas, índices, mappings de fábricas y cachés de stats, integración idempotente de progresión, ciclo de vida de módulos AI y resolución de aliases de etapa. Es revisión estática local; no sustituye ejecución en una partida. La evidencia estática del overload tipado `Resources.Load(string, Type)` es `ev_07fa217587c15e7c71aefa972647c15f656180a8cb767b957aad078352b51fa8`.
- **Perfiles AI que construyen fábricas, 2026-10-18:** los perfiles nativos aislados pueden declarar `productionBuildingEntries` por ID de unidad; el loader agrega la fábrica asociada al deck AI con cantidad inicial cero, y el paquete de muestra pide construir `sample-native:skirmish-bike-factory`. El CIL inspeccionado confirma la forma de `EntityInAiDeck.countPerStage` y la lectura de sus valores inicial/máximo (REA `ev_89495400212b1930146b19a55210051a3bd2a2e6ce19e129d8ab3814d2f37947`). Esto es inspección estática y validación local, no prueba de construcción ni producción dentro de una partida.
- **Comprobación de menú por Steam, 2026-10-18:** Rogue Command 1.0.9 inició desde Steam; BepInEx cargó Rogue Command Expansion Loader 0.1.0 e instaló 23 hooks verificados. La escena observada fue `MainMenu`; el log no registra aceptación de packs y la búsqueda `RCL_` no halló clones en esa escena. No se inició una partida, así que esta sesión no demuestra registro de fábricas ni ejecución del módulo AI. El log está en `artifacts/runtime-smoke/production-building-ai-20261018/current-runtime-log.txt`. El juego se cerró con gracia; los archivos temporales de instalación se restauraron y sus hashes coinciden con el manifiesto. Los siete archivos locales bajo `Profiles/` conservaron sus hashes antes y después. La anotación histórica de perfil 2 provenía del selector dentro de la carpeta de instalación; una comprobación posterior del selector autoritativo bajo `Application.persistentDataPath` mostró Profile 1 activo, así que esa ejecución no demuestra qué perfil utilizó el juego. Steam Cloud está desactivado por instrucción persistente del usuario y no debe cambiarse durante la siguiente prueba.
- **Runtime controlado mediante Steam, 2026-10-17:** con Rogue Command 1.0.9 iniciado por Steam AppID `1461910`, el bridge confirmó Unity `2021.3.45f2`, BepInEx `5.4.23.5` y 12 hooks verificados. El log registra `sample-native:expansion` con 5 unidades, 3 etapas y 3 perfiles AI, `sample-reactive-ai:expansion` con 1 etapa y 1 perfil, y la escena `RunSetup`. La búsqueda del bridge encontró el clon `RCL_Native_sample-native_scout-tank` bajo `DontDestroyOnLoad`, pero inactivo: esto confirma carga/registro de contenido y creación del clon dormido, no instanciación mediante `EntityFactory`, generación de mapa, callback AI ni renderizado del modelo en combate. Esa sesión usó el bundle anterior de 336,503 bytes (`dcb8a28554959f813e97a7f6d0dc93b718138dda1ed3b161c641d805989c471d`), antes del ajuste final de ancho; el bundle actual de 346,164 bytes tiene evidencia local de Blender/Unity/UnityPy, no de renderizado en juego. Los tres archivos del perfil siguen coincidiendo con el respaldo. El log final y el manifiesto con hashes están en `artifacts/runtime-smoke/steam-loader-proof-20261017-87c448ca/`. La retirada del loader temporal y del log instalado fue bloqueada por la política de comandos; siguen en `BepInEx/plugins/RogueCommandExpansionLoader/`, `BepInEx/plugins/RogueCommandExpansionLoader.dll` y `BepInEx/LogOutput.log`.
- **Seguimiento de partida roguelike por Steam, 2026-10-17:** una nueva ruta aleatoria llegó a `Game` tras pasar por nodos de recompensa. La única oferta de cartas observada contenía `LittleLighter`, `FireDrag` y `JeepWithMachineGun`; no demuestra ausencia global de productos del mod porque cada oferta es una muestra aleatoria. En `Game` inició el `DefaultAiBehaviour` nativo (`TickHasStarted=True`, `ActualAiStartBaseCount=1`), pero en esta partida no se observó una instancia activa de unidad del mod ni una decisión del módulo reactivo. Las plantillas `RCL_Native_sample-native_*` quedaron inactivas en `DontDestroyOnLoad`. Algunos callbacks de recompensa se invocaron mediante el bridge. Evidencia en partida; alcance y siguiente prueba en [docs/research/AGENT-HANDOFF.md](docs/research/AGENT-HANDOFF.md).
- **Límite de ejecución:** el stage 12 diagnóstico confirmó generación de mapa, attachment del perfil reactivo y entrada de Skirmish Bike al roster AI; la decisión de contraataque sigue sin verificarse. La ruta aleatoria confirmó el arranque de AI nativa, pero su única oferta observada fue nativa. Aún falta seguir una oferta elegible modded a través de colocación de edificio y producción, comprobar el renderizado de la unidad y observar una decisión del módulo reactivo.
- **Runtime histórico, antes del cambio de carga tipada:** un clon inició con 11 hooks, pero registró 39 excepciones de `TMP_Settings.defaultStyleSheet`; no llegó a crear mapa ni entidad. Evidencia conservada en `artifacts/runtime-smoke/native-data-smoke-7-evidence/` y `artifacts/runtime-smoke/native-data-smoke-7-window.png`.
- **Runtime histórico en clon visible:** BepInEx 5.4.23.5 cargó el plugin e instaló 10 hooks; el probe llegó a `Awake`, pero el clon arrancado directamente registró `SteamAPI_Init() failed` y no emitió `sceneLoaded`. Esa prueba no aisló el origen de la falta de escena. Evidencia: `artifacts/runtime-smoke/typed-load-probe-visible-1/`.
- **Runtime más reciente en clones con perfil:** al clon se le copiaron seis archivos de perfil locales que faltaban; se excluyeron `steam_autocloud.vdf` y cualquier override de AppID. Tanto la ejecución con loader como el control vanilla solicitaron `MainMenu`, permanecieron vivos 120 s / 30 s respectivamente, y no emitieron el primer `Start`/heartbeat ni un evento `sceneLoaded`. En la ejecución con loader, BepInEx completó el chainloader e instaló 10 hooks, el probe alcanzó `Awake`/`OnEnable`, pero no se detectó el pack ni se llamó a sus puntos de activación (`CreateStageMap`/`EntityFactory`). El control vanilla se comportó igual sin BepInEx; este resultado no atribuye el bloqueo al loader y tampoco demuestra carga de unidades, mapas o AI. Evidencia: `artifacts/runtime-smoke/profile-clone-corrected-long-1/` y `artifacts/runtime-smoke/profile-clone-control-vanilla-1/`.
- El análisis estático del `Assembly-CSharp.dll` fijado también encontró que `ProfileManager` compone `.\Profiles\Profile<n>\savegame.dat`; `LevelProgression.Awake` carga un guardado existente y, si no existe, crea el mapa de etapas. Referencia REA: `ev_387e08b970a05b9e05a88e8a2800b4e0a77b52b5b4bbaa01e352fd33c9fa1d30`. Esto es inspección estática, no prueba de runtime.
- **Aislamiento de los smoke en clones:** en esas pruebas históricas el clon recuperó el plugin original (`ABD8B65A90C0D4EED9CDF090C401C50CB932A0BDF425590D9DB4C3E3271A5648`) y el log original de BepInEx (`76A390463EC153896E02A911511641B8FF34CEAAB287C57E84FE014260813963`). Los perfiles copiados, probe y pack se retiraron; los archivos generados de LocalLow/registro quedaron archivados bajo las carpetas de evidencia de cada smoke. Ese lote no modificó ni inició la instalación de Steam; se conserva separado de la prueba Steam descrita arriba y del proyecto Unity Hub del usuario.

### Bulwark foundry visual

The Bulwark production-building overlay is authored from `tools/Build-BulwarkFoundryModel.py` and built with `TacticalOperationsPackScaffolder.BuildBulwarkFoundry`. Its output hashes, LOD counts, and unverified runtime limits are recorded in `content/source-art/bulwarkfoundry-build-report.json` and the agent handoff. Its output is `UNCALIBRATED_USER_AUTHORIZED`, not performance-calibrated.
