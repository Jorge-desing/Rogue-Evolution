# Rogue Command: control de pruebas en vivo por Steam

Este runbook es obligatorio antes de interactuar con una partida de Rogue Command. Léelo junto con `AGENTS.md`, `docs/research/AGENT-HANDOFF.md` y la skill `.agents/skills/rogue-command-steam-runtime-validation/SKILL.md`. El handoff enlaza aquí para que futuros agentes encuentren el procedimiento.

## Ruta de control

Usa el puente **BepInEx MCP** para inspeccionar y controlar una partida. En la sesión del 18 de octubre de 2026, el puente conectado ejecutó consultas y llamadas de Unity sin que Rogue Command tuviera que estar enfocado; no hace falta Computer Use para dar foco o para los controles que expone el puente. Recurre a control visual solo si una observación no está disponible por el puente y el usuario lo solicita.

No inicies `RogueCommand.exe` directamente. La partida de aceptación debe venir de Steam. Si ya hay una instancia abierta, inspecciona esa instancia y no la reinicies para repetir una prueba.

## Antes de usar firmas o cambiar estado

1. Comprueba el puente con `mcp__bepinex_mcp__ping_unity_bridge` y lee `mcp__bepinex_mcp__get_runtime_capabilities`. Anota runtime, versión de Unity, versión de BepInEx y arquitectura.
2. Obtén la ruta real del juego con `mcp__bepinex_mcp__get_game_paths`.
3. Revalida juntos versión del juego, SHA-256 y MVID de `Assembly-CSharp.dll`. Usa `mcp__rea__inspect_managed_artifact` en modo `summary` sobre la ruta devuelta y confirma la versión en el log del juego. No uses firmas reflejadas ni metadata tokens si cualquiera de los tres valores cambió.
4. Verifica el PID/estado de Steam y del juego, la escena activa, el perfil elegido y la existencia de `savegame.dat` antes de pulsar botones.

Identidad observada en esta sesión: Rogue Command 1.0.9; Unity 2021.3.45f2; BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Son valores de referencia, no sustituyen la comprobación de cada sesión.

## Inspección primero

Empieza con operaciones de lectura acotadas:

- `search_gameobjects` o `find_objects_with_component` para localizar un objeto.
- `get_hierarchy_snapshot` con profundidad y límite de nodos pequeños para ver sus hijos y estado activo.
- `inspect_components`, `get_component_details`, `get_component_member` y `list_component_methods` para identificar el control y su comportamiento.
- `events_get_new_events` y el log de BepInEx para correlacionar cambios con el paquete y la escena.

Anota escena, ruta/ID del GameObject, componente, perfil, modo de progresión y nivel del run. Los IDs Unity cambian entre escenas y ejecuciones; vuelve a resolverlos después de cada transición. No llames métodos globales del juego hasta entender sus efectos.

### Pulsar controles de Unity

Un `ButtonManager` puede tener listeners que abren diálogos o avanzan una secuencia, sin iniciar todavía la partida. Antes de invocarlo:

1. Lee `onClick` y enumera sus listeners persistentes (`GetPersistentTarget`, `GetPersistentMethodName`) con un único componente de inspección temporal.
2. Verifica `IsActive()` e `IsInteractable()` y revisa que el listener sea el esperado.
3. Invoca una sola vez mediante el componente temporal o el método específico expuesto por el puente. Comprueba escena y jerarquía después.
4. Destruye el componente temporal y confirma que ya no aparece en `inspect_components`.

No confirmes el diálogo de sobrescritura de una partida guardada. `StartNewRun.OnClick()` comprueba si existe el archivo del perfil; si existe, ofrece sobrescribirlo. Usa un perfil de prueba sin `savegame.dat` y conserva una copia previa de sus archivos. Nunca llames a `Game.Init`, `MetaGame.AddExperiencePoints` ni escribas campos de progresión como atajo para demostrar una recompensa real.

En una ejecución observada, el botón `Start Run Button` de `RunSetup` abrió `RunOpenerStoryCanvas`; su botón `ConfirmButton` avanzó la secuencia y llevó a `Game`. Ese recorrido terminó en `FirstRun`, nivel de run 4. No asumir que este flujo equivale a una partida normal ni que la pantalla de inicio es una oferta de recompensa.

## Comprobación de unidades y desbloqueos

El nivel persistente de perfil (`MetaGame.CurrentExperienceLevel`) es distinto del nivel del run, nodo del mapa y progresión de etapa. La inspección estática documentada en el handoff indica que la experiencia persistente se actualiza al terminar el run; pasar un encuentro intermedio no actualiza por sí solo el nivel de perfil. Una unidad con `neededExperienceLevel` solo puede entrar en candidatos cuando se cumple ese nivel, y el sorteo sigue siendo aleatorio.

En `sample-native-expansion`, cada producto y su fábrica declaran el mismo `rarity` y `neededExperienceLevel`. Niveles configurados actualmente:

| Nivel de perfil | Carta | Rareza |
|---:|---|---|
| 0 | Sandlance Scout | Common |
| 1 | Skirmish Strike Bike | Common |
| 2 | Ironclad Gatling Walker | Common |
| 3 | Surge Arc Tank | Common |
| 4 | Colossus Siege Platform | Common |

Las estadísticas de coste, producción, capacidad, salud, escudo/armadura, movilidad, visión, ataque, daño, alcance, recarga y habilidad deben declararse en los overrides de la unidad. El manifiesto y el validador local comprueban estos datos; la inspección de una oferta visible comprueba otra cosa y sigue siendo evidencia de partida.

Para afirmar que una carta apareció, registra la oferta que realmente se renderizó y su ID/nombre. Que el plugin cargue el paquete, que un ID exista en el registro o que un test local pase no prueba que la carta haya salido en una oferta aleatoria. La primera selección de unidades del stage 0 solo admite vehículos o unidades pequeñas, independientemente del nodo inicial; que no salga una unidad pesada allí no es un fallo del cargador. Repite ofertas elegibles antes de concluir que un contenido está ausente.

## Perfiles, datos y reversión

- No uses Profile 2: tiene una partida guardada. Profile 3 también tiene `savegame.dat`.
- El perfil 1 se eligió para esta prueba y se respaldó antes de iniciar. Su copia previa está en `artifacts/runtime-smoke/profile1-safe-test-2026-10-18/`; antes de la prueba no tenía `savegame.dat`.
- Steam Cloud está desactivado por instrucción persistente del usuario. No volver a preguntar ni cambiar esa opción.
- No borres los archivos que el juego cree durante la prueba. Conserva el nuevo save de prueba y vuelve al perfil anterior al salir limpiamente del juego. El selector previo era Profile 2 (`currentProfile.txt` contenía `2`); actualmente el intento de prueba usa Profile 1 (`1`).
- Antes de instalar o cambiar archivos del juego, conserva rutas, copias y SHA-256 de cada destino. No restaure una DLL/contenido mientras el juego siga abierto. Al acabar, restaura los archivos de instalación temporales y verifica sus hashes.
- Conserva logs, trazas y snapshots bajo `artifacts/runtime-smoke/` (ruta ignorada por Git). No incluyas logs locales, saves, BepInEx, Harmony ni assemblies del juego en el ZIP distribuible.
- Desactiva y elimina las sondas, watchers o patches creados para la prueba. Usa como máximo un inspector temporal por perfil de prueba; no dejes componentes de diagnóstico en objetos persistentes.
- Cambia `currentProfile.txt` solo después de confirmar que `Rogue Command.exe` ya terminó. Así evitas que la partida sobrescriba el selector durante la limpieza.

## Evidencia de la sesión del 18 de octubre de 2026

- **Runtime:** el puente respondió; Mono, Unity 2021.3.45f2 y BepInEx 5.4.23.5 x64. El log del plugin indicó 26 hooks verificados y ambos paquetes de muestra cargados: cinco unidades más cinco fábricas en `sample-native-expansion`; paquete de AI/stage reactivo separado.
- **Estado del run:** Profile 1, experiencia persistente nivel 0 y 0 XP; escena `Game`, modo `FirstRun`, nivel de run 4, mazo `RoboCrystalMill`, `BountyTankFactory`, `Tier0TankFactory`. Aún no se observó una pantalla `ChooseCard` ni una oferta aleatoria de unidades.
- **Conclusión:** esto confirma carga del plugin/paquetes y estado de un run de prueba. No confirma que las cartas nuevas aparezcan en ofertas, que una unidad se produzca o renderice, ni que una etapa o AI modded se ejecute.
- **Validación local:** 132/133 checks del harness pasaron; el único omitido fue la fixture de symlink por falta de privilegio de Windows. El validador resolvió 10/10 referencias de Sprite/prefab y el preflight estático resolvió 5/5 raíces visuales. Son pruebas locales, no de oferta o renderizado en partida.
- **Sondas:** los componentes temporales `Rcl*Probe` usados en la lectura se destruyeron y se comprobó su ausencia del HUD. No se modificaron archivos versionados ni de instalación; las salidas de validación están bajo `artifacts/`.

Cuando el juego cierre, restaura el selector a Profile 2 y deja intactos los datos recién creados en el perfil de prueba. No sobrescribas ni elimines perfiles para volver a un estado limpio.
