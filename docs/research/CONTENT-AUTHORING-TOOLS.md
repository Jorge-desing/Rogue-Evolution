# Herramientas para crear contenido de Rogue Command

**Revisado:** 2026-10-17

**Objetivo:** producir unidades, etapas/mapas y AI originales para el mod Rogue Evolution. El loader se mantiene como infraestructura necesaria.

## Resultado de la búsqueda de skills

No encontré una skill pública específica para crear contenido de Rogue Command ni para desarrollar su cargador. Sí encontré `bepinex-mono-modding`, una guía comunitaria de BepInEx 5/Mono, y una guía para IL2CPP. La segunda no se aplica a este juego. La primera cubre patching y diagnóstico general, pero se enfoca en trainers y menús; además, propone un cambio de ciclo de vida que no se ha comprobado para Rogue Command. No la instalé sin adaptar. La skill comunitaria de `bepinex-mcp` se centra en inspeccionar y modificar un juego que ya está ejecutándose; no crea modelos, mapas o datos de juego.

La documentación oficial de BepInEx y HarmonyX sirve para el trabajo de plugin y Harmony. La búsqueda en el catálogo curado de `skill-installer` tampoco encontró una skill de creación de mods de Unity Mono que encaje con el contenido de este repositorio. Se mantienen como guía principal la documentación de este repo, `reverse-engineer-anything` para evidencia de binarios y `engineering:code-review` para revisar cambios; no se instaló una skill de otro backend como sustituto.

## Inventario local verificado

| Herramienta | Estado | Uso adecuado |
|---|---|---|
| Unity Editor **2021.3.45f2** | Instalado en `C:\Program Files\Unity\Hub\Editor\2021.3.45f2` | Authoring y build de AssetBundles para la misma línea de Unity que Rogue Command. El proyecto del repo ya incluye un builder y scaffolder; conservar sus `ProjectSettings`, paquetes y configuración de Unity Cloud/Version Control. |
| UnityPy **1.25.4** | Instalado en `.venv` | Inspección offline de archivos serializados y bundles. No demuestra que un objeto vaya a cargar ni comportarse en una partida. |
| AssetRipper **2.0.0** | Disponible en `.tools/AssetRipper-2.0.0/` | Extracción puntual de assets para referencia de authoring. En la prueba local produjo PNG/YAML/JSON para un Sprite, no un prefab o proyecto Unity importable; no tratar los archivos del juego como assets originales del mod. |
| ILSpy (`ilspycmd`) | Disponible en `.tools/ilspycmd/` | Inspección estática de firmas y lógica administrada; antes de usar firmas reflejadas, confirmar versión, SHA-256 y MVID juntos. |
| Blender **4.5.14 LTS** | Instalado en `.tools/Blender-4.5.14/`; SHA-256 del ZIP oficial `b9533d2397ac1984db4466fb23a7a4649391cca93f6e84209f9bcc60d071c8b9`; `blender.exe --version` confirmó 4.5.14 LTS | Editor opcional de modelos 3D. Exporta FBX para importar geometría a Unity. No hace falta para los meshes básicos que hoy genera el scaffolder, pero permite crear unidades con silueta propia. |
| UnityExplorer **4.8.2** | Paquete oficial de Thunderstore descargado, SHA-256 `b3d30229ee785e2c85914ba79c6d4f6d0eca97d11c8715e55cc71d3085489c24`, extraído a `.tools/UnityExplorer-4.8.2/`; aún no instalado en el juego | El paquete incluye `UnityExplorer.BIE5.Mono.dll`, `UniverseLib.Mono.dll` y sus dependencias. Su README declara soporte para Unity 2021+ y BepInEx 5 Mono, aunque la última publicación es de 2022 y el repo original fue archivado en 2023. Para observar una partida requiere BepInEx en el juego; añadirlo solo al preparar una prueba reversible iniciada por Steam. Usarlo en modo de inspección; evitar su consola y hooks salvo que hagan falta. |
| BepInEx MCP (`bepinex-mcp`) | No instalado | Puente opcional para consultar o cambiar objetos de un juego que ya corre. Requiere un plugin/servidor adicionales y ofrece operaciones de runtime; no es necesario para el flujo de authoring ni sustituye la validación integrada del loader. |

La carpeta `.tools/` y `.venv/` están excluidas de Git por `.gitignore`. La salida distribuible vive bajo `content/packs/<id>/bundles/`, que sí se versiona.

## Utilidades para backend y diagnóstico runtime

| Herramienta | Estado | Uso adecuado |
|---|---|---|
| Runtime Unity Editor **6.3.2.1, BepInEx 5** | Release upstream descargada y verificada localmente; ZIP SHA-256 `b0f0f0ac89e50d31e542fbcdd611a482cf8fef7dc5abdd12501611aefa77e348`; queda bajo `.tools/RuntimeUnityEditor-6.3.2.1/` | Inspector de objetos/Componentes, consola C# y Harmony Patch Inspector. Es la opción preferida para observar estado real de unidades, etapas, AI y patches una vez que el juego llega a una escena. La consola permite mutar el estado: en pruebas iniciales usarla solo para leer y añadirla únicamente a un perfil/clon reversible iniciado por Steam. El ZIP local fue descargado del release oficial y su hash se fijó en este repositorio; upstream no publicó un digest de ese asset. |
| UnityExplorer **4.8.2** | Sigue en `.tools/UnityExplorer-4.8.2/` como herramienta disponible, pero es una publicación de 2022 del proyecto original archivado | Fallback conocido. No instalar UnityExplorer y Runtime Unity Editor simultáneamente en el mismo perfil de prueba: ambos añaden herramientas de inspección y consola al runtime. |
| ILSpy (`ilspycmd`) | Ya disponible bajo `.tools/ilspycmd/` | Fuente para leer tipos, firmas y lógica compilada offline. Mantener el control de identidad del juego antes de usar los resultados para nuevos patches. |
| BepInEx ConfigurationManager | No preparado | No aporta valor inmediato porque el plugin todavía usa su overlay propio y no define entradas BepInEx `ConfigEntry`. Reconsiderar cuando el loader exponga opciones editables desde `ConfigFile`. |

Preparar Runtime Unity Editor con `tools/Setup-BackendTools.ps1 -RuntimeUnityEditor`. El script comprueba el SHA-256, extrae solo el subdirectorio del plugin a `.tools/` y no instala nada en Rogue Command, no cambia su configuración y no inicia el juego.

Para el backend, BepInEx 5 Mono y HarmonyX ya están presentes como referencias de compilación. No se requiere una segunda capa de patches. Mantener `RuntimeOverlayUi` como diagnóstico del loader y usar el inspector externo para confirmar objetos y estado nativos en runtime; son funciones distintas.

Las copias locales se pueden preparar de nuevo con `tools/Setup-ContentAuthoringTools.ps1 -Blender -UnityExplorer`. El script fija las versiones, verifica SHA-256 antes de extraer y deja los programas bajo `.tools/`; no cambia la instalación del juego ni lo inicia. Para UnityExplorer, el juego todavía no tiene BepInEx instalado y el inspector no se ha probado en una partida.

## Recomendaciones de uso

1. **Unidad:** empezar desde una plantilla nativa compatible para conservar la lógica de gameplay y crear una silueta original en Blender o con geometría de Unity. Importar el modelo a `unity/RogueCommandAssetAuthoring/`, compilarlo como AssetBundle Windows x64 y asignarlo a una ruta visual nativa validada. La ruta de overlay actual conserva los componentes nativos, pero no reasigna rigs o animaciones. Una nueva apariencia puede probarse en Editor; el control de movimiento, armas, sombras/materiales y renderizado se confirma dentro del juego. Si llega a escena, observar la jerarquía y los componentes con Runtime Unity Editor.
2. **Etapa y mapa:** la variante de etapa actual clona `StageParametersScriptableObject` y reutiliza mundo y blueprint pools del juego. Esto es útil para probar progresión y balance, pero no crea por sí mismo un mapa original. Un mapa nuevo necesita las referencias serializadas completas de mundo, generador y pools que el juego espera; investigar y construir ese grafo forma parte del contenido, no se resuelve añadiendo campos numéricos al loader.
3. **AI:** los paquetes eligen módulos compilados por ID; no distribuyen DLLs. `sample:scheduled-base-build` amplía producción nativa; `sample:reactive-base-defense` añade una decisión táctica acotada: detecta unidades del jugador atacando un edificio AI y envía hasta cuatro unidades móviles, capaces de atacar, al punto en que detectó la amenaza. Solo utiliza unidades con orden `DoNothing` o `Move`, limita radio/cooldown/respuestas y deja que el juego reanude el movimiento guardado tras la orden posicional. El paquete `sample-reactive-ai` proporciona una etapa de plantilla y perfil nativo para probarlo. La implementación y el manifest pasan validación local; todavía falta observar el comportamiento en una partida iniciada por Steam.
4. **Validación:** hacer preflight del pack, inspeccionar el AssetBundle con Unity Editor/UnityPy y registrar hashes. Estos son checks offline. La aceptación requiere una ejecución reversible del juego **a través de Steam**, confirmando que la unidad se instancia y aparece en el pool, la etapa genera el mapa previsto y la AI ejecuta la nueva decisión. Runtime Unity Editor ofrece una vista adicional del runtime, pero su presencia no cuenta por sí sola como prueba de integración.

## Herramientas que no añaden valor ahora

- **Addressables:** el mod ya entrega bundles y el loader usa `AssetBundle` directamente. Addressables es una capa superior que genera catálogos, direcciones y flujo de dependencias; todavía no hay evidencia de que el juego tenga un catálogo Addressables utilizable por packs externos. No añadir esa dependencia sin una necesidad demostrada.
- **Asset Bundle Browser:** Unity lo describe como herramienta auxiliar/pre-release y de alcance básico. El repo ya tiene `RogueCommandAssetBundleBuilder.cs`, que aplica las rutas y límites del pack; usar el builder existente da una salida más controlable.
- **Habilidades BepInEx IL2CPP:** no aplicar sus instrucciones al juego Mono ni mover el plugin a BepInEx 6/Il2CppInterop sin que cambie y se verifique la identidad técnica del juego.
- **Herramientas de runtime:** Runtime Unity Editor 6.3.2.1 y UnityExplorer ya están preparados bajo `.tools/`, pero el juego aún no tiene BepInEx instalado. No desplegar ninguno hasta una prueba reversible iniciada por Steam; conservar los hashes/archivos originales y restaurarlos al cerrar esa prueba. Empezar con observación de solo lectura; ambas herramientas ofrecen superficies que pueden cambiar estado o ejecutar código.

## Fuentes

- [BepInEx: documentación oficial y soporte Unity Mono/Harmony](https://docs.bepinex.dev/)
- [BepInEx: guía oficial para crear plugins](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html)
- [Unity 2021.3: AssetBundles y recursos serializados sin distribuir definiciones de clases](https://docs.unity3d.com/2021.3/Documentation/Manual/AssetBundlesIntro.html)
- [Unity 2021.3: paquete Addressables](https://docs.unity.cn/2021.3/Documentation/Manual/com.unity.addressables.html)
- [Unity 2021.1: Asset Bundle Browser y sus límites](https://docs.unity.cn/2021.1/Documentation/Manual/AssetBundles-Browser.html)
- [Blender 4.5 LTS: exportación FBX](https://docs.blender.org/manual/en/4.5/addons/import_export/scene_fbx.html)
- [Blender 4.5.14 LTS: versión, soporte y descargas oficiales](https://www.blender.org/releases/4-5/)
- [UnityExplorer: README de soporte Unity 2021+/BepInEx 5 Mono; repositorio archivado](https://github.com/sinai-dev/UnityExplorer)
- [UnityExplorer 4.8.2 en Thunderstore, con fecha y archivos manuales](https://thunderstore.io/c/backpack-hero/p/sinai-dev/UnityExplorer/versions/)
- [Runtime Unity Editor: inspector, consola y herramientas de depuración](https://github.com/ManlyMarco/RuntimeUnityEditor)
- [Runtime Unity Editor v6.3.2.1: release de BepInEx 5](https://github.com/ManlyMarco/RuntimeUnityEditor/releases/tag/v6.3.2.1)
- [BepInEx ConfigurationManager: soporte separado para BepInEx 5 Mono y BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx.ConfigurationManager)
- [Skill comunitaria BepInEx Mono](https://github.com/KaelLim/bepinex-modding/tree/main/skills/bepinex-mono-modding) — referencia general, no instalada sin adaptación al ciclo de vida/evidencia del juego.
- [HarmonyX: documentación del patcher usado por BepInEx](https://github.com/BepInEx/HarmonyX)
- [BepInEx MCP: puente de inspección en runtime](https://github.com/rkuhn153/bepinex-mcp)
- [Skill de BepInEx IL2CPP encontrada durante la búsqueda](https://github.com/KaelLim/bepinex-modding/tree/main/skills/bepinex-il2cpp-modding) — referencia de descarte por backend incompatible.
- [Unity AssetRipper: releases](https://github.com/AssetRipper/AssetRipper/releases)
