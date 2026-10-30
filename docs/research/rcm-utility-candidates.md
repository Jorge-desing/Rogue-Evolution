# Candidatos para recreación clean-room de utilidades RCM

## Criterio

Se comparan funciones que aportan valor al loader sin reutilizar código, assets, dependencias ni organización interna de los checkouts locales. Las observaciones de esta tabla son inspección estática de documentos o comportamiento descrito en ellos; no son resultados de ejecución de RCM. La licencia reutilizable de los checkouts no está establecida.

| Candidato | Conducta observable documentada | APIs/dependencias del juego | Tamaño/riesgo clean-room | Valor para este loader | Decisión |
|---|---|---|---|---|---|
| RCM GUI: resumir mensajes idénticos consecutivos | El encabezado conserva el mensaje más reciente, muestra `no logs.` si no hay mensaje y añade un contador de repeticiones cuando se recibe consecutivamente el mismo texto. Un mensaje diferente reinicia el contador. | Ninguna API de gameplay. El loader ya mantiene su propio búfer acotado de logs y su inspector lo muestra. | Muy pequeño; lógica pura y aislable, sin dependencia del código original. | Reduce ruido visual en la pestaña Diagnóstico sin quitar el historial ni cambiar los logs. | Implementado en `LoaderLogBurstSummaryPolicy`; validación local, runtime visual pendiente. |
| RCM GUI: atajo configurable para mostrar/ocultar el panel | `Hotkeys.TogglePanel` usa un `KeyboardShortcut` BepInEx configurable, F6 por defecto; puede ser tecla o combinación, y el título presenta el valor actual. Al activarlo, alterna la visibilidad del overlay. | Solo configuración BepInEx e input de Unity ya referenciados; sin API de gameplay. | Pequeño; política de transición pura más el binding existente, sin dependencias nuevas. | Resolver los atajos fijos actuales del inspector (F8/F10) y permitir combinación configurable, conservando el badge como vía de acceso. | Implementado como `Inspector.ToggleKey` (F8 inicial); validación local aprobada, sin evidencia visual en partida. |
| RCM Randomizer: rolls deterministas de estadísticas | Su documentación describe rolls de estadísticas alrededor del baseline, con modos PerSave/PerRun, semilla y compensación de poder en coste/tiempo. | Aplicación de cambios requiere el pipeline de balance del juego; el propio Randomizer declara dependencia dura del manager GUI y usa otros sistemas opcionales. | Grande si se integra en gameplay; alto riesgo de balance, persistencia y similitud accidental. El núcleo matemático podría estudiarse después de precisar el contrato. | Potencial de gameplay, pero supera el tamaño de una primera utilidad adicional y depende de APIs que requerirían verificación de identidad. | Pospuesto; limitar una futura fase a un núcleo puro y tests antes de cualquier adapter. |
| RCM RangeDisplay | Superposición de radios de unidades seleccionadas; ya existe una recreación independiente en el loader. | Unity/renderizado y lectura de entidades del juego. | Ya implementado; aún sin aceptación visual en partida. | Aporta una herramienta de combate, pero duplicarla no añadiría valor. | No reimplementar. |

## Elección y límites

Se recrearon dos conductas en slices separados: el resumen de mensajes consecutivos y el atajo configurable del inspector. El resumen mantiene intactos el búfer de hasta 100 líneas y su filtro, y solo presenta una tarjeta adicional. Al comparar se ignora el prefijo local exacto `HH:mm:ss `; líneas sin ese prefijo se comparan literalmente. Se muestran el mensaje final del grupo y las repeticiones posteriores a la primera, sin fusionar apariciones separadas. El atajo usa `Inspector.ToggleKey` (F8 inicial), alterna la ventana, conserva el badge, y Escape cierra con prioridad.

Ambos tienen validación local mediante build y harness; eso no prueba la visualización dentro del juego. La aceptación visual de las dos utilidades sigue pendiente de un runtime Steam controlado.

## Fuentes conductuales observadas

- Checkout local de RCM-Manager, `RCMManager.UpdateTitle`/`LogToUI`, commit `e2fb9e468049574c8f9e622ccac936b2da17e569`; checkout conservado fuera del repo bajo Hermes scratch. Sin código o assets del checkout incluidos.
- Checkout local de RCM-Randomizer: `README.md` y `docs/balance-analysis.md`; commit `c2dc05b916033a1c92a7f3642e778a119989d2b9`. Se usaron únicamente sus descripciones para clasificar el tamaño/las dependencias, no para reproducir el algoritmo.
- El contrato y el estado del loader se contrastaron con `RuntimeOverlayUi` y `ContentPackRuntimeLoader` del repositorio.
