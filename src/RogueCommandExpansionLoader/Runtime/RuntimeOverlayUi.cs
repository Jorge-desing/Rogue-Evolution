using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// In-game diagnostic overlay and mod inspector for Rogue Command Expansion Loader.
    /// Provides the high-contrast inspector via configurable Inspector.ToggleKey (F8 by default) or the screen badge, for inspecting loaded packs, units, stages, AI modules, and runtime logs.
    /// </summary>
    public sealed class RuntimeOverlayUi : MonoBehaviour
    {
        private const int WindowWidth = 820;
        private const int WindowHeight = 560;

        private IRuntimeContentProvider _provider;
        private string _pluginVersion;
        private RuntimePatchInstallationResult _patchResult;
        private string _compatibilityError;
        private Action<string> _logWarning;
        private ConfigEntry<KeyboardShortcut> _toggleShortcut;

        private bool _isOpen;
        private bool _watermarkVisible = true;
        private bool _guiDisabled;
        private bool _guiFailureReported;
        private int _selectedTab;
        private Vector2 _scrollPos;
        private string _logFilter = string.Empty;
        private LoaderDiagnosticsSnapshot _snapshot;
        private float _nextSnapshotRefresh;

        // Cache the GUI resources so they are not recreated on every GUI pass.
        private bool _stylesInitialized;
        private Texture2D _texBackground;
        private Texture2D _texPanel;
        private Texture2D _texBorder;
        private Texture2D _texAccent;
        private Texture2D _texSuccess;
        private Texture2D _texWarning;
        private Texture2D _texError;
        private Texture2D _texCardBg;
        private Texture2D _texTabActive;
        private Texture2D _texTabInactive;

        private GUIStyle _styleWindow;
        private GUIStyle _styleBadge;
        private GUIStyle _styleHeaderTitle;
        private GUIStyle _styleHeaderSubtitle;
        private GUIStyle _styleTabActive;
        private GUIStyle _styleTabInactive;
        private GUIStyle _styleCard;
        private GUIStyle _styleCardTitle;
        private GUIStyle _styleLabelBold;
        private GUIStyle _styleLabelRegular;
        private GUIStyle _styleLabelMuted;
        private GUIStyle _styleLabelSuccess;
        private GUIStyle _styleLabelWarning;
        private GUIStyle _styleLabelError;
        private GUIStyle _styleLogEntry;
        private GUIStyle _styleCloseButton;
        private GUIStyle _styleFilterField;

        public void Initialize(
            IRuntimeContentProvider provider,
            string pluginVersion,
            RuntimePatchInstallationResult patchResult,
            ConfigFile config,
            string compatibilityError = null,
            Action<string> logWarning = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _provider = provider;
            _pluginVersion = pluginVersion ?? "1.20.0";
            _patchResult = patchResult;
            _compatibilityError = compatibilityError;
            _logWarning = logWarning;
            _toggleShortcut = config.Bind("Inspector", "ToggleKey", new KeyboardShortcut(KeyCode.F8),
                "Toggle the loader inspector. Supports a key with optional modifier keys.");
        }

        /// <summary>Live open/closed state, readable by an agent without a visual capture.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Currently bound toggle shortcut, readable by an agent without a visual capture.</summary>
        public string ToggleShortcutText => GetToggleShortcutText();

        /// <summary>Currently selected inspector tab, readable by an agent without a visual capture.</summary>
        public int SelectedTab => _selectedTab;

        /// <summary>Whether the corner watermark is drawn, readable by an agent without a visual capture.</summary>
        public bool WatermarkVisible => _watermarkVisible;

        /// <summary>Whether the GUI was disabled after a rendering exception, readable by an agent.</summary>
        public bool IsGuiDisabled => _guiDisabled;

        /// <summary>Whether the GUI failure has already been reported to the log, readable by an agent.</summary>
        public bool IsGuiFailureReported => _guiFailureReported;

        /// <summary>Active log filter text, readable by an agent without a visual capture.</summary>
        public string LogFilter => _logFilter ?? string.Empty;

        /// <summary>Reported plugin version, readable by an agent without a visual capture.</summary>
        public string PluginVersion => _pluginVersion ?? string.Empty;

        /// <summary>Compatibility error text, empty when the loader is compatible, readable by an agent.</summary>
        public string CompatibilityError => _compatibilityError ?? string.Empty;

        /// <summary>Whether the runtime patch install succeeded, readable by an agent without a visual capture.</summary>
        public bool IsPatchResultSuccess => _patchResult != null && _patchResult.Success;

        /// <summary>Runtime patch installation message, readable by an agent without a visual capture.</summary>
        public string PatchResultMessage => _patchResult != null ? _patchResult.Message : string.Empty;

        /// <summary>Sets inspector visibility programmatically (bridge control).</summary>
        public void SetOpen(bool open)
        {
            _isOpen = open;
        }

        /// <summary>Selects inspector tab programmatically (bridge control).</summary>
        public void SetSelectedTab(int tab)
        {
            int clamped;
            GuiControlPolicy.TryClampTab(tab, out clamped);
            _selectedTab = clamped;
        }

        private void Update()
        {
            if (_provider != null && Time.unscaledTime >= _nextSnapshotRefresh)
            {
                _snapshot = _provider.GetDiagnosticsSnapshot();
                _nextSnapshotRefresh = Time.unscaledTime + 1f;
            }

            bool closePressed = Input.GetKeyDown(KeyCode.Escape);
            _isOpen = InspectorTogglePolicy.Resolve(
                _isOpen,
                _toggleShortcut != null && _toggleShortcut.Value.IsDown(),
                closePressed);
        }

        private void OnGUI()
        {
            if (_guiDisabled)
            {
                return;
            }

            try
            {
                EnsureStyles();

                if (_watermarkVisible)
                {
                    DrawWatermark();
                }

                if (_isOpen)
                {
                    DrawMainWindow();
                }
            }
            catch (Exception exception)
            {
                // The inspector must never crash Unity, but a silent catch hid why it was absent.
                _guiDisabled = true;
                try
                {
                    DestroyOwnedTextures();
                }
                catch
                {
                    // Cleanup failures are isolated from the Unity GUI loop too.
                }
                if (!_guiFailureReported)
                {
                    _guiFailureReported = true;
                    try
                    {
                        if (_logWarning != null)
                        {
                            _logWarning("[ExpansionLoader] Inspector UI disabled after " +
                                exception.GetType().Name + ": " + exception.Message);
                        }
                    }
                    catch
                    {
                        // A logging failure must remain isolated from the Unity GUI loop.
                    }
                }
            }
        }

        private string GetToggleShortcutText()
        {
            return _toggleShortcut == null ? "F8" : _toggleShortcut.Value.ToString();
        }

        private void DrawWatermark()
        {
            float width = 230f;
            float height = 28f;
            float x = Screen.width - width - 12f;
            float y = 10f;
            Rect badgeRect = new Rect(x, y, width, height);

            bool isHealthy = _compatibilityError == null && (_patchResult == null || _patchResult.Success);
            string statusDot = isHealthy ? "[OK]" : "[ERR]";
            string text = "RCL v" + _pluginVersion + " " + statusDot + " | " + GetToggleShortcutText() + ": Inspector";

            Color origColor = GUI.backgroundColor;
            GUI.backgroundColor = isHealthy ? new Color(0.12f, 0.16f, 0.23f, 0.92f) : new Color(0.35f, 0.10f, 0.10f, 0.95f);

            if (GUI.Button(badgeRect, text, _styleBadge))
            {
                _isOpen = !_isOpen;
            }

            GUI.backgroundColor = origColor;
        }

        private void DrawMainWindow()
        {
            float x = (Screen.width - WindowWidth) * 0.5f;
            float y = (Screen.height - WindowHeight) * 0.5f;
            Rect windowRect = new Rect(x, y, WindowWidth, WindowHeight);

            GUI.Box(windowRect, GUIContent.none, _styleWindow);

            // Title bar
            GUILayout.BeginArea(new Rect(x + 16, y + 14, WindowWidth - 32, 54));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label("ROGUE COMMAND EXPANSION LOADER", _styleHeaderTitle);
            GUILayout.Label("v" + _pluginVersion + " • Mono x64 • Target 1.0.9 • Unity 2021.3.45f2", _styleHeaderSubtitle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Status badge
            bool isCompatible = _compatibilityError == null && (_patchResult == null || _patchResult.Success);
            GUIStyle statusStyle = isCompatible ? _styleLabelSuccess : _styleLabelError;
            string statusText = isCompatible ? "[SISTEMA ACTIVO]" : "[ERROR COMPATIBILIDAD]";
            GUILayout.Label(statusText, statusStyle, GUILayout.Height(28));

            GUILayout.Space(12);

            if (GUILayout.Button("[X]", _styleCloseButton, GUILayout.Width(34), GUILayout.Height(30)))
            {
                _isOpen = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Tab bar
            string[] tabs = new[] { "Resumen", "Paquetes", "Unidades", "Etapas", "Perfiles IA", "Diagnostico" };
            float tabY = y + 74;
            float tabWidth = (WindowWidth - 32) / tabs.Length;

            for (int i = 0; i < tabs.Length; i++)
            {
                Rect tabRect = new Rect(x + 16 + (i * tabWidth), tabY, tabWidth - 2, 32);
                bool isActive = _selectedTab == i;
                GUIStyle tabStyle = isActive ? _styleTabActive : _styleTabInactive;

                if (GUI.Button(tabRect, tabs[i], tabStyle))
                {
                    _selectedTab = i;
                    _scrollPos = Vector2.zero;
                }
            }

            // Content Area
            Rect contentRect = new Rect(x + 16, tabY + 38, WindowWidth - 32, WindowHeight - 158);
            GUILayout.BeginArea(contentRect);

            LoaderDiagnosticsSnapshot snapshot = _snapshot;

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true, GUILayout.Width(contentRect.width), GUILayout.Height(contentRect.height));

            switch (_selectedTab)
            {
                case 0:
                    DrawOverviewTab(snapshot);
                    break;
                case 1:
                    DrawPacksTab(snapshot);
                    break;
                case 2:
                    DrawUnitsTab(snapshot);
                    break;
                case 3:
                    DrawStagesTab(snapshot);
                    break;
                case 4:
                    DrawAiProfilesTab(snapshot);
                    break;
                case 5:
                    DrawDiagnosticsTab(snapshot);
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Footer
            Rect footerRect = new Rect(x + 16, y + WindowHeight - 38, WindowWidth - 32, 28);
            GUILayout.BeginArea(footerRect);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Atajos: " + GetToggleShortcutText() + " para alternar • Esc para cerrar", _styleLabelMuted);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cerrar", _styleTabInactive, GUILayout.Width(80), GUILayout.Height(24)))
            {
                _isOpen = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawOverviewTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);

            // System Card
            BeginCard("Estado del Motor y Compatibilidad");
            if (_compatibilityError != null)
            {
                GUILayout.Label("Incompatibilidad detectada: " + _compatibilityError, _styleLabelError);
            }
            else
            {
                GUILayout.Label("• Rogue Command: 1.0.9 (Mono x64) - Hash y MVID verificados", _styleLabelSuccess);
                GUILayout.Label("• Unity Engine: 2021.3.45f2 - Compatibilidad fijada", _styleLabelSuccess);
                string hookMsg = _patchResult != null ? _patchResult.Message : "Harmony hooks vinculados";
                GUILayout.Label("• Harmony Patches: " + hookMsg, _styleLabelRegular);
            }
            EndCard();

            GUILayout.Space(8);

            // Content Summary Card
            BeginCard("Resumen de Contenido Externo");
            if (snapshot != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Paquetes activos: " + snapshot.ActivePackCount, _styleLabelRegular);
                GUILayout.Label("Paquetes fallidos: " + snapshot.FailedPackCount, snapshot.FailedPackCount > 0 ? _styleLabelWarning : _styleLabelMuted);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Unidades registradas: " + snapshot.Units.Count, _styleLabelRegular);
                GUILayout.Label("Etapas registradas: " + snapshot.Stages.Count, _styleLabelRegular);
                GUILayout.Label("Perfiles IA: " + snapshot.AiProfiles.Count, _styleLabelRegular);
                GUILayout.EndHorizontal();

                double megabytes = snapshot.TotalBundleBytes / (1024.0 * 1024.0);
                GUILayout.Label("Memoria AssetBundles cargados: " + megabytes.ToString("F2") + " MiB / 2048.00 MiB", _styleLabelRegular);
            }
            else
            {
                GUILayout.Label("El cargador de contenido aun no ha inicializado su registro.", _styleLabelMuted);
            }
            EndCard();

            GUILayout.Space(8);

            // Instructions Card
            BeginCard("Instrucciones para Creadores de Contenido");
            GUILayout.Label("• Directorio de paquetes: BepInEx/plugins/RogueCommandExpansionLoader/content/packs/", _styleLabelRegular);
            GUILayout.Label("• Cada paquete requiere un manifest content.json y sus AssetBundles en bundles/", _styleLabelRegular);
            GUILayout.Label("• Las unidades y etapas anadidas se inyectan dinamicamente en los perfiles de progresion", _styleLabelRegular);
            EndCard();
        }

        private void DrawPacksTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);
            if (snapshot == null || snapshot.Packs.Count == 0)
            {
                BeginCard("Paquetes Instalados");
                GUILayout.Label("No se encontraron paquetes en content/packs/.", _styleLabelMuted);
                EndCard();
                return;
            }

            for (int i = 0; i < snapshot.Packs.Count; i++)
            {
                PackDiagnosticReport pack = snapshot.Packs[i];
                BeginCard(pack.PackId + " [" + pack.FolderName + "]", IdentifierColorPolicy.Resolve(pack.PackId));

                GUILayout.BeginHorizontal();
                if (pack.IsLoaded)
                {
                    GUILayout.Label("ESTADO: ACTIVO", _styleLabelSuccess);
                }
                else
                {
                    GUILayout.Label("ESTADO: DESACTIVADO (" + pack.StatusMessage + ")", _styleLabelError);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Bundles: " + pack.BundleCount + " (" + (pack.TotalBundleBytes / 1024.0).ToString("F1") + " KiB)", _styleLabelRegular);
                GUILayout.Label("Unidades: " + pack.UnitCount, _styleLabelRegular);
                GUILayout.Label("Etapas: " + pack.StageCount, _styleLabelRegular);
                GUILayout.Label("Perfiles IA: " + pack.AiProfileCount, _styleLabelRegular);
                GUILayout.EndHorizontal();

                EndCard();
                GUILayout.Space(6);
            }
        }

        private void DrawUnitsTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);
            if (snapshot == null || snapshot.Units.Count == 0)
            {
                BeginCard("Unidades de Expansion");
                GUILayout.Label("No hay unidades registradas por paquetes de expansion.", _styleLabelMuted);
                EndCard();
                return;
            }

            for (int i = 0; i < snapshot.Units.Count; i++)
            {
                UnitDiagnosticReport unit = snapshot.Units[i];
                BeginCard(unit.UnitId);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Paquete: " + unit.PackId, _styleLabelRegular);
                GUILayout.Label("Elegible en Blueprints: " + (unit.IsAllowedAsBlueprint ? "Si" : "No"), unit.IsAllowedAsBlueprint ? _styleLabelSuccess : _styleLabelMuted);
                GUILayout.EndHorizontal();

                GUILayout.Label("Prefab Resource Key: " + unit.PrefabKey, _styleLabelMuted);
                GUILayout.Label("Sprite Card Key: " + unit.ImageKey, _styleLabelMuted);
                GUILayout.Label(UnitVariantDiagnosticsFormatter.FormatVariantCount(unit), _styleLabelMuted);

                string mechanicsSummary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary(unit.UnitId);
                if (!string.IsNullOrEmpty(mechanicsSummary))
                {
                    GUILayout.Label("Mecanicas activas: " + mechanicsSummary, _styleLabelSuccess);
                }

                EndCard();
                GUILayout.Space(6);
            }
        }

        private void DrawStagesTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);
            if (snapshot == null || snapshot.Stages.Count == 0)
            {
                BeginCard("Etapas de Expansion");
                GUILayout.Label("No hay etapas registradas por paquetes de expansion.", _styleLabelMuted);
                EndCard();
                return;
            }

            for (int i = 0; i < snapshot.Stages.Count; i++)
            {
                StageDiagnosticReport stage = snapshot.Stages[i];
                BeginCard(stage.StageId);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Paquete: " + stage.PackId, _styleLabelRegular);
                GUILayout.Label("Tamano de mapa: " + stage.MapSizeMin + " - " + stage.MapSizeMax, _styleLabelRegular);
                GUILayout.EndHorizontal();

                GUILayout.Label("Unidades Blueprint: " + (stage.UnitIds.Count > 0 ? string.Join(", ", stage.UnitIds) : "Ninguna"), _styleLabelMuted);
                GUILayout.Label("Perfiles IA asociados: " + (stage.AiProfileIds.Count > 0 ? string.Join(", ", stage.AiProfileIds) : "Ninguno"), _styleLabelMuted);

                EndCard();
                GUILayout.Space(6);
            }
        }

        private void DrawAiProfilesTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);
            if (snapshot == null || snapshot.AiProfiles.Count == 0)
            {
                BeginCard("Perfiles IA de Expansion");
                GUILayout.Label("No hay perfiles IA registrados por paquetes de expansion.", _styleLabelMuted);
                EndCard();
                return;
            }

            for (int i = 0; i < snapshot.AiProfiles.Count; i++)
            {
                AiProfileDiagnosticReport profile = snapshot.AiProfiles[i];
                BeginCard(profile.ProfileId);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Paquete: " + profile.PackId, _styleLabelRegular);
                GUILayout.Label("Instancias IA activas: " + profile.ActiveAiInstances, _styleLabelRegular);
                GUILayout.EndHorizontal();

                GUILayout.Label("Prefab Asset: " + profile.PrefabAssetName, _styleLabelMuted);
                GUILayout.Label("Modulos compilados: " + (profile.ModuleIds.Count > 0 ? string.Join(", ", profile.ModuleIds) : "Ninguno"), _styleLabelRegular);

                EndCard();
                GUILayout.Space(6);
            }
        }

        private void DrawDiagnosticsTab(LoaderDiagnosticsSnapshot snapshot)
        {
            GUILayout.Space(8);
            if (snapshot != null)
            {
                DrawUnitEligibilityDiagnostics(snapshot);
                DrawUnitFactoryDiagnostics(snapshot);
                DrawStagePoolScopeDiagnostics(snapshot);
                DrawAiProfileStageRangeDiagnostics(snapshot);
                DrawUnitAssetResolutionDiagnostics(snapshot);
                DrawUnitLocalizationCoverageDiagnostics(snapshot);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filtro:", _styleLabelRegular, GUILayout.Width(45));
            _logFilter = GUILayout.TextField(_logFilter, _styleFilterField, GUILayout.Width(220));
            if (GUILayout.Button("Limpiar Filtro", _styleTabInactive, GUILayout.Width(100), GUILayout.Height(24)))
            {
                _logFilter = string.Empty;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            LoaderLogBurstSummary latestLog = LoaderLogBurstSummaryPolicy.Create(
                snapshot == null ? null : snapshot.RecentLogs);
            if (latestLog != null)
            {
                BeginCard("Actividad reciente");
                GUIStyle latestStyle = _styleLogEntry;
                if (latestLog.DisplayText.IndexOf("desactivado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    latestLog.DisplayText.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    latestLog.DisplayText.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    latestStyle = _styleLabelError;
                }
                else if (latestLog.DisplayText.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         latestLog.DisplayText.IndexOf("advertencia", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    latestStyle = _styleLabelWarning;
                }

                GUILayout.Label(latestLog.DisplayText, latestStyle);
                EndCard();
                GUILayout.Space(6);
            }

            if (snapshot == null || snapshot.RecentLogs.Count == 0)
            {
                BeginCard("Registro de Eventos");
                GUILayout.Label("No hay registros recientes.", _styleLabelMuted);
                EndCard();
                return;
            }

            BeginCard("Ultimos Eventos de Carga y Validacion (" + snapshot.RecentLogs.Count + ")");
            for (int i = snapshot.RecentLogs.Count - 1; i >= 0; i--)
            {
                string line = snapshot.RecentLogs[i];
                if (!string.IsNullOrEmpty(_logFilter) && line.IndexOf(_logFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                GUIStyle lineStyle = _styleLogEntry;
                if (line.IndexOf("desactivado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineStyle = _styleLabelError;
                }
                else if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("advertencia", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lineStyle = _styleLabelWarning;
                }

                GUILayout.Label(line, lineStyle);
            }
            EndCard();
        }

        private void DrawUnitFactoryDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.UnitFactories.Count == 0)
            {
                return;
            }

            BeginCard("Relaciones unidad-fabrica (" + snapshot.UnitFactories.Count + ")");
            for (int i = 0; i < snapshot.UnitFactories.Count; i++)
            {
                UnitFactoryDiagnosticReport report = snapshot.UnitFactories[i];
                GUIStyle style = String.IsNullOrWhiteSpace(report.FactoryId)
                    ? _styleLabelMuted
                    : (report.IsLoaded ? _styleLabelSuccess : _styleLabelError);
                GUILayout.Label(LoaderDiagnosticsFormatter.FormatUnitFactory(report), style);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void DrawStagePoolScopeDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.StagePoolScopes.Count == 0)
            {
                return;
            }

            BeginCard("Alcance registrado por pool (" + snapshot.StagePoolScopes.Count + ")");
            for (int i = 0; i < snapshot.StagePoolScopes.Count; i++)
            {
                StagePoolScopeDiagnosticReport report = snapshot.StagePoolScopes[i];
                GUIStyle style = report.CandidateIds.Count > 0 ? _styleLabelRegular : _styleLabelMuted;
                GUILayout.Label(LoaderDiagnosticsFormatter.FormatStagePoolScope(report), style);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void DrawAiProfileStageRangeDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.AiProfileStageRanges.Count == 0)
            {
                return;
            }

            BeginCard("Rangos de etapas por perfil IA (" + snapshot.AiProfileStageRanges.Count + ")");
            for (int i = 0; i < snapshot.AiProfileStageRanges.Count; i++)
            {
                AiProfileStageRangeDiagnosticReport report = snapshot.AiProfileStageRanges[i];
                GUILayout.Label(
                    LoaderDiagnosticsFormatter.FormatAiProfileStageRange(report),
                    report.IsContiguous ? _styleLabelSuccess : _styleLabelError);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void DrawUnitEligibilityDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.UnitEligibilities.Count == 0)
            {
                return;
            }

            BeginCard("Elegibilidad de unidades (" + snapshot.UnitEligibilities.Count + ")");
            for (int i = 0; i < snapshot.UnitEligibilities.Count; i++)
            {
                UnitEligibilityDiagnosticReport report = snapshot.UnitEligibilities[i];
                GUILayout.Label(
                    LoaderDiagnosticsFormatter.FormatUnitEligibility(report),
                    report.IsResolved ? _styleLabelRegular : _styleLabelWarning);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void DrawUnitAssetResolutionDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.UnitAssetResolutions.Count == 0)
            {
                return;
            }

            BeginCard("Resolución de recursos de unidades (" + snapshot.UnitAssetResolutions.Count + ")");
            for (int i = 0; i < snapshot.UnitAssetResolutions.Count; i++)
            {
                UnitAssetResolutionDiagnosticReport report = snapshot.UnitAssetResolutions[i];
                UnitAssetResolutionSeverity severity =
                    LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(report);
                GUIStyle style = severity == UnitAssetResolutionSeverity.Error
                    ? _styleLabelError
                    : (severity == UnitAssetResolutionSeverity.Warning
                        ? _styleLabelWarning
                        : (severity == UnitAssetResolutionSeverity.Muted
                            ? _styleLabelMuted
                            : _styleLabelSuccess));
                GUILayout.Label(LoaderDiagnosticsFormatter.FormatUnitAssetResolution(report), style);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void DrawUnitLocalizationCoverageDiagnostics(LoaderDiagnosticsSnapshot snapshot)
        {
            if (snapshot.UnitLocalizationCoverage.Count == 0)
            {
                return;
            }

            BeginCard("Cobertura de localización de unidades (" + snapshot.UnitLocalizationCoverage.Count + ")");
            for (int i = 0; i < snapshot.UnitLocalizationCoverage.Count; i++)
            {
                UnitLocalizationCoverageDiagnosticReport report = snapshot.UnitLocalizationCoverage[i];
                GUIStyle style = report.HasEnglish
                    ? _styleLabelSuccess
                    : (report.HasNativeTemplate ? _styleLabelRegular : _styleLabelWarning);
                GUILayout.Label(LoaderDiagnosticsFormatter.FormatUnitLocalizationCoverage(report), style);
            }
            EndCard();
            GUILayout.Space(6);
        }

        private void BeginCard(string title)
        {
            BeginCard(title, null);
        }

        private void BeginCard(string title, UnityEngine.Color? accent)
        {
            GUILayout.BeginVertical(_styleCard);
            if (!string.IsNullOrEmpty(title))
            {
                if (accent.HasValue)
                {
                    Color previous = GUI.color;
                    GUI.color = accent.Value;
                    GUILayout.Label(title, _styleCardTitle);
                    GUI.color = previous;
                }
                else
                {
                    GUILayout.Label(title, _styleCardTitle);
                }

                GUILayout.Space(4);
            }
        }

        private void EndCard()
        {
            GUILayout.EndVertical();
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _texBackground = MakeTex(new Color(0.06f, 0.09f, 0.16f, 0.95f)); // Slate 900
            _texPanel = MakeTex(new Color(0.12f, 0.16f, 0.23f, 0.95f));      // Slate 800
            _texBorder = MakeTex(new Color(0.20f, 0.25f, 0.33f, 1.0f));       // Slate 700
            _texAccent = MakeTex(new Color(0.22f, 0.74f, 0.97f, 1.0f));       // Cyan 400
            _texSuccess = MakeTex(new Color(0.20f, 0.83f, 0.60f, 1.0f));      // Emerald 400
            _texWarning = MakeTex(new Color(0.98f, 0.75f, 0.14f, 1.0f));      // Amber 400
            _texError = MakeTex(new Color(0.97f, 0.44f, 0.44f, 1.0f));        // Rose 400
            _texCardBg = MakeTex(new Color(0.09f, 0.12f, 0.18f, 0.90f));
            _texTabActive = MakeTex(new Color(0.16f, 0.22f, 0.32f, 1.0f));
            _texTabInactive = MakeTex(new Color(0.09f, 0.13f, 0.20f, 0.90f));

            _styleWindow = new GUIStyle();
            _styleWindow.normal.background = _texBackground;
            _styleWindow.border = new RectOffset(2, 2, 2, 2);
            _styleWindow.padding = new RectOffset(14, 14, 14, 14);

            _styleBadge = new GUIStyle(GUI.skin.button);
            _styleBadge.normal.textColor = new Color(0.95f, 0.96f, 0.98f);
            _styleBadge.hover.textColor = new Color(0.22f, 0.74f, 0.97f);
            _styleBadge.fontSize = 11;
            _styleBadge.fontStyle = FontStyle.Bold;
            _styleBadge.alignment = TextAnchor.MiddleCenter;

            _styleHeaderTitle = new GUIStyle();
            _styleHeaderTitle.normal.textColor = new Color(0.22f, 0.74f, 0.97f); // Cyan 400
            _styleHeaderTitle.fontSize = 16;
            _styleHeaderTitle.fontStyle = FontStyle.Bold;

            _styleHeaderSubtitle = new GUIStyle();
            _styleHeaderSubtitle.normal.textColor = new Color(0.58f, 0.64f, 0.72f); // Slate 400
            _styleHeaderSubtitle.fontSize = 11;

            _styleTabActive = new GUIStyle(GUI.skin.button);
            _styleTabActive.normal.background = _texTabActive;
            _styleTabActive.normal.textColor = new Color(0.22f, 0.74f, 0.97f);
            _styleTabActive.fontStyle = FontStyle.Bold;
            _styleTabActive.fontSize = 12;

            _styleTabInactive = new GUIStyle(GUI.skin.button);
            _styleTabInactive.normal.background = _texTabInactive;
            _styleTabInactive.normal.textColor = new Color(0.70f, 0.75f, 0.82f);
            _styleTabInactive.fontSize = 12;

            _styleCard = new GUIStyle();
            _styleCard.normal.background = _texCardBg;
            _styleCard.padding = new RectOffset(12, 12, 10, 10);
            _styleCard.margin = new RectOffset(0, 0, 4, 4);

            _styleCardTitle = new GUIStyle();
            _styleCardTitle.normal.textColor = new Color(0.95f, 0.96f, 0.98f);
            _styleCardTitle.fontSize = 13;
            _styleCardTitle.fontStyle = FontStyle.Bold;

            _styleLabelRegular = new GUIStyle();
            _styleLabelRegular.normal.textColor = new Color(0.90f, 0.92f, 0.95f);
            _styleLabelRegular.fontSize = 12;

            _styleLabelBold = new GUIStyle();
            _styleLabelBold.normal.textColor = new Color(0.95f, 0.96f, 0.98f);
            _styleLabelBold.fontSize = 12;
            _styleLabelBold.fontStyle = FontStyle.Bold;

            _styleLabelMuted = new GUIStyle();
            _styleLabelMuted.normal.textColor = new Color(0.58f, 0.64f, 0.72f);
            _styleLabelMuted.fontSize = 11;

            _styleLabelSuccess = new GUIStyle();
            _styleLabelSuccess.normal.textColor = new Color(0.20f, 0.83f, 0.60f);
            _styleLabelSuccess.fontSize = 12;
            _styleLabelSuccess.fontStyle = FontStyle.Bold;

            _styleLabelWarning = new GUIStyle();
            _styleLabelWarning.normal.textColor = new Color(0.98f, 0.75f, 0.14f);
            _styleLabelWarning.fontSize = 12;
            _styleLabelWarning.fontStyle = FontStyle.Bold;

            _styleLabelError = new GUIStyle();
            _styleLabelError.normal.textColor = new Color(0.97f, 0.44f, 0.44f);
            _styleLabelError.fontSize = 12;
            _styleLabelError.fontStyle = FontStyle.Bold;

            _styleLogEntry = new GUIStyle();
            _styleLogEntry.normal.textColor = new Color(0.85f, 0.88f, 0.92f);
            _styleLogEntry.fontSize = 11;
            _styleLogEntry.wordWrap = true;

            _styleCloseButton = new GUIStyle(GUI.skin.button);
            _styleCloseButton.normal.textColor = new Color(0.97f, 0.44f, 0.44f);
            _styleCloseButton.fontStyle = FontStyle.Bold;
            _styleCloseButton.fontSize = 12;

            _styleFilterField = new GUIStyle(GUI.skin.textField);
            _styleFilterField.fontSize = 11;

            _stylesInitialized = true;
        }

        private static Texture2D MakeTex(Color col)
        {
            var pix = new Color[1] { col };
            var result = new Texture2D(1, 1);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnDestroy()
        {
            DestroyOwnedTextures();
        }

        private void DestroyOwnedTextures()
        {
            DestroyTexture(ref _texBackground);
            DestroyTexture(ref _texPanel);
            DestroyTexture(ref _texBorder);
            DestroyTexture(ref _texAccent);
            DestroyTexture(ref _texSuccess);
            DestroyTexture(ref _texWarning);
            DestroyTexture(ref _texError);
            DestroyTexture(ref _texCardBg);
            DestroyTexture(ref _texTabActive);
            DestroyTexture(ref _texTabInactive);
        }

        private static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                Destroy(tex);
                tex = null;
            }
        }
    }
}
