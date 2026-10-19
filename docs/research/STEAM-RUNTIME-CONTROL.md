# Rogue Command: control de pruebas en vivo por Steam

Este runbook es obligatorio antes de interactuar con una partida de Rogue Command. Léelo junto con `AGENTS.md`, `docs/research/AGENT-HANDOFF.md` y la skill `.agents/skills/rogue-command-steam-runtime-validation/SKILL.md`. El handoff enlaza aquí para que futuros agentes encuentren el procedimiento.

## Ruta de control

Usa el puente **BepInEx MCP** para inspeccionar y controlar una partida. En la sesión del 18 de octubre de 2026, el puente conectado ejecutó consultas y llamadas de Unity sin que Rogue Command tuviera que estar enfocado; no hace falta Computer Use para dar foco o para los controles que expone el puente. Recurre a control visual solo si una observación no está disponible por el puente y el usuario lo solicita.

Si tras dos intentos una consulta/control sigue fallando o el resultado del puente contradice el estado visible, detén los intentos a ciegas y aplica la skill `screenshot` como diagnóstico visual. Prefiere `tools/Capture-RogueCommand-Background.ps1`, que usa `PrintWindow` sin activar la ventana; registra la ruta de salida y compara el HWND en primer plano antes y después para demostrar que la captura no robó el foco. La captura complementa las lecturas del puente, no sustituye la comprobación de flags o del reloj del juego.

No inicies `RogueCommand.exe` directamente. La partida de aceptación debe venir de Steam. Si ya hay una instancia abierta, inspecciona esa instancia y no la reinicies para repetir una prueba.

## Pausa segura y control de botones

Cuando el usuario pida detener el avance, abre el menú nativo con Escape o, si el puente es el medio disponible, resuelve en la escena actual `InGameMenuCanvas` y llama una sola vez a `InGameMenu.Show()`. Después verifica por separado el menú y la simulación: `InGameMenu._isShowing=true` solo confirma que el menú está visible. En la build identificada abajo, `InGameMenu.Show()` dejó el HUD avanzando de `00:16` a `01:19` con `Time.timeScale=1`; abrir el menú por sí solo no detuvo la partida. Para pausar de verdad, usa primero la ruta nativa `Navigator.Pause()` y confirma `Navigator._isPaused=true` junto con `Time.timeScale=0`. Si la build o el puente no permiten esa ruta, verifica el reloj en dos observaciones separadas por varios segundos y usa el helper temporal de escala descrito abajo solo como último recurso.

Si Escape/`Show()` deja el menú abierto, la ruta `Navigator.Pause()` no está disponible y `Time.timeScale` sigue en `1`, el último recurso es congelar la simulación con un `MonoBehaviour` temporal compilado mediante `mod_inject_class`, adjuntarlo al `InGameMenuCanvas` actual y llamar su método `Freeze()` una sola vez por el puente. El campo `scaleAfter` solo registra la lectura tomada dentro de esa llamada: no es una lectura viva. Antes de reassertar una pausa temporal, toma una lectura nueva de `Time.timeScale`. En la sesión actual, la clase dinámica inicial no tenía el guard `hasFrozen`; una segunda llamada a `Freeze()` sobrescribió `scaleBefore` de `1` a `0`. Se restauró el campo a `1` sin reanudar el juego. Una implementación nueva debe conservar el valor original solo una vez, como en este patrón. No llames `ResumeAndRemove()` después de activar la pausa nativa, porque escribiría el `Time.timeScale` anterior mientras el juego todavía considera activa la pausa:

```csharp
using UnityEngine;

public sealed class RclRuntimeTimeControl : MonoBehaviour
{
    public float scaleBefore;
    public float scaleAfter;
    private bool hasFrozen;
    public float currentScale;
    public float scaledTime;

    public void Freeze()
    {
        if (!hasFrozen)
            scaleBefore = Time.timeScale;
        Time.timeScale = 0f;
        scaleAfter = Time.timeScale;
        currentScale = Time.timeScale;
        scaledTime = Time.time;
        hasFrozen = true;
    }

    public void Sample()
    {
        currentScale = Time.timeScale;
        scaledTime = Time.time;
    }

    public void ResumeAndRemove()
    {
        if (hasFrozen)
            Time.timeScale = scaleBefore;
        Destroy(this);
    }
}
```

El helper realmente usado al principio leyó `Time.timeScale=1`, lo cambió a `0` y expuso `scaleBefore=1`, `scaleAfter=0`; su `ResumeAndRemove()` restablecía `scaleBefore`. Esa congelación directa no establecía por sí sola el estado nativo de `Navigator`. Después se llamó `Navigator.Pause()`, se retiró el helper sin reanudar y se comprobó la pausa nativa con una sonda de solo lectura. La sonda observó `Time.timeScale=0` y `Time.time=537.5239`; 16,38 segundos después de tiempo no escalado, el reloj escalado seguía exactamente en `537.5239` y el HUD continuaba en `03:51`. `Time.unscaledTime` sí avanzó, lo cual es esperado y por sí solo no significa que siga la simulación. Para separar una lectura actual de una captura anterior, usa `Sample()` y vuelve a leer sus campos.

### Pausa nativa de Rogue Command

La identidad comprobada para esta lectura fue Rogue Command `1.0.9`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. La inspección estática de esa build encontró `Navigator.Pause()` y `Navigator.Unpause()`. `Pause()` escribe `Navigator._isPaused=true`, asigna escala cero y ajusta el audio; `Unpause()` limpia las banderas y restablece la velocidad normal. `InGameMenu.Show()` muestra el overlay y empuja un estado de navegación, pero el overlay visible por sí solo no es evidencia suficiente de pausa: confirma el flag y la escala en el runtime.

En la sesión supervisada del 2026-10-18, se llamó `Navigator.Pause()` por reflexión con el menú abierto. La lectura antes y después indicó `_isPaused=true` y `Time.timeScale=0`. Luego se retiró `RclRuntimeTimeControl` sin invocar `ResumeAndRemove()`; una sonda de solo lectura, sin el helper de pausa, confirmó `_isPaused=true`, escala `0` y `Time.time=537.5239`. Después se retiraron la sonda y la acción temporal; la lista final de componentes de `InGameMenuCanvas` quedó solo con los componentes del juego. El HUD siguió en `03:51` en la captura sin foco [rogue-command-native-pause-verified.png](%TEMP%/rogue-command-native-pause-verified.png). No dejes componentes temporales en `InGameMenuCanvas`. Para reanudar usa el control nativo del menú o `Navigator.Unpause()`; no restaures `Time.timeScale` directamente mientras `_isPaused` siga activo.

**Comprobación de reanudación supervisada (2026-10-18):** se resolvió de nuevo `GameButton`, se verificó el texto `Reanudar el juego` y `UnityEngine.UI.Button.interactable=true`, y se pulsó una vez con `Button.Press()`. La lectura en vivo inmediatamente posterior indicó `_isPaused=false`, `Time.timeScale=1` y `Time.time=549.9843`; el HUD avanzó de `03:51` a `04:03`. Esto confirma que reanudar vuelve a ejecutar la simulación. Para dejar la sesión segura, se llamó `Navigator.Pause()`; la lectura posterior confirmó `_isPaused=true`, escala `0` y el mismo `Time.time=549.9843`. `InGameMenu.Show()` restauró el overlay sin reanudar: `_isShowing=true` y la pausa nativa siguió activa. Se retiraron las sondas temporales y el Canvas quedó solo con componentes del juego. La captura `PrintWindow` [rogue-command-resumed-then-paused-20261018.png](%TEMP%/rogue-command-resumed-then-paused-20261018.png) fue 1920×1080; el handle en primer plano permaneció `0x190054E` antes y después. La primera congelación de esta sesión había sido una asignación directa de `Time.timeScale=0` desde Unity; al reportar una pausa, distingue siempre esa intervención, la pausa nativa y el reloj que corre normalmente al reanudar.

**Continuación de aceptación y pausa nativa verificada (2026-10-18):** se inició un nuevo `FirstRun` en dificultad Meditativo. La escena `Game` quedó con 3 vidas, tutorial `7/26` y sin `ChooseCard`. Antes de pasar a trabajo de código se llamó `Navigator.Pause()` desde la instancia Steam activa (PID `41992`), con `Time.timeScale` de `1` a `0`. Una sonda temporal de solo lectura confirmó `Navigator._isPaused=true`, `_isDebugPaused=false`, `Time.timeScale=0` y `Time.time=1180.464` sin cambio entre muestras separadas por 8 segundos; `Time.unscaledTime` avanzó. El HUD permaneció en `09:04` en ambas capturas `PrintWindow` de 1920×1080. `InGameMenu._isShowing=false`: el overlay puede estar oculto mientras la pausa nativa está activa; verifica el flag y el tiempo escalado, no solo la visibilidad del menú. Las capturas y los valores están guardados en `artifacts/runtime-smoke/loader-continuation-20261018/`. La sonda se retiró y se comprobó que no queda ningún componente `Rcl*` en `Game Menu UI HUD`. El perfil activo sigue siendo Profile 1; conserva su respaldo existente en `artifacts/runtime-smoke/baseline-20261018-160435`. La oferta moddeada, producción, mapa moddeado y AI moddeada siguen pendientes.

**Avance desde el opener y corrección del estado de pausa (2026-10-18):** al volver a iniciar el run, la captura sin foco mostraba una pantalla de inicio porque `LoadingHider` estaba activo en la propia escena `Game`; una lectura del puente encontró los textos vivos `IntroRunText="An Engineer Awakes"` y `ObjectiveText="Destruye el control de la base PCX"`, además del botón nativo `UnityEngine.UI.Button` `Button` con texto `"Iniciar"` e `interactable=true`. No la clasifiques como una captura anterior sin revisar el objeto y sus textos. Tras pulsar una vez `Button.Press()`, `LoadingHider` se desactivó y una nueva captura `PrintWindow` mostró el mapa, tres vidas y reloj HUD `00:10`.

En este recorrido, una acción temporal adjunta a `InGameMenuCanvas` no se ejecutó porque ese objeto estaba inactivo; la lectura dio `Time.timeScale=0` pero `Navigator._isPaused=false`. Reancla acciones temporales a un objeto activo y confirma que la acción se ejecutó. Después de pulsar `Iniciar`, el listener cambió `Time.timeScale` a `1` aunque `_isPaused` seguía en `true`. Se sincronizó con llamadas nativas consecutivas `Navigator.Unpause(); Navigator.Pause();`; una sonda independiente, 8 segundos después, observó `_isPaused=true`, `_isDebugPaused=false`, `Time.timeScale=0` y el mismo `Time.time=1282.264`, con `Time.unscaledTime` avanzando. Tras tomar la evidencia se retiraron cuatro componentes de diagnóstico; las listas de componentes de `Game Menu UI HUD` e `InGameMenuCanvas` quedaron sin `Rcl*`. La captura de mapa es `artifacts/runtime-smoke/loader-continuation-20261018/post-opener-pause-20261018.png` (1920×1080, `PrintWindowSucceeded=true`, handle de primer plano `0x1020C` sin cambios).

La autorización explícita de control permite avanzar durante una interacción supervisada; al terminarla, vuelve a pausar y confirma de nuevo el estado real de la simulación. Si la sesión forma parte de una tarea activa, mantenla abierta y pausada entre interacciones supervisadas. Ciérrala normalmente cuando esa tarea haya terminado; no la cierres solo porque no se esté usando en ese momento.

Antes de pulsar un botón del juego, vuelve a resolver su GameObject en la escena actual; los Instance IDs cambian al cargar otra escena o iniciar otra ejecución. Inspecciona componentes, `interactable`, texto visible (`UnityEngine.UI.Text.text` o el componente de texto presente) y listeners de Unity. Para controles nativos, `call_component_method` sobre `UnityEngine.UI.Button.Press()` ejecutó el evento normal del botón en la sesión observada. Pulsa solo una vez y comprueba de nuevo escena, paneles y estado de pausa. No trates una respuesta truncada como fallo de la operación: reduce el árbol o consulta el componente concreto.

Los nombres de GameObject no siempre coinciden con el tipo de su script: el objeto `BuildingMenu`, por ejemplo, tiene el componente `ShowBuildingMenu`. Llama primero a `inspect_components` y usa el tipo devuelto; no adivines el tipo por el nombre del objeto. Si dos consultas/control independientes fallan o dan estados contradictorios, deja de repetirlas y usa la skill `screenshot` junto con `PrintWindow` sin foco antes de elegir otra ruta.

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

`ping_unity_bridge` comprueba que responde el endpoint HTTP del puente; por sí solo no confirma que haya una sesión Unity conectada. Después del ping, confirma capacidades/rutas y una consulta de escena o componente. En esta sesión, `get_game_paths`, búsqueda de componentes y lecturas de `DefaultAiBehaviour` respondieron correctamente. Una búsqueda con un nombre de tipo incorrecto (por ejemplo, `ProgressionMap`, que no existe con ese nombre en las assemblies cargadas) devuelve un error de consulta y no indica que el puente esté caído; descubre primero el tipo real o usa un objeto/componente presente en la escena.

El puente se aloja dentro del proceso del juego. Si `Rogue Command.exe` está cerrado, `ping_unity_bridge` puede responder `Cannot connect to the Unity MCP bridge`; al iniciar la partida desde Steam, vuelve a comprobar ping, capacidades y una lectura Unity real. En la sesión del 18-10-2026, ese error desapareció tras el inicio por Steam (AppID `1461910`), y se confirmó tanto el PID `26220` durante la validación como el PID `36476` en `MainMenu` después de restaurar la instalación. No lo trates como fallo de instalación cuando no haya un proceso Unity activo.

### Reparar el entorno MCP local

Si el juego está abierto desde Steam pero el puente sigue sin responder, separa primero el estado del proceso de los errores del servidor MCP local. Desde la raíz del repositorio, ejecuta:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Repair-BepInExMCP.ps1
```

El script valida las rutas confinadas a `.tools/BepInExMCP`, instala una versión compatible de Pydantic con las restricciones del repositorio, ejecuta `pip check`, exige MCP SDK 1.20.0, comprueba la reconstrucción de `FastMCPSettings` y verifica `ModdersHelperApp.py --help`. Puede completar la reparación local idempotente de `FastMCPSettings.model_rebuild()`; no instala el puente en el juego, no modifica la configuración de Codex, no inicia Rogue Command y no llama herramientas de Unity. Tras el script, repite ping, capacidades y una consulta acotada de escena/componente antes de afirmar que se recuperó el puente.

Un `ping` correcto solo demuestra que responde el endpoint. Si una lectura grande se trunca, reduce profundidad/número de nodos y consulta campos concretos. Si falla un helper PowerShell/C# local, clasifica primero si falló la compilación del helper o la llamada MCP; un error de `Add-Type` (por ejemplo, un `using System` ausente) no demuestra que BepInEx esté caído.

### Captura visual como respaldo de diagnóstico

Si dos intentos independientes de inspección fallan o devuelven estados contradictorios, deja de repetir la misma llamada y consulta la skill `screenshot` (`C:\Users\Alex\.codex\skills\screenshot\SKILL.md`). Para una captura de escritorio normal, su helper es `scripts/take_screenshot.ps1`; para análisis temporal usa `-Mode temp`. Si el juego está cubierto por otra ventana y no quieres activarlo, usa `tools/Capture-RogueCommand-Background.ps1`: llama `PrintWindow(hwnd, memoryDC, 2)` directamente, no llama `SetForegroundWindow`, guarda el PNG en `%TEMP%` y registra el handle de primer plano antes y después. Ejemplo:

```powershell
$shot = Join-Path $env:TEMP ("rogue-command-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.png')
.\tools\Capture-RogueCommand-Background.ps1 -OutputPath $shot
```

El script busca exactamente un proceso `Rogue Command.exe`; si hay varios, vuelve a resolver el PID con `Get-CimInstance Win32_Process` y pásalo en `-ProcessId`. Inspecciona la imagen resultante: `PrintWindow` puede no producir contenido útil con una ventana minimizada o ciertos renderizadores. Si la captura falla, reporta su resultado y usa la skill para capturar la pantalla visible; no cambies el foco de la ventana sin autorización. Conserva una copia bajo `artifacts/runtime-smoke/` únicamente si aporta evidencia útil. Correlaciona imagen, escena, log y lecturas del puente; la imagen no demuestra estado interno que no se vea.

Identidad observada en esta sesión: Rogue Command 1.0.9; Unity 2021.3.45f2; BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Son valores de referencia, no sustituyen la comprobación de cada sesión.

### Resolver el perfil activo

En el `Assembly-CSharp.dll` de esta identidad, el inicializador de `ProfileManager` construye `ProfilesPath` y `CurrentProfileTextPath` desde `Application.persistentDataPath`. En esta instalación, `persistentDataPath` es `C:\Users\Alex\AppData\LocalLow\Feneq\Rogue Command`; el juego lee el selector `Profiles\currentProfile.txt` bajo esa raíz y obtiene `savegame.dat` desde el perfil resultante. No uses `currentProfile.txt` ni `Profiles\currentProfile.txt` dentro de la carpeta de instalación como selector activo: son rutas heredadas y pueden conservar valores antiguos distintos.

En la lectura en vivo del 2026-10-18, el selector de `LocalLow` valía `1`, mientras los dos archivos de la instalación valían `2` y `3`. El perfil activo era `Profile1`; al iniciar no tenía `savegame.dat`. `Profile2` y `Profile3` sí tenían guardados. Esta comprobación estática y de archivos corrige la clasificación de la lectura histórica más abajo: vuelve a consultar la ruta de `ProfileManager` en cada sesión y no cambies ninguno de los selectores heredados para elegir un perfil de prueba.

### Completar FirstRun en el perfil activo del usuario

Para las pruebas de aceptación que no tengan por objetivo probar el tutorial, el agente omite `FirstRun` completando el estado del tutorial en el perfil activo del usuario. Este cambio es persistente y se conserva después de la prueba. No cambies de perfil ni uses las opciones nativas de reset: pueden afectar datos ajenos al tutorial.

1. Hazlo desde `MainMenu` o `RunSetup`, antes de que empiece una corrida. Si ya hay una partida activa, no la abandones ni la sobrescribas solo para llegar al menú; pausa y deja este cambio para cuando vuelva a un estado seguro.
2. Resuelve el selector activo por `ProfileManager.CurrentProfileTextPath` bajo `Application.persistentDataPath`. Registra el número de perfil, ruta, hashes de todos los archivos existentes y una copia de `tutorialState.txt` si existe. No cambies el selector y no escribas directamente ningún archivo del perfil.
3. Antes de usar las firmas, vuelve a comprobar en conjunto la versión de Rogue Command, el SHA-256 y el MVID de `Assembly-CSharp.dll`. Para Rogue Command `1.0.9`, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, la inspección estática encontró estas llamadas nativas en `TutorialState`: `FinishMainTutorial()` y `OnPlayerHasFinishedLevel1()`. La primera persiste el fin del tutorial principal y llama a `AutoFinishCustomTutorialsWithMainTutorial()`; la segunda persiste el indicador usado por `GameBalancingStore.DetermineLevelProgressionModeForNewRun()` para dejar de escoger `FirstRun`. Este camino no usa Harmony ni escribe XP.
4. Invoca cada método una vez si su flag correspondiente sigue falso, directamente por el puente si este ofrece esa llamada. Si hace falta un helper temporal, compílalo con `mod_inject_class`, adjunta uno solo a un GameObject activo, llama una vez a `Apply()`, lee los dos flags y el modo de próxima corrida, llama `Remove()` y confirma con `inspect_components` que ya no queda ningún `RclTutorialCompletion`. No lo dejes adjunto al pasar de escena. El helper de referencia es:

   ```csharp
   using UnityEngine;

   public sealed class RclTutorialCompletion : MonoBehaviour
   {
       public bool mainTutorialFinished;
       public bool playerHasFinishedLevel1;
       public string nextRunMode;

       public void Apply()
       {
           var tutorial = Game.TutorialState;
           if (!tutorial.IsMainTutorialFinished)
               tutorial.FinishMainTutorial();
           if (!tutorial.PlayerHasFinishedLevel1)
               tutorial.OnPlayerHasFinishedLevel1();

           mainTutorialFinished = tutorial.IsMainTutorialFinished;
           playerHasFinishedLevel1 = tutorial.PlayerHasFinishedLevel1;
           nextRunMode = GameBalancingStore.DetermineLevelProgressionModeForNewRun().ToString();
       }

       public void Remove() => Destroy(this);
   }
   ```
5. Confirma `IsMainTutorialFinished=true`, `PlayerHasFinishedLevel1=true` y modo de próxima corrida `Default` o `Heat` antes de empezar. Tras la transición, comprueba `Game.CurrentLevelProgressionMode`; debe ser distinto de `FirstRun`. Si no lo es, detente y diagnostica la identidad, el selector y la carga de `TutorialState`; no avances el tutorial manualmente.
6. Después de la aceptación, confirma que el selector sigue apuntando al perfil registrado y compara sus hashes. Se espera que cambie `tutorialState.txt`; si varía cualquier otro archivo, detente y determina la causa antes de continuar o restaurarlo. Conserva los demás datos y deja los flags completados. Restaura los archivos temporales de instalación por hash y retira helpers/sondas. No restaures automáticamente la copia previa de `tutorialState.txt`, porque eso reactivaría el tutorial que el usuario pidió quitar.

La ruta de las firmas y los efectos anteriores tiene **inspección estática** en la identidad indicada; no se afirma una nueva ejecución en vivo del bypass en esta documentación. Si cambia cualquier elemento de la identidad, no reutilices esas firmas sin volver a inspeccionarlas.

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
- **Menú frente a pausa real:** en la continuación del tutorial, `KeepPlaying` cerró la confirmación sin abandonar y `InGameMenu._isShowing=true` mantuvo visible el menú. Eso solo confirmó el overlay; no se había probado que la simulación estuviera detenida. La lectura posterior del HUD confirmó que el tiempo siguió avanzando hasta que se ajustó `Time.timeScale` a `0`.
- **Corrección de pausa real y captura en segundo plano (seguimiento, 2026-10-18):** se inició FirstRun desde el botón nativo `INICIAR`, se llegó al tutorial `7/26` y se descartó el cuadro actual con `Entendido`. La partida continuó aunque `InGameMenu._isShowing=true`: el HUD pasó de `00:16` a `01:19`. El helper temporal primero cambió `Time.timeScale` de `1` a `0`, pero eso no probaba que el juego hubiera entrado en su pausa nativa. Tras verificar la identidad de la build, se llamó `Navigator.Pause()` por reflexión y se confirmó `_isPaused=true` y escala cero. Se retiraron el helper, la sonda y la acción temporal; la lista final de componentes no contenía componentes `Rcl*`. Dos capturas con `PrintWindow` separadas por siete segundos mostraron el mismo reloj HUD (`03:51`), resolución `1920×1080` y el mismo handle de primer plano antes/después de cada captura. Una lectura independiente, sin helper, confirmó la pausa nativa y `Time.time=537.5239`. La captura más reciente es [rogue-command-native-pause-verified.png](%TEMP%/rogue-command-native-pause-verified.png). El usuario confirmó que el reloj avanza al reanudar.
- **Opener del run, estado sincronizado y límite del build instalado (2026-10-18):** se cambió de flujo desde la partida anterior con autorización explícita del usuario; el nuevo FirstRun mostró su opener activo en `LoadingHider`, no una pantalla de menú antigua. Tras pulsar `Iniciar`, el mapa apareció en la captura de fondo y el HUD mostró `00:10`, pero la pausa quedó inconsistente (`_isPaused=true`, `Time.timeScale=1`). La secuencia nativa `Unpause()`/`Pause()` volvió a sincronizarla; tras 8 segundos `Time.time` permaneció en `1282.264` y `Time.timeScale=0`. El proceso PID `41992` sigue abierto y pausado. `Assembly-CSharp.dll` continúa identificado por SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`; el juego informa versión `1.0.9`. La DLL instalada es `0543e76a28e5afb7a19824ca95ba9f10f53f04896244b06cc4f151b646437c1f`, distinta del build local actual `5aaf2d6b2fa97a14b4b287014053a9caafb920057aaffbb329f5a3c80ba31c35`. La corrida confirma únicamente el flujo nativo y la pausa; no valida la DLL local ni la oferta/producción de contenido moddeado.
- **Conclusión:** esto confirma carga del plugin/paquetes y estado de un run de prueba. No confirma que las cartas nuevas aparezcan en ofertas, que una unidad se produzca o renderice, ni que una etapa o AI modded se ejecute.
- **Validación local:** 132/133 checks del harness pasaron; el único omitido fue la fixture de symlink por falta de privilegio de Windows. El validador resolvió 10/10 referencias de Sprite/prefab y el preflight estático resolvió 5/5 raíces visuales. Son pruebas locales, no de oferta o renderizado en partida.
- **Sondas:** los componentes temporales `Rcl*Probe`, el helper `RclRuntimeTimeControl` y la acción temporal se retiraron. La lista final de componentes de `InGameMenuCanvas` contenía solo componentes del juego. Las salidas de validación están bajo `artifacts/`.

Cuando el juego cierre, conserva la selección del perfil activo que registraste antes de la sesión y deja intactos los datos recién creados en el perfil de prueba. No cambies el selector a Profile 2 ni sobrescribas o elimines perfiles para volver a un estado limpio.
