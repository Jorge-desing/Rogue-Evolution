# Línea base de investigación — Rogue Command

**Fecha:** 2026-10-16
**Objetivo:** preparar el análisis del sistema de mods y de las rutas de unidades y niveles antes de diseñar contenido nuevo.

## Alcance inspeccionado en la línea base original

- Carpeta de trabajo: raíz del repositorio (la ruta local depende del checkout).
- Instalación observada: `C:\Program Files (x86)\Steam\steamapps\common\Rogue Command`.
- Archivo de versión: `version.txt` muestra `Version 1.0.9`.
- En la inspección inicial no se encontraron archivos de proyecto, instrucciones `AGENTS.md` ni metadatos Git en la carpeta de trabajo. El repositorio y el cargador descritos en las secciones de actualización se crearon después. No se modificaron archivos de la instalación del juego ni se inició el juego durante esta línea base.
- Se leyeron los archivos de ayuda y manifiestos de `Rogue Command\Mod` y se inspeccionó estáticamente `Rogue Command_Data\Managed\Assembly-CSharp.dll` con REA. No se cargó ni ejecutó el ensamblado.

## Identidad del artefacto

| Dato | Observación |
|---|---|
| Ensamblado principal de juego | `Rogue Command_Data/Managed/Assembly-CSharp.dll` |
| Tamaño | 1,859,072 bytes |
| SHA-256 | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` |
| MVID | `db0e9462-128d-4296-9900-de18b4bd0c00` |
| Formato/runtime | PE32 x86; código administrado CIL; clasificado como Unity Mono |
| Resultado de metadatos | Completo, sin problemas de lectura reportados |

Las coordenadas de metadatos como tokens solo son válidas para esta pareja SHA-256/MVID. Si Steam actualiza el juego, hay que repetir el inventario y las inspecciones antes de reutilizar esos tokens.

## Hallazgos observados

### Sistema de mods integrado

`Mod/README.txt` indica que el juego sobrescribe sus archivos de ejemplo al iniciar, lee los nombres de los archivos desde las listas de `manifest.json` y procesa esas listas en orden. También dice explícitamente que `EntityBalancing` cambia datos de entidades existentes y no agrega entidades. El `manifest.json` instalado está vacío (`{"entityBalancing":[]}`); el manifiesto de ejemplo contiene `exampleEntityBalancing.tsv`.

La inspección estática del código coincide con ese alcance acotado:

- `ModManifest` tiene un campo `EntityBalancing` de tipo lista de cadenas.
- `EntityBalancingStore.Init` llama a `InitFromTsv`, a la rutina de actualización de ejemplos y a `OverwriteWithModFiles`.
- `InitFromTsv` referencia la tabla base `Balancing/Balancing - Entity`, valida encabezados y usa lectores TSV tipados para cargar valores.
- `OverwriteWithModFiles` obtiene sobrescrituras mediante `Mod.GetEntityBalancingOverwrites` y procesa filas y encabezados TSV.

**Conclusión acotada de la investigación inicial:** el formato de mods documentado y observado permite cambiar parámetros de entidades existentes; no registra entidades nuevas.

### Estructuras para niveles y mapas

La metadata y CIL de las clases seleccionadas muestran estos componentes:

- `LevelProgressionParametersScriptableObject` guarda listas de etapas (`stages`), AI, generadores de paisaje y mundos.
- `StageParametersScriptableObject` expone `levelCount`, `maxWidth`, recompensas, pools de blueprints y lista de mundos.
- `LevelProgression.CreateLevelMap` arma la progresión y campos del mapa, coloca recompensas y valida candidatos de AI, generador de paisaje y mundo.
- `LandscapeGenerator.Generate` aplica reglas de generación, construye la representación del mapa y ejecuta comprobaciones de conectividad y geometría.
- `StaticMapGenerator.Generate` crea mapas mediante generación procedural.

Esto identificó las rutas de código que luego se verificaron para la implementación del cargador. La inspección estática por sí sola no demuestra que los ScriptableObjects o mapas nuevos se carguen correctamente durante una partida.

## Registro de evidencia REA

| Evidencia | Uso | Alcance y límite |
|---|---|---|
| `ev_eea143512c436bb6579559b439d25e7356d59246194b33404955c85560424097` (enlace al registro original `ev_68e6372cbc7ac3d7a4e76b7126431e1e5b2a2dbcd8b1d2aec7042ac3cd4f7834`) | Identidad, SHA-256, MVID, runtime y estado completo de metadata del ensamblado. | Inspección estática de metadata; no decompila C# ni ejecuta el juego. |
| `ev_d6eec24c3950cf9ac31dc79fa83370d14eff5c4948a1488a3e8b3900a1d0284f` (registro completo seleccionado `ev_d72e4ede448d8b8f63ef672541e6426c62782b33bcd6ef81f6f8f0c93afeac88`) | Selección de 21 tipos relacionados con manifiestos, balance, etapas y mapas; metadata/CIL de esos tipos completa. | Cobertura completa para esos tipos seleccionados; no para todas las clases del ensamblado. |
| `ev_c3329ff9e2215a77bad6b5b9346a75f849894487c32b463e4fd36562d71a957a` | Métodos, anclas CIL y referencias de llamada de la selección; incluye `InitFromTsv`, `OverwriteWithModFiles`, `CreateLevelMap` y generadores. | CIL estático decodificado; los tokens de llamada son locales a la identidad del artefacto anterior. |
| `ev_fe7d770895011f2b0243419cc6771111bc9447e010800d2756e2ecf6442af9d1` | Campos seleccionados de manifiesto, progresión, etapas, mapas y generadores. | Página completa de los campos devueltos por la selección. |
| `Mod/README.txt`, `Mod/manifest.json`, `Mod/exampleManifest.json`, `Mod/exampleEntityBalancing.tsv` en la instalación indicada | Formato de manifiesto y límites documentados por el juego; ejemplos actuales de encabezados/filas. | Archivos locales de la instalación 1.0.9; el aviso del juego dice que los ejemplos se regeneran al iniciar. |

Una primera inspección sin selección de tipos superó el límite de representación de 64 MiB de REA. La selección exacta de 21 tipos sí se completó; por eso los resultados anteriores se limitan explícitamente a esas clases.

## Preguntas por resolver

| Pregunta | Estado | Próxima evidencia necesaria |
|---|---|---|
| ¿El TSV integrado puede añadir unidades? | Respondida: no; es una capa de sobrescritura de entidades existentes. | La instalación 1.0.9 documenta `EntityBalancing` como sobrescritura TSV. |
| ¿Cómo se registran unidades nuevas? | Implementado en el cargador para las colecciones verificadas de balance, índice y relaciones de fábrica; falta aceptación en partida. | `RuntimeContentMutator` valida y añade filas, índice y asignaciones. |
| ¿Cómo se cargan los prefabs y datos de unidades? | El cargador resuelve nombres namespaced desde AssetBundles y adapta puntos de fábrica observados; aún falta probar prefabs reales del juego. | Ver el contrato y limitaciones actuales en el `README.md` de la raíz. |
| ¿Se pueden añadir etapas o mundos mediante archivos externos? | Se incorporan etapas y AI a perfiles de progresión verificados; las etapas requieren referencias serializadas válidas a mundos y pools existentes. No se agregan definiciones nuevas de clases de juego. | El cargador actual modifica perfiles en memoria y exige assets compatibles existentes. |
| ¿Qué parte de AI se puede extender? | El plugin enlaza módulos C# compilados tras inicializar la AI nativa; los paquetes solo seleccionan IDs y parámetros. | `IRogueCommandAiModule`; el ejemplo no demuestra decisiones de AI personalizadas. |
| ¿Cómo se valida dentro del juego? | Aún pendiente: no se ejecutó el plugin en una partida ni se construyó el bundle de ejemplo. | Se necesita un prefab/escena con tipos del juego válidos y una prueba controlada en la instalación 1.0.9. |

## Plan de investigación

1. Confirmar y fijar la versión/identidad de cada artefacto antes de comparar resultados.
2. Trazar el descubrimiento de archivos del directorio `Mod`, el parseo del manifiesto y cada cargador de datos externo.
3. Relacionar IDs, definiciones de balance, prefabs, representación visual, blueprints y selección de AI.
4. Seguir la progresión de niveles: etapa, tamaño, mundo, generador de paisaje, reglas, mapa y recompensas.
5. Registrar cada conclusión como observación, inferencia o desconocido, con evidencia y cobertura explícitas.
6. Solo después de confirmar una ruta de extensión, preparar contenido nuevo del mod y definir una validación controlada.

## Límites de esta línea base

- No se ejecutó Rogue Command ni se observó comportamiento en tiempo real.
- El contenido del juego sigue intacto. El inventario inicial de assets fue estático: nombres/tipos y decodificación de cuatro `MonoBehaviour` seleccionados. Una prueba posterior con AssetRipper extrajo un `Sprite` a un scratch de `%TEMP%`; no se exportaron proyectos Unity, prefabs, audio, modelos ni bundles. El resultado y sus límites están en [ASSET-CATALOG.md](ASSET-CATALOG.md).
- La metadata/CIL revela nombres, firmas, cadenas cargadas y relaciones estáticas; no equivale a código fuente completo ni demuestra cómo resuelve Unity los assets durante una partida.
- `mod/manifest.json` es una plantilla vacía para la única forma de mod confirmada, sobrescrituras de balance; no registra contenido nuevo ni se copió a la instalación.

## Preparación completada — 2026-10-16

- Se inicializó Git en la carpeta de trabajo y se creó `.gitignore` para el entorno virtual, cachés Python y los índices grandes regenerables.
- Se añadieron `tools/asset_inventory.py` y `tools/inspect_unity_objects.py`, ambos de solo lectura, más `requirements.txt` con versiones fijadas.
- Se instaló Python 3.12 localmente en `.venv` con UnityPy 1.25.4 y TypeTreeGeneratorAPI 0.0.10. El Python 3.7 de WindowsApps no se usó.
- El inventario local leyó 16 archivos serializados sin error, encontró Unity 2021.3.45f2 y catalogó 562.122 objetos. El resumen e índice quedan en `docs/research/generated/` y no se versionan.
- La progresión y perfiles de etapa observados están resumidos en [ASSET-CATALOG.md](ASSET-CATALOG.md). Esa evidencia demuestra que hay datos internos serializados de etapas, pools, mundos y generadores, pero no una ruta para cargarlos desde un mod externo.
- La plantilla del formato integrado permanece en `mod/manifest.json` para sobrescrituras TSV. El cargador externo y el paquete de ejemplo se añadieron después, pero la aceptación en partida y el AssetBundle completo siguen pendientes.

## Actualización del cargador — 2026-10-16

- El código del plugin vive en `src/RogueCommandExpansionLoader/`; el proyecto auxiliar de Unity para AssetBundles vive en `unity/RogueCommandAssetAuthoring/`.
- El plugin comprueba la versión del juego, Unity, SHA-256 y MVID antes de instalar Harmony. Cambios de identidad desactivan los patches para evitar reutilizar firmas obsoletas.
- La compilación local del plugin terminó con cero errores y cero advertencias. La implementación cuenta con revisión estática; no equivale a aceptación en el juego.
- El AssetBundle del paquete de muestra no existe todavía. Faltan assets de juego serializados compatibles y una validación en una partida real.
