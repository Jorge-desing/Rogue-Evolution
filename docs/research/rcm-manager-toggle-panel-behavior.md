# RCM Manager: atajo configurable del panel

## Alcance de la observación

**Inspección estática** del checkout local de RCM-Manager (`e2fb9e468049574c8f9e622ccac936b2da17e569`): durante el arranque, configura `Hotkeys.TogglePanel` como un `KeyboardShortcut` BepInEx con F6 inicial y permite cambiarlo a una tecla o combinación. Al cambiar el valor actualiza el encabezado. En cada `Update`, si el atajo está pulsado y existe el overlay, invierte su visibilidad. El encabezado refleja el atajo vigente. El comentario del checkout afirma que F5 corresponde a una acción del juego y que F6 no está asignado en sus mapas de entrada; esa afirmación no se extiende a este loader ni a otras instalaciones/mods.

La conducta es una especificación externa observada en código; no se confirmó licencia para reutilizar ese checkout. No se copia código, assets, nombres de tipos ni dependencias de RCM.

## Adaptación clean-room al inspector

El loader ya usa BepInEx `ConfigFile` en `RangeDisplayRuntime` y su inspector ofrece dos teclas fijas (F8/F10), además del botón de marca de agua y Escape. La adaptación registra una tecla configurable mediante la `ConfigFile` existente, con F8 como valor inicial para conservar el atajo más habitual del loader. Una pulsación alterna únicamente la ventana del inspector; la marca de agua/badge permanece disponible. Se conserva Escape para cerrar y el botón de badge para alternar. F10 deja de ser un atajo separado; quien lo prefiera puede configurarlo como `ToggleKey`.

El atajo se muestra en el badge y pie del inspector. La transición de visibilidad se expresa como una política pura para comprobar apertura, cierre y prioridad del cierre si ambos eventos llegan en el mismo frame. No se añade parche Harmony, API de gameplay, almacenamiento propio ni dependencia: se usan BepInEx Configuration e Input Legacy ya referenciados por el proyecto.

## Verificación y límites de evidencia

- **Inspección estática:** la conducta de RCM se dedujo de su `RCMManager` local. No prueba que F6 esté libre en el juego actual ni que otros mods no lo utilicen.
- **Implementación independiente:** el contrato anterior se usa para una adaptación deliberadamente distinta al ciclo de vida del overlay del loader.
- **Validación local:** pruebas de la política pura y builds comprobarán la transición y compilación, no la captura efectiva de la combinación en Unity.
- **Runtime:** no se afirmará que la tecla funciona en el juego hasta observarla en una sesión Steam; el valor inicial sigue configurable para resolver conflictos.
