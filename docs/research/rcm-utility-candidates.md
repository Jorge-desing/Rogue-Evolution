# Candidatos para recreación clean-room de utilidades RCM

## Criterio

Se comparan funciones que aportan valor al loader sin reutilizar código, assets, dependencias ni organización interna de los checkouts locales. Las observaciones de esta tabla son inspección estática de documentos o comportamiento descrito en ellos; no son resultados de ejecución de RCM. La licencia reutilizable de los checkouts no está establecida.

| Candidato | Conducta observable documentada | APIs/dependencias del juego | Tamaño/riesgo clean-room | Valor para este loader | Decisión |
|---|---|---|---|---|---|
| RCM GUI: resumir mensajes idénticos consecutivos | El encabezado conserva el mensaje más reciente, muestra `no logs.` si no hay mensaje y añade un contador de repeticiones cuando se recibe consecutivamente el mismo texto. Un mensaje diferente reinicia el contador. | Ninguna API de gameplay. El loader ya mantiene su propio búfer acotado de logs y su inspector lo muestra. | Muy pequeño; lógica pura y aislable, sin dependencia del código original. | Reduce ruido visual en la pestaña Diagnóstico sin quitar el historial ni cambiar los logs. | **Elegido para este vertical slice.** |
| RCM Randomizer: rolls deterministas de estadísticas | Su documentación describe rolls de estadísticas alrededor del baseline, con modos PerSave/PerRun, semilla y compensación de poder en coste/tiempo. | Aplicación de cambios requiere el pipeline de balance del juego; el propio Randomizer declara dependencia dura del manager GUI y usa otros sistemas opcionales. | Grande si se integra en gameplay; alto riesgo de balance, persistencia y similitud accidental. El núcleo matemático podría estudiarse después de precisar el contrato. | Potencial de gameplay, pero supera el tamaño de una primera utilidad adicional y depende de APIs que requerirían verificación de identidad. | Pospuesto; limitar una futura fase a un núcleo puro y tests antes de cualquier adapter. |
| RCM RangeDisplay | Superposición de radios de unidades seleccionadas; ya existe una recreación independiente en el loader. | Unity/renderizado y lectura de entidades del juego. | Ya implementado; aún sin aceptación visual en partida. | Aporta una herramienta de combate, pero duplicarla no añadiría valor. | No reimplementar. |

## Elección y límites

Se implementa solo el resumen de mensajes consecutivos en el inspector del loader. Se mantiene intacto el búfer de hasta 100 líneas y el filtro existente; el resumen es una tarjeta adicional. Al comparar se ignora únicamente el prefijo local `HH:mm:ss ` si tiene formato exacto; las líneas sin ese prefijo se comparan literalmente. Se muestran el mensaje final del grupo y, si es mayor que cero, las repeticiones posteriores al primer mensaje; no se fusionan apariciones separadas por otro evento. No se añade hotkey ni configuración.

La política será una función local pura con cobertura de entradas vacías, timestamps y grupos interrumpidos. La compilación/pruebas locales no prueban la visualización dentro del juego; la aceptación visual queda pendiente de un runtime Steam controlado.

## Fuentes conductuales observadas

- Checkout local de RCM-Manager, `RCMManager.UpdateTitle`/`LogToUI`, commit `e2fb9e468049574c8f9e622ccac936b2da17e569`; checkout conservado fuera del repo bajo Hermes scratch. Sin código o assets del checkout incluidos.
- Checkout local de RCM-Randomizer: `README.md` y `docs/balance-analysis.md`; commit `c2dc05b916033a1c92a7f3642e778a119989d2b9`. Se usaron únicamente sus descripciones para clasificar el tamaño/las dependencias, no para reproducir el algoritmo.
- El contrato y el estado del loader se contrastaron con `RuntimeOverlayUi` y `ContentPackRuntimeLoader` del repositorio.
