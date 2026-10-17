# Herramientas para crear contenido de Rogue Command

**Revisado:** 2026-10-17

**Objetivo:** producir unidades, etapas/mapas y AI originales para el mod Rogue Evolution. El loader se mantiene como infraestructura necesaria.

## Resultado de la búsqueda de skills

No encontré una skill pública específica para crear contenido de Rogue Command ni una que cubra BepInEx 5 Mono y authoring de unidades, etapas y AI para Unity 2021.3. La skill de BepInEx para IL2CPP que aparece en GitHub no es compatible con este juego: Rogue Command usa Mono y la rama objetivo de BepInEx es 5. La skill comunitaria asociada a `bepinex-mcp` se centra en inspeccionar y modificar un juego que ya está ejecutándose; no crea modelos, mapas o datos de juego.

La documentación oficial de BepInEx sí sirve para el trabajo de plugin y Harmony. La búsqueda en el catálogo local de `skill-installer` tampoco encontró una skill de creación de mods de Unity Mono que encaje con el contenido de este repositorio. Por eso no se instaló una skill de otra plataforma o backend como sustituto.

## Inventario local verificado

| Herramienta | Estado | Uso adecuado |
|---|---|---|
| Unity Editor **2021.3.45f2** | Instalado en `C:\Program Files\Unity\Hub\Editor\2021.3.45f2` | Authoring y build de AssetBundles para la misma línea de Unity que Rogue Command. El proyecto del repo ya incluye un builder y scaffolder; conservar sus `ProjectSettings`, paquetes y configuración de Unity Cloud/Version Control. |
| UnityPy **1.25.4** | Instalado en `.venv` | Inspección offline de archivos serializados y bundles. No demuestra que un objeto vaya a cargar ni comportarse en una partida. |
| AssetRipper **2.0.0** | Disponible en `.tools/AssetRipper-2.0.0/` | Extracción puntual de assets para referencia de authoring. En la prueba local produjo PNG/YAML/JSON para un Sprite, no un prefab o proyecto Unity importable; no tratar los archivos del juego como assets originales del mod. |
| ILSpy (`ilspycmd`) | Disponible en `.tools/ilspycmd/` | Inspección estática de firmas y lógica administrada; antes de usar firmas reflejadas, confirmar versión, SHA-256 y MVID juntos. |
| Blender | No aparece en PATH ni en la ubicación estándar consultada | Editor opcional de modelos 3D. Conviene usar FBX para pasar geometría a Unity. No hace falta para los meshes básicos que hoy genera el scaffolder, pero sí será útil para unidades con silueta propia. |
| UnityExplorer | No instalado; repositorio original archivado en 2023 | Su README declara soporte para Unity 2021+ y BepInEx 5 Mono. Puede ayudar a observar GameObjects y componentes en runtime, pero la falta de mantenimiento es una limitación. Es una instalación dentro de BepInEx del juego; considerar solo una versión fijada y reversible cuando el arranque por Steam alcance una escena. |
| BepInEx MCP (`bepinex-mcp`) | No instalado | Puente opcional para consultar o cambiar objetos de un juego que ya corre. Requiere un plugin/servidor adicionales y ofrece operaciones de runtime; no es necesario para el flujo de authoring ni sustituye la validación integrada del loader. |

La carpeta `.tools/` y `.venv/` están excluidas de Git por `.gitignore`. La salida distribuible vive bajo `content/packs/<id>/bundles/`, que sí se versiona.

## Recomendaciones de uso

1. **Unidad:** empezar desde una plantilla nativa compatible para conservar la lógica de gameplay y crear una silueta original en Blender o con geometría de Unity. Importar el modelo a `unity/RogueCommandAssetAuthoring/`, compilarlo como AssetBundle Windows x64 y asignarlo a una ruta visual nativa validada. La ruta de overlay actual conserva los componentes nativos, pero no reasigna rigs o animaciones. Una nueva apariencia puede probarse en Editor; el control de movimiento, armas, sombras/materiales y renderizado se confirma dentro del juego.
2. **Etapa y mapa:** la variante de etapa actual clona `StageParametersScriptableObject` y reutiliza mundo y blueprint pools del juego. Esto es útil para probar progresión y balance, pero no crea por sí mismo un mapa original. Un mapa nuevo necesita las referencias serializadas completas de mundo, generador y pools que el juego espera; investigar y construir ese grafo forma parte del contenido, no se resuelve añadiendo campos numéricos al loader.
3. **AI:** los paquetes eligen módulos compilados por ID; no distribuyen DLLs. El módulo de muestra actual solicita construir una unidad del deck nativo después de un intervalo y no aporta táctica nueva. Para contenido AI, implementar módulos C# que alteren una decisión observable, respeten la vida útil del AI nativo y tengan parámetros acotados; luego medir el resultado en una partida.
4. **Validación:** hacer preflight del pack, inspeccionar el AssetBundle con Unity Editor/UnityPy y registrar hashes. Estos son checks offline. La aceptación requiere una ejecución reversible del juego **a través de Steam**, confirmando que la unidad se instancia y aparece en el pool, la etapa genera el mapa previsto y la AI ejecuta la nueva decisión.

## Herramientas que no añaden valor ahora

- **Addressables:** el mod ya entrega bundles y el loader usa `AssetBundle` directamente. Addressables es una capa superior que genera catálogos, direcciones y flujo de dependencias; todavía no hay evidencia de que el juego tenga un catálogo Addressables utilizable por packs externos. No añadir esa dependencia sin una necesidad demostrada.
- **Asset Bundle Browser:** Unity lo describe como herramienta auxiliar/pre-release y de alcance básico. El repo ya tiene `RogueCommandAssetBundleBuilder.cs`, que aplica las rutas y límites del pack; usar el builder existente da una salida más controlable.
- **Habilidades BepInEx IL2CPP:** no aplicar sus instrucciones al juego Mono ni mover el plugin a BepInEx 6/Il2CppInterop sin que cambie y se verifique la identidad técnica del juego.
- **Herramientas de runtime:** instalar UnityExplorer o BepInEx MCP solo cuando el arranque por Steam llegue a una escena y los diagnósticos actuales no basten. Hacerlo en una instalación reversible, registrar los archivos temporales y retirarlos tras la inspección.

## Fuentes

- [BepInEx: documentación oficial y soporte Unity Mono/Harmony](https://docs.bepinex.dev/)
- [BepInEx: guía oficial para crear plugins](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html)
- [Unity 2021.3: AssetBundles y recursos serializados sin distribuir definiciones de clases](https://docs.unity3d.com/2021.3/Documentation/Manual/AssetBundlesIntro.html)
- [Unity 2021.3: paquete Addressables](https://docs.unity.cn/2021.3/Documentation/Manual/com.unity.addressables.html)
- [Unity 2021.1: Asset Bundle Browser y sus límites](https://docs.unity.cn/2021.1/Documentation/Manual/AssetBundles-Browser.html)
- [Blender 4.5 LTS: exportación FBX](https://docs.blender.org/manual/en/4.5/addons/import_export/scene_fbx.html)
- [UnityExplorer: README de soporte Unity 2021+/BepInEx 5 Mono y estado archivado](https://github.com/sinai-dev/UnityExplorer)
- [BepInEx MCP: puente de inspección en runtime](https://github.com/rkuhn153/bepinex-mcp)
- [Skill de BepInEx IL2CPP encontrada durante la búsqueda](https://github.com/KaelLim/bepinex-modding/tree/main/skills/bepinex-il2cpp-modding) — referencia de descarte por backend incompatible.
- [Unity AssetRipper: releases](https://github.com/AssetRipper/AssetRipper/releases)
