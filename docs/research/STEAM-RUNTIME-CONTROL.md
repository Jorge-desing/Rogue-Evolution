# Rogue Command: control de pruebas en vivo por Steam

Este runbook es obligatorio antes de interactuar con una partida de Rogue Command. Léelo junto con `AGENTS.md`, `docs/research/AGENT-HANDOFF.md` y la skill `.agents/skills/rogue-command-steam-runtime-validation/SKILL.md`. El handoff enlaza aquí para que futuros agentes encuentren el procedimiento.

## Ruta de control

Usa el puente **BepInEx MCP** para inspeccionar y controlar una partida. En la sesión del 18 de octubre de 2026, el puente conectado ejecutó consultas y llamadas de Unity sin que Rogue Command tuviera que estar enfocado; no hace falta Computer Use para dar foco o para los controles que expone el puente. Recurre a control visual solo si una observación no está disponible por el puente y el usuario lo solicita.

No inicies `RogueCommand.exe` directamente. La partida de aceptación debe venir de Steam. Si ya hay una instancia abierta, inspecciona esa instancia y no la reinicies para repetir una prueba.

## Pausa segura y control de botones

Cuando el usuario pida detener el avance, abre el menú nativo de pausa con Escape o, si el puente es el medio disponible, resuelve en la escena actual `InGameMenuCanvas` y llama una sola vez a `InGameMenu.Show()`. Lee `InGameMenu._isShowing` después de la acción. Ese campo confirma que el menú está visible; si es importante probar que la simulación se congeló, comprueba además el reloj/contador de juego o `Time.timeScale` disponible en esa build. No dejes correr la partida mientras inspeccionas o razonas.

Antes de pulsar un botón del juego, vuelve a resolver su GameObject en la escena actual; los Instance IDs cambian al cargar otra escena o iniciar otra ejecución. Inspecciona componentes, `interactable`, texto visible (`UnityEngine.UI.Text.text` o el componente de texto presente) y listeners de Unity. Para controles nativos, `call_component_method` sobre `UnityEngine.UI.Button.Press()` ejecutó el evento normal del botón en la sesión observada. Pulsa solo una vez y comprueba de nuevo escena, paneles y estado de pausa. No trates una respuesta truncada como fallo de la operación: reduce el árbol o consulta el componente concreto.

En el tutorial FirstRun, el menú contiene `AbandonGoToMenuPanel`. Su texto en vivo avisa: «Esto hará que pierdas la partida actual. Aún así recibirás experiencia por los niveles anteriores. Si sales entre niveles, se guardará tu partida.» El botón `Sí, abandonar.` descarta el run actual; `No, seguir jugando.` cierra solo la confirmación y conserva el menú de pausa. No confirmes el abandono para llegar a otro flujo sin autorización explícita. En la sesión del 2026-10-18 se eligió `No, seguir jugando.` y se verificó `AbandonGoToMenuPanel.active=false` junto con `InGameMenu._isShowing=true`.

## Antes de usar firmas o cambiar estado

1. Comprueba el puente con `mcp__bepinex_mcp__ping_unity_bridge` y lee `mcp__bepinex_mcp__get_runtime_capabilities`. Anota runtime, versión de Unity, versión de BepInEx y arquitectura.
2. Obtén la ruta real del juego con `mcp__bepinex_mcp__get_game_paths`.
3. Revalida juntos versión del juego, SHA-256 y MVID de `Assembly-CSharp.dll`. Usa `mcp__rea__inspect_managed_artifact` en modo `summary` sobre la ruta devuelta y confirma la versión en el log del juego. No uses firmas reflejadas ni metadata tokens si cualquiera de los tres valores cambió.
4. Verifica el PID/estado de Steam y del juego, la escena activa, el selector de perfil que realmente usa el juego y la existencia de `savegame.dat` antes de pulsar botones. Resuelve el selector desde `ProfileManager.CurrentProfileTextPath`/`CurrentProfilePath`, no desde un archivo histórico de la carpeta de instalación.

En esta instalación el proceso se llama exactamente `Rogue Command.exe` (con espacio). `Get-Process -Name RogueCommand` no lo encuentra y puede producir un falso diagnóstico de que el juego está cerrado. Comprueba el ejecutable real con:

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Rogue Command.exe' } |
  Select-Object ProcessId, Name, ExecutablePath
```

`ping_unity_bridge` comprueba que responde el endpoint HTTP del puente; por sí solo no confirma que haya una sesión Unity conectada. Después del ping, confirma capacidades/rutas y una consulta de escena o componente. En esta sesión, `get_game_paths`, búsqueda de componentes y lecturas de `DefaultAiBehaviour` respondieron correctamente.

El puente se aloja dentro del proceso del juego. Si `Rogue Command.exe` está cerrado, `ping_unity_bridge` puede responder `Cannot connect to the Unity MCP bridge`; al iniciar la partida desde Steam, vuelve a comprobar ping, capacidades y una lectura Unity real. En la sesión del 18-10-2026, ese error desapareció tras el inicio por Steam (AppID `1461910`), y se confirmó tanto el PID `26220` durante la validación como el PID `36476` en `MainMenu` después de restaurar la instalación. No lo trates como fallo de instalación cuando no haya un proceso Unity activo.

### Reparar el entorno MCP local

Si el juego está abierto desde Steam pero el puente sigue sin responder, separa primero el estado del proceso de los errores del servidor MCP local. Desde la raíz del repositorio, ejecuta:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Repair-BepInExMCP.ps1
```

El script valida las rutas confinadas a `.tools/BepInExMCP`, instala una versión compatible de Pydantic con las restricciones del repositorio, ejecuta `pip check`, exige MCP SDK 1.20.0, comprueba la reconstrucción de `FastMCPSettings` y verifica `ModdersHelperApp.py --help`. Puede completar la reparación local idempotente de `FastMCPSettings.model_rebuild()`; no instala el puente en el juego, no modifica la configuración de Codex, no inicia Rogue Command y no llama herramientas de Unity. Tras el script, repite ping, capacidades y una consulta acotada de escena/componente antes de afirmar que se recuperó el puente.

Un `ping` correcto solo demuestra que responde el endpoint. Si una lectura grande se trunca, reduce profundidad/número de nodos y consulta campos concretos. Si falla un helper PowerShell/C# local, clasifica primero si falló la compilación del helper o la llamada MCP; un error de `Add-Type` (por ejemplo, un `using System` ausente) no demuestra que BepInEx esté caído.

### Captura visual como respaldo de diagnóstico

Si dos intentos independientes de inspección fallan o devuelven estados contradictorios, deja de repetir la misma llamada y usa la skill `screenshot` (`C:\Users\Alex\.codex\skills\screenshot\SKILL.md`) para inspeccionar la ventana objetivo. En Windows, el helper es `scripts/take_screenshot.ps1`; para una captura que solo sirve al análisis, usa `-Mode temp`. Conserva una copia bajo `artifacts/runtime-smoke/` únicamente si aporta evidencia útil a la prueba. Verifica el título y el contenido antes de interpretarla: una captura del escritorio puede mostrar Codex/Antigravity en primer plano con el juego detrás, y capturar los límites de una ventana tapada no revela su contenido. Si la ventana del juego no está visible, la imagen no es evidencia del estado del juego; pide al usuario que lo ponga al frente antes de repetir una captura visual. Correlaciona la imagen con escena, log y lecturas del puente; una captura no demuestra estado interno que no se vea.

Identidad observada en esta sesión: Rogue Command 1.0.9; Unity 2021.3.45f2; BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Son valores de referencia, no sustituyen la comprobación de cada sesión.

### Resolver el perfil activo

En el `Assembly-CSharp.dll` de esta identidad, el inicializador de `ProfileManager` construye `ProfilesPath` y `CurrentProfileTextPath` desde `Application.persistentDataPath`. En esta instalación, `persistentDataPath` es `C:\Users\Alex\AppData\LocalLow\Feneq\Rogue Command`; el juego lee el selector `Profiles\currentProfile.txt` bajo esa raíz y obtiene `savegame.dat` desde el perfil resultante. No uses `currentProfile.txt` ni `Profiles\currentProfile.txt` dentro de la carpeta de instalación como selector activo: son rutas heredadas y pueden conservar valores antiguos distintos.

En la lectura en vivo del 2026-10-18, el selector de `LocalLow` valía `1`, mientras los dos archivos de la instalación valían `2` y `3`. El perfil activo era `Profile1`; al iniciar no tenía `savegame.dat`. `Profile2` y `Profile3` sí tenían guardados. Esta comprobación estática y de archivos corrige la clasificación de la lectura histórica más abajo: vuelve a consultar la ruta de `ProfileManager` en cada sesión y no cambies ninguno de los selectores heredados para elegir un perfil de prueba.

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

- Vuelve a comprobar cada perfil en cada intento: la presencia de `savegame.dat` y otros datos cambia y no debe inferirse de una captura histórica. La lectura histórica de la carpeta de instalación que clasificó Profile 3 como vacío no consultó el selector real de `persistentDataPath`; la lectura actual muestra guardados en Profile 2 y Profile 3.
- El manifiesto `loader-stage-ai-20261018-113519/deployment-manifest.json` anotó `originalSelector=3` y temporal `1` en la ruta de la instalación. Ese cambio no seleccionó el perfil activo que usa `ProfileManager`: el selector de `LocalLow` ya valía `1`. La limpieza registrada restauró los archivos que había tocado, pero no prueba que Profile 3 fuese el perfil activo.
- La reversión de archivos del juego y de perfiles debe basarse en las rutas y SHA-256 del intento actual. Nunca restaures el selector real a `2` o `3` basándote solo en el manifiesto histórico citado arriba.
- Steam Cloud está desactivado por instrucción persistente del usuario. No volver a preguntar ni cambiar esa opción.
- No borres los archivos que el juego cree durante la prueba. Respalda todos los datos existentes del perfil activo antes de iniciarla y, al cerrar el juego, restaura desde ese respaldo los archivos previos que la prueba haya modificado. Conserva los archivos nuevos creados durante la prueba y restaura únicamente el selector bajo `persistentDataPath` si el intento lo cambió expresamente.
- Antes de instalar o cambiar archivos del juego, conserva rutas, copias y SHA-256 de cada destino. No restaure una DLL/contenido mientras el juego siga abierto. Al acabar, restaura los archivos de instalación temporales y verifica sus hashes.
- Conserva logs, trazas y snapshots bajo `artifacts/runtime-smoke/` (ruta ignorada por Git). No incluyas logs locales, saves, BepInEx, Harmony ni assemblies del juego en el ZIP distribuible.
- Desactiva y elimina las sondas, watchers o patches creados para la prueba. Usa como máximo un inspector temporal por perfil de prueba; no dejes componentes de diagnóstico en objetos persistentes.
- Cambia `currentProfile.txt` solo después de confirmar que `Rogue Command.exe` ya terminó. Así evitas que la partida sobrescriba el selector durante la limpieza.

## Evidencia de la sesión del 18 de octubre de 2026

- **Runtime:** el puente respondió; Mono, Unity 2021.3.45f2 y BepInEx 5.4.23.5 x64. El log del plugin indicó 26 hooks verificados y ambos paquetes de muestra cargados: cinco unidades más cinco fábricas en `sample-native-expansion`; paquete de AI/stage reactivo separado.
- **Estado del run:** Profile 1, experiencia persistente nivel 0 y 0 XP; escena `Game`, modo `FirstRun`, nivel de run 4, mazo `RoboCrystalMill`, `BountyTankFactory`, `Tier0TankFactory`. Aún no se observó una pantalla `ChooseCard` ni una oferta aleatoria de unidades.
- **Control en pausa:** en la continuación del tutorial se comprobó `InGameMenu._isShowing=true`. La lectura de jerarquía/textos expuso la advertencia de abandono; `UnityEngine.UI.Button.Press()` sobre `KeepPlaying` cerró el diálogo sin abandonar y se verificó `AbandonGoToMenuPanel.active=false`, manteniendo la pausa. Una captura de escritorio mostró Antigravity en primer plano y no se usó como evidencia visual del juego.
- **Conclusión:** esto confirma carga del plugin/paquetes y estado de un run de prueba. No confirma que las cartas nuevas aparezcan en ofertas, que una unidad se produzca o renderice, ni que una etapa o AI modded se ejecute.
- **Validación local:** 132/133 checks del harness pasaron; el único omitido fue la fixture de symlink por falta de privilegio de Windows. El validador resolvió 10/10 referencias de Sprite/prefab y el preflight estático resolvió 5/5 raíces visuales. Son pruebas locales, no de oferta o renderizado en partida.
- **Sondas:** los componentes temporales `Rcl*Probe` usados en la lectura se destruyeron y se comprobó su ausencia del HUD. No se modificaron archivos versionados ni de instalación; las salidas de validación están bajo `artifacts/`.

Cuando el juego cierre, conserva la selección del perfil activo que registraste antes de la sesión y deja intactos los datos recién creados en el perfil de prueba. No cambies el selector a Profile 2 ni sobrescribas o elimines perfiles para volver a un estado limpio.
