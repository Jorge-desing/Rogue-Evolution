# Native UI and intermission workflow

Use this reference for screenshot-free control discovery and the run director between combat stages. It does not cover the combat AI Director.

## Discover and operate native UI

1. Read the active scene and run state. Use `diagnostics_fast_snapshot` with `gui` for a quick overview; use `diagnostics_get_ui_accessibility_census` with a label query when locating a particular control. The census returns active controls, labels, scene-qualified hierarchy paths, interactability, selection, and screen bounds without capturing an image.
2. If a screen is changing, use `diagnostics_wait_for_ui_text` with the exact label or current hierarchy path and a bounded timeout. Re-run the census after every scene or panel transition; Unity instance IDs and paths can become stale.
3. For a Unity `Selectable`, use `diagnostics_ui_click_text` with a unique exact label or the exact scene-qualified census path. This uses the native EventSystem click path and fails closed for ambiguous, occluded, inactive, or non-interactable targets.
4. For a non-Selectable `Michsky.UI.Reach.ButtonManager`, use `diagnostics_ui_invoke_native_button` with the current scene, exact object selector, expected button text, and hierarchy path when needed to disambiguate. Before invoking a control that opens a dialog or advances a flow, inspect its listener target and method; do not invoke arbitrary callbacks through generic reflection.
5. Read the resulting scene/UI/run state and verify the requested postcondition. After a timeout, query that postcondition before retrying an in-flight action.

Prefer purpose-built actions over UI clicks for gameplay controls. Use the UI endpoints for native menus and controls that have no dedicated typed action. If an unlabeled control is needed, resolve its exact path; do not guess coordinates or switch to screenshots/Computer Use.

## Intermission and run director

- Start with `diagnostics_fast_snapshot` preset `intermission`. It groups run progression, the bounded node census and graph, reward/deck sub-screen state, navigator state, and accessible UI labels. Its reads may span Unity frames and are not an atomic view; call the focused census again before acting.
- Use `diagnostics_get_intermission_census` to obtain the current node's integer `level`/`field` keys and `choosable` status. Use `diagnostics_get_intermission_graph` to inspect routes and `diagnostics_get_intermission_node` for one node's rewards and edges. Do not infer choosability from a map graphic or localized preview name.
- `diagnostics_order_select_level_node` changes map selection only and does not load a level. `diagnostics_order_enter_level` follows the native director flow for a choosable node and changes the active run; use it only when the requested action authorizes continuing that run. Do not use synthetic stage jumps as normal navigation.
- Before choosing, skipping, rerolling, or assigning a reward, read `diagnostics_get_intermission_reward_state` and confirm the active queue, offered slot/ID, and intended effect. Use `diagnostics_order_choose_intermission_reward`, `diagnostics_order_skip_intermission_reward`, `diagnostics_order_reroll_intermission_reward`, or `diagnostics_order_assign_intermission_upgrade` as appropriate, then read the reward state and UI again. Treat reward and deck changes as run mutations; do not choose or skip on the user's behalf without the request specifying that choice.
- After a level transition, inspect `LoadingHider` and the native `Iniciar` control with the accessibility census. Click its exact scene-qualified path through `diagnostics_ui_click_text`, then verify the gameplay state. A `Game` scene or `Time.timeScale=1` by itself does not prove gameplay has started.
- If the native director cannot enter the requested node, or a transition stalls, stop at that boundary and report the observed state. Follow `docs/research/STEAM-RUNTIME-CONTROL.md` for any documented exceptional transition procedure; do not advance a saved route just to force a different stage/profile.

Preserve the existing profile, save, and tutorial state. Any new-run QA must follow the cloned-profile procedure in `rogue-command-steam-runtime-validation` and the runtime runbook.
