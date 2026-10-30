using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Live, agent-readable bridge over the loader's clean-room and visual utilities.
    /// Attached to the plugin's persistent GameObject so the BepInEx MCP bridge can
    /// read its public members (component/get_details) and call its methods
    /// (component/call_method) to obtain structured JSON without capturing the
    /// IMGUI overlay. This exposes behavior only; it adds no gameplay dependency.
    /// </summary>
    public sealed class LoaderDiagnosticsComponent : MonoBehaviour
    {
        private IRuntimeContentProvider _provider;
        private RuntimeOverlayUi _overlay;
        private RangeDisplayRuntime _rangeDisplay;

        public void Initialize(
            IRuntimeContentProvider provider,
            RuntimeOverlayUi overlay,
            RangeDisplayRuntime rangeDisplay)
        {
            _provider = provider;
            _overlay = overlay;
            _rangeDisplay = rangeDisplay;
        }

        // ---- Public members readable via component/get_details (no arguments) ----

        public bool ProviderReady => _provider != null;
        public bool OverlayReady => _overlay != null;
        public bool RangeDisplayReady => _rangeDisplay != null;

        public bool OverlayOpen => _overlay != null && _overlay.IsOpen;
        public string OverlayToggleShortcut => _overlay != null ? _overlay.ToggleShortcutText : string.Empty;

        public bool RangeDisplayEnabled => _rangeDisplay != null && _rangeDisplay.IsEnabled;
        public string RangeDisplayToggleKey => _rangeDisplay != null ? _rangeDisplay.ToggleKey : string.Empty;
        public int RangeDisplayTrackedEntities => _rangeDisplay != null ? _rangeDisplay.TrackedEntityCount : 0;

        public string TargetGameVersion => _provider != null ? _provider.TargetGameVersion : string.Empty;
        public bool IsNativeRegistryReady
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null && snapshot.IsNativeRegistryReady;
            }
        }

        public int ActivePackCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.ActivePackCount : 0;
            }
        }

        public int FailedPackCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.FailedPackCount : 0;
            }
        }

        public int RegisteredUnitCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.Units.Count : 0;
            }
        }

        public int RegisteredStageCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.Stages.Count : 0;
            }
        }

        public int RegisteredAiProfileCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.AiProfiles.Count : 0;
            }
        }

        // ---- Methods callable via component/call_method ----

        /// <summary>
        /// Returns the full loader diagnostics snapshot as a single-line JSON object.
        /// Readable by an agent without a visual capture.
        /// </summary>
        public string GetDiagnosticsJson()
        {
            return AgentDiagnosticsFormatter.SerializeSnapshot(SafeSnapshot());
        }

        /// <summary>
        /// Returns the state of every clean-room and visual utility as JSON:
        /// identifier-color resolution, log burst summary, inspector toggle, and
        /// range display. Readable by an agent without a visual capture.
        /// </summary>
        public string GetUtilitiesJson()
        {
            var overlayState = new OverlayStateView(
                _overlay != null,
                _overlay != null && _overlay.IsOpen,
                _overlay != null ? _overlay.ToggleShortcutText : string.Empty);
            var rangeState = new RangeStateView(
                _rangeDisplay != null,
                _rangeDisplay != null && _rangeDisplay.IsEnabled,
                _rangeDisplay != null ? _rangeDisplay.ToggleKey : string.Empty,
                _rangeDisplay != null ? _rangeDisplay.TrackedEntityCount : 0);

            return AgentDiagnosticsFormatter.SerializeUtilities(SafeSnapshot(), overlayState, rangeState);
        }

        /// <summary>
        /// Resolves the stable accent color the loader assigns to an identifier.
        /// Returns JSON like {"identifier":"...","index":N,"hex":"#RRGGBB"}.
        /// </summary>
        public string ResolveIdentifierColor(string identifier)
        {
            return AgentDiagnosticsFormatter.SerializeIdentifierColor(identifier);
        }

        private LoaderDiagnosticsSnapshot SafeSnapshot()
        {
            if (_provider == null)
            {
                return null;
            }

            try
            {
                return _provider.GetDiagnosticsSnapshot();
            }
            catch
            {
                return null;
            }
        }
    }
}
