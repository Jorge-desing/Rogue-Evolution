# RCM Manager: resumen de mensajes consecutivos

## Alcance de la observación

La inspección estática del checkout local de RCM-Manager (`e2fb9e468049574c8f9e622ccac936b2da17e569`) muestra un panel central cuyo encabezado presenta el último mensaje recibido; si no hay ninguno, muestra `no logs.`. Los mensajes consecutivos idénticos se resumen con un contador entre paréntesis. Cuando llega otro mensaje, el último mensaje cambia y el contador vuelve a cero. El panel actualiza el encabezado cuando cambia el registro y escribe el mensaje en el log de BepInEx.

La conducta se observó como especificación externa en el código del checkout; no se confirmó una licencia que permita reutilizarlo. No se incorpora fuente, asset, nombre de tipo, ni dependencia de RCM. Esta nota describe el comportamiento, no proporciona implementación de referencia.

## Adaptación clean-room al loader

El loader ya conserva hasta 100 entradas recientes en `ContentPackRuntimeLoader` y las presenta en la pestaña Diagnóstico de `RuntimeOverlayUi`. La adaptación resumirá las entradas finales de ese registro cuando compartan el mismo texto lógico. El prefijo local de hora (`HH:mm:ss `) no forma parte del mensaje lógico y se ignorará al comparar; el texto más reciente seguirá visible y se indicará el número de repeticiones posteriores a la primera. Un mensaje distinto separa los grupos; no se combinarán mensajes iguales separados por otro mensaje.

La UI seguirá mostrando el historial completo y el filtro existente. El resumen es adicional y no altera logs, hooks, configuración, selección aleatoria ni persistencia. No se añade una nueva hotkey.

## Verificación y límites de evidencia

- **Inspección estática:** la conducta descrita arriba se dedujo del checkout local de RCM Manager. No se afirma que ese checkout esté licenciado para reutilización.
- **Implementación independiente:** la política de resumen se escribirá desde este contrato y se probará con entradas controladas del loader.
- **Validación local:** las pruebas del helper y la compilación del loader solo demostrarán la lógica y compilación locales.
- **Runtime:** hasta hacer una partida por Steam y ver la pestaña Diagnóstico, no se afirmará que el resumen aparece correctamente en el juego.
