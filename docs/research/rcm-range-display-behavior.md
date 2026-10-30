# Especificación conductual: visualización de alcances

## Propósito y clean-room

Esta especificación resume comportamientos observables en la documentación y el checkout local de RCM Range Display para orientar una implementación nueva. No se confirmó una licencia reutilizable; no se copia ni adapta código fuente, y RCM no será una dependencia. Las observaciones de abajo son evidencia estática del material local, no una prueba en partida.

## Comportamiento observado

- La utilidad muestra círculos de alcance para entidades que el jugador selecciona o sobre las que pasa el cursor; puede incluir enemigos al pasar el cursor.
- Ofrece categorías de alcance de arma, habilidad, efecto 1, efecto 2 y visión. Arma, habilidad y efectos están habilitados inicialmente; visión está deshabilitada.
- Distingue visualmente las categorías con colores y permite activar/desactivar cada categoría y la visualización global.
- El alcance de habilidad ilimitado no se representa como un círculo finito. Las armas cuerpo a cuerpo no reciben círculo de arma. Se evita dibujar el segundo radio de efecto cuando casi coincide con el primero.
- El radio efectivo puede cambiar durante la partida; la presentación se refresca mientras la entidad permanece observada.
- La presentación deja de seguir entidades al deseleccionarlas, perder el resaltado, ser destruidas o reutilizarse.
- La tecla predeterminada observada es F8; la versión referenciada también se integra con un panel externo de configuración.

## Alcance de la recreación independiente (MVP)

- Considera únicamente entidades vivas, seleccionadas y controladas por el jugador. La detección de hover/enemigos no forma parte de esta primera versión.
- Calcula las categorías a partir de propiedades públicas de `EntityController`; convierte celdas de juego a unidades de mundo con el factor nativo 10. Omite radios diminutos y radios coincidentes para evitar trazos superpuestos.
- Dibuja geometría de línea propia, sin clonar el círculo nativo. No añade Harmony hooks ni depende de RCM Manager.
- Usa ajustes BepInEx: apagado por defecto, tecla F11 configurable (el inspector del loader usa F8/F10; no se verificaron conflictos con otros mods ni reasignaciones), toggles por categoría, grosor, segmentos y desplazamiento vertical.
- Al desactivar la opción o descargar el componente, elimina las líneas que creó. La selección se vuelve a consultar con un intervalo limitado; no se agrega ningún componente al prefab del juego.

El MVP es deliberadamente más acotado que el comportamiento observado. La compilación y las pruebas puras locales no demuestran que los círculos se vean o sigan correctamente en una partida real; esa verificación queda pendiente.

## Compatibilidad y evidencia estática

Antes de usar las propiedades reflejadas/compiladas del juego se verificó conjuntamente: Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. La inspección administrada confirmó `EntityController` con `IsSelected`, `IsControlledByPlayer`, `IsDestroyed`, `CanAttack`, `melee`, `HasActiveSkill`, `SkillRangeIsUnlimited`, `WeaponRange`, `SkillRange`, `EffectRadius1`, `EffectRadius2` y `SightRadius`. También confirmó que el dibujo nativo escala cada radio por 10, pero el MVP usa su propio `LineRenderer`.
