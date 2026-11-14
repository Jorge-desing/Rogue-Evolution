#nullable enable
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using RogueCommandExpansionLoader.Runtime.Ai;
using RogueCommandExpansionLoader.Runtime.Visual;

internal static partial class ValidationTestRunner
{
    internal static IEnumerable<(string Name, Action Run)> GetRuntimeAndDiagnosticsValidationTests() => new (string Name, Action Run)[]
    {
    ("native Shop exit rejects stale and disabled controls and preserves uncertain invocation", () =>
    {
        try
        {
            Shop._instance = null;
            if (RuntimeIntermissionNativePanels.TryLeaveShop(1).NativeCallInvoked) throw new Exception("closed Shop invoked");
            Shop._instance = new Shop();
            var button = new ShopLeaveButton();
            var ui = new Michsky.UI.Reach.ButtonManager();
            button.gameObject.AddComponentExisting(ui);
            ui.gameObject = button.gameObject;
            UnityEngine.Object.NativeControlTestObjects = new UnityEngine.Object[] { button };
            int id = button.gameObject.GetInstanceID();
            if (RuntimeIntermissionNativePanels.TryLeaveShop(id + 1).NativeCallInvoked) throw new Exception("stale target invoked");
            ui.isInteractable = false;
            if (RuntimeIntermissionNativePanels.TryLeaveShop(id).NativeCallInvoked) throw new Exception("disabled target invoked");
            ui.isInteractable = true;
            button.Close = false;
            var uncertain = RuntimeIntermissionNativePanels.TryLeaveShop(id);
            if (!uncertain.NativeCallInvoked || uncertain.EffectObserved || button.Calls != 1) throw new Exception("uncertain result lost");
            button.Throw = true;
            var failed = RuntimeIntermissionNativePanels.TryLeaveShop(id);
            if (!failed.NativeCallInvoked || failed.EffectObserved || button.Calls != 2) throw new Exception("exception result lost");
            button.Throw = false; button.Close = true;
            var success = RuntimeIntermissionNativePanels.TryLeaveShop(id);
            if (!success.Success || !success.EffectObserved || button.Calls != 3) throw new Exception("native exit failed");
        }
        finally { Shop._instance = null; UnityEngine.Object.NativeControlTestObjects = Array.Empty<UnityEngine.Object>(); }
    }),
    ("screen overlay canvases bypass camera projection while camera canvases keep their assigned camera", () =>
    {
        var overlayCamera = new Camera();
        var overlayCanvas = new Canvas
        {
            renderMode = RenderMode.ScreenSpaceOverlay,
            worldCamera = overlayCamera,
        };
        if (RuntimeUiScreenProjection.GetProjectionCamera(overlayCanvas) != null)
        {
            throw new InvalidOperationException("A ScreenSpaceOverlay canvas must not project screen pixels through a camera.");
        }

        var rootCamera = new Camera();
        var rootCanvas = new Canvas
        {
            renderMode = RenderMode.ScreenSpaceCamera,
            worldCamera = rootCamera,
        };
        var nestedCanvas = new Canvas
        {
            rootCanvas = rootCanvas,
            renderMode = RenderMode.WorldSpace,
        };
        if (!ReferenceEquals(RuntimeUiScreenProjection.GetProjectionCamera(nestedCanvas), rootCamera))
        {
            throw new InvalidOperationException("A nested camera/world-space canvas without an assigned camera must fall back to its root camera.");
        }
    }),
    ("active stage association serializes configured stage sequence separately from live AI bindings", () =>
    {
        var sequence = new StageAiAssociationSequenceDiagnostic(
            status: "resolved",
            activeStageMapIndex: 1,
            totalStageCount: 2,
            omittedStageCount: 0,
            identitySource: "live_progression_sequence",
            failureReason: string.Empty,
            entries: new[]
            {
                new StageAiAssociationSequenceEntry(
                    stageMapIndex: 0,
                    stageIdentityResolved: true,
                    stageId: "frontier",
                    packId: "sample-pack",
                    stageKind: "custom",
                    failureReason: string.Empty,
                    declaredAiProfileIds: new[] { "sample-pack:raider" }),
                new StageAiAssociationSequenceEntry(
                    stageMapIndex: 1,
                    stageIdentityResolved: true,
                    stageId: "StageParameters_Native",
                    packId: string.Empty,
                    stageKind: "native",
                    failureReason: string.Empty,
                    declaredAiProfileIds: null),
            });
        var activeStage = new ActiveStageAiAssociationDiagnostic(
            stageIdentityResolved: true,
            stageMapIndex: 1,
            stageId: "StageParameters_Native",
            packId: string.Empty,
            stageKind: "native",
            identitySource: "live_progression_sequence",
            failureReason: string.Empty,
            declaredAiProfileIds: null);

        string json = AgentDiagnosticsFormatter.SerializeActiveStageAiAssociation(
            activeStage,
            loaderSnapshot: null,
            stageSequence: sequence);
        int omittedCounterIndex = json.IndexOf("\"omittedStageSequenceCount\"", StringComparison.Ordinal);
        int sequenceRowsIndex = json.IndexOf("\"stageSequenceRows\"", StringComparison.Ordinal);
        if (omittedCounterIndex < 0 || sequenceRowsIndex <= omittedCounterIndex)
        {
            throw new InvalidOperationException("Stage-sequence omission counters must precede sequence rows.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            JsonElement root = doc.RootElement;
            JsonElement rows = root.GetProperty("stageSequenceRows");
            if (root.GetProperty("stageSequenceStatus").GetString() != "resolved" ||
                root.GetProperty("stageSequenceIdentitySource").GetString() != "live_progression_sequence" ||
                root.GetProperty("stageIdentityStatus").GetString() != "resolved" ||
                root.GetProperty("runtimeBindingStatus").GetString() != "unavailable" ||
                rows.GetArrayLength() != 2 ||
                rows[0].GetProperty("qualifiedStageId").GetString() != "sample-pack:frontier" ||
                rows[0].GetProperty("declaredAiProfileIds")[0].GetString() != "sample-pack:raider" ||
                rows[1].GetProperty("qualifiedStageId").GetString() != "native:StageParameters_Native" ||
                !rows[1].GetProperty("isActiveStageMapIndex").GetBoolean())
            {
                throw new InvalidOperationException("The active-stage diagnostic must preserve stable custom/native sequence IDs and configured/live evidence boundaries.");
            }
        }

        var partialSequence = new StageAiAssociationSequenceDiagnostic(
            status: "partial",
            activeStageMapIndex: 0,
            totalStageCount: 2,
            omittedStageCount: 0,
            identitySource: "persisted_rclStageOrderV1",
            failureReason: "stage_identity_unresolved",
            entries: new[]
            {
                new StageAiAssociationSequenceEntry(
                    stageMapIndex: 0,
                    stageIdentityResolved: false,
                    stageId: string.Empty,
                    packId: string.Empty,
                    stageKind: "custom",
                    failureReason: "custom_stage_qualified_identity_unavailable",
                    declaredAiProfileIds: new[] { "sample-pack:raider", "sample-pack:artillery" }),
                new StageAiAssociationSequenceEntry(
                    stageMapIndex: 1,
                    stageIdentityResolved: true,
                    stageId: "StageParameters_Native",
                    packId: string.Empty,
                    stageKind: "native",
                    failureReason: string.Empty,
                    declaredAiProfileIds: null),
            });
        string boundedJson = AgentDiagnosticsFormatter.SerializeActiveStageAiAssociation(
            activeStage,
            loaderSnapshot: null,
            stageSequence: partialSequence,
            maxProfiles: 1);
        using (var doc = JsonDocument.Parse(boundedJson))
        {
            JsonElement root = doc.RootElement;
            JsonElement rows = root.GetProperty("stageSequenceRows");
            if (root.GetProperty("stageSequenceStatus").GetString() != "partial" ||
                root.GetProperty("omittedStageSequenceCount").GetInt32() != 1 ||
                rows.GetArrayLength() != 1 ||
                rows[0].GetProperty("stageIdentityStatus").GetString() != "unknown" ||
                rows[0].GetProperty("identityFailureReason").GetString() != "custom_stage_qualified_identity_unavailable" ||
                rows[0].GetProperty("omittedDeclaredAiProfileCount").GetInt32() != 1)
            {
                throw new InvalidOperationException("Bounded stage-sequence serialization must report omissions and retain explicit unknown identity reasons.");
            }
        }
    }),
    ("native rig retarget maps local position and rotation deltas onto target bind pose", () =>
    {
        Vector3 position = NativeRigRetargetPoseMath.RetargetLocalPosition(
            new Vector3(1f, 2f, 3f),
            new Vector3(2f, 4f, 6f),
            new Vector3(10f, 20f, 30f),
            0.5f);
        if (Math.Abs(position.x - 10.5f) > 0.0001f ||
            Math.Abs(position.y - 21f) > 0.0001f ||
            Math.Abs(position.z - 31.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Retargeted local position must preserve the target bind pose and scale only the source delta.");
        }

        float halfSqrt = (float)Math.Sqrt(0.5f);
        Quaternion rotation = NativeRigRetargetPoseMath.RetargetLocalRotation(
            new Quaternion(0f, 0f, halfSqrt, halfSqrt),
            new Quaternion(0f, 0f, 1f, 0f),
            new Quaternion(0f, halfSqrt, 0f, halfSqrt));
        if (Math.Abs(rotation.x - 0.5f) > 0.0001f ||
            Math.Abs(rotation.y - 0.5f) > 0.0001f ||
            Math.Abs(rotation.z - 0.5f) > 0.0001f ||
            Math.Abs(rotation.w - 0.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Retargeted rotation must apply the source bind-relative delta to the target bind rotation.");
        }
    }),
    ("native rig retarget binds mapped transforms, suspends on death, and restores on pool reuse", () =>
    {
        static Transform AddChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.parent = parent;
            return child.transform;
        }

        static bool Near(float actual, float expected) => Math.Abs(actual - expected) <= 0.0001f;

        var root = new GameObject("Entity");
        EntityController entity = root.AddComponent<EntityController>();
        Transform source = AddChild(AddChild(root.transform, "Native"), "Bone");
        Transform visualRoot = AddChild(root.transform, "Visual");
        Transform target = AddChild(AddChild(visualRoot, "Rig"), "Bone");
        Vector3 sourceRestPosition = new Vector3(1f, 2f, 3f);
        Vector3 targetRestPosition = new Vector3(10f, 20f, 30f);
        float halfSqrt = (float)Math.Sqrt(0.5f);
        Quaternion sourceRestRotation = new Quaternion(0f, 0f, 0f, 1f);
        Quaternion targetRestRotation = new Quaternion(0f, halfSqrt, 0f, halfSqrt);
        Quaternion sourceCurrentRotation = new Quaternion(0f, 0f, halfSqrt, halfSqrt);
        Vector3 targetScale = new Vector3(2f, 3f, 4f);
        source.localPosition = sourceRestPosition;
        source.localRotation = sourceRestRotation;
        target.localPosition = targetRestPosition;
        target.localRotation = targetRestRotation;
        target.localScale = targetScale;

        NativeRigRetargetProfile profile = root.AddComponent<NativeRigRetargetProfile>();
        profile.Configure(
            new[] { "Native/Bone" },
            new[] { "Rig/Bone" },
            "Visual",
            0.5f);
        FieldInfo? configuredMappingCountField = typeof(NativeRigRetargetProfile).GetField(
            "_configuredMappingCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (configuredMappingCountField == null)
        {
            throw new InvalidOperationException("The retarget mapping-count fixture must be able to emulate a cloned Unity component.");
        }

        configuredMappingCountField.SetValue(profile, 0);
        var unboundMappingSamples = new List<NativeRigRetargetMappingSample>();
        if (profile.ConfiguredMappingCount != 1 ||
            profile.CopyDiagnosticMappingSamples(unboundMappingSamples, 1) != 1 ||
            unboundMappingSamples.Count != 0)
        {
            throw new InvalidOperationException("Serialized mapping paths must preserve configured counts when Unity clones the profile component.");
        }

        if (!profile.Bind(entity) || !profile.IsBound || profile.ConfiguredMappingCount != 1 ||
            profile.BoundMappingCount != 1 || profile.IsSuspended)
        {
            throw new InvalidOperationException("A unique source/target path pair must bind exactly one retarget mapping.");
        }

        MethodInfo? lateUpdate = typeof(NativeRigRetargetProfile).GetMethod(
            "LateUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (lateUpdate == null)
        {
            throw new InvalidOperationException("The retarget LateUpdate entry point must remain available to the runtime lifecycle.");
        }

        source.localPosition = new Vector3(3f, 4f, 7f);
        source.localRotation = sourceCurrentRotation;
        lateUpdate.Invoke(profile, null);
        if (!Near(target.localPosition.x, 11f) || !Near(target.localPosition.y, 21f) ||
            !Near(target.localPosition.z, 32f) || !Near(target.localRotation.x, 0.5f) ||
            !Near(target.localRotation.y, 0.5f) || !Near(target.localRotation.z, 0.5f) ||
            !Near(target.localRotation.w, 0.5f) || !Near(target.localScale.x, targetScale.x) ||
            !Near(target.localScale.y, targetScale.y) || !Near(target.localScale.z, targetScale.z))
        {
            throw new InvalidOperationException("LateUpdate must transfer only bind-relative position/rotation deltas and preserve target scale.");
        }

        Vector3 deathPose = target.localPosition;
        profile.SuspendForDeath();
        source.localPosition = new Vector3(50f, 60f, 70f);
        lateUpdate.Invoke(profile, null);
        if (!Near(target.localPosition.x, deathPose.x) || !Near(target.localPosition.y, deathPose.y) ||
            !Near(target.localPosition.z, deathPose.z))
        {
            throw new InvalidOperationException("A death-suspended retarget profile must stop copying later native poses.");
        }

        profile.ResetForReuse();
        if (!Near(source.localPosition.x, sourceRestPosition.x) ||
            !Near(source.localPosition.y, sourceRestPosition.y) ||
            !Near(source.localPosition.z, sourceRestPosition.z) ||
            !Near(target.localPosition.x, targetRestPosition.x) ||
            !Near(target.localPosition.y, targetRestPosition.y) ||
            !Near(target.localPosition.z, targetRestPosition.z))
        {
            throw new InvalidOperationException("Pool reuse must restore both mapped bind poses before retargeting resumes.");
        }

        source.localPosition = new Vector3(3f, 4f, 7f);
        lateUpdate.Invoke(profile, null);
        if (!Near(target.localPosition.x, 11f) || !Near(target.localPosition.y, 21f) ||
            !Near(target.localPosition.z, 32f))
        {
            throw new InvalidOperationException("Retargeting must resume for the new pooled unit lifetime.");
        }
    }),
    ("native rig retarget fails closed when a target path is ambiguous", () =>
    {
        var root = new GameObject("Entity");
        EntityController entity = root.AddComponent<EntityController>();
        Transform nativeRoot = new GameObject("Native").transform;
        nativeRoot.parent = root.transform;
        Transform source = new GameObject("Bone").transform;
        source.parent = nativeRoot;
        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.parent = root.transform;
        Transform rig = new GameObject("Rig").transform;
        rig.parent = visualRoot;
        new GameObject("Bone").transform.parent = rig;
        new GameObject("Bone").transform.parent = rig;

        NativeRigRetargetProfile profile = root.AddComponent<NativeRigRetargetProfile>();
        profile.Configure(new[] { "Native/Bone" }, new[] { "Rig/Bone" }, "Visual", 1f);
        if (profile.Bind(entity) || profile.IsBound || profile.ConfiguredMappingCount != 1 ||
            profile.BoundMappingCount != 0 || !profile.IsSuspended)
        {
            throw new InvalidOperationException("An ambiguous target path must disable the whole retarget profile without partial bindings.");
        }
    }),
    ("EntityController.Destroy Harmony transpiler suspends native rig retargeting before native destroy", () =>
    {
        var root = new GameObject("RCL_RetargetDeathHook");
        EntityController entity = root.AddComponent<EntityController>();
        Transform nativeRoot = new GameObject("Native").transform;
        nativeRoot.parent = root.transform;
        Transform source = new GameObject("Bone").transform;
        source.parent = nativeRoot;
        Transform visualRoot = new GameObject("RCL_Visual_RetargetDeathHook").transform;
        visualRoot.parent = root.transform;
        Transform target = new GameObject("Bone").transform;
        target.parent = visualRoot;

        NativeRigRetargetProfile profile = root.AddComponent<NativeRigRetargetProfile>();
        profile.Configure(new[] { "Native/Bone" }, new[] { "Bone" }, "RCL_Visual_RetargetDeathHook", 1f);
        if (!profile.Bind(entity) || profile.IsSuspended)
        {
            throw new InvalidOperationException("The death-hook fixture must begin with a bound, active retarget profile.");
        }

        RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook(entity, false, null);
        if (!profile.IsSuspended)
        {
            throw new InvalidOperationException("The EntityController.Destroy transpiler entry hook must suspend retargeting immediately.");
        }

        source.localPosition = new Vector3(5f, 0f, 0f);
        MethodInfo? lateUpdate = typeof(NativeRigRetargetProfile).GetMethod(
            "LateUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (lateUpdate == null)
        {
            throw new InvalidOperationException("The retarget LateUpdate entry point must remain available to verify suspension.");
        }
        lateUpdate.Invoke(profile, null);
        if (Math.Abs(target.localPosition.x) > 0.0001f)
        {
            throw new InvalidOperationException("The destroy entry hook must prevent later native poses from replacing the visual death pose.");
        }
    }),
    ("localization language resolution falls back when the reflected getter throws", () =>
    {
        int logCount = 0;
        string language = RuntimeLocalizationLanguage.GetOrDefault(
            () => { throw new InvalidOperationException("synthetic language getter failure"); },
            _ => logCount++,
            "relic test");

        if (language != "en-US" || logCount != 1)
        {
            throw new InvalidOperationException("A localization getter failure must return en-US and log once.");
        }
    }),
    ("diagnostics host follows the loaded additive scene", () =>
    {
        string previousSceneName = UnityEngine.SceneManagement.SceneManager.ActiveSceneName;
        int previousSceneHandle = UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle;
        try
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = "IntermissionScreen";
            UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle = 1;
            var host = new GameObject("LoaderDiagnostics");
            var gameScene = new UnityEngine.SceneManagement.Scene { name = "Game", isLoaded = true, handle = 2 };

            if (!RuntimeDiagnosticsScenePlacement.TryMoveHostToScene(host, gameScene) ||
                host.scene.name != "Game" || !host.scene.isLoaded)
            {
                throw new InvalidOperationException("The diagnostics host must be moved to the scene that was loaded, not left in the previous active scene.");
            }

            if (!RuntimeDiagnosticsScenePlacement.TryMoveHostToScene(host, gameScene))
            {
                throw new InvalidOperationException("Moving the diagnostics host to its current scene should be idempotent.");
            }
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = previousSceneName;
            UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle = previousSceneHandle;
        }
    }),
        ("diagnostics host ignores unrelated additive scenes once registered", () =>
        {
            if (RuntimeDiagnosticsScenePlacement.ShouldEnsureOnSceneLoaded(true, "AudioOverlay", false, false))
            {
                throw new InvalidOperationException("Loading an unrelated additive scene must not move an existing diagnostics host out of Game.");
            }

            if (!RuntimeDiagnosticsScenePlacement.ShouldEnsureOnSceneLoaded(true, "Game", false, false))
            {
                throw new InvalidOperationException("Loading Game must rebind an existing diagnostics host to the gameplay scene.");
            }

            if (!RuntimeDiagnosticsScenePlacement.ShouldEnsureOnSceneLoaded(false, "MainMenu", false, false))
            {
                throw new InvalidOperationException("A scene load should recreate a diagnostics host that no longer exists.");
            }

            if (!RuntimeDiagnosticsScenePlacement.ShouldEnsureOnSceneLoaded(true, "MainMenu", true, false))
            {
                throw new InvalidOperationException("A single-scene transition should rebind the host to the replacement scene.");
            }

            if (!RuntimeDiagnosticsScenePlacement.ShouldEnsureOnSceneLoaded(true, "Game", false, true))
            {
                throw new InvalidOperationException("The active diagnostics host should be reinitialized when its scene is reloaded.");
            }
        }),
        ("diagnostics host rejects an unloaded target scene", () =>
    {
        string previousSceneName = UnityEngine.SceneManagement.SceneManager.ActiveSceneName;
        int previousSceneHandle = UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle;
        try
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = "IntermissionScreen";
            UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle = 1;
            var host = new GameObject("LoaderDiagnostics");
            var unloadedScene = new UnityEngine.SceneManagement.Scene { name = "Game", isLoaded = false, handle = 2 };

            if (RuntimeDiagnosticsScenePlacement.TryMoveHostToScene(host, unloadedScene) ||
                host.scene.name != "IntermissionScreen")
            {
                throw new InvalidOperationException("An unloaded target scene must not detach the diagnostics host from its current scene.");
            }
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = previousSceneName;
            UnityEngine.SceneManagement.SceneManager.ActiveSceneHandle = previousSceneHandle;
        }
    }),
    ("runtime log burst summary counts only adjacent equal messages", () =>
    {
        LoaderLogBurstSummary summary = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded alpha",
            "12:00:01 loaded alpha",
            "12:00:02 loaded beta",
            "12:00:03 loaded alpha"
        });
        if (summary == null || summary.Message != "loaded alpha" || summary.RepeatCount != 0)
        {
            throw new InvalidOperationException("Only the final consecutive run should be summarized, without merging earlier messages.");
        }

        LoaderLogBurstSummary repeated = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded alpha",
            "12:00:01 loaded alpha",
            "12:00:02 loaded alpha"
        });
        if (repeated == null || repeated.Message != "loaded alpha" || repeated.RepeatCount != 2 ||
            repeated.DisplayText != "loaded alpha (2)")
        {
            throw new InvalidOperationException("The summary should display the count of repeated messages after the first entry.");
        }
    }),
    ("runtime log burst summary preserves entries without a valid timestamp", () =>
    {
        LoaderLogBurstSummary summary = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "not a timestamp: same",
            "not a timestamp: same"
        });
        if (summary == null || summary.Message != "not a timestamp: same" || summary.RepeatCount != 1)
        {
            throw new InvalidOperationException("Non-timestamped messages must remain intact while still being grouped consecutively.");
        }

        LoaderLogBurstSummary singleRepeat = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded beta",
            "12:00:01 loaded beta"
        });
        if (singleRepeat == null || singleRepeat.RepeatCount != 1 ||
            singleRepeat.DisplayText != "loaded beta (1)")
        {
            throw new InvalidOperationException("The first repeated entry must display a repeat count of one.");
        }

        if (LoaderLogBurstSummaryPolicy.Create(Array.Empty<string>()) != null)
        {
            throw new InvalidOperationException("An empty log should not produce a burst summary.");
        }
    }),
    ("inspector toggle policy opens, toggles, and gives close precedence", () =>
    {
        if (!InspectorTogglePolicy.Resolve(false, true, false) ||
            InspectorTogglePolicy.Resolve(true, true, false) ||
            InspectorTogglePolicy.Resolve(true, false, true) ||
            InspectorTogglePolicy.Resolve(false, true, true) ||
            InspectorTogglePolicy.Resolve(false, false, false))
        {
            throw new InvalidOperationException("Toggle should invert state unless close is pressed, which must leave the inspector closed.");
        }
    }),
    ("inspector toggle state serializes resolved open flag", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeInspectorToggleState(false, true, false);
        if (!json.Contains("\"resolvedOpen\":true"))
        {
            throw new InvalidOperationException("SerializeInspectorToggleState must report resolvedOpen=true for a toggle press.");
        }

        string closedJson = AgentDiagnosticsFormatter.SerializeInspectorToggleState(true, false, true);
        if (!closedJson.Contains("\"resolvedOpen\":false"))
        {
            throw new InvalidOperationException("SerializeInspectorToggleState must report resolvedOpen=false when close is pressed.");
        }
    }),
    ("gui control policy validates tab and time scale", () =>
    {
        int tab;
        if (!GuiControlPolicy.TryClampTab(3, out tab) || tab != 3)
        {
            throw new InvalidOperationException("TryClampTab must accept in-range tab 3.");
        }
        if (!GuiControlPolicy.TryClampTab(-1, out tab) || tab != 0)
        {
            throw new InvalidOperationException("TryClampTab must clamp negative tab to 0.");
        }
        if (!GuiControlPolicy.TryClampTab(99, out tab) || tab != 7)
        {
            throw new InvalidOperationException("TryClampTab must clamp high tab to 7.");
        }
        float ts;
        if (!GuiControlPolicy.TryValidateTimeScale(1.5f, out ts) || System.Math.Abs(ts - 1.5f) > 0.001f)
        {
            throw new InvalidOperationException("TryValidateTimeScale must accept finite 1.5.");
        }
        if (GuiControlPolicy.TryValidateTimeScale(float.NaN, out ts))
        {
            throw new InvalidOperationException("TryValidateTimeScale must reject NaN.");
        }
        if (GuiControlPolicy.TryValidateTimeScale(float.PositiveInfinity, out ts))
        {
            throw new InvalidOperationException("TryValidateTimeScale must reject infinity.");
        }
    }),
    ("gui control policy exposes unified ten-times time scale cap", () =>
    {
        if (GuiControlPolicy.MaximumTimeScale != 10f)
        {
            throw new InvalidOperationException("GuiControlPolicy.MaximumTimeScale must match the native 0-10 time scale contract.");
        }
        float ts;
        if (!GuiControlPolicy.TryValidateTimeScale(9.5f, out ts) || System.Math.Abs(ts - 9.5f) > 0.001f)
        {
            throw new InvalidOperationException("TryValidateTimeScale must accept 9.5 within the 0-10 contract.");
        }
        float cx;
        if (!GuiControlPolicy.TryValidateCameraFloat(-42.5f, out cx) || System.Math.Abs(cx - (-42.5f)) > 0.001f)
        {
            throw new InvalidOperationException("TryValidateCameraFloat must accept finite camera coordinates.");
        }
        if (GuiControlPolicy.TryValidateCameraFloat(float.NaN, out cx))
        {
            throw new InvalidOperationException("TryValidateCameraFloat must reject NaN camera coordinates.");
        }
    }),
    ("gui state serializes overlay and range with counters first", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeGuiState(
            new OverlayStateView(true, true, "F8", 2, true, false, false, "", "1.19.0", "", true, ""),
            new RangeStateView(true, true, "F11", 5, true, true, true, false, 0.08f, 48, 0.15f),
            new GuiPlaybackView(1.0f, true, 10f, 20f, 60f),
            3,
            5,
            2);
        int kindIdx = json.IndexOf("\"kind\"", System.StringComparison.Ordinal);
        int omittedIdx = json.IndexOf("\"omittedGuiFieldCount\"", System.StringComparison.Ordinal);
        int overlayIdx = json.IndexOf("\"overlay\"", System.StringComparison.Ordinal);
        if (kindIdx < 0 || omittedIdx < 0 || overlayIdx < 0 || omittedIdx > overlayIdx)
        {
            throw new InvalidOperationException("SerializeGuiState must place omittedGuiFieldCount before overlay detail.");
        }
        if (!json.Contains("\"overlayOpen\":true") || !json.Contains("\"rangeEnabled\":true"))
        {
            throw new InvalidOperationException("SerializeGuiState must report overlayOpen and rangeEnabled.");
        }
        string control = AgentDiagnosticsFormatter.SerializeGuiControlResult(true, "", 0f, 0f, 0f);
        if (!control.Contains("\"success\":true"))
        {
            throw new InvalidOperationException("SerializeGuiControlResult must report success.");
        }
        string failed = AgentDiagnosticsFormatter.SerializeGuiControlResult(false, "bad tab", 0f, 0f, 0f);
        if (!failed.Contains("\"success\":false") || !failed.Contains("bad tab"))
        {
            throw new InvalidOperationException("SerializeGuiControlResult must report failure with message.");
        }
    }),
    ("range circle point count serializes validity and count", () =>
    {
        string validJson = AgentDiagnosticsFormatter.SerializeRangeCirclePointCount(32);
        if (!validJson.Contains("\"valid\":true") || !validJson.Contains("\"pointCount\":33"))
        {
            throw new InvalidOperationException("SerializeRangeCirclePointCount must report valid=true and pointCount=segments+1.");
        }

        string invalidJson = AgentDiagnosticsFormatter.SerializeRangeCirclePointCount(2);
        if (!invalidJson.Contains("\"valid\":false") || !invalidJson.Contains("\"pointCount\":0"))
        {
            throw new InvalidOperationException("SerializeRangeCirclePointCount must report valid=false and pointCount=0 for out-of-range segments.");
        }
    }),
    ("runtime hooks state serializes installed flags, owner id and xp count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(true, 42, "RogueCommand.ExpansionLoader.Runtime", 7);
        if (!json.Contains("\"hooksInstalled\":true") || !json.Contains("\"installedHookCount\":42") ||
            !json.Contains("\"harmonyOwnerId\":\"RogueCommand.ExpansionLoader.Runtime\"") ||
            !json.Contains("\"registeredCombatXpCount\":7"))
        {
            throw new InvalidOperationException("SerializeRuntimeHooksState must report the installed flags, owner id and combat xp count.");
        }

        string nullOwner = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(false, 0, null, 0);
        if (!nullOwner.Contains("\"harmonyOwnerId\":\"\""))
        {
            throw new InvalidOperationException("SerializeRuntimeHooksState must emit an empty owner id string when null.");
        }
    }),
    ("installed patch names serializes a de-duplicated name list with its count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeInstalledPatchNames(new[] { "EntityControllerDiePostfix", "", null, "EntityTakeDamagePostfix" });
        if (!json.Contains("\"installedPatchCount\":2") ||
            !json.Contains("\"EntityControllerDiePostfix\"") ||
            !json.Contains("\"EntityTakeDamagePostfix\""))
        {
            throw new InvalidOperationException("SerializeInstalledPatchNames must drop null/empty entries and report the surviving count.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeInstalledPatchNames(null);
        if (!empty.Contains("\"installedPatchCount\":0") || !empty.Contains("\"installedPatchNames\":[]"))
        {
            throw new InvalidOperationException("SerializeInstalledPatchNames must emit an empty list and count 0 for null input.");
        }
    }),
    ("registered profiles serializes ids with their count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeRegisteredProfiles(new[] { "zebra", "alpha" });
        if (!json.Contains("\"registeredProfileCount\":2") ||
            !json.Contains("\"registeredProfileIds\":[\"zebra\",\"alpha\"]"))
        {
            throw new InvalidOperationException("SerializeRegisteredProfiles must preserve id order and report the count.");
        }

        string dropped = AgentDiagnosticsFormatter.SerializeRegisteredProfiles(new[] { "", null, "solo" });
        if (!dropped.Contains("\"registeredProfileCount\":1") || !dropped.Contains("\"registeredProfileIds\":[\"solo\"]"))
        {
            throw new InvalidOperationException("SerializeRegisteredProfiles must drop null/empty ids and report the surviving count.");
        }
    }),
    ("applied baseline effects serializes the entry count and names", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeAppliedBaselineEffects(3, new[] { "Burning", "Chilled", "Burning" });
        if (!json.Contains("\"appliedBaselineEffectEntryCount\":3") ||
            !json.Contains("\"appliedBaselineEffectNames\":[\"Burning\",\"Chilled\",\"Burning\"]"))
        {
            throw new InvalidOperationException("SerializeAppliedBaselineEffects must preserve names and report the entry count.");
        }

        string none = AgentDiagnosticsFormatter.SerializeAppliedBaselineEffects(0, null);
        if (!none.Contains("\"appliedBaselineEffectEntryCount\":0") || !none.Contains("\"appliedBaselineEffectNames\":[]"))
        {
            throw new InvalidOperationException("SerializeAppliedBaselineEffects must emit an empty name list for a null input.");
        }
    }),
    ("combat xp entries serializes entity keys and experience", () =>
    {
        var entries = new[]
        {
            new RuntimeUnitMechanicsDirector.CombatXpEntry(201, 12),
            new RuntimeUnitMechanicsDirector.CombatXpEntry(105, 4),
        };
        string json = AgentDiagnosticsFormatter.SerializeCombatXpEntries(entries);
        if (!json.Contains("\"combatXpEntryCount\":2") ||
            !json.Contains("\"entityKeyId\":201") || !json.Contains("\"experience\":12") ||
            !json.Contains("\"entityKeyId\":105") || !json.Contains("\"experience\":4"))
        {
            throw new InvalidOperationException("SerializeCombatXpEntries must report each entity key id and its experience.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeCombatXpEntries(null);
        if (!empty.Contains("\"combatXpEntryCount\":0") || !empty.Contains("\"combatXpEntries\":[]"))
        {
            throw new InvalidOperationException("SerializeCombatXpEntries must emit an empty list and count 0 for null input.");
        }
    }),
    ("battlefield census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new BattlefieldUnitRow("unit:alpha", false, 60f, 100f, 20f, 30f, 2, false, true, true),
            new BattlefieldUnitRow("unit:beta", true, 0f, 50f, 0f, 0f, 0, true, false, false),
        };
        var census = new BattlefieldCensus(5, 2, 1, 1, 1, rows);
        string json = AgentDiagnosticsFormatter.SerializeBattlefieldCensus(census);
        if (!json.Contains("\"battlefieldEntityCount\":5") ||
            !json.Contains("\"playerUnitCount\":2") ||
            !json.Contains("\"aiUnitCount\":1") ||
            !json.Contains("\"aiBuildingCount\":1") ||
            !json.Contains("\"harvesterCount\":1") ||
            !json.Contains("\"playerUnitRowCount\":2") ||
            !json.Contains("\"entityKeyId\":\"unit:alpha\"") ||
            !json.Contains("\"healthRatio\":0.6") ||
            !json.Contains("\"isAlive\":false"))
        {
            throw new InvalidOperationException("SerializeBattlefieldCensus must report counts, rows, and per-unit health/armor/shield/rank state.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeBattlefieldCensus(null);
        if (!empty.Contains("\"battlefieldEntityCount\":0") || !empty.Contains("\"playerUnitRows\":[]"))
        {
            throw new InvalidOperationException("SerializeBattlefieldCensus must emit an empty census for null input.");
        }
    }),
    ("entity balance lookup serializer emits base params", () =>
    {
        var row = new EntityBalanceRow("unit:alpha", false, 150, 200, 25f, 40, 120f, 3, 1.5f, 4f, 7.5f, 12f, 6f);
        string json = AgentDiagnosticsFormatter.SerializeEntityBalanceLookup(row);
        if (!json.Contains("\"entityId\":\"unit:alpha\"") ||
            !json.Contains("\"isBuilding\":false") ||
            !json.Contains("\"cost\":150") ||
            !json.Contains("\"maxHealth\":200") ||
            !json.Contains("\"maxArmor\":25") ||
            !json.Contains("\"maxShield\":40") ||
            !json.Contains("\"maxMana\":120") ||
            !json.Contains("\"maxRank\":3") ||
            !json.Contains("\"moveSpeed\":1.5") ||
            !json.Contains("\"weaponRange\":4") ||
            !json.Contains("\"dps\":7.5") ||
            !json.Contains("\"damage1\":12") ||
            !json.Contains("\"damage2\":6"))
        {
            throw new InvalidOperationException("SerializeEntityBalanceLookup must report the base balance parameters in field order.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeEntityBalanceLookup(default(EntityBalanceRow));
        if (!empty.Contains("\"entityId\":\"\"") || !empty.Contains("\"cost\":0") || !empty.Contains("\"dps\":0"))
        {
            throw new InvalidOperationException("SerializeEntityBalanceLookup must emit neutral defaults for an empty row.");
        }
    }),
    ("card change request serializer emits request fields", () =>
    {
        var request = CardChangeRequest.Create(
            "MaxHealth", "Add", 25f, true, null, "Friendly", false,
            new[] { "unit:alpha" }, new[] { "unit:beta" }, "rcl-test");
        string json = AgentDiagnosticsFormatter.SerializeCardChangeRequest(request);
        if (!json.Contains("\"valueToChange\":\"MaxHealth\"") ||
            !json.Contains("\"operation\":\"Add\"") ||
            !json.Contains("\"value\":25") ||
            !json.Contains("\"multiplyValueWithCurrentHeatLevel\":true") ||
            !json.Contains("\"multiplyValueWithMetaProgressionMultiplier\":\"\"") ||
            !json.Contains("\"side\":\"Friendly\"") ||
            !json.Contains("\"changeableValueHasToBeGreaterZero\":false") ||
            !json.Contains("\"onlyForTheseEntityIdsCount\":1") ||
            !json.Contains("\"entityIdsNotAllowedCount\":1") ||
            !json.Contains("\"sourceId\":\"rcl-test\""))
        {
            throw new InvalidOperationException("SerializeCardChangeRequest must report the request fields in field order.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeCardChangeRequest(default(CardChangeRequest));
        if (!empty.Contains("\"valueToChange\":\"\"") || !empty.Contains("\"value\":0") || !empty.Contains("\"sourceId\":\"\""))
        {
            throw new InvalidOperationException("SerializeCardChangeRequest must emit neutral defaults for an empty request.");
        }
    }),
    ("runtime entity balance lookup reads base params from EntityBalancingStore", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:alpha"] = 150;
            EntityBalancingStore.MaxHealthOf["unit:alpha"] = 200;
            EntityBalancingStore.MaxArmorOf["unit:alpha"] = 25f;
            EntityBalancingStore.MaxShieldOf["unit:alpha"] = 40;
            EntityBalancingStore.MaxManaOf["unit:alpha"] = 120f;
            EntityBalancingStore.MaxRankOf["unit:alpha"] = 3;
            EntityBalancingStore.MoveSpeedOf["unit:alpha"] = 1.5f;
            EntityBalancingStore.WeaponRangeOf["unit:alpha"] = 4f;
            EntityBalancingStore.DpsOf["unit:alpha"] = 7.5f;
            EntityBalancingStore.Damage1Of["unit:alpha"] = 12f;
            EntityBalancingStore.Damage2Of["unit:alpha"] = 6f;
            EntityBalancingStore.BuildingEntityIds.Add("building:hq");

            EntityBalanceRow alpha = RuntimeEntityBalanceLookup.Current("unit:alpha");
            if (alpha.EntityId != "unit:alpha" || alpha.Cost != 150 || alpha.MaxHealth != 200 ||
                alpha.MaxArmor != 25f || alpha.MaxShield != 40 || alpha.MaxMana != 120f ||
                alpha.MaxRank != 3 || alpha.MoveSpeed != 1.5f || alpha.WeaponRange != 4f ||
                alpha.Dps != 7.5f || alpha.Damage1 != 12f || alpha.Damage2 != 6f || alpha.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must read base params from EntityBalancingStore.");
            }

            EntityBalanceRow building = RuntimeEntityBalanceLookup.Current("building:hq");
            if (!building.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must report IsBuilding from EntityBalancingStore.");
            }

            EntityBalanceRow unknown = RuntimeEntityBalanceLookup.Current("unit:unknown");
            if (unknown.EntityId != "unit:unknown" || unknown.Cost != 100 || unknown.MaxHealth != 100 ||
                unknown.MaxRank != 1 || unknown.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must return store defaults for an unknown entity id.");
            }

            EntityBalanceRow blank = RuntimeEntityBalanceLookup.Current("  ");
            if (!string.IsNullOrEmpty(blank.EntityId))
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must return an empty row for a blank entity id.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime battlefield census collects from the existing controllers registry", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            ExistingControllers.Instance.PlayerUnits().Add(new EntityController
            {
                entityId = "unit:player-1",
                CurrentHealth = 80f,
                MaxHealth = 100f,
                CurrentArmor = 10f,
                CurrentShield = 5f,
                CurrentRank = 1,
                IsBuilding = false,
                IsControlledByPlayer = true,
                IsControlledByAi = false,
            });
            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                entityId = "unit:ai-1",
                CurrentHealth = 40f,
                MaxHealth = 40f,
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                entityId = "building:ai-hq",
                IsBuilding = true,
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.Harvesters.Add(new EntityController
            {
                entityId = "unit:harvester-1",
                CurrentHealth = 30f,
                MaxHealth = 30f,
                IsControlledByPlayer = true,
            });

            BattlefieldCensus census = RuntimeBattlefieldCensus.Current();
            if (census.BattlefieldEntityCount != 3 ||
                census.PlayerUnitCount != 1 ||
                census.AiUnitCount != 1 ||
                census.AiBuildingCount != 1 ||
                census.HarvesterCount != 1 ||
                census.PlayerUnitRows.Count != 1 ||
                census.PlayerUnitRows[0].EntityKeyId != "unit:player-1" ||
                census.PlayerUnitRows[0].CurrentHealth != 80f ||
                census.PlayerUnitRows[0].HealthRatio != 0.8f ||
                census.PlayerUnitRows[0].CurrentArmor != 10f ||
                census.PlayerUnitRows[0].CurrentShield != 5f ||
                census.PlayerUnitRows[0].CurrentRank != 1)
            {
                throw new InvalidOperationException("RuntimeBattlefieldCensus must aggregate live player, AI, building, and harvester state from the registry.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("ai roster census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new AiRosterRow("unit:ai-1", false, 100, 80, 5f, 10f, 12f, 6f, 101),
            new AiRosterRow("building:ai-hq", true, 300, 200, 0f, 0f, 0f, 0f, 102),
        };
        var census = new AiRosterCensus(1, 1, new[] { rows[0] }, new[] { rows[1] });
        string json = AgentDiagnosticsFormatter.SerializeAiRoster(census);
        if (!json.Contains("\"aiUnitCount\":1") ||
            !json.Contains("\"aiBuildingCount\":1") ||
            !json.Contains("\"aiUnitRowCount\":1") ||
            !json.Contains("\"aiBuildingRowCount\":1") ||
            !json.Contains("\"entityKeyId\":\"unit:ai-1\",\"runtimeEntityKeyId\":\"101\"") ||
            !json.Contains("\"dps\":10") ||
            !json.Contains("\"entityKeyId\":\"building:ai-hq\",\"runtimeEntityKeyId\":\"102\""))
        {
            throw new InvalidOperationException("SerializeAiRoster must report counts and rows.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeAiRoster(null);
        if (!empty.Contains("\"aiUnitCount\":0") || !empty.Contains("\"aiUnitRows\":[]") ||
            !empty.Contains("\"aiBuildingRows\":[]"))
        {
            throw new InvalidOperationException("SerializeAiRoster must emit empty arrays for null input.");
        }
    }),
    ("runtime ai roster census collects from the existing controllers registry", () =>
    {
        EntityBalancingStore.Reset();
        ExistingControllers.Instance.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:ai-1"] = 100;
            EntityBalancingStore.MaxHealthOf["unit:ai-1"] = 80;
            EntityBalancingStore.MoveSpeedOf["unit:ai-1"] = 5f;
            EntityBalancingStore.DpsOf["unit:ai-1"] = 10f;
            EntityBalancingStore.Damage1Of["unit:ai-1"] = 12f;
            EntityBalancingStore.WeaponRangeOf["unit:ai-1"] = 6f;

            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                entityId = "unit:ai-1",
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                entityId = "building:ai-hq",
                IsBuilding = true,
                IsControlledByAi = true,
            });

            AiRosterCensus census = RuntimeAiRosterCensus.Current();
            if (census.AiUnitCount != 1 || census.AiBuildingCount != 1 ||
                census.AiUnitRows.Count != 1 || census.AiBuildingRows.Count != 1 ||
                census.AiUnitRows[0].EntityKeyId != "unit:ai-1" ||
                census.AiUnitRows[0].Cost != 100 ||
                census.AiUnitRows[0].MaxHealth != 80 ||
                census.AiUnitRows[0].MoveSpeed != 5f ||
                census.AiUnitRows[0].Dps != 10f ||
                census.AiBuildingRows[0].EntityKeyId != "building:ai-hq" ||
                !census.AiBuildingRows[0].IsBuilding)
            {
                throw new InvalidOperationException("RuntimeAiRosterCensus must aggregate live AI units and buildings with balance.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            EntityBalancingStore.Reset();
        }
    }),
    ("player roster census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new PlayerRosterRow("unit:player-1", false, 80f, 100f, 0.8f, 150, 5f, 10f, 12f, 6f),
        };
        var census = new PlayerRosterCensus(1, rows);
        string json = AgentDiagnosticsFormatter.SerializePlayerRoster(census);
        if (!json.Contains("\"playerUnitCount\":1") ||
            !json.Contains("\"playerUnitRowCount\":1") ||
            !json.Contains("\"entityKeyId\":\"unit:player-1\"") ||
            !json.Contains("\"healthRatio\":0.8") ||
            !json.Contains("\"cost\":150"))
        {
            throw new InvalidOperationException("SerializePlayerRoster must report counts and rows.");
        }

        string empty = AgentDiagnosticsFormatter.SerializePlayerRoster(null);
        if (!empty.Contains("\"playerUnitCount\":0") || !empty.Contains("\"playerUnitRows\":[]"))
        {
            throw new InvalidOperationException("SerializePlayerRoster must emit empty arrays for null input.");
        }
    }),
    ("runtime player roster census collects from the battlefield census and balance store", () =>
    {
        EntityBalancingStore.Reset();
        ExistingControllers.Instance.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:player-1"] = 150;
            EntityBalancingStore.MaxHealthOf["unit:player-1"] = 100;
            EntityBalancingStore.MoveSpeedOf["unit:player-1"] = 5f;
            EntityBalancingStore.DpsOf["unit:player-1"] = 10f;
            EntityBalancingStore.Damage1Of["unit:player-1"] = 12f;
            EntityBalancingStore.WeaponRangeOf["unit:player-1"] = 6f;

            ExistingControllers.Instance.PlayerUnits().Add(new EntityController
            {
                entityId = "unit:player-1",
                CurrentHealth = 80f,
                MaxHealth = 100f,
                IsControlledByPlayer = true,
            });

            PlayerRosterCensus census = RuntimePlayerRosterCensus.Current();
            if (census.PlayerUnitCount != 1 || census.PlayerUnitRows.Count != 1 ||
                census.PlayerUnitRows[0].EntityKeyId != "unit:player-1" ||
                census.PlayerUnitRows[0].Cost != 150 ||
                census.PlayerUnitRows[0].MaxHealth != 100 ||
                census.PlayerUnitRows[0].CurrentHealth != 80f ||
                census.PlayerUnitRows[0].HealthRatio != 0.8f)
            {
                throw new InvalidOperationException("RuntimePlayerRosterCensus must aggregate live player units with balance.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            EntityBalancingStore.Reset();
        }
    }),
    ("identifier color policy hashes with FNV-1a and maps to a stable palette slot", () =>
    {
        if (IdentifierColorPolicy.Hash(string.Empty) != 0x811C9DC5u)
        {
            throw new InvalidOperationException("Empty identifier must yield the FNV-1a 32-bit offset basis.");
        }

        if (IdentifierColorPolicy.Hash("a") != 0xE40C292Cu)
        {
            throw new InvalidOperationException("The hash must follow the public FNV-1a 32-bit test vector for 'a'.");
        }

        if (IdentifierColorPolicy.Hash("foobar") != 0xBF9CF968u)
        {
            throw new InvalidOperationException("The hash must follow the public FNV-1a 32-bit test vector for 'foobar'.");
        }

        int paletteSize = IdentifierColorPolicy.PaletteSize;
        if (paletteSize <= 0 || (paletteSize & (paletteSize - 1)) != 0)
        {
            throw new InvalidOperationException("The palette size must be a positive power of two for even index distribution.");
        }

        int index = IdentifierColorPolicy.PaletteIndex("sample-native:scout-tank");
        if (index < 0 || index >= paletteSize)
        {
            throw new InvalidOperationException("The palette index must stay within the palette bounds.");
        }
    }),
    ("identifier color policy is deterministic and stable across calls", () =>
    {
        string id = "sample-native:expansion";
        UnityEngine.Color first = IdentifierColorPolicy.Resolve(id);
        UnityEngine.Color second = IdentifierColorPolicy.Resolve(id);
        if (first.r != second.r || first.g != second.g || first.b != second.b || first.a != second.a)
        {
            throw new InvalidOperationException("The same identifier must always resolve to the same accent color.");
        }

        if (IdentifierColorPolicy.Resolve(id) != IdentifierColorPolicy.Resolve(id))
        {
            throw new InvalidOperationException("Color equality must be consistent for the same identifier.");
        }
    }),
    ("agent diagnostics formatter exposes clean-room and visual utilities as JSON", () =>
    {
        var packs = new List<PackDiagnosticReport>
        {
            new PackDiagnosticReport(
                "sample-native:expansion", "sample-native-expansion", true, "Cargado correctamente.",
                1, 2048L, 5, 3, 3),
        };
        var units = new List<UnitDiagnosticReport>
        {
            new UnitDiagnosticReport(
                "sample-native:scout-tank", "sample-native:expansion", "scout.prefab", "scout.png",
                true, false, true, false, false, null, 0),
        };
        var stages = new List<StageDiagnosticReport>
        {
            new StageDiagnosticReport("stage-a", "sample-native:expansion", 24, 48,
                new[] { "sample-native:scout-tank" }, new[] { "default" }),
        };
        var aiProfiles = new List<AiProfileDiagnosticReport>
        {
            new AiProfileDiagnosticReport("ai-a", "sample-native:expansion", "ai.prefab",
                new[] { "module-x" }, 1),
        };
        var snapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 1, 0, 2048L,
            packs, units, stages, aiProfiles,
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            Array.Empty<UnitEligibilityDiagnosticReport>(),
            Array.Empty<UnitAssetResolutionDiagnosticReport>(),
            Array.Empty<UnitLocalizationCoverageDiagnosticReport>(),
            new[] { "12:00:00 loaded alpha", "12:00:01 loaded alpha" });

        string snapshotJson = AgentDiagnosticsFormatter.SerializeSnapshot(snapshot);
        if (string.IsNullOrEmpty(snapshotJson) ||
            !snapshotJson.Contains("\"kind\":\"loaderDiagnostics\"") ||
            !snapshotJson.Contains("\"sample-native:scout-tank\""))
        {
            throw new InvalidOperationException("The snapshot JSON must expose packs, units, and identifiers.");
        }

        string utilitiesJson = AgentDiagnosticsFormatter.SerializeUtilities(snapshot, null, null);
        if (!utilitiesJson.Contains("\"kind\":\"utilities\"") ||
            !utilitiesJson.Contains("\"cleanRoom\"") ||
            !utilitiesJson.Contains("\"visual\"") ||
            !utilitiesJson.Contains("\"inspector\"") ||
            !utilitiesJson.Contains("\"rangeDisplay\""))
        {
            throw new InvalidOperationException("The utilities JSON must expose clean-room and visual utility state.");
        }

        string colorJson = AgentDiagnosticsFormatter.SerializeIdentifierColor("sample-native:scout-tank");
        if (!colorJson.Contains("\"hex\":\"#") || !colorJson.Contains("\"paletteSize\":16"))
        {
            throw new InvalidOperationException("The identifier color JSON must expose a hex color and palette size.");
        }
    }),
    ("range display policy exposes authored per-kind colors", () =>
    {
        UnityEngine.Color weapon = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Weapon);
        UnityEngine.Color skill = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Skill);
        UnityEngine.Color effect1 = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect1);
        UnityEngine.Color effect2 = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect2);
        UnityEngine.Color sight = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Sight);

        if (Math.Abs(weapon.r - 0.95f) > 0.001f || Math.Abs(weapon.g - 0.18f) > 0.001f ||
            Math.Abs(weapon.b - 0.12f) > 0.001f || Math.Abs(weapon.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The weapon range color must match the authored red value.");
        }

        if (Math.Abs(skill.r - 0.12f) > 0.001f || Math.Abs(skill.g - 0.48f) > 0.001f ||
            Math.Abs(skill.b - 0.95f) > 0.001f || Math.Abs(skill.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The skill range color must match the authored blue value.");
        }

        if (Math.Abs(effect1.r - 0.98f) > 0.001f || Math.Abs(effect1.g - 0.48f) > 0.001f ||
            Math.Abs(effect1.b - 0.08f) > 0.001f || Math.Abs(effect1.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The effect1 range color must match the authored orange value.");
        }

        if (Math.Abs(effect2.r - 0.96f) > 0.001f || Math.Abs(effect2.g - 0.72f) > 0.001f ||
            Math.Abs(effect2.b - 0.18f) > 0.001f || Math.Abs(effect2.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The effect2 range color must match the authored gold value.");
        }

        if (Math.Abs(sight.r - 0.72f) > 0.001f || Math.Abs(sight.g - 0.76f) > 0.001f ||
            Math.Abs(sight.b - 0.82f) > 0.001f || Math.Abs(sight.a - 0.62f) > 0.001f)
        {
            throw new InvalidOperationException("The sight range color must fall back to the authored neutral value.");
        }
    }),
    ("identifier color policy exposes the full palette by index", () =>
    {
        if (IdentifierColorPolicy.PaletteSize != 16)
        {
            throw new InvalidOperationException("The authored palette must expose 16 entries.");
        }

        UnityEngine.Color first = IdentifierColorPolicy.PaletteColorAt(0);
        UnityEngine.Color firstAgain = IdentifierColorPolicy.PaletteColorAt(0);
        if (first != firstAgain)
        {
            throw new InvalidOperationException("Palette lookups must be deterministic.");
        }

        for (int i = 0; i < IdentifierColorPolicy.PaletteSize; i++)
        {
            IdentifierColorPolicy.PaletteColorAt(i);
        }

        bool threwLow = false;
        bool threwHigh = false;
        try
        {
            IdentifierColorPolicy.PaletteColorAt(-1);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            threwLow = true;
        }

        try
        {
            IdentifierColorPolicy.PaletteColorAt(IdentifierColorPolicy.PaletteSize);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            threwHigh = true;
        }

        if (!threwLow || !threwHigh)
        {
            throw new InvalidOperationException("Palette lookups must reject out-of-range indices.");
        }
    }),
    ("agent diagnostics formatter exposes range colors and the full palette as JSON", () =>
    {
        string rangeColorsJson = AgentDiagnosticsFormatter.SerializeRangeColors();
        if (!rangeColorsJson.Contains("\"kind\":\"rangeColors\"") ||
            !rangeColorsJson.Contains("\"weapon\":\"#") ||
            !rangeColorsJson.Contains("\"skill\":\"#") ||
            !rangeColorsJson.Contains("\"effect1\":\"#") ||
            !rangeColorsJson.Contains("\"effect2\":\"#") ||
            !rangeColorsJson.Contains("\"sight\":\"#"))
        {
            throw new InvalidOperationException("The range colors JSON must expose a hex color per range kind.");
        }

        string paletteJson = AgentDiagnosticsFormatter.SerializeIdentifierPalette();
        if (!paletteJson.Contains("\"kind\":\"identifierPalette\"") ||
            !paletteJson.Contains("\"paletteSize\":16") ||
            !paletteJson.Contains("\"colors\":["))
        {
            throw new InvalidOperationException("The palette JSON must expose the palette size and the color list.");
        }

        var overlayState = new OverlayStateView(
            true, false, "F8", 2, true, false, false, "alpha", "1.19.0", string.Empty, true, "ok");
        var rangeState = new RangeStateView(
            true, true, "F11", 3, true, false, true, false, 0.08f, 64, 0.15f);
        string utilitiesJson = AgentDiagnosticsFormatter.SerializeUtilities(null, overlayState, rangeState);
        if (!utilitiesJson.Contains("\"rangeColors\"") ||
            !utilitiesJson.Contains("\"identifierPalette\"") ||
            !utilitiesJson.Contains("\"selectedTab\":2") ||
            !utilitiesJson.Contains("\"guiDisabled\":false") ||
            !utilitiesJson.Contains("\"showWeaponRange\":true") ||
            !utilitiesJson.Contains("\"lineWidth\":0.08") ||
            !utilitiesJson.Contains("\"segments\":64") ||
            !utilitiesJson.Contains("\"heightOffset\":0.15") ||
            !utilitiesJson.Contains("\"pluginVersion\":\"1.19.0\"") ||
            !utilitiesJson.Contains("\"patchResultSuccess\":true"))
        {
            throw new InvalidOperationException("The utilities JSON must expose the extended overlay and range display fields.");
        }
    }),
    ("agent diagnostics formatter returns an available=false snapshot when provider is null", () =>
    {
        string snapshotJson = AgentDiagnosticsFormatter.SerializeSnapshot(null);
        if (!snapshotJson.Contains("\"available\":false"))
        {
            throw new InvalidOperationException("A null snapshot must serialize as available=false, not throw.");
        }
    }),
    ("range display resolves selected player radii in world units", () =>
    {
        RangeDisplayEntityState entity = new RangeDisplayEntityState(
            isSelected: true, isPlayerControlled: true, isDestroyed: false,
            canAttack: true, isMelee: false, weaponRange: 4f,
            hasActiveSkill: true, skillRangeIsUnlimited: false, skillRange: 6f,
            effectRadius1: 2f, effectRadius2: 3f, sightRadius: 8f);
        RangeDisplaySpec[] specs = RangeDisplayPolicy.Resolve(entity, RangeDisplayOptions.Default);
        RangeDisplaySpec[] disabled = RangeDisplayPolicy.Resolve(entity, RangeDisplayOptions.Default, enabled: false);
        if (disabled.Length != 0)
        {
            throw new InvalidOperationException("Disabled range display must not resolve circles.");
        }
        RangeDisplayKind[] expectedKinds =
        {
            RangeDisplayKind.Weapon, RangeDisplayKind.Skill,
            RangeDisplayKind.Effect1, RangeDisplayKind.Effect2
        };
        float[] expectedRadii = { 40f, 60f, 20f, 30f };
        if (specs.Length != expectedKinds.Length)
        {
            throw new InvalidOperationException("Range display must resolve each enabled non-sight radius and omit sight by default.");
        }
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].Kind != expectedKinds[i] || Math.Abs(specs[i].Radius - expectedRadii[i]) > 0.001f)
            {
                throw new InvalidOperationException("Range display kinds must be ordered and scaled from cells to world units.");
            }
        }
    }),
    ("range display rejects ineligible, unlimited, melee, tiny, and duplicate radii", () =>
    {
        RangeDisplayEntityState baseEntity = new RangeDisplayEntityState(
            isSelected: true, isPlayerControlled: true, isDestroyed: false,
            canAttack: true, isMelee: false, weaponRange: 0.2f,
            hasActiveSkill: true, skillRangeIsUnlimited: true, skillRange: 10f,
            effectRadius1: 2f, effectRadius2: 2.02f, sightRadius: 4f);
        RangeDisplaySpec[] specs = RangeDisplayPolicy.Resolve(baseEntity, new RangeDisplayOptions(
            showWeapon: true, showSkill: true, showEffects: true, showSight: true));
        if (specs.Length != 2 || specs[0].Kind != RangeDisplayKind.Effect1 ||
            specs[1].Kind != RangeDisplayKind.Sight || Math.Abs(specs[1].Radius - 40f) > 0.001f)
        {
            throw new InvalidOperationException("Range display must filter tiny weapon, unlimited skill, and coincident radii.");
        }

        RangeDisplayEntityState[] ineligible =
        {
            new RangeDisplayEntityState(false, true, false, true, false, 4f, false, false, 0f, 0f, 0f, 0f),
            new RangeDisplayEntityState(true, false, false, true, false, 4f, false, false, 0f, 0f, 0f, 0f),
            new RangeDisplayEntityState(true, true, true, true, false, 4f, false, false, 0f, 0f, 0f, 0f)
        };
        for (int i = 0; i < ineligible.Length; i++)
        {
            if (RangeDisplayPolicy.Resolve(ineligible[i], RangeDisplayOptions.Default).Length != 0)
            {
                throw new InvalidOperationException("Only living, selected, player-controlled entities may receive range circles.");
            }
        }

        RangeDisplayEntityState melee = new RangeDisplayEntityState(
            true, true, false, true, true, 8f, false, false, 0f, 0f, 0f, 0f);
        if (RangeDisplayPolicy.Resolve(melee, RangeDisplayOptions.Default).Length != 0)
        {
            throw new InvalidOperationException("Melee entities must not receive a weapon-range circle.");
        }
    }),
    ("range display selection tracker reports deterministic additions and removals", () =>
    {
        var tracker = new RangeDisplaySelectionTracker<int>();
        RangeDisplaySelectionDelta<int> first = tracker.Update(new[] { 3, 1, 2 });
        if (!first.Added.SequenceEqual(new[] { 1, 2, 3 }) || first.Removed.Length != 0)
        {
            throw new InvalidOperationException("A new selection snapshot must report sorted additions and no removals.");
        }

        RangeDisplaySelectionDelta<int> second = tracker.Update(new[] { 2, 4 });
        if (!second.Added.SequenceEqual(new[] { 4 }) || !second.Removed.SequenceEqual(new[] { 1, 3 }))
        {
            throw new InvalidOperationException("Selection reconciliation must report only the changed entities in stable order.");
        }

        RangeDisplaySelectionDelta<int> cleared = tracker.Update(Array.Empty<int>());
        if (cleared.Added.Length != 0 || !cleared.Removed.SequenceEqual(new[] { 2, 4 }))
        {
            throw new InvalidOperationException("Clearing a selection snapshot must release every tracked entity.");
        }
    }),
    ("range display circle geometry closes at the requested radius", () =>
    {
        const float radius = 40f;
        const int segments = 32;
        const float heightOffset = 0.15f;
        UnityEngine.Vector3[] points = RangeDisplayGeometry.CreateCirclePoints(radius, segments, heightOffset);
        if (points.Length != segments + 1)
        {
            throw new InvalidOperationException("Circle geometry must include one closing vertex beyond its segment count.");
        }

        UnityEngine.Vector3 first = points[0];
        UnityEngine.Vector3 last = points[points.Length - 1];
        if (Math.Abs(first.x - last.x) > 0.001f || Math.Abs(first.z - last.z) > 0.001f ||
            Math.Abs(first.y - heightOffset) > 0.001f)
        {
            throw new InvalidOperationException("The generated range circle must close and preserve its height offset.");
        }

        for (int i = 0; i < segments; i++)
        {
            float pointRadius = (float)Math.Sqrt(points[i].x * points[i].x + points[i].z * points[i].z);
            if (Math.Abs(pointRadius - radius) > 0.001f || Math.Abs(points[i].y - heightOffset) > 0.001f)
            {
                throw new InvalidOperationException("Every range circle vertex must lie on the requested XZ radius and height.");
            }
        }

        foreach (Action invalidGeometry in new Action[]
        {
            () => RangeDisplayGeometry.CreateCirclePoints(0f, segments, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(float.NaN, segments, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, 2, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, 257, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, segments, float.PositiveInfinity)
        })
        {
            bool rejected = false;
            try
            {
                invalidGeometry();
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Circle geometry must reject invalid radius, segment, and height inputs.");
            }
        }
    }),
    ("visual overlays correct only models authored facing backward", () =>
    {
        float backwardCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(-2f, 1f);
        float forwardCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(2f, -1f);
        float ambiguousCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(0.01f, 0f);
        if (Math.Abs(backwardCorrection - 180f) > 0.001f ||
            Math.Abs(forwardCorrection) > 0.001f ||
            Math.Abs(ambiguousCorrection) > 0.001f)
        {
            throw new InvalidOperationException("Overlay orientation must flip only when its muzzle lies clearly behind the rear anchor.");
        }
    }),
    ("visual overlay scale calibration fits both undersized and oversized models", () =>
    {
        float combinedExtent = VisualOverlayScaleCalibration.CalculateCombinedHalfExtent(new[]
        {
            new VisualRendererBounds(0f, 0f, 0f, 2f, 4f, 4f),
            new VisualRendererBounds(3f, 0f, 0f, 2f, 2f, 2f)
        });
        if (Math.Abs(combinedExtent - 2.5f) > 0.001f)
        {
            throw new InvalidOperationException("Overlay calibration must aggregate renderer bounds before choosing a uniform extent.");
        }

        if (Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(5f, false) - 4f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, false) - 0.85f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.NaN, true) - 1.60f) > 0.001f)
        {
            throw new InvalidOperationException("Native, unit-fallback, and building-fallback extents must be selected deterministically.");
        }

        float undersizedScale = VisualOverlayScaleCalibration.CalculateUniformScale(0.25f, 0.85f);
        float oversizedScale = VisualOverlayScaleCalibration.CalculateUniformScale(6.0f, 0.85f);
        float matchingScale = VisualOverlayScaleCalibration.CalculateUniformScale(0.85f, 0.85f);
        if (Math.Abs(undersizedScale - 3.4f) > 0.001f ||
            Math.Abs(oversizedScale - (0.85f / 6.0f)) > 0.001f ||
            Math.Abs(matchingScale - 1.0f) > 0.001f)
        {
            throw new InvalidOperationException(
                "Visual scale must match measured renderer extents without the current 2.5x cap.");
        }

        // Scale clamp limits.
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(0.0011f, 100f) - 100f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(100f, 1f) - 0.01f) > 0.001f)
        {
            throw new InvalidOperationException("Visual scale calibration must honor its 0.01 and 100 safety limits.");
        }

        // Invalid target extents (NaN, infinity, zero, negative) must be rejected.
        foreach (float invalidTarget in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(1f, invalidTarget);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Visual scale calibration must reject invalid target extents.");
            }
        }

        // Invalid model extents (NaN, infinity, negative) must be rejected.
        foreach (float invalidModel in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(invalidModel, 0.85f);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Visual scale calibration must reject invalid model extents.");
            }
        }

        // Building vs unit fallback selection.
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, true) - 1.60f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, false) - 0.85f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.NaN, true) - 1.60f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.PositiveInfinity, false) - 0.85f) > 0.001f)
        {
            throw new InvalidOperationException("Native, unit-fallback, and building-fallback extents must be selected deterministically.");
        }
        foreach (float invalidExtent in new[] { 0f, 0.001f })
        {
            bool rejectsInvalidExtent = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(invalidExtent, 0.85f);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectsInvalidExtent = true;
            }

            if (!rejectsInvalidExtent)
            {
                throw new InvalidOperationException("Visual scale calibration must reject zero and degenerate renderer extents.");
            }
        }
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(0.0011f, 0.85f) - 100f) > 0.001f)
        {
            throw new InvalidOperationException("Visual scale calibration must keep valid tiny extents within its safety clamp.");
        }

        if (VisualOverlayScaleCalibration.IsUsableRendererExtent(0f) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(0.001f) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(float.NaN) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(float.PositiveInfinity) ||
            !VisualOverlayScaleCalibration.IsUsableRendererExtent(0.0011f))
        {
            throw new InvalidOperationException("Renderer extents must reject empty bounds and accept measurable geometry.");
        }

        if (!VisualOverlayScaleCalibration.IsExcludedRendererObjectName("Armor_COLLIDER_Hull") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererObjectName("AntennaMast") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererObjectName("Socket_MuzzlePrimary") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererHierarchy(new[] { "Vehicle", "Socket_MuzzlePrimary", "Mesh" }) ||
            VisualOverlayScaleCalibration.IsExcludedRendererObjectName("HullArmor") ||
            VisualOverlayScaleCalibration.IsExcludedRendererHierarchy(new[] { "Vehicle", "HullArmor" }))
        {
            throw new InvalidOperationException("Renderer-bound calibration must exclude helper geometry anywhere in the hierarchy.");
        }
    }),
    ("visual socket discovery accepts canonical IDs and exact legacy suffixes only", () =>
    {
        if (!RogueCommandSocketCatalog.TryResolveCanonicalSocketName("Socket_TurretPivot", out string canonical) ||
            canonical != "Socket_TurretPivot" ||
            !RogueCommandSocketCatalog.TryResolveCanonicalSocketName(
                "Sandlance_Socket_Hardpoint_ERA", out string legacy) ||
            legacy != "Socket_Hardpoint_ERA")
        {
            throw new InvalidOperationException("A canonical or exact legacy socket name was not resolved.");
        }

        string[] unsupportedNames =
        {
            "Unit_Socket_TurretPivotExtra",
            "Socket_TurretPivot_Mesh",
            "Sensor_Eye",
            "VehicleSocket_RadarMast",
            "_Socket_TurretPivot"
        };
        foreach (string unsupportedName in unsupportedNames)
        {
            if (RogueCommandSocketCatalog.TryResolveCanonicalSocketName(unsupportedName, out _))
            {
                throw new InvalidOperationException("A noncanonical socket-like name was accepted: " + unsupportedName);
            }
        }
    }),
    ("visual socket roles disable ambiguous duplicate aliases", () =>
    {
        object canonical = new object();
        var oneRole = new UniqueSocketRoleBinding<object>();
        oneRole.Add(canonical);
        oneRole.Add(canonical);
        if (oneRole.IsAmbiguous || !ReferenceEquals(oneRole.Value, canonical))
        {
            throw new InvalidOperationException("Repeated registration of one transform should preserve its unique role.");
        }

        oneRole.Add(new object());
        if (!oneRole.IsAmbiguous || oneRole.Value != null)
        {
            throw new InvalidOperationException("Distinct aliases for one behavior should disable the ambiguous role.");
        }

        oneRole.Add(new object());
        if (!oneRole.IsAmbiguous || oneRole.Value != null)
        {
            throw new InvalidOperationException("An ambiguous role should remain disabled after later candidates arrive.");
        }
    }),
    ("ambiguous attachment group does not fall back to its physical socket", () =>
    {
        object socket = new object();
        object groupVisual = new object();
        var ambiguousGroup = new UniqueSocketRoleBinding<object>();
        ambiguousGroup.Add(new object());
        ambiguousGroup.Add(new object());

        if (UniqueSocketRoleBinding<object>.TryResolveVisual(ambiguousGroup, null!, socket, out _))
        {
            throw new InvalidOperationException("An ambiguous visual group fell back to its physical socket.");
        }

        var uniqueGroup = new UniqueSocketRoleBinding<object>();
        uniqueGroup.Add(groupVisual);
        if (!UniqueSocketRoleBinding<object>.TryResolveVisual(uniqueGroup, groupVisual, socket, out object? resolved) ||
            !ReferenceEquals(resolved, groupVisual))
        {
            throw new InvalidOperationException("A unique detached group was not preferred over its physical socket.");
        }

        if (!UniqueSocketRoleBinding<object>.TryResolveVisual(null!, null!, socket, out resolved) ||
            !ReferenceEquals(resolved, socket))
        {
            throw new InvalidOperationException("A missing detached group did not preserve the legacy socket fallback.");
        }
    }),
    ("runtime Harmony patch installer hook names resolve to declared methods", () =>
    {
        string[] hookNames =
        [
            "EntityBalancingStoreInitPostfix",
            "SetScriptableObjectPostfix",
            "CreateStageMapPrefix",
            "CreateStageMapPostfix",
            "StageMapJsonConstructorPostfix",
            "StageMapToJsonPostfix",
            "CreateLevelMapPrefix",
            "CreateLevelMapFinalizer",
            "InstantiateEntityPrefix",
            "InstantiateEntityPostfix",
            "CreateEntityMeshPrefix",
            "AiBehaviourUpdatePostfix",
            "DefaultAiBehaviourInitPostfix",
            "AiBehaviourDestroyPostfix",
            "BlueprintNamePrefix",
            "BlueprintDescriptionPrefix",
            "SkillDescriptionPrefix",
            "LandscapeLocalizationPrefix",
            "LandscapeLocalizationFinalizer",
            "FallbackLandscapeGeneratorsPostfix",
            "OutlyingStartAiBuildingDistancePrefix",
            "BlueprintPoolIsInPoolPrefix",
            "BlueprintPoolFindPrefix",
            "BlueprintPoolExclusiveBlueprintsPostfix",
            "PresentCardsToChoosePrefix",
            "PresentCardsToChooseFinalizer",
            "InstantiateCardsWithDelayPrefix",
            "BlueprintPoolBlueprintsInPoolPostfix",
            "PresentCardAsRewardRecordPostfix",
            "AllEntityIdsAllowedAsBlueprintsPostfix",
            "EntityFactorySetupPrespawningPostfix",
            "EntityFactorySetupAiPrespawningPostfix",
            "EntityAttackIsTargetInRangePostfix",
            "EntityControllerDestroyPostfix",
            "EntityControllerDiePostfix",
            "EntityControllerHealPostfix",
            "EntityControllerRepairArmorPostfix",
            "CreateBuildingPlacementGhostPrefix",
            "CreateBuildingPlacementGhostPostfix",
            "InitEntityControllerPrefix",
            "InitEntityControllerPostfix",
            "EntityControllerResetAndInitForReusePrefix",
            "EntityControllerOnHasBeenInstantiatedForReusePostfix",
            "EntityControllerProducePrefix",
            "EntityControllerProducePostfix",
            "ExistingControllersInitPostfix",
            "ExistingControllersOnEntityCreatedPostfix",
            "ExistingControllersOnEntityWillBeDestroyedPostfix",
            "EntityControllerExecuteCommandPostfix",
            "EntityControllerExecuteNextCommandPostfix",
            "EntityControllerResetCurrentCommandPostfix",
            "EntityControllerDeleteCommandChainPostfix"
        ];

        foreach (string hookName in hookNames)
        {
            if (typeof(RuntimeHarmonyPatches).GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Static) == null)
            {
                throw new InvalidOperationException($"RuntimePatchInstaller expects missing hook '{hookName}'.");
            }
        }

        MethodInfo? destroyedEntityHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "EntityControllerDestroyPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] destroyedEntityParameters = destroyedEntityHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (destroyedEntityHook == null || destroyedEntityHook.ReturnType != typeof(void) ||
            destroyedEntityParameters.Length != 1 || destroyedEntityParameters[0].ParameterType != typeof(object))
        {
            throw new InvalidOperationException("EntityControllerDestroyPostfix has an invalid Harmony signature.");
        }

        MethodInfo? instantiateCardsHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "InstantiateCardsWithDelayPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] instantiateCardsParams = instantiateCardsHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (instantiateCardsHook == null || instantiateCardsHook.ReturnType != typeof(void) ||
            instantiateCardsParams.Length != 1 || instantiateCardsParams[0].ParameterType != typeof(List<string>))
        {
            throw new InvalidOperationException("InstantiateCardsWithDelayPrefix has an invalid Harmony signature.");
        }

        MethodInfo? poolCandidatesHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolBlueprintsInPoolPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] poolCandidateParameters = poolCandidatesHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolCandidatesHook == null || poolCandidatesHook.ReturnType != typeof(void) ||
            poolCandidateParameters.Length != 4 || poolCandidateParameters[0].ParameterType != typeof(object) ||
            poolCandidateParameters[1].ParameterType != typeof(List<string>) ||
            poolCandidateParameters[2].ParameterType != typeof(int?) ||
            poolCandidateParameters[3].ParameterType != typeof(bool))
        {
            throw new InvalidOperationException("Blueprint pool candidate hook has an invalid Harmony signature.");
        }

        MethodInfo? rewardRecordHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "PresentCardAsRewardRecordPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] rewardRecordParameters = rewardRecordHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (rewardRecordHook == null || rewardRecordHook.ReturnType != typeof(void) ||
            rewardRecordParameters.Length != 2 || rewardRecordParameters.Any(parameter =>
                parameter.ParameterType != typeof(string)))
        {
            throw new InvalidOperationException("Blueprint reward record hook has an invalid Harmony signature.");
        }

        MethodInfo? nameHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintNamePrefix",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? descriptionHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintDescriptionPrefix",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? skillDescriptionHook = typeof(RuntimeHarmonyPatches).GetMethod("SkillDescriptionPrefix",
            BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo? hook in new[] { nameHook, descriptionHook, skillDescriptionHook })
        {
            ParameterInfo[] parameters = hook?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (hook == null || hook.ReturnType != typeof(bool) || parameters.Length != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(string).MakeByRefType())
            {
                throw new InvalidOperationException("Blueprint localization hooks have an invalid Harmony signature.");
            }
        }

        MethodInfo? poolEligibilityHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolIsInPoolPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] eligibilityParameters = poolEligibilityHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolEligibilityHook == null || poolEligibilityHook.ReturnType != typeof(bool) ||
            eligibilityParameters.Length != 3 || eligibilityParameters[0].ParameterType != typeof(object) ||
            eligibilityParameters[1].ParameterType != typeof(string) ||
            eligibilityParameters[2].ParameterType != typeof(bool).MakeByRefType())
        {
            throw new InvalidOperationException("BlueprintPool.IsInPool hook has an invalid Harmony signature.");
        }

        MethodInfo? savedMapConstructorHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "StageMapJsonConstructorPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] savedMapConstructorParameters = savedMapConstructorHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (savedMapConstructorHook == null || savedMapConstructorHook.ReturnType != typeof(void) ||
            savedMapConstructorParameters.Length != 2 || savedMapConstructorParameters[0].ParameterType != typeof(object) ||
            savedMapConstructorParameters[1].ParameterType != typeof(string))
        {
            throw new InvalidOperationException("The StageMap JSON constructor hook has an invalid Harmony signature.");
        }

        MethodInfo? savedMapSerializeHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "StageMapToJsonPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] savedMapSerializeParameters = savedMapSerializeHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (savedMapSerializeHook == null || savedMapSerializeHook.ReturnType != typeof(void) ||
            savedMapSerializeParameters.Length != 2 || savedMapSerializeParameters[0].ParameterType != typeof(object) ||
            savedMapSerializeParameters[1].ParameterType != typeof(string).MakeByRefType())
        {
            throw new InvalidOperationException("The StageMap JSON serialization hook has an invalid Harmony signature.");
        }

        MethodInfo? outlyingStartDistanceHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "OutlyingStartAiBuildingDistancePrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] distanceParameters = outlyingStartDistanceHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (outlyingStartDistanceHook == null || outlyingStartDistanceHook.ReturnType != typeof(bool) ||
            distanceParameters.Length != 1 || distanceParameters[0].ParameterType != typeof(int).MakeByRefType())
        {
            throw new InvalidOperationException("The appended-stage AI spacing hook has an invalid Harmony signature.");
        }

        MethodInfo? poolFindHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolFindPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] findParameters = poolFindHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolFindHook == null || poolFindHook.ReturnType != typeof(bool) || findParameters.Length != 2 ||
            findParameters[0].ParameterType != typeof(string) ||
            findParameters[1].ParameterType != typeof(global::BlueprintPool).MakeByRefType())
        {
            throw new InvalidOperationException("BlueprintPool.FindBlueprintPool hook has an invalid Harmony signature.");
        }

        MethodInfo? exclusiveHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolExclusiveBlueprintsPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] exclusiveParameters = exclusiveHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (exclusiveHook == null || exclusiveHook.ReturnType != typeof(void) || exclusiveParameters.Length != 2 ||
            exclusiveParameters[0].ParameterType != typeof(string) ||
            exclusiveParameters[1].ParameterType != typeof(HashSet<string>))
        {
            throw new InvalidOperationException("BlueprintPool.ExclusiveBlueprints hook has an invalid Harmony signature.");
        }

        if (typeof(UnityResourcesLoadTypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static) == null)
        {
            throw new InvalidOperationException("The typed Resources.Load patch prefix is missing.");
        }

        if (typeof(UnityResourcesLoadUntypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static) == null)
        {
            throw new InvalidOperationException("The untyped Resources.Load patch prefix is missing.");
        }
    }),
    ("unit diagnostic report variant count defaults to zero and accepts two", () =>
    {
        var defaultReport = new UnitDiagnosticReport("test:unit", "test:pack", "prefab", "card", false);
        PropertyInfo? count = typeof(UnitDiagnosticReport).GetProperty("VariantCount");
        ConstructorInfo? constructor = typeof(UnitDiagnosticReport).GetConstructors()
            .SingleOrDefault(candidate => candidate.GetParameters().LastOrDefault()?.Name == "variantCount");
        if (count == null || count.PropertyType != typeof(int) || constructor == null ||
            !constructor.GetParameters().Last().IsOptional ||
            count.GetValue(defaultReport) is not int defaultCount || defaultCount != 0)
        {
            throw new InvalidOperationException("VariantCount must be an optional final constructor parameter defaulting to zero.");
        }
        var twoReport = new UnitDiagnosticReport("test:unit", "test:pack", "prefab", "card", false,
            variantCount: 2);
        if (count.GetValue(twoReport) is not int declaredCount || declaredCount != 2)
        {
            throw new InvalidOperationException("The diagnostic report lost its explicitly declared variant count.");
        }
        if (UnitVariantDiagnosticsFormatter.FormatVariantCount(twoReport) !=
            "Variantes declaradas (solo metadatos): 2")
        {
            throw new InvalidOperationException("The unit diagnostics label must expose the metadata-only variant count.");
        }
    }),
    ("runtime entity registration stores MoveSpeed on the row across cache invalidation", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var unit = new ContentUnitDefinition
            {
                id = "sample:move-cache",
                movement = new ContentUnitMovementDefinition { moveSpeed = 12.5f }
            };
            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters row = RuntimeMutatorTestData.Row(unit.id);
            row.moveSpeed = 1f;
            provider.EntityRows.Add(row);
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = unit.id,
                Parameters = UnitBalanceOverrideMapper.CreateCustomBalance(unit)
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("Entity registration failed.");
            }

            EntityBalancingStore.InvalidateCache(unit.id);
            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            EntityBalancingParameters source = (EntityBalancingParameters)provider.EntityRows[0];
            if (registered.moveSpeed != 12.5f || source.moveSpeed != 12.5f ||
                EntityBalancingStore.ChangeableFloatValueCache[unit.id].Count != 0)
            {
                throw new InvalidOperationException(
                    "Structured MoveSpeed must remain in both provider and registered rows after cache invalidation.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration stores MaxHealth on the row across cache invalidation", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            const string entityId = "sample:max-health-cache";
            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters row = RuntimeMutatorTestData.Row(entityId);
            row.maxHealth = 100;
            provider.EntityRows.Add(row);
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = entityId,
                Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["MaxHealth"] = 450d
                }
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("Entity registration failed.");
            }

            EntityBalancingStore.InvalidateCache(entityId);
            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            EntityBalancingParameters source = (EntityBalancingParameters)provider.EntityRows[0];
            if (registered.maxHealth != 450 || source.maxHealth != 450 ||
                EntityBalancingStore.ChangeableIntValueCache[entityId].Count != 0)
            {
                throw new InvalidOperationException(
                    "MaxHealth must remain in both provider and registered rows after cache invalidation.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration rejects integers rounded by the native getter", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            const string entityId = "sample:rounded-max-health";
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row(entityId));
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = entityId,
                Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["MaxHealth"] = 16777217d
                }
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() || EntityBalancingStore.EntityBalancingParametersList.Count != 0)
            {
                throw new InvalidOperationException(
                    "An integer balance override rounded by the native getter must not be registered.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime mutation handles invalid targets and fields", () =>
    {
        var mutationManager = new RuntimeMutationManager(msg => { });

        // Null target
        if (mutationManager.MutateField(null!, "someField", 1.0f))
            throw new InvalidOperationException("Mutation should fail for null target.");

        // Non-existent field
        var target = new object();
        if (mutationManager.MutateField(target, "NonExistentField", 1.0f))
            throw new InvalidOperationException("Mutation should fail for non-existent field.");

        // Wrong type
        var buff = new EntityValueModifier { value = 1.0f };
        if (mutationManager.MutateField(buff, "value", "not a float"))
            throw new InvalidOperationException("Mutation should fail for type mismatch.");

        // AI Behaviour: null manager
        if (mutationManager.MutateActiveAiBehaviour(null!, "tickRate", 0.5f))
            throw new InvalidOperationException("AI mutation should fail for null manager.");

        // AI Behaviour: null activeBehaviour
        var manager = new AiBehaviourManager { activeBehaviour = null! };
        if (mutationManager.MutateActiveAiBehaviour(manager, "tickRate", 0.5f))
            throw new InvalidOperationException("AI mutation should fail for null activeBehaviour.");
    }),
    ("production building visual-source compatibility is explicit and independent", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            ContentProductionBuildingDefinition building = unit.productionBuilding;
            string originalUnitBundleId = unit.assetBundleId;
            string originalUnitCardImageAssetName = unit.cardImageAssetName;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = "FactoryModel";
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (unit.assetBundleId != originalUnitBundleId ||
                unit.cardImageAssetName != originalUnitCardImageAssetName ||
                building.assetBundleId != pack.assetBundles[0].id ||
                building.visualAssetName != "FactoryVisual" ||
                building.visualRootPath != "FactoryModel")
                throw new InvalidOperationException("Factory visual content should not mutate product unit asset fields.");
        });
    }),
    ("game compatibility rejects an unsupported Unity version", () =>
    {
        string previousVersion = UnityEngine.Application.unityVersion;
        try
        {
            UnityEngine.Application.unityVersion = "2022.3.0f1";
            if (GameCompatibility.TryValidate(Path.GetTempPath(), out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("Unsupported Unity version", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An unsupported Unity version was not rejected with a local diagnostic.");
            }
        }
        finally
        {
            UnityEngine.Application.unityVersion = previousVersion;
        }
    }),
    ("game compatibility rejects an unsupported game version marker", () =>
    {
        WithOwnedTemporaryDirectory("rogue-command-version-", root =>
        {
            UnityEngine.Application.unityVersion = GameCompatibility.SupportedUnityVersion;
            File.WriteAllText(Path.Combine(root, "version.txt"), "9= Version 1.0.8\n");
            if (GameCompatibility.TryValidate(root, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("Unsupported Rogue Command version marker", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An unsupported game version marker was not rejected.");
            }
        });
    }),
    ("game compatibility rejects an Assembly-CSharp hash mismatch", () =>
    {
        WithOwnedTemporaryDirectory("rogue-command-hash-", root =>
        {
            UnityEngine.Application.unityVersion = GameCompatibility.SupportedUnityVersion;
            File.WriteAllText(Path.Combine(root, "version.txt"), "9= Version 1.0.9\n");
            string managedDirectory = Path.Combine(root, "Rogue Command_Data", "Managed");
            Directory.CreateDirectory(managedDirectory);
            File.WriteAllBytes(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), [0x4D, 0x5A]);
            if (GameCompatibility.TryValidate(root, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("SHA-256 mismatch", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A changed Assembly-CSharp hash was not rejected.");
            }
        });
    }),
    ("runtime build guard rejects an unsupported Rogue Command version", () =>
    {
        Assembly fakeGameAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Assembly-CSharp"), AssemblyBuilderAccess.Run);
        if (RuntimeBuildGuard.TryValidate(Path.GetTempPath(), "1.0.8", fakeGameAssembly,
            out string diagnostic) || diagnostic == null ||
            !diagnostic.Contains("Expected Rogue Command 1.0.9", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RuntimeBuildGuard did not fail closed on a mismatched game version.");
        }
    }),
    ("runtime build guard rejects a mismatched Assembly-CSharp MVID", () =>
    {
        Assembly fakeGameAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Assembly-CSharp"), AssemblyBuilderAccess.Run);
        if (RuntimeBuildGuard.TryValidate(Path.GetTempPath(), RuntimeBuildGuard.ExpectedGameVersion,
            fakeGameAssembly, out string diagnostic) || diagnostic == null ||
            !diagnostic.Contains("MVID does not match", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RuntimeBuildGuard did not reject an unverified Assembly-CSharp MVID.");
        }
    }),
    ("runtime entity mutator appends balancing rows indices factory mappings and stat caches idempotently", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows() || !mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("The runtime mutator did not accept a valid entity registration batch.");
            }

            if (EntityBalancingStore.EntityBalancingParametersList.Count != 2 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 2 ||
                EntityBalancingStore.ParameterListIndexOf["sample:product"] != 0 ||
                EntityBalancingStore.ParameterListIndexOf["sample:factory"] != 1 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 1 ||
                EntityBalancingStore.FactoryEntityIdOf["sample:product"] != "sample:factory" ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 2 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 2 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:factory"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:factory"].Count != 0 ||
                provider.EntityRowsRegisteredCount != 1)
            {
                throw new InvalidOperationException("The runtime mutator left inconsistent registry rows, indices, factory mappings, or stat caches.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration invalidates cached image misses for new unit and factory IDs", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.ImageOf.Add("sample:product", null!);
            var cachedFactorySprite = new UnityEngine.Sprite();
            EntityBalancingStore.ImageOf.Add("sample:factory", cachedFactorySprite);
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (!registered || EntityBalancingStore.ImageOf.ContainsKey("sample:product") ||
                !EntityBalancingStore.ImageOf.ContainsKey("sample:factory") ||
                !ReferenceEquals(EntityBalancingStore.ImageOf["sample:factory"], cachedFactorySprite))
            {
                throw new InvalidOperationException("Entity registration cache state mismatch. registered=" + registered +
                    ", imageCount=" + EntityBalancingStore.ImageOf.Count +
                    ", product=" + EntityBalancingStore.ImageOf.ContainsKey("sample:product") +
                    ", factory=" + EntityBalancingStore.ImageOf.ContainsKey("sample:factory") +
                    ", sameSprite=" + (EntityBalancingStore.ImageOf.ContainsKey("sample:factory") &&
                        ReferenceEquals(EntityBalancingStore.ImageOf["sample:factory"], cachedFactorySprite)));
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rejects an ID collision without partially changing the registry", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.EntityBalancingParametersList.Add(RuntimeMutatorTestData.Row("sample:existing"));
            EntityBalancingStore.ParameterListIndexOf.Add("sample:existing", 0);
            EntityBalancingStore.ChangeableIntValueCache.Add("sample:existing",
                new Dictionary<EntityBalancingStore.ChangeableValue, int>());
            EntityBalancingStore.ChangeableFloatValueCache.Add("sample:existing",
                new Dictionary<EntityBalancingStore.ChangeableValue, float>());

            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:new"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:existing"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 1 ||
                EntityBalancingStore.EntityBalancingParametersList[0].entityId != "sample:existing" ||
                EntityBalancingStore.ParameterListIndexOf.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf["sample:existing"] != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 1 ||
                !EntityBalancingStore.ChangeableIntValueCache.ContainsKey("sample:existing") ||
                !EntityBalancingStore.ChangeableFloatValueCache.ContainsKey("sample:existing") ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A duplicate entity ID changed existing registry state or partially appended its batch.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rolls back rows and value caches after index insertion fails", () =>
    {
        var comparer = new ThrowOnFirstTargetHashComparer("sample:product");
        EntityBalancingStore.Reset(comparer);
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (registered || !comparer.FailureTriggered ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 0 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 0 ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A failed index commit did not roll back all appended registry state: " +
                    "registered=" + registered +
                    ", injectedFailure=" + comparer.FailureTriggered +
                    ", targetHashCalls=" + comparer.TargetHashCalls +
                    ", comparerAttached=" + ReferenceEquals(EntityBalancingStore.ParameterListIndexOf.Comparer, comparer) +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count +
                    ", indices=" + EntityBalancingStore.ParameterListIndexOf.Count +
                    ", factoryMappings=" + EntityBalancingStore.FactoryEntityIdOf.Count +
                    ", intCache=" + EntityBalancingStore.ChangeableIntValueCache.Count +
                    ", floatCache=" + EntityBalancingStore.ChangeableFloatValueCache.Count +
                    ", notifications=" + provider.EntityRowsRegisteredCount + ".");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator preserves rows index and both caches when cache rollback fails", () =>
    {
        var indexComparer = new ThrowOnFirstTargetHashComparer("sample:product");
        var cacheComparer = new ThrowOnTargetHashCallComparer("sample:product", 3);
        EntityBalancingStore.Reset(indexComparer);
        EntityBalancingStore.ChangeableFloatValueCache =
            new Dictionary<string, Dictionary<EntityBalancingStore.ChangeableValue, float>>(cacheComparer);
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (registered || !cacheComparer.FailureTriggered ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf["sample:product"] != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:product"].Count != 0 ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("The mutator did not preserve a consistent unit row, index, and cache pair after cleanup failed: " +
                    "registered=" + registered +
                    ", injectedFailure=" + cacheComparer.FailureTriggered +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count +
                    ", indices=" + EntityBalancingStore.ParameterListIndexOf.Count +
                    ", intCache=" + EntityBalancingStore.ChangeableIntValueCache.Count +
                    ", floatCache=" + EntityBalancingStore.ChangeableFloatValueCache.Count + ".");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rejects a missing factory product before registry changes", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:missing-product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() || EntityBalancingStore.EntityBalancingParametersList.Count != 0 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 || provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A factory with an unresolved product changed the game registry.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("loader diagnostics snapshot preserves its pre-extension constructor", () =>
    {
        var snapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 0, 0, 0,
            Array.Empty<PackDiagnosticReport>(),
            Array.Empty<UnitDiagnosticReport>(),
            Array.Empty<StageDiagnosticReport>(),
            Array.Empty<AiProfileDiagnosticReport>(),
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            Array.Empty<string>());

        if (snapshot.UnitEligibilities.Count != 0 || snapshot.UnitAssetResolutions.Count != 0)
        {
            throw new InvalidOperationException("The legacy snapshot constructor must initialize new diagnostic lists as empty.");
        }
    }),
    ("loader unit diagnostic builder reports eligibility and asset resolution", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];
        var resolvedResources = new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal);
        var visualRequests = new HashSet<string>(new[] { "test:unit" }, StringComparer.Ordinal);
        var resolvedVisuals = new HashSet<string>(new[] { "test:unit" }, StringComparer.Ordinal);

        IReadOnlyList<UnitEligibilityDiagnosticReport> eligibility =
            LoaderUnitDiagnosticsBuilder.CreateEligibilityReports(units);
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> assets =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units, resolvedResources, visualRequests, resolvedVisuals);

        string eligibilityLine = LoaderDiagnosticsFormatter.FormatUnitEligibility(eligibility[0]);
        if (eligibility.Count != 1 || eligibility[0].IsAllowedAsBlueprint ||
            !eligibility[0].IsAllowedAsStartingBlueprint || eligibility[0].IsAllowedForAi ||
            !eligibility[0].IsAllowedForDemo ||
            eligibilityLine != "test:unit [Blueprint=False, StartingBlueprint=True, AI=False, Demo=True, Specialist=False]" ||
            assets.Count != 1 || !assets[0].PrefabResolved || !assets[0].CardImageResolved ||
            !assets[0].VisualResolved)
        {
            throw new InvalidOperationException("Loader diagnostics did not preserve unit eligibility and resolved asset state.");
        }

        IReadOnlyList<UnitAssetResolutionDiagnosticReport> missing =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units, new HashSet<string>(StringComparer.Ordinal), visualRequests,
                new HashSet<string>(StringComparer.Ordinal));
        if (missing[0].PrefabResolved || missing[0].CardImageResolved || missing[0].VisualResolved)
        {
            throw new InvalidOperationException("Unresolved unit resources must be reported as missing.");
        }
    }),
    ("unit asset diagnostics distinguish missing, unresolved, and resolved requests", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit-visual", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];

        IReadOnlyList<UnitAssetResolutionDiagnosticReport> missingVisual =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> unverifiedNativeCard =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab" }, StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> resolvedVisual =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal));

        if (LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(missingVisual[0]) !=
                UnitAssetResolutionSeverity.Error ||
            LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(unverifiedNativeCard[0]) !=
                UnitAssetResolutionSeverity.Warning ||
            LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(resolvedVisual[0]) !=
                UnitAssetResolutionSeverity.Success)
        {
            throw new InvalidOperationException("Asset diagnostics must preserve error, warning, and success severity.");
        }
    }),
    ("cross-pack graph cycle diagnostics exclude external predecessors", () =>
    {
        var packs = new[]
        {
            new ContentPackDefinition
            {
                packId = "test:alpha-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:alpha-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:beta-unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:beta-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:beta-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:alpha-unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:gamma-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:gamma-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:alpha-unit" } }
                    }
                }
            }
        };

        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (issues.Any(issue => issue.PackId == "test:gamma-pack") ||
            !issues.Any(issue => issue.PackId == "test:alpha-pack") ||
            !issues.Any(issue => issue.PackId == "test:beta-pack"))
        {
            throw new InvalidOperationException("Cycle diagnostics must include cycle owners, not external predecessors.");
        }
    }),
    ("prespawn quota binds production factory ID and preserves anti-hitch quotas", () =>
    {
        PrespawnQuota quota = new PrespawnQuota("sample:unit-scout", 20, false, "sample:building-scout-factory");
        if (quota.EntityId != "sample:unit-scout" ||
            quota.Quota != 20 ||
            quota.IsBuilding != false ||
            quota.FactoryId != "sample:building-scout-factory")
        {
            throw new InvalidOperationException("PrespawnQuota does not preserve entity ID, quota, building flag, or factory ID.");
        }

        PrespawnQuota standaloneQuota = new PrespawnQuota("sample:building-turret", 5, true);
        if (standaloneQuota.EntityId != "sample:building-turret" ||
            standaloneQuota.Quota != 5 ||
            standaloneQuota.IsBuilding != true ||
            standaloneQuota.FactoryId != null)
        {
            throw new InvalidOperationException("Standalone PrespawnQuota does not default factory ID to null.");
        }
    }),
    ("reused entity clears runtime mechanics when its next profile has no component mechanics", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-source", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-source",
                ShieldRegen = new ContentUnitShieldRegenDefinition
                {
                    rechargeRate = 10f,
                    rechargeDelay = 0f
                }
            });
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-descriptive-only", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-descriptive-only",
                MinAttackRange = 4f
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-source",
                UniqueEntityId = 401,
                HasShield = true,
                CurrentShield = 0f,
                MaxShield = 50f
            };

            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            RuntimeUnitMechanicsDirector director = reused.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()
                ?? throw new InvalidOperationException("The source shield-regeneration profile did not attach its director.");

            reused.entityId = "test:reuse-descriptive-only";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            UnityEngine.Time.time = 11f;
            director.TickMechanics();
            if (reused.CurrentShield != 0f)
            {
                throw new InvalidOperationException("A reused entity with a descriptive-only profile retained the previous shield-regeneration mechanic.");
            }

            reused.entityId = "test:reuse-source";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            UnityEngine.Time.time = 12f;
            director.TickMechanics();
            if (reused.CurrentShield != 0f)
            {
                throw new InvalidOperationException("A reused entity without a profile retained the previous shield-regeneration mechanic.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("reused entity releases baseline status effects when its next profile has no mechanics", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-baseline", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-baseline",
                BaselineStatusEffects = new[] { "Armored" }
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-baseline",
                UniqueEntityId = 811,
                IsUnit = true
            };
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            if (!reused.AppliedStatusEffects.Any(effect => effect.Effect == StatusEffect.Armored && effect.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("The baseline status effect was not initially applied.");
            }

            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);

            if (reused.HasActiveStatusEffect(StatusEffect.Armored))
            {
                throw new InvalidOperationException("A baseline status effect from the old profile remained after entity reuse.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("reused entity releases command followers when its next profile has no mechanics", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-commander", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-commander",
                Command = new ContentUnitCommandDefinition
                {
                    commandType = "Attack",
                    commandRange = 4f,
                    maxCommandChain = 1
                }
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-commander",
                UniqueEntityId = 812,
                IsUnit = true,
                IsControlledByPlayer = true,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var follower = new EntityController
            {
                entityId = "test:reuse-follower",
                UniqueEntityId = 813,
                IsUnit = true,
                IsControlledByPlayer = true,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };
            var otherCaptain = new EntityController { IsUnit = true, IsControlledByPlayer = true };
            var occupied = new EntityController
            {
                IsUnit = true, IsControlledByPlayer = true,
                CaptainOfThisEntity = otherCaptain,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(occupied);
            ExistingControllers.Instance.PlayerUnits().Add(reused);
            ExistingControllers.Instance.PlayerUnits().Add(follower);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            RuntimeUnitMechanicsDirector director = reused.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()
                ?? throw new InvalidOperationException("The commander profile did not attach its director.");
            UnityEngine.Time.time = 10.6f;
            director.TickMechanics();
            if (!ReferenceEquals(follower.CaptainOfThisEntity, reused))
            {
                throw new InvalidOperationException("The command pulse did not attach its eligible follower.");
            }

            if (!ReferenceEquals(occupied.CaptainOfThisEntity, otherCaptain) || occupied.AppliedSpecificValueChanges.Count != 0)
                throw new InvalidOperationException("Command pulse affected a follower owned by another captain.");
            follower.Position = new UnityEngine.Vector3(100f, 0f, 0f);
            UnityEngine.Time.time += 1.1f;
            director.TickMechanics();
            if (follower.CaptainOfThisEntity != null)
                throw new InvalidOperationException("Command follower remained linked outside command range.");
            follower.Position = new UnityEngine.Vector3(5f, 0f, 0f);
            UnityEngine.Time.time += 1.1f;
            director.TickMechanics();
            if (!ReferenceEquals(follower.CaptainOfThisEntity, reused))
                throw new InvalidOperationException("Returning follower could not rejoin the command group.");

            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            if (follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("A command follower remained attached to a commander after entity reuse.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("unit statusEffects apply timed native statuses on attachment", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:timed-status", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:timed-status",
                StatusEffects = new[]
                {
                    new ContentUnitStatusEffectDefinition
                    {
                        id = "test:timed-status-effect",
                        effectType = "Stun",
                        duration = 2.5f,
                        magnitude = 1f
                    }
                }
            });

            var unit = new EntityController
            {
                entityId = "test:timed-status",
                UniqueEntityId = 814,
                IsUnit = true
            };
            RuntimeUnitMechanicsDirector.AttachIfApplicable(unit);

            bool applied = unit.AppliedStatusEffects.Any(effect =>
                effect.Effect == StatusEffect.Stun &&
                effect.DurationType == SetStatusEffect.DurationType.Seconds &&
                System.Math.Abs(effect.Duration - 2.5f) < 0.001f);
            if (!applied)
            {
                throw new InvalidOperationException("A declared timed unit status effect was not applied through the native status path.");
            }

            unit.entityId = "test:timed-status-cleared";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(unit);
            if (unit.HasActiveStatusEffect(StatusEffect.Stun))
            {
                throw new InvalidOperationException("A timed unit status effect leaked when the pooled controller changed profile.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("unit aiBehavior caps and skill intervals use bounded per-unit policy", () =>
    {
        if (RuntimeUnitAiBehaviorPolicy.AllowsProduction(2, 0, true, 2, 0) ||
            RuntimeUnitAiBehaviorPolicy.AllowsProduction(1, 1, true, 2, 0) ||
            !RuntimeUnitAiBehaviorPolicy.AllowsProduction(2, 0, false, 3, 1) ||
            RuntimeUnitAiBehaviorPolicy.AllowsProduction(1, 0, true, 0, 1))
        {
            throw new InvalidOperationException("AI unit caps must count living plus queued products and apply combat caps only to combat units.");
        }

        if (RuntimeUnitAiBehaviorPolicy.ResolveSkillActivationInterval(0f, 1.5f) != 1.5f ||
            RuntimeUnitAiBehaviorPolicy.ResolveSkillActivationInterval(2.0f, 1.5f) != 2.0f ||
            RuntimeUnitAiBehaviorPolicy.ShouldRunAiSkillCheck(0.24f, 0f, 0f) ||
            !RuntimeUnitAiBehaviorPolicy.ShouldRunAiSkillCheck(0.25f, 0f, 0f) ||
            RuntimeUnitAiBehaviorPolicy.ShouldRunAiSkillCheck(0.99f, 0f, 1.0f) ||
            !RuntimeUnitAiBehaviorPolicy.ShouldRunAiSkillCheck(1.0f, 0f, 1.0f) ||
            !RuntimeUnitAiBehaviorPolicy.ShouldForceNativeAiSkillCheck(0.10f, 0f, 0.10f) ||
            RuntimeUnitAiBehaviorPolicy.ShouldForceNativeAiSkillCheck(0.25f, 0f, 0.10f) ||
            !RuntimeUnitAiBehaviorPolicy.ShouldDelayNativeAiSkillCheck(1.0f, 1.0f, 1.0f) ||
            RuntimeUnitAiBehaviorPolicy.DelayedNativeAiSkillCheckTimestamp(1.0f, 1.0f) != 1.75f)
        {
            throw new InvalidOperationException("Skill activation intervals must preserve the native default and honor a valid per-unit interval.");
        }
        var capUnitId = "test:cap-unit";
        float originalTime = Time.time;
        ExistingControllers.Instance.Reset();
        EntityBalancingStore.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            EntityBalancingStore.RolesOf[capUnitId] = UnitRole.FrontalAttacker;
            RuntimeUnitMechanicsDirector.RegisterProfile(capUnitId, new UnitRuntimeMechanicsProfile
            {
                EntityId = capUnitId,
                AiBehavior = new ContentUnitAiBehaviorDefinition { unitCap = 2, combatUnitCap = 2 }
            });

            var liveUnit = new EntityController { entityId = capUnitId, IsUnit = true, IsControlledByAi = true };
            var productionInfo = new ProductionInfo { entityId = capUnitId, queuedCount = 1 };
            var producer = new EntityController
            {
                entityId = "native:factory",
                IsBuilding = true,
                IsFactory = true,
                IsControlledByAi = true,
                HideProductionInfoProperty = true,
                ProductionInfo = productionInfo
            };
            ExistingControllers.Instance.AiUnits().Add(liveUnit);
            ExistingControllers.Instance.AiBuildings().Add(producer);

            if (!RuntimeUnitMechanicsDirector.ShouldBlockAiProduction(producer))
            {
                throw new InvalidOperationException("The runtime AI cap did not count a living unit and queued product together.");
            }

            producer.IsControlledByAi = false;
            if (RuntimeUnitMechanicsDirector.ShouldBlockAiProduction(producer))
            {
                throw new InvalidOperationException("The AI production cap leaked into a player-controlled producer.");
            }
            producer.IsControlledByAi = true;
            productionInfo.queuedCount = 0;
            if (RuntimeUnitMechanicsDirector.ShouldBlockAiProduction(producer))
            {
                throw new InvalidOperationException("The runtime AI cap blocked production below the effective combat cap.");
            }

            RuntimeUnitMechanicsDirector.RegisterProfile(capUnitId, new UnitRuntimeMechanicsProfile
            {
                EntityId = capUnitId,
                AiBehavior = new ContentUnitAiBehaviorDefinition { skillActivationCheckInterval = 1.0f }
            });
            var intervalUnit = new EntityController { entityId = capUnitId };
            Time.time = 0f;
            RuntimeUnitMechanicsDirector.ResetNativeAiSkillActivationInterval(intervalUnit);
            Time.time = 0.5f;
            RuntimeUnitMechanicsDirector.ApplyNativeAiSkillActivationInterval(intervalUnit, false);
            if (Math.Abs(intervalUnit.LastAiSkillActivationCheckTimeForTest - 0.75f) > 0.001f)
            {
                throw new InvalidOperationException("The pooled unit interval reset did not preserve the logical native check timestamp.");
            }

            Time.time = 1.5f;
            RuntimeUnitMechanicsDirector.ApplyNativeAiSkillActivationInterval(intervalUnit, false);
            if (Math.Abs(intervalUnit.LastAiSkillActivationCheckTimeForTest - 1.25f) > 0.001f)
            {
                throw new InvalidOperationException("A due per-unit skill check was incorrectly delayed by the prefix.");
            }

            intervalUnit.LastAiSkillActivationCheckTimeForTest = Time.time;
            RuntimeUnitMechanicsDirector.ApplyNativeAiSkillActivationInterval(intervalUnit, true);
            if (Math.Abs(intervalUnit.LastAiSkillActivationCheckTimeForTest - 2.25f) > 0.001f)
            {
                throw new InvalidOperationException("The native skill check timestamp was not shifted by the declared interval.");
            }
        }
        finally
        {
            Time.time = originalTime;
            RuntimeUnitMechanicsDirector.ClearProfiles();
            EntityBalancingStore.Reset();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("unit aiBehavior aiTag reapplies after pooled controller reuse", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:ai-tag-unit", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:ai-tag-unit",
                AiBehavior = new ContentUnitAiBehaviorDefinition { aiTag = "RCL_TestHarvester" }
            });

            var unit = new EntityController
            {
                entityId = "test:ai-tag-unit",
                UniqueEntityId = 815,
                IsUnit = true,
                IsControlledByAi = true
            };

            RuntimeUnitMechanicsDirector.AttachIfApplicable(unit);
            if (!unit.AiTags.Contains("RCL_TestHarvester"))
            {
                throw new InvalidOperationException("The declared unit AI tag was not applied through EntityController.AddAiTag.");
            }

            unit.RemoveAllAiTags();
            RuntimeUnitMechanicsDirector.AttachIfApplicable(unit);
            if (!unit.AiTags.Contains("RCL_TestHarvester"))
            {
                throw new InvalidOperationException("The declared unit AI tag was not reapplied after pooled controller reuse.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("prepare runtime keeps registered profiles while async content loading is pending", () =>
    {
        if (RuntimeContentPreparationPolicy.ShouldRefreshNativeUnitIds(false, true))
        {
            throw new InvalidOperationException("PrepareContentForRuntime must not clear profiles while LoadAllPacksAsync is still pending.");
        }

        if (RuntimeContentPreparationPolicy.ShouldRefreshNativeUnitIds(false, true) ||
            RuntimeContentPreparationPolicy.ShouldRefreshNativeUnitIds(true, false) ||
            RuntimeContentPreparationPolicy.ShouldRefreshNativeUnitIds(true, true) ||
            !RuntimeContentPreparationPolicy.ShouldRefreshNativeUnitIds(false, false))
        {
            throw new InvalidOperationException("PrepareContentForRuntime refresh gating did not distinguish ready and pending states.");
        }
    }),
    ("bundle fallback stays disabled unless an explicit fallback path is configured", () =>
    {
        if (BundleFallbackPolicy.ShouldAttemptFallback(true, null) ||
            BundleFallbackPolicy.ShouldAttemptFallback(true, "") ||
            BundleFallbackPolicy.ShouldAttemptFallback(true, "   ") ||
            !BundleFallbackPolicy.ShouldAttemptFallback(true, "C:/fallback.bundle") ||
            BundleFallbackPolicy.ShouldAttemptFallback(false, "C:/fallback.bundle"))
        {
            throw new InvalidOperationException("Bundle fallback must stay fail-closed unless a non-empty fallback path is configured for a missing bundle.");
        }
    }),
    ("shield overload fires before immediate recharge and rearms after cooldown", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            UnityEngine.Time.time = 10f;
            RuntimeUnitMechanicsDirector.RegisterProfile("test:shield-combination", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:shield-combination",
                ShieldRegen = new ContentUnitShieldRegenDefinition { rechargeRate = 10f, rechargeDelay = 0f, burstAmount = 5f },
                ShieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { novaRadius = 2f, damage = 10f, cooldown = 5f }
            });
            var self = new EntityController
            {
                entityId = "test:shield-combination", UniqueEntityId = 9801,
                IsUnit = true, IsControlledByPlayer = true, HasShield = true,
                CurrentShield = 50f, MaxShield = 100f
            };
            var enemy = new EntityController { IsUnit = true, IsControlledByAi = true, CurrentHealth = 100f, MaxHealth = 100f };
            ExistingControllers.Instance.PlayerUnits().Add(self);
            ExistingControllers.Instance.AiUnits().Add(enemy);
            RuntimeUnitMechanicsDirector.AttachIfApplicable(self);
            var director = self.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()!;
            self.CurrentShield = 0f;
            UnityEngine.Time.time = 11f;
            director.TickMechanics();
            if (enemy.CurrentHealth != 90f || self.CurrentShield <= 0f)
                throw new InvalidOperationException("Shield break must fire its nova even when the same update immediately recharges it.");
            UnityEngine.Time.time = 12f;
            director.TickMechanics();
            if (enemy.CurrentHealth != 90f)
                throw new InvalidOperationException("Recharge without another break repeated the nova.");
            self.CurrentShield = 0f;
            UnityEngine.Time.time = 13f;
            director.TickMechanics();
            if (enemy.CurrentHealth != 90f)
                throw new InvalidOperationException("Shield break ignored the nova cooldown.");
            self.CurrentShield = 0f;
            UnityEngine.Time.time = 17f;
            director.TickMechanics();
            if (enemy.CurrentHealth != 80f)
                throw new InvalidOperationException("Recharged shield did not rearm the next nova after cooldown.");
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("runtime unit mechanics director executes baseline status effects, combat boosts, veterancy rank-ups, healing, and auras", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:mechanics-unit",
                MinAttackRange = 4.5f,
                CriticalHitChance = 0.25f,
                OnHitStatusEffects = new[]
                {
                    new ContentOnHitStatusEffectDefinition { statusEffect = "Stun", duration = 2.0f, chance = 0.35f }
                },
                BaselineStatusEffects = new[] { "Armored", "Robust" },
                Healing = new ContentUnitHealingDefinition
                {
                    healAmount = 15f,
                    repairAmount = 20f,
                    healRange = 4.0f,
                    healRate = 1.0f
                },
                Aura = new ContentUnitAuraDefinition
                {
                    auraRadius = 4.0f,
                    auraType = "Shield",
                    buffAmount = 12f,
                    affectsAllies = true,
                    affectsEnemies = false
                },
                CombatBoosts = new[]
                {
                    new ContentCombatBoostDefinition
                    {
                        id = "test:boost-speed",
                        statName = "MoveSpeed",
                        multiplier = 1.3f,
                        duration = 3.0f
                    }
                },
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1.0f,
                    levelUpBonus = 0.20f
                }
            };

            RuntimeUnitMechanicsDirector.RegisterProfile("test:mechanics-unit", profile);
            string summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("test:mechanics-unit");
            if (string.IsNullOrWhiteSpace(summary) ||
                !summary.Contains("MinRange: 4.5m", StringComparison.Ordinal) ||
                !summary.Contains("Crit: 25%", StringComparison.Ordinal) ||
                !summary.Contains("OnHit: Stun", StringComparison.Ordinal) ||
                !summary.Contains("Pasivos: Armored, Robust", StringComparison.Ordinal) ||
                !summary.Contains("Curación: 15 HP / 20 Arm", StringComparison.Ordinal) ||
                !summary.Contains("Aura: Shield", StringComparison.Ordinal) ||
                !summary.Contains("Veteranía: Rango 3", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected mechanics summary: " + summary);
            }

            var self = new EntityController
            {
                entityId = "test:mechanics-unit",
                UniqueEntityId = 101,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 60f,
                MaxHealth = 100f,
                HasShield = true,
                CurrentShield = 10f,
                MaxShield = 50f,
                CurrentRank = 0,
                MaxRank = 3,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var ally = new EntityController
            {
                entityId = "test:ally",
                UniqueEntityId = 102,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 50f,
                MaxHealth = 100f,
                CurrentArmor = 30f,
                MaxArmor = 100f,
                HasShield = true,
                CurrentShield = 20f,
                MaxShield = 60f,
                Position = new UnityEngine.Vector3(15f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "test:enemy",
                UniqueEntityId = 201,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 0f,
                MaxHealth = 100f,
                DestroyHasBeenStarted = true,
                Position = new UnityEngine.Vector3(10f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(self);
            ExistingControllers.Instance.PlayerUnits().Add(ally);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            var healingBlocked = new EntityController
            {
                entityId = "test:healing-blocked", UniqueEntityId = 103,
                IsControlledByPlayer = true, IsUnit = true, canNotBeHealed = true,
                CurrentHealth = 1f, MaxHealth = 100f, CurrentArmor = 0f, MaxArmor = 100f,
                HasShield = true, CurrentShield = 20f, MaxShield = 60f,
                Position = new UnityEngine.Vector3(15f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(healingBlocked);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(self);
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Armored && e.DurationType == SetStatusEffect.DurationType.Unlimited) ||
                !self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Robust && e.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("AttachIfApplicable did not apply baseline unlimited status effects.");
            }

            var director = self.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
            if (director == null)
            {
                throw new InvalidOperationException("AttachIfApplicable did not attach RuntimeUnitMechanicsDirector component.");
            }

            // Advance time past initial 0.5s check window and tick healing + aura
            UnityEngine.Time.time = 11f;
            director.TickMechanics();

            if (ally.CurrentHealth != 65f || ally.CurrentArmor != 30f || ally.CurrentShield != 32f ||
                healingBlocked.CurrentHealth != 1f || healingBlocked.CurrentArmor != 20f)
            {
                throw new InvalidOperationException(
                    $"Healing must skip blocked health while independently repairing its armor; ally HP={ally.CurrentHealth}, armor={ally.CurrentArmor}, shield={ally.CurrentShield}; blocked HP={healingBlocked.CurrentHealth}, armor={healingBlocked.CurrentArmor}");
            }

            // Trigger OnEntityDealtDamage on a killed target (3 XP >= threshold 3 -> immediate RankUp + CombatBoost + levelUpBonus heal)
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 45f, self);

            if (self.CurrentRank != 1)
            {
                throw new InvalidOperationException($"Expected self rank to promote to 1 on kill, got {self.CurrentRank}");
            }
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo && e.DurationType == SetStatusEffect.DurationType.Seconds && e.Duration == 3.0f))
            {
                throw new InvalidOperationException("Expected MoveSpeed combatBoost to apply timed Turbo status effect.");
            }
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.CombatBoost && e.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("Expected veterancy promotion to apply unlimited CombatBoost status effect.");
            }
            if (self.CurrentHealth <= 60f)
            {
                throw new InvalidOperationException($"Expected veterancy levelUpBonus to heal self above 60 HP, got {self.CurrentHealth}");
            }
            if (!self.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.MoveSpeed &&
                    Math.Abs(c.Change.ValueToAdd - 0.3f) < 0.001f &&
                    c.Change.Add == SpecificValueChange.AddType.Relative &&
                    !c.IsStackable))
            {
                throw new InvalidOperationException("Expected MoveSpeed combatBoost to apply relative +30% SpecificValueChange.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("runtime unit mechanics director reflects damage once and emits death nova only on kill path", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 30f;

        try
        {
            var tankProfile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reflect-tank",
                DamageReflect = new ContentUnitDamageReflectDefinition { reflectRatio = 0.5f, maxReflectPerHit = 20f },
                DeathNova = new ContentUnitDeathNovaDefinition
                {
                    novaRadius = 4f,
                    damage = 30f,
                    stunDuration = 1.5f,
                    slowDuration = 2.0f,
                    maxTargets = 6
                }
            };
            var attackerProfile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:enemy-attacker",
                DamageReflect = new ContentUnitDamageReflectDefinition { reflectRatio = 0.5f, maxReflectPerHit = 20f }
            };
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reflect-tank", tankProfile);
            RuntimeUnitMechanicsDirector.RegisterProfile("test:enemy-attacker", attackerProfile);

            var tank = new EntityController
            {
                entityId = "test:reflect-tank",
                UniqueEntityId = 501,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var attacker = new EntityController
            {
                entityId = "test:enemy-attacker",
                UniqueEntityId = 502,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(10f, 0f, 0f)
            };
            var farEnemy = new EntityController
            {
                entityId = "test:far-enemy",
                UniqueEntityId = 503,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(80f, 0f, 0f)
            };
            var ally = new EntityController
            {
                entityId = "test:ally",
                UniqueEntityId = 504,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(tank);
            ExistingControllers.Instance.PlayerUnits().Add(ally);
            ExistingControllers.Instance.AiUnits().Add(attacker);
            ExistingControllers.Instance.AiUnits().Add(farEnemy);

            // OnEntityDealtDamage(target=tank, amount=20, originator=attacker):
            // tank reflects 20*0.5=10 to attacker; mutual reflect must not loop back.
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(tank, 20f, attacker);

            if (Math.Abs(attacker.CurrentHealth - 90f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected attacker HP=90 after reflect, got {attacker.CurrentHealth}");
            }

            if (Math.Abs(tank.CurrentHealth - 100f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected tank HP=100 after mutual reflect guard, got {tank.CurrentHealth}");
            }

            // Death nova on kill path: nearby enemy takes 30 + stun/slow; far/ally untouched.
            RuntimeUnitMechanicsDirector.OnEntityKilled(tank);

            if (Math.Abs(attacker.CurrentHealth - 60f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected attacker HP=60 after death nova, got {attacker.CurrentHealth}");
            }

            if (!attacker.HasActiveStatusEffect(StatusEffect.Stun) ||
                !attacker.HasActiveStatusEffect(StatusEffect.SlowedDown))
            {
                throw new InvalidOperationException("Expected death nova to apply Stun and SlowedDown to nearby enemy.");
            }

            if (Math.Abs(farEnemy.CurrentHealth - 100f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected far enemy HP=100 outside nova, got {farEnemy.CurrentHealth}");
            }

            if (Math.Abs(ally.CurrentHealth - 100f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected ally HP=100 (allies unaffected), got {ally.CurrentHealth}");
            }

            // Pooled reuse path (OnEntityDied) must not trigger death nova for a fresh tank.
            var freshTank = new EntityController
            {
                entityId = "test:reflect-tank",
                UniqueEntityId = 505,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            RuntimeUnitMechanicsDirector.OnEntityDied(freshTank);

            if (Math.Abs(attacker.CurrentHealth - 60f) > 0.01f)
            {
                throw new InvalidOperationException($"Expected attacker HP=60 after reuse path (no nova), got {attacker.CurrentHealth}");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("runtime unit mechanics director executes synergies, autonomous stealth, command aura, and kill experience rewards", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();
        UnityEngine.Time.time = 20f;

        try
        {
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:commander-unit",
                Synergies = new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "test:synergy-armor",
                        statName = "ArmorProtection",
                        multiplier = 1.25f,
                        radius = 4.0f,
                        targetUnitIds = new[] { "*" }
                    }
                },
                Stealth = new ContentUnitStealthDefinition
                {
                    stealthLevel = 2.0f,
                    cloakDuration = 4.5f,
                    detectionReduction = 0.5f
                },
                Command = new ContentUnitCommandDefinition
                {
                    commandRange = 4.0f,
                    commandType = "Attack",
                    maxCommandChain = 3
                },
                ExperienceReward = new ContentUnitExperienceRewardDefinition
                {
                    experienceAmount = 20,
                    experienceType = "Crystals",
                    bonusMultiplier = 1.5f
                }
            };

            RuntimeUnitMechanicsDirector.RegisterProfile("test:commander-unit", profile);
            string summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("test:commander-unit");
            if (string.IsNullOrWhiteSpace(summary) ||
                !summary.Contains("Sinergias: 1", StringComparison.Ordinal) ||
                !summary.Contains("Sigilo: 4.5s", StringComparison.Ordinal) ||
                !summary.Contains("Mando: Attack (4m)", StringComparison.Ordinal) ||
                !summary.Contains("Recompensa: +20", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected commander mechanics summary: " + summary);
            }

            var commander = new EntityController
            {
                entityId = "test:commander-unit",
                UniqueEntityId = 301,
                IsControlledByPlayer = true,
                IsUnit = true,
                TimeOfLastDamage = 5f,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var escort = new EntityController
            {
                entityId = "test:escort-ally",
                UniqueEntityId = 302,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new UnityEngine.Vector3(12f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "test:target-enemy",
                UniqueEntityId = 401,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 0f,
                DestroyHasBeenStarted = true,
                Position = new UnityEngine.Vector3(8f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(commander);
            ExistingControllers.Instance.PlayerUnits().Add(escort);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(commander);
            var director = commander.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
            if (director == null)
            {
                throw new InvalidOperationException("AttachIfApplicable did not attach RuntimeUnitMechanicsDirector to commander.");
            }

            UnityEngine.Time.time = 21f;
            director.TickMechanics();

            if (!commander.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Stealth && e.DurationType == SetStatusEffect.DurationType.Seconds && Math.Abs(e.Duration - 4.5f) < 0.01f))
            {
                throw new InvalidOperationException("Expected out-of-combat commander to activate Stealth status effect.");
            }

            if (!ReferenceEquals(escort.CaptainOfThisEntity, commander))
            {
                throw new InvalidOperationException("Expected Command pulse to bind escort's CaptainOfThisEntity to commander.");
            }
            if (!escort.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.Damage &&
                    Math.Abs(c.Change.ValueToAdd - 0.12f) < 0.001f))
            {
                throw new InvalidOperationException("Expected Attack Command pulse to apply +12% Damage SpecificValueChange to escort.");
            }

            if (!escort.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.ArmorProtection &&
                    Math.Abs(c.Change.ValueToAdd - 0.25f) < 0.001f))
            {
                throw new InvalidOperationException("Expected Synergy pulse to apply +25% ArmorProtection SpecificValueChange to escort.");
            }

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 50f, commander);
            if (!Bank.Deposits.Any(d => d.Account == "Player" && Math.Abs(d.Amount - 30f) < 0.01f))
            {
                throw new InvalidOperationException("Expected ExperienceReward on kill to deposit 30 crystals into Player account.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("native shot feedback includes the first shot after a negative idle clock and resets on reuse", () =>
    {
        var tracker = new WeaponShotFeedbackTracker();
        if (tracker.Observe(float.MinValue) || tracker.Observe(float.MinValue))
            throw new InvalidOperationException("An idle native shot clock generated firing feedback.");
        if (!tracker.Observe(0f) || tracker.Observe(0f) || !tracker.Observe(1f))
            throw new InvalidOperationException("First or subsequent shot did not produce exactly one feedback transition.");
        tracker.Reset();
        if (tracker.Observe(1f) || tracker.Observe(float.MinValue) || !tracker.Observe(0f))
            throw new InvalidOperationException("Reuse replayed an old shot or lost the first shot of the next life.");
        if (tracker.Observe(float.NaN) || tracker.Observe(float.PositiveInfinity) || tracker.Observe(0f))
            throw new InvalidOperationException("Invalid samples corrupted the last real shot timestamp.");
    }),
    ("faction tint resolves native player AI and AI-building materials without guessing neutral colors", () =>
    {
        var saved = new Dictionary<string, UnityEngine.Object>(Resources.LoadedResourcesForTest);
        try
        {
            var player = new Material();
            player.SetColor(RuntimeFactionColorResolver.NativeColorProperty, new Color(0.8f, 0.7f, 0.3f, 0f));
            var ai = new Material();
            ai.SetColor(RuntimeFactionColorResolver.NativeColorProperty, new Color(0.9f, 0.3f, 0.2f, 0f));
            var building = new Material();
            building.SetColor(RuntimeFactionColorResolver.NativeColorProperty, new Color(0.6f, 0.2f, 0.1f, 0f));
            Resources.LoadedResourcesForTest["Materials/Player"] = player;
            Resources.LoadedResourcesForTest["Materials/Ai"] = ai;
            Resources.LoadedResourcesForTest["Materials/DynamicMaterials/AiBuilding"] = building;
            var entity = new EntityController { IsControlledByPlayer = true };
            if (!RuntimeFactionColorResolver.TryResolve(entity, out Color color) || color != new Color(0.8f, 0.7f, 0.3f, 1f))
                throw new InvalidOperationException("Player material tint was not read from the native shader color property.");
            entity.IsControlledByPlayer = false;
            entity.IsControlledByAi = true;
            if (!RuntimeFactionColorResolver.TryResolve(entity, out color) || color.r != 0.9f)
                throw new InvalidOperationException("AI unit did not resolve its faction material.");
            entity.IsBuilding = true;
            if (!RuntimeFactionColorResolver.TryResolve(entity, out color) || color.r != 0.6f)
                throw new InvalidOperationException("AI building did not use its separate native material.");
            entity.IsControlledByAi = false;
            if (RuntimeFactionColorResolver.TryResolve(entity, out _))
                throw new InvalidOperationException("Neutral entity received a guessed faction tint.");
            entity.IsControlledByPlayer = true;
            Resources.LoadedResourcesForTest.Remove("Materials/Player");
            if (RuntimeFactionColorResolver.TryResolve(entity, out _))
                throw new InvalidOperationException("Missing native faction material was treated as resolved.");
        }
        finally
        {
            Resources.LoadedResourcesForTest.Clear();
            foreach (var entry in saved) Resources.LoadedResourcesForTest.Add(entry.Key, entry.Value);
        }
    }),
    ("support auras reach damaged allies after full-health or healing-blocked candidates", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            UnityEngine.Time.time = 10f;
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:aura-capacity",
                Aura = new ContentUnitAuraDefinition { auraRadius = 4f, auraType = "Heal", buffAmount = 5f, affectsAllies = true }
            };
            RuntimeUnitMechanicsDirector.RegisterProfile(profile.EntityId, profile);
            var source = new EntityController { entityId = profile.EntityId, IsUnit = true, IsControlledByPlayer = true };
            ExistingControllers.Instance.PlayerUnits().Add(source);
            for (int i = 0; i < 20; i++)
                ExistingControllers.Instance.PlayerUnits().Add(new EntityController
                {
                    IsUnit = true, IsControlledByPlayer = true, CurrentHealth = i % 2 == 0 ? 100f : 10f,
                    canNotBeHealed = i % 2 != 0, MaxHealth = 100f,
                    HasShield = true, CurrentShield = 100f, MaxShield = 100f
                });
            var target = new EntityController
            {
                IsUnit = true, IsControlledByPlayer = true, CurrentHealth = 50f, MaxHealth = 100f,
                HasShield = true, CurrentShield = 50f, MaxShield = 100f
            };
            ExistingControllers.Instance.PlayerUnits().Add(target);
            RuntimeUnitMechanicsDirector.AttachIfApplicable(source);
            var director = source.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()!;
            UnityEngine.Time.time = 11f;
            director.TickMechanics();
            if (target.CurrentHealth != 55f)
                throw new InvalidOperationException("Healthy or unhealable candidates consumed the healing aura recipient budget.");
            profile.Aura.auraType = "Shield";
            UnityEngine.Time.time = 12.1f;
            director.TickMechanics();
            if (target.CurrentShield != 55f)
                throw new InvalidOperationException("Fully charged candidates consumed the shield aura recipient budget.");
            var extraTargets = new List<EntityController>();
            for (int i = 0; i < 20; i++)
            {
                var extra = new EntityController { IsUnit = true, IsControlledByPlayer = true, CurrentHealth = 50f, MaxHealth = 100f };
                extraTargets.Add(extra);
                ExistingControllers.Instance.PlayerUnits().Add(extra);
            }
            profile.Aura.auraType = "Heal";
            UnityEngine.Time.time = 13.2f;
            director.TickMechanics();
            if (target.CurrentHealth != 60f || extraTargets.Count(e => e.CurrentHealth == 55f) != 15)
                throw new InvalidOperationException("Healing aura must retain its sixteen effective-recipient limit.");
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("timed stat changes support native attributes and correctly invert severe fire-rate penalties", () =>
    {
        var unit = new EntityController();
        UnityEngine.Time.time = 10f;
        foreach (var attribute in Enum.GetValues<EntityController.ChangeableValue>())
        {
            RuntimeUnitMechanicsDirector.ApplyTimedStatChange(unit, attribute.ToString(), 0.2f, 3f, "test:native-stat");
            if (!unit.AppliedSpecificValueChanges.Any(c => c.Value == attribute && Math.Abs(c.Change.ValueToAdd - 0.2f) < 0.001f && c.Change.ValidUntilTimestamp == 13f))
                throw new InvalidOperationException("Native timed attribute was not applied: " + attribute);
        }
        unit.AppliedSpecificValueChanges.Clear();
        RuntimeUnitMechanicsDirector.ApplyTimedStatChange(unit, "AttackSpeed", -0.95f, 3f, "test:slow-fire");
        var slow = unit.AppliedSpecificValueChanges.Single();
        if (slow.Value != EntityController.ChangeableValue.AttackCooldown || Math.Abs(slow.Change.ValueToAdd - 19f) > 0.001f)
            throw new InvalidOperationException("Five-percent fire rate must produce twenty-times attack cooldown.");
        int count = unit.AppliedSpecificValueChanges.Count;
        RuntimeUnitMechanicsDirector.ApplyTimedStatChange(unit, "123", 0.2f, 3f, "test:invalid-stat");
        RuntimeUnitMechanicsDirector.ApplyTimedStatChange(unit, "AttackSpeed", -1f, 3f, "test:zero-fire");
        if (unit.AppliedSpecificValueChanges.Count != count)
            throw new InvalidOperationException("Invalid attribute or undefined zero-rate conversion produced a modifier.");
        UnityEngine.Time.time = 0f;
    }),
    ("vampiric siphon splits shield overflow into health within one per-hit budget", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:split-siphon", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:split-siphon",
                VampiricLeech = new ContentUnitVampiricLeechDefinition
                {
                    siphonRatio = 0.5f, siphonToShield = true, maxSiphonPerHit = 10f
                }
            });
            var attacker = new EntityController
            {
                entityId = "test:split-siphon", IsUnit = true, IsControlledByPlayer = true,
                HasShield = true, CurrentShield = 98f, MaxShield = 100f,
                CurrentHealth = 50f, MaxHealth = 100f
            };
            var target = new EntityController { IsUnit = true, IsControlledByAi = true, CurrentHealth = 100f };
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 100f, attacker);
            if (attacker.CurrentShield != 100f || attacker.CurrentHealth != 58f)
                throw new InvalidOperationException("Capped siphon should restore two shield and eight health instead of losing overflow.");
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 100f, attacker);
            if (attacker.CurrentShield != 100f || attacker.CurrentHealth != 68f)
                throw new InvalidOperationException("Full shield should route the siphon budget to health.");
            attacker.CurrentShield = 90f;
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 100f, attacker);
            if (attacker.CurrentShield != 100f || attacker.CurrentHealth != 68f)
                throw new InvalidOperationException("Shield restoration duplicated the siphon budget into health.");
            attacker.CurrentShield = 99.95f;
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 2f, attacker);
            if (Math.Abs(attacker.CurrentShield - 100f) > 0.001f || Math.Abs(attacker.CurrentHealth - 68.95f) > 0.001f)
                throw new InvalidOperationException("Near-full shield should preserve fractional siphon overflow.");
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("combat experience applies all earned promotions immediately and retains partial progress", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:multi-promotion", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:multi-promotion",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1, maxLevel = 3, experienceGainMultiplier = 8f, levelUpBonus = 0.1f
                }
            });
            var attacker = new EntityController
            {
                entityId = "test:multi-promotion", UniqueEntityId = 9850,
                IsUnit = true, IsControlledByPlayer = true, CurrentRank = 0, MaxRank = 5,
                CurrentHealth = 10f, MaxHealth = 100f
            };
            var target = new EntityController { IsUnit = true, IsControlledByAi = true, CurrentHealth = 100f };
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            if (attacker.CurrentRank != 2 || attacker.CurrentHealth != 30f)
                throw new InvalidOperationException("One high-XP hit must grant both earned ranks and both promotion heals.");
            var xp = RuntimeUnitMechanicsDirector.CombatXpEntries.Single(e => e.EntityKeyId == 9850);
            if (xp.Experience != 2)
                throw new InvalidOperationException("Partial experience was lost after multiple promotions.");
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            if (attacker.CurrentRank != 3 || attacker.CurrentHealth != 40f ||
                RuntimeUnitMechanicsDirector.CombatXpEntries.Single(e => e.EntityKeyId == 9850).Experience != 0)
                throw new InvalidOperationException("Promotion exceeded profile cap or left unusable experience at maximum rank.");
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            if (attacker.CurrentRank != 3 || attacker.CurrentHealth != 40f)
                throw new InvalidOperationException("Capped unit received another promotion reward.");
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("reattaching an active entity preserves its accumulated combat experience", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reattach-xp", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reattach-xp",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                },
                ShieldRegen = new ContentUnitShieldRegenDefinition
                {
                    rechargeRate = 1f,
                    rechargeDelay = 0f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:reattach-xp",
                UniqueEntityId = 702,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:reattach-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.AttachIfApplicable(attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            if (attacker.CurrentRank != 1)
            {
                throw new InvalidOperationException("Reattaching an active entity discarded its accumulated combat experience.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("destroyed entity combat experience is cleared by its lifecycle hook", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:destroy-cleanup", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:destroy-cleanup",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:destroy-cleanup",
                UniqueEntityId = 501,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:destroy-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            MethodInfo destroyHook = typeof(RuntimeHarmonyPatches).GetMethod(
                "EntityControllerDestroyPostfix", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("EntityController.Destroy cleanup hook is missing.");
            destroyHook.Invoke(null, new object[] { attacker });

            var replacement = new EntityController
            {
                entityId = "test:destroy-cleanup",
                UniqueEntityId = 501,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, replacement);

            if (replacement.CurrentRank != 0)
            {
                throw new InvalidOperationException("Destroyed entity experience leaked into a later entity instance.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
        }
    }),
    ("destroyed entity combat experience fallback key is cleared", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:destroy-fallback-cleanup", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:destroy-fallback-cleanup",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:destroy-fallback-cleanup",
                UniqueEntityId = -1,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:destroy-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDestroyed(attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            if (attacker.CurrentRank != 0)
            {
                throw new InvalidOperationException("Destroyed entity retained combat XP under its fallback identity.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
        }
    }),
    ("experience reward does not pay for killing a friendly unit", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:player-killer", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:player-killer",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1.0f,
                    levelUpBonus = 0.2f
                },
                CombatBoosts = new[]
                {
                    new ContentCombatBoostDefinition
                    {
                        id = "test:friendly-hit-boost",
                        statName = "MoveSpeed",
                        multiplier = 1.3f,
                        duration = 3.0f
                    }
                },
                ExperienceReward = new ContentUnitExperienceRewardDefinition
                {
                    experienceAmount = 12,
                    experienceType = "Crystals",
                    bonusMultiplier = 1.0f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:player-killer",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 60f,
                MaxHealth = 100f,
                CurrentRank = 0,
                MaxRank = 3
            };
            var friendlyTarget = new EntityController
            {
                entityId = "test:friendly-target",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 10f,
                MaxHealth = 100f
            };

            if (attacker.IsEnemy(friendlyTarget))
            {
                throw new InvalidOperationException("Fixture reports same-player entities as enemies.");
            }

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(friendlyTarget, 2f, attacker);
            if (attacker.CurrentRank != 0 || attacker.AppliedSpecificValueChanges.Count != 0 ||
                attacker.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo || e.Effect == StatusEffect.CombatBoost))
            {
                throw new InvalidOperationException("Friendly hits must not trigger attacker combat boosts or veterancy.");
            }

            friendlyTarget.CurrentHealth = 0f;
            friendlyTarget.DestroyHasBeenStarted = true;
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(friendlyTarget, 10f, attacker);

            if (attacker.CurrentRank != 0 || attacker.AppliedSpecificValueChanges.Count != 0 ||
                attacker.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo || e.Effect == StatusEffect.CombatBoost))
            {
                throw new InvalidOperationException("Expected no combat boost or veterancy for killing a friendly unit.");
            }

            if (Bank.Deposits.Count != 0)
            {
                throw new InvalidOperationException(
                    "Expected no Player reward for killing a friendly unit, got " + Bank.Deposits.Count + " deposit(s).");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("on-hit status effects do not affect friendly units", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeHooks.OnHitStatusEffectsResolver = entityId => entityId == "test:on-hit-attacker"
            ? new List<ContentOnHitStatusEffectDefinition>
            {
                new() { statusEffect = "Stun", duration = 2f, chance = 1f }
            }
            : null;

        try
        {
            var attacker = new EntityController
            {
                entityId = "test:on-hit-attacker",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
            var friendlyTarget = new EntityController
            {
                entityId = "test:on-hit-friendly-target",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            if (attacker.IsEnemy(friendlyTarget))
            {
                throw new InvalidOperationException("Fixture reports same-player entities as enemies.");
            }

            RuntimeHooks.EntityTakeDamagePostfix(friendlyTarget, 1f, attacker);
            if (friendlyTarget.AppliedStatusEffects.Any(effect => effect.Effect == StatusEffect.Stun))
            {
                throw new InvalidOperationException("On-hit status effects must not affect a friendly target.");
            }

            var hostileTarget = new EntityController
            {
                entityId = "test:on-hit-hostile-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
            RuntimeHooks.EntityTakeDamagePostfix(hostileTarget, 1f, attacker);
            if (!hostileTarget.AppliedStatusEffects.Any(effect =>
                    effect.Effect == StatusEffect.Stun &&
                    effect.DurationType == SetStatusEffect.DurationType.Seconds &&
                    effect.Duration == 2f))
            {
                throw new InvalidOperationException("On-hit status effects should still affect a hostile target.");
            }
        }
        finally
        {
            RuntimeHooks.OnHitStatusEffectsResolver = null;
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime ai modules census captures registered modules and serializes cleanly", () =>
    {
        AiModuleRegistry prevActive = AiModuleRegistry.Active;
        try
        {
            var isolated = new AiModuleRegistry();
            AiModuleRegistry.Active = isolated;
            AiModulesCensus emptyCensus = RuntimeAiModulesCensus.Current();
            if (emptyCensus == null || emptyCensus.RegisteredModuleCount != 0 || emptyCensus.ModuleRows.Count != 0)
            {
                throw new InvalidOperationException("Initial AI modules census must be empty.");
            }

            string emptyJson = AgentDiagnosticsFormatter.SerializeAiModulesCensus(emptyCensus);
            using (var doc = JsonDocument.Parse(emptyJson))
            {
                if (doc.RootElement.GetProperty("registeredModuleCount").GetInt32() != 0 ||
                    doc.RootElement.GetProperty("moduleRowCount").GetInt32() != 0)
                {
                    throw new InvalidOperationException("Serialized empty AI modules census must report 0 counts.");
                }
            }

            isolated.Register(new ThreatPrioritizationAiModule());
            AiModulesCensus activeCensus = RuntimeAiModulesCensus.Current();
            if (activeCensus.RegisteredModuleCount != 1 || activeCensus.ModuleRows.Count != 1)
            {
                throw new InvalidOperationException("AI modules census must report registered threat-prioritization module.");
            }

            if (activeCensus.ModuleRows[0].ModuleId != "sample:threat-prioritization" ||
                !activeCensus.ModuleRows[0].IsRegistered)
            {
                throw new InvalidOperationException("AI module row did not capture correct module ID or registration state.");
            }

            string activeJson = AgentDiagnosticsFormatter.SerializeAiModulesCensus(activeCensus);
            using (var doc = JsonDocument.Parse(activeJson))
            {
                if (doc.RootElement.GetProperty("registeredModuleCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("moduleRowCount").GetInt32() != 1)
                {
                    throw new InvalidOperationException("Serialized active census must reflect 1 registered module.");
                }

                JsonElement rows = doc.RootElement.GetProperty("moduleRows");
                if (rows.GetArrayLength() != 1 ||
                    rows[0].GetProperty("moduleId").GetString() != "sample:threat-prioritization" ||
                    !rows[0].GetProperty("isRegistered").GetBoolean())
                {
                    throw new InvalidOperationException("Serialized moduleRows array does not match registered module properties.");
                }
            }
        }
        finally
        {
            AiModuleRegistry.Active = prevActive;
        }
    }),
    ("runtime economy census captures match telemetry and degrades safely", () =>
    {
        EconomyCensus custom = new EconomyCensus(250, 400, 5, 120.5f, 3);
        if (custom.PlayerCredits != 250 || custom.AiCredits != 400 || custom.AttackWaveNumber != 5 ||
            Math.Abs(custom.ElapsedGameTimeSeconds - 120.5f) > 0.001f || custom.HarvesterCount != 3)
        {
            throw new InvalidOperationException("EconomyCensus constructor did not assign values accurately.");
        }

        string json = AgentDiagnosticsFormatter.SerializeEconomyCensus(custom);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("playerCredits").GetInt32() != 250 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 400 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 5 ||
                Math.Abs(doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() - 120.5f) > 0.001f ||
                doc.RootElement.GetProperty("harvesterCount").GetInt32() != 3)
            {
                throw new InvalidOperationException("Serialized EconomyCensus JSON properties do not match expected values.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeEconomyCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("playerCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 0 ||
                doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() != 0f ||
                doc.RootElement.GetProperty("harvesterCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Serialized null EconomyCensus must fall back gracefully to zeroed primitives.");
            }
        }

        EconomyCensus live = RuntimeEconomyCensus.Current();
        if (live == null)
        {
            throw new InvalidOperationException("RuntimeEconomyCensus.Current() must never return null.");
        }
    }),
    ("runtime combat status census serializes loss bounds and tracking details", () =>
    {
        var rows = new List<CombatEntityStatusRow>
        {
            new CombatEntityStatusRow("test:unit-1", true, 10.0f, true, 2),
            new CombatEntityStatusRow("test:unit-2", false, 12.5f, false, 0),
        };
        CombatStatusCensus census = new CombatStatusCensus(1, 3, 1, rows.AsReadOnly());

        if (census.OmittedUnitCount != 1 || census.TotalCombatActiveUnitCount != 3 ||
            census.UnitsWithActiveStatusEffectsCount != 1 || census.CombatActiveUnits.Count != 2)
        {
            throw new InvalidOperationException("CombatStatusCensus failed to retain field values.");
        }

        string json = AgentDiagnosticsFormatter.SerializeCombatStatusCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedUnitCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 3 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("trackedUnitRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Serialized CombatStatusCensus counts do not match expected bounds.");
            }

            JsonElement units = doc.RootElement.GetProperty("combatActiveUnits");
            if (units.GetArrayLength() != 2 ||
                units[0].GetProperty("entityKeyId").GetString() != "test:unit-1" ||
                !units[0].GetProperty("isControlledByPlayer").GetBoolean() ||
                units[0].GetProperty("activeStatusEffectsCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Serialized combat active units array does not match row elements.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeCombatStatusCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("omittedUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("trackedUnitRowCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null CombatStatusCensus must serialize with zero counts.");
            }
        }

        CombatStatusCensus live = RuntimeCombatStatusCensus.Current();
        if (live == null)
        {
            throw new InvalidOperationException("RuntimeCombatStatusCensus.Current() must never return null.");
        }
    }),
    ("runtime unified system health synthesizes subsystems cleanly", () =>
    {
        var dummySnapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 2, 0, 2048L,
            Array.Empty<PackDiagnosticReport>(),
            new List<UnitDiagnosticReport>
            {
                new UnitDiagnosticReport("sample:unit-a", "sample_pack", "prefab", "img", true)
            }.AsReadOnly(),
            Array.Empty<StageDiagnosticReport>(),
            Array.Empty<AiProfileDiagnosticReport>(),
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            new List<string> { "log1", "log2" }.AsReadOnly());

        var aiCensus = new AiModulesCensus(2, new List<AiModuleDiagnosticRow>
        {
            new AiModuleDiagnosticRow("sample:threat-prioritization", 1, true),
            new AiModuleDiagnosticRow("sample:skirmish-kiting", 1, true),
        }.AsReadOnly());

        var economyCensus = new EconomyCensus(500, 750, 4, 300f, 6);
        var combatCensus = new CombatStatusCensus(0, 5, 2, Array.Empty<CombatEntityStatusRow>());

        string json = AgentDiagnosticsFormatter.SerializeSystemHealth(
            dummySnapshot,
            aiCensus,
            economyCensus,
            combatCensus);

        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isNativeRegistryReady").GetBoolean() ||
                doc.RootElement.GetProperty("activePackCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("failedPackCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredUnitCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("registeredAiModuleCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("playerCredits").GetInt32() != 500 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 750 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 4 ||
                Math.Abs(doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() - 300f) > 0.001f ||
                doc.RootElement.GetProperty("omittedCombatUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 5 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("recentLogCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Unified system health serialization did not preserve subsystem values.");
            }
        }

        string nullHealthJson = AgentDiagnosticsFormatter.SerializeSystemHealth(null, null, null, null);
        using (var doc = JsonDocument.Parse(nullHealthJson))
        {
            if (doc.RootElement.GetProperty("isNativeRegistryReady").GetBoolean() ||
                doc.RootElement.GetProperty("activePackCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredAiModuleCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("playerCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null system health must serialize safe default primitive values.");
            }
        }
    }),
    ("runtime card change applier availability check handles unreflected state safely", () =>
    {
        if (RuntimeCardChangeApplier.IsAvailable(null))
        {
            throw new InvalidOperationException("IsAvailable must return false when native assembly/reflection is unavailable.");
        }
    }),
    ("threat-prioritization census serializes telemetry and live registry", () =>
    {
        ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ResetThreatPrioritizationRegistryForTests();
        string json = AgentDiagnosticsFormatter.SerializeThreatPrioritizationCensus(2, 5, 3, 1, 4);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("activeBehaviourCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("processedAiUnitCount").GetInt32() != 5 ||
                doc.RootElement.GetProperty("processedAlliedUnitCount").GetInt32() != 3 ||
                doc.RootElement.GetProperty("lastRetargetedCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalRetargetCommandCount").GetInt32() != 4)
            {
                throw new InvalidOperationException("Threat-prioritization census did not preserve telemetry values.");
            }
        }

        string nullSafeJson = AgentDiagnosticsFormatter.SerializeThreatPrioritizationCensus(-1, -2, -3, -4, -5);
        using (var doc = JsonDocument.Parse(nullSafeJson))
        {
            if (doc.RootElement.GetProperty("activeBehaviourCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("processedAiUnitCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Threat-prioritization census must clamp negative telemetry to zero.");
            }
        }

        if (ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ActiveBehaviourCount < 0)
        {
            throw new InvalidOperationException("Threat-prioritization registry count must be non-negative.");
        }
    }),
    ("diagnostic catalog lists available agent getters", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "diagnosticCatalog" ||
                doc.RootElement.GetProperty("diagnosticCount").GetInt32() <= 0)
            {
                throw new InvalidOperationException("Diagnostic catalog must report a non-empty count.");
            }

            bool found = false;
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                if (item.GetProperty("name").GetString() == "GetThreatPrioritizationCensusJson")
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                throw new InvalidOperationException("Diagnostic catalog must include threat-prioritization census getter.");
            }
        }
    }),
    ("production census serializes factory queue telemetry with omission bounds first", () =>
    {
        var rows = new List<ProductionFactoryDiagnosticRow>
        {
            new ProductionFactoryDiagnosticRow("factory:player-heavy", true, 2, true, 450f, 500f, 0.9f),
            new ProductionFactoryDiagnosticRow("factory:ai-barracks", false, 0, false, 300f, 300f, 1.0f)
        };
        var census = new ProductionCensus(0, 2, 1, 2, 1, 1, rows);
        string json = AgentDiagnosticsFormatter.SerializeProductionCensus(census);

        int omittedIndex = json.IndexOf("omittedFactoryCount");
        int rowsIndex = json.IndexOf("factoryRows");
        if (omittedIndex < 0 || rowsIndex < 0 || omittedIndex > rowsIndex)
        {
            throw new InvalidOperationException("Omission counter must be serialized before detail collection in production census.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalFactoryCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("activeProducingCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalQueuedUnitsCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("playerFactoryCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("aiFactoryCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("factoryRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Production census did not preserve factory telemetry metrics.");
            }

            JsonElement first = doc.RootElement.GetProperty("factoryRows")[0];
            if (first.GetProperty("entityKeyId").GetString() != "factory:player-heavy" ||
                !first.GetProperty("isControlledByPlayer").GetBoolean() ||
                first.GetProperty("inProductionCount").GetInt32() != 2 ||
                !first.GetProperty("isProducing").GetBoolean())
            {
                throw new InvalidOperationException("Production census did not serialize factory row correctly.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeProductionCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("omittedFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("factoryRowCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null production census must serialize default values.");
            }
        }
    }),
    ("base integrity census serializes HQ and AI structure telemetry with omission bounds first", () =>
    {
        var rows = new List<BaseStructureDiagnosticRow>
        {
            new BaseStructureDiagnosticRow("building:player-hq", true, 900f, 1000f, 0.9f, true),
            new BaseStructureDiagnosticRow("building:ai-defense", false, 200f, 400f, 0.5f, false)
        };
        var census = new BaseIntegrityCensus(0, true, 900f, 1000f, 0.9f, 1, 0.5f, rows);
        string json = AgentDiagnosticsFormatter.SerializeBaseIntegrityCensus(census);

        int omittedIndex = json.IndexOf("omittedStructureCount");
        int rowsIndex = json.IndexOf("structureRows");
        if (omittedIndex < 0 || rowsIndex < 0 || omittedIndex > rowsIndex)
        {
            throw new InvalidOperationException("Omission counter must be serialized before detail collection in base integrity census.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedStructureCount").GetInt32() != 0 ||
                !doc.RootElement.GetProperty("hasPlayerHq").GetBoolean() ||
                Math.Abs(doc.RootElement.GetProperty("playerHqHealthRatio").GetSingle() - 0.9f) > 0.001f ||
                doc.RootElement.GetProperty("livingAiBuildingCount").GetInt32() != 1 ||
                Math.Abs(doc.RootElement.GetProperty("averageAiStructureHealthRatio").GetSingle() - 0.5f) > 0.001f ||
                doc.RootElement.GetProperty("structureRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Base integrity census did not preserve structure metrics.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeBaseIntegrityCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("hasPlayerHq").GetBoolean() ||
                doc.RootElement.GetProperty("livingAiBuildingCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null base integrity census must serialize default values.");
            }
        }
    }),
    ("runtime production census queries living factories from existing controllers", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var pFactory = new EntityController
            {
                entityId = "building:player-factory",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = true,
                CurrentHealth = 400f,
                MaxHealth = 500f,
                InProductionCount = 1
            };
            var aiBuilding = new EntityController
            {
                entityId = "building:ai-turret-factory",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = false,
                CurrentHealth = 250f,
                MaxHealth = 250f,
                InProductionCount = 0
            };
            ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(pFactory);
            ExistingControllers.Instance.AiBuildings().Add(aiBuilding);

            ProductionCensus census = RuntimeProductionCensus.Current();
            if (census.TotalFactoryCount != 2 ||
                census.PlayerFactoryCount != 1 ||
                census.AiFactoryCount != 1 ||
                census.ActiveProducingCount != 1 ||
                census.TotalQueuedUnitsCount != 1 ||
                census.FactoryRows.Count != 2)
            {
                throw new InvalidOperationException($"RuntimeProductionCensus did not collect expected factory stats. Total={census.TotalFactoryCount}, Active={census.ActiveProducingCount}");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime base integrity census collects HQ and living AI buildings", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var hq = new EntityController
            {
                entityId = "building:engineering-hq",
                IsBuilding = true,
                IsControlledByPlayer = true,
                CurrentHealth = 800f,
                MaxHealth = 1000f
            };
            var aiTurret = new EntityController
            {
                entityId = "building:ai-turret",
                IsBuilding = true,
                IsControlledByPlayer = false,
                CurrentHealth = 100f,
                MaxHealth = 200f
            };
            ExistingControllers.Instance.EngineeringHqEntity = hq;
            ExistingControllers.Instance.AiBuildings().Add(aiTurret);

            BaseIntegrityCensus census = RuntimeBaseIntegrityCensus.Current();
            if (!census.HasPlayerHq ||
                Math.Abs(census.PlayerHqHealthRatio - 0.8f) > 0.001f ||
                census.LivingAiBuildingCount != 1 ||
                Math.Abs(census.AverageAiStructureHealthRatio - 0.5f) > 0.001f ||
                census.StructureRows.Count != 2)
            {
                throw new InvalidOperationException($"RuntimeBaseIntegrityCensus did not collect expected HQ/AI structure metrics. HasHQ={census.HasPlayerHq}, AiBuildings={census.LivingAiBuildingCount}");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime gameplay production director orders production on player factory and validates constraints", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            ProductionOrderResult emptyRes = RuntimeGameplayProductionDirector.TryOrderProduction("");
            if (emptyRes.Success)
            {
                throw new InvalidOperationException("Production order on empty factory ID must fail.");
            }

            ProductionOrderResult notFoundRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:ghost-factory");
            if (notFoundRes.Success)
            {
                throw new InvalidOperationException("Production order on nonexistent factory must fail.");
            }

            var aiFactory = new EntityController
            {
                entityId = "building:ai-factory-1",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = false,
                InProductionCount = 0
            };
            ExistingControllers.Instance.PlayerUnits().Add(aiFactory);

            ProductionOrderResult aiRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:ai-factory-1");
            if (aiRes.Success)
            {
                throw new InvalidOperationException("Production order on non-player factory must fail.");
            }

            var playerFactory = new EntityController
            {
                entityId = "building:player-factory-1",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = true,
                InProductionCount = 3
            };
            ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(playerFactory);

            ProductionOrderResult successRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:player-factory-1", instant: false, forFree: true);
            if (!successRes.Success || successRes.FactoryKeyId != "building:player-factory-1" || successRes.InProductionCount != 3)
            {
                throw new InvalidOperationException("Production order on player factory must succeed.");
            }

            string resultJson = AgentDiagnosticsFormatter.SerializeProductionOrderResult(successRes);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("factoryKeyId").GetString() != "building:player-factory-1" ||
                    doc.RootElement.GetProperty("inProductionCount").GetInt32() != 3)
                {
                    throw new InvalidOperationException("ProductionOrderResult serialization mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("diagnostic catalog contains new production and base integrity endpoints", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            var names = new HashSet<string>();
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetProductionCensusJson") ||
                !names.Contains("GetBaseIntegrityCensusJson") ||
                !names.Contains("OrderFactoryProduceJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog must include GetProductionCensusJson, GetBaseIntegrityCensusJson, and OrderFactoryProduceJson.");
            }
        }
    }),
    ("run progression census collects scene, stage, ascension/heat, relics and rewards with loss counters first", () =>
    {
        try
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = "Desert_Combat_01";
            Game.CurrentLevel = 3;
            Game.IsLastLevel = false;
            Game.IsFirstLevelOrGameHasBeenResetted = false;
            Game.CardsInDeck = new List<string> { "card_1", "card_2", "card_3" };

            MetaGame.Instance.CurrentAscensionLevel = 2;
            MetaGame.Instance.CurrentHeat = 5;
            MetaGame.Instance.CurrentAscensionAndHeatLevelString = "Ascension 2 Heat 5";

            var testRelics = new List<string>();
            for (int i = 1; i <= 40; i++)
            {
                testRelics.Add("relic_item_" + i);
            }
            Game.Relics = testRelics;

            var testRewards = new List<OpenReward>();
            for (int i = 1; i <= 35; i++)
            {
                testRewards.Add(new OpenReward { blueprintPoolId = "pool_reward_" + i });
            }
            Game.OpenCardRewards = testRewards;

            RunProgressionCensus census = RuntimeRunProgressionCensus.Current();

            if (census.ActiveSceneName != "Desert_Combat_01" ||
                census.CurrentLevel != 3 ||
                census.IsLastLevel ||
                census.IsFirstLevel ||
                census.AscensionLevel != 2 ||
                census.HeatLevel != 5 ||
                census.AscensionAndHeatString != "Ascension 2 Heat 5" ||
                census.CardsInDeckCount != 3)
            {
                throw new InvalidOperationException("RunProgressionCensus failed to capture core stage/meta progression parameters.");
            }

            if (census.TotalRelicCount != 40 || census.ActiveRelicIds.Count != 32 || census.OmittedRelicCount != 8)
            {
                throw new InvalidOperationException("RunProgressionCensus must cap relics at 32 and track 8 omitted relics.");
            }

            if (census.TotalBlueprintRewardCount != 35 || census.OfferedBlueprintPoolIds.Count != 32 || census.OmittedBlueprintCount != 3)
            {
                throw new InvalidOperationException("RunProgressionCensus must cap blueprint pools at 32 and track 3 omitted blueprints.");
            }

            string json = AgentDiagnosticsFormatter.SerializeRunProgressionCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (root.GetProperty("kind").GetString() != "runProgressionCensus" ||
                    root.GetProperty("omittedRelicCount").GetInt32() != 8 ||
                    root.GetProperty("omittedBlueprintCount").GetInt32() != 3 ||
                    root.GetProperty("activeSceneName").GetString() != "Desert_Combat_01" ||
                    root.GetProperty("currentLevel").GetInt32() != 3 ||
                    root.GetProperty("ascensionLevel").GetInt32() != 2 ||
                    root.GetProperty("heatLevel").GetInt32() != 5 ||
                    root.GetProperty("relicCount").GetInt32() != 32 ||
                    root.GetProperty("offeredBlueprintRewardCount").GetInt32() != 32)
                {
                    throw new InvalidOperationException("SerializeRunProgressionCensus JSON property mismatch.");
                }

                int omittedRelicIdx = json.IndexOf("\"omittedRelicCount\"");
                int activeRelicsIdx = json.IndexOf("\"activeRelicIds\"");
                int omittedBpIdx = json.IndexOf("\"omittedBlueprintCount\"");
                int offeredBpIdx = json.IndexOf("\"offeredBlueprintPoolIds\"");

                if (omittedRelicIdx < 0 || activeRelicsIdx < 0 || omittedRelicIdx >= activeRelicsIdx ||
                    omittedBpIdx < 0 || offeredBpIdx < 0 || omittedBpIdx >= offeredBpIdx)
                {
                    throw new InvalidOperationException("Loss/omission counters must strictly precede collections in JSON output.");
                }
            }
        }
        finally
        {
            Game.Relics.Clear();
            Game.OpenCardRewards.Clear();
            Game.CardsInDeck.Clear();
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = string.Empty;
        }
    }),
    ("gameplay command director grants credits with bounded limits and updates balance", () =>
    {
        try
        {
            Bank.Reset();

            CreditGrantResult grantSuccess = RuntimeGameplayCommandDirector.TryGrantCredits(750, "Player");
            if (!grantSuccess.Success || grantSuccess.AmountGranted != 750 || grantSuccess.AccountName != "Player")
            {
                throw new InvalidOperationException("Valid credit grant of 750 credits must succeed.");
            }

            CreditGrantResult grantZero = RuntimeGameplayCommandDirector.TryGrantCredits(0, "Player");
            if (grantZero.Success || grantZero.AmountGranted != 0)
            {
                throw new InvalidOperationException("Credit grant of 0 credits must fail.");
            }

            CreditGrantResult grantNegative = RuntimeGameplayCommandDirector.TryGrantCredits(-100, "Player");
            if (grantNegative.Success || grantNegative.AmountGranted != 0)
            {
                throw new InvalidOperationException("Negative credit grant must fail.");
            }

            CreditGrantResult grantExcessive = RuntimeGameplayCommandDirector.TryGrantCredits(1000000, "Player");
            if (grantExcessive.Success || grantExcessive.AmountGranted != 0)
            {
                throw new InvalidOperationException("Credit grant exceeding 500,000 cap must fail.");
            }

            string json = AgentDiagnosticsFormatter.SerializeCreditGrantResult(grantSuccess);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("accountName").GetString() != "Player" ||
                    doc.RootElement.GetProperty("amountGranted").GetInt32() != 750)
                {
                    throw new InvalidOperationException("SerializeCreditGrantResult JSON property mismatch.");
                }
            }
        }
        finally
        {
            Bank.Reset();
        }
    }),
    ("gameplay command director dispatches unit move and attack commands and validates targets", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var playerUnit = new EntityController
            {
                name = "player_strike_tank",
                EntityId = "unit:strike-tank-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 250f,
                MaxHealth = 250f,
                Position = new Vector3(10f, 0f, 10f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(playerUnit);

            var enemyUnit = new EntityController
            {
                name = "enemy_raider",
                EntityId = "unit:enemy-raider-01",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 120f,
                MaxHealth = 120f,
                Position = new Vector3(60f, 0f, 80f)
            };
            ExistingControllers.Instance.AiUnits().Add(enemyUnit);

            // Test Unit Move Command
            UnitCommandResult moveRes = RuntimeGameplayCommandDirector.TryOrderUnitMove("unit:strike-tank-01", 35f, 45f);
            if (!moveRes.Success || moveRes.CommandType != "Move" || moveRes.TargetX != 35f || moveRes.TargetZ != 45f)
            {
                throw new InvalidOperationException("TryOrderUnitMove must succeed for existing player unit.");
            }

            if (!playerUnit.LastExecutedCommand.HasValue ||
                playerUnit.LastExecutedCommand.Value.command != EntityCommand.Command.Move ||
                playerUnit.LastExecutedCommand.Value.destination.x != 35f ||
                playerUnit.LastExecutedCommand.Value.destination.z != 45f)
            {
                throw new InvalidOperationException("Player unit must have received EntityCommand.Move with destination (35, 45).");
            }

            // Test Unit Move on non-existent unit
            UnitCommandResult moveMissing = RuntimeGameplayCommandDirector.TryOrderUnitMove("unit:non-existent", 0f, 0f);
            if (moveMissing.Success)
            {
                throw new InvalidOperationException("TryOrderUnitMove must fail for missing entity.");
            }

            // Test Unit Attack Command
            UnitCommandResult attackRes = RuntimeGameplayCommandDirector.TryOrderUnitAttack("unit:strike-tank-01", "unit:enemy-raider-01");
            if (!attackRes.Success || attackRes.CommandType != "AttackMoveOnTarget" ||
                attackRes.TargetX != 60f || attackRes.TargetZ != 80f)
            {
                throw new InvalidOperationException("TryOrderUnitAttack must succeed for existing attacker and target.");
            }

            if (!playerUnit.LastExecutedCommand.HasValue ||
                playerUnit.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnTarget ||
                playerUnit.LastExecutedCommand.Value.target != enemyUnit)
            {
                throw new InvalidOperationException("Player unit must have received EntityCommand.AttackMoveOnTarget with target enemyUnit.");
            }

            // Test Unit Attack with missing target
            UnitCommandResult attackMissingTarget = RuntimeGameplayCommandDirector.TryOrderUnitAttack("unit:strike-tank-01", "unit:missing-target");
            if (attackMissingTarget.Success)
            {
                throw new InvalidOperationException("TryOrderUnitAttack must fail for missing target.");
            }

            // AttackMove must not fall back to the previously selected unit when its key is missing.
            UnitCommandResult attackMoveMissingUnit = RuntimeGameplayCommandDirector.TryOrderAttackMove("unit:non-existent", 25f, 35f);
            if (attackMoveMissingUnit.Success)
            {
                throw new InvalidOperationException("TryOrderAttackMove must fail for a missing entity instead of commanding stale selection.");
            }

            // Verify serialization
            string moveJson = AgentDiagnosticsFormatter.SerializeUnitCommandResult(moveRes);
            using (var doc = JsonDocument.Parse(moveJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("commandType").GetString() != "Move" ||
                    doc.RootElement.GetProperty("targetX").GetSingle() != 35f)
                {
                    throw new InvalidOperationException("SerializeUnitCommandResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("diagnostic catalog contains run progression, credit grant, card change, and unit command endpoints", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            var names = new HashSet<string>();
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            string[] required =
            {
                "GetRunProgressionCensusJson",
                "GetRuntimeControlSurfaceJson",
                "GetIntermissionNativePanelStateJson",
                "OrderBuyIntermissionShopOfferJson",
                "OrderRerollIntermissionShopJson",
                "OrderRemoveIntermissionCardJson",
                "OrderCancelIntermissionCardRemovalJson",
                "GrantPlayerCreditsJson",
                "ApplyInGameCardChangeJson",
                "OrderUnitMoveJson",
                "OrderUnitAttackJson",
                "OrderUnitActiveSkillJson",
                "AdjustEntityVitalityJson",
                "ApplyStatusEffectJson",
                "ClearStatusEffectJson",
                "GetGridMetricsJson",
                "EvaluateGridPositionJson"
            };

            foreach (string req in required)
            {
                if (!names.Contains(req))
                {
                    throw new InvalidOperationException("Diagnostic catalog must include " + req + ".");
                }
            }
        }
    }),
    ("runtime control surface serializer captures scene, frame, UI, conditional intermission, bounds, and section errors", () =>
    {
        var ui = new Dictionary<string, object>
        {
            ["kind"] = "uiAccessibilityCensus",
            ["controls"] = new List<object>
            {
                new Dictionary<string, object> { ["label"] = "Continue Run", ["interactable"] = true }
            }
        };

        string menuJson = AgentDiagnosticsFormatter.SerializeRuntimeControlSurface(
            "MainMenu", 812, 1000, ui, null, null, null, null, null);
        using (JsonDocument doc = JsonDocument.Parse(menuJson))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "runtimeControlSurface"
                || root.GetProperty("activeSceneName").GetString() != "MainMenu"
                || root.GetProperty("frameCount").GetInt32() != 812
                || root.GetProperty("maxUiResults").GetInt32() != 100
                || root.GetProperty("uiCensus").GetProperty("controls")[0].GetProperty("label").GetString() != "Continue Run"
                || root.GetProperty("isIntermissionActive").GetBoolean()
                || root.GetProperty("intermission").GetProperty("included").GetBoolean()
                || root.GetProperty("intermission").TryGetProperty("nodeCensus", out _))
            {
                throw new InvalidOperationException("Menu control-surface snapshot must retain scene/frame/UI and omit intermission reads.");
            }
        }

        var rewardError = new Dictionary<string, object>
        {
            ["error"] = "section_capture_failed",
            ["section"] = "rewardAndDeckState"
        };
        var nativePanels = new Dictionary<string, object>
        {
            ["kind"] = "intermissionNativePanelState",
            ["isShopActive"] = true,
            ["shopOfferCount"] = 2
        };
        string intermissionJson = AgentDiagnosticsFormatter.SerializeRuntimeControlSurface(
            "IntermissionScreen", 913, 0,
            new Dictionary<string, object> { ["error"] = "section_capture_failed", ["section"] = "uiCensus" },
            new Dictionary<string, object> { ["kind"] = "runProgressionCensus", ["currentLevel"] = 3 },
            new Dictionary<string, object> { ["kind"] = "intermissionCensus", ["nodeCount"] = 4 },
            new Dictionary<string, object> { ["kind"] = "intermissionGraph", ["routeCount"] = 2 },
            rewardError,
            new Dictionary<string, object> { ["kind"] = "navigatorState", ["isPaused"] = false },
            nativePanels);
        using (JsonDocument doc = JsonDocument.Parse(intermissionJson))
        {
            JsonElement root = doc.RootElement;
            JsonElement intermission = root.GetProperty("intermission");
            if (root.GetProperty("frameCount").GetInt32() != 913
                || root.GetProperty("maxUiResults").GetInt32() != 1
                || !root.GetProperty("isIntermissionActive").GetBoolean()
                || !intermission.GetProperty("included").GetBoolean()
                || intermission.GetProperty("runProgression").GetProperty("currentLevel").GetInt32() != 3
                || intermission.GetProperty("nodeCensus").GetProperty("nodeCount").GetInt32() != 4
                || intermission.GetProperty("graphAndRoutes").GetProperty("routeCount").GetInt32() != 2
                || intermission.GetProperty("rewardAndDeckState").GetProperty("error").GetString() != "section_capture_failed"
                || intermission.GetProperty("navigator").GetProperty("isPaused").GetBoolean()
                || root.GetProperty("nativePanelState").GetProperty("shopOfferCount").GetInt32() != 2
                || intermission.GetProperty("nativePanels").GetProperty("isShopActive").GetBoolean() != true)
            {
                throw new InvalidOperationException("Intermission control-surface snapshot must retain bounded sections, native panels, and independent errors.");
            }
        }
    }),
    ("intermission node-flow classifier keeps rewards and unknown nodes out of the combat corridor", () =>
    {
        var reward = new IntermissionNodeDetail(
            true, 1, 2, true, false, true, false, false, false, false,
            false, false, false, false, false, string.Empty, 0, 0, 1);
        var blueprintReward = new IntermissionNodeDetail(
            true, 1, 3, true, false, false, false, false, false, false,
            false, false, false, false, false, "BlueprintPool_1_3", 0, 0, 1);
        var combat = new IntermissionNodeDetail(
            true, 2, 0, true, false, false, false, false, false, false,
            false, false, false, false, false, string.Empty, 0, 1, 1);
        var unknown = IntermissionNodeDetail.NotFound(8, 9);

        if (RuntimeIntermissionDirector.ClassifyNodeFlow(reward, true) != IntermissionNodeFlowKind.Reward
            || RuntimeIntermissionDirector.ClassifyNodeFlow(blueprintReward, true) != IntermissionNodeFlowKind.Reward
            || RuntimeIntermissionDirector.ClassifyNodeFlow(combat, true) != IntermissionNodeFlowKind.Combat
            || RuntimeIntermissionDirector.ClassifyNodeFlow(combat, false) != IntermissionNodeFlowKind.Unknown
            || RuntimeIntermissionDirector.ClassifyNodeFlow(unknown, true) != IntermissionNodeFlowKind.Unknown
            || RuntimeIntermissionDirector.ShouldStartCombatLoadNextLevelCorridor(IntermissionNodeFlowKind.Reward)
            || RuntimeIntermissionDirector.ShouldStartCombatLoadNextLevelCorridor(IntermissionNodeFlowKind.Unknown)
            || !RuntimeIntermissionDirector.ShouldStartCombatLoadNextLevelCorridor(IntermissionNodeFlowKind.Combat))
        {
            throw new InvalidOperationException("Reward and unknown node kinds must fail the LoadNextLevel corridor gate.");
        }

        string rewardEntryJson = AgentDiagnosticsFormatter.SerializeIntermissionLevelEntryResult(
            new IntermissionLevelEntry(1, 2, 33, true, "reward resolved", true, false, "Reward", true, true));
        using (JsonDocument doc = JsonDocument.Parse(rewardEntryJson))
        {
            JsonElement root = doc.RootElement;
            if (!root.GetProperty("success").GetBoolean()
                || !root.GetProperty("selected").GetBoolean()
                || root.GetProperty("entered").GetBoolean()
                || root.GetProperty("nodeFlowKind").GetString() != "Reward"
                || !root.GetProperty("nativeCallInvoked").GetBoolean()
                || !root.GetProperty("effectObserved").GetBoolean())
            {
                throw new InvalidOperationException("Reward node entry result must distinguish reward resolution from combat entry.");
            }
        }
    }),
    ("native intermission panel serializer preserves bounded Shop and removal details", () =>
    {
        var panelState = new IntermissionNativePanelState(
            true, true, true, 44, 90, 2, 3, 1, 7, true,
            2, 1, 3,
            new[] { new IntermissionShopOfferRow("RelicCard:44", "RelicCard", "Relic_01", "Shield", "20", 44, true, 20) },
            true, true, false, 100,
            2, 1, 0,
            new[] { new IntermissionRemovalCardRow(0, "Unit_Tank", "Unit_Tank", "Tank", 91) });
        string json = AgentDiagnosticsFormatter.SerializeIntermissionNativePanelState(panelState);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            JsonElement root = doc.RootElement;
            if (!root.GetProperty("isAvailable").GetBoolean()
                || root.GetProperty("omittedShopOfferCount").GetInt32() != 1
                || root.GetProperty("unsupportedShopOfferCount").GetInt32() != 3
                || root.GetProperty("shopOffers")[0].GetProperty("offerId").GetString() != "RelicCard:44"
                || root.GetProperty("shopOffers")[0].GetProperty("priceText").GetString() != "20"
                || root.GetProperty("omittedRemovalCardCount").GetInt32() != 1
                || root.GetProperty("removalCards")[0].GetProperty("entityId").GetString() != "Unit_Tank"
                || root.GetProperty("isRemoveCardClickReady").GetBoolean())
            {
                throw new InvalidOperationException("Native panel serializer must retain bounded current offers, removal candidates, and readiness.");
            }
            if (json.IndexOf("\"omittedShopOfferCount\"", StringComparison.Ordinal)
                > json.IndexOf("\"shopOffers\"", StringComparison.Ordinal)
                || json.IndexOf("\"omittedRemovalCardCount\"", StringComparison.Ordinal)
                > json.IndexOf("\"removalCards\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Native panel omission counters must precede their detail arrays.");
            }
        }

        string invokedUnknownJson = AgentDiagnosticsFormatter.SerializeIntermissionActionResult(
            new IntermissionActionResult(false, "BuyShopOffer", "RelicCard:44", -1, "", false,
                "effect pending", true, false));
        using (JsonDocument doc = JsonDocument.Parse(invokedUnknownJson))
        {
            if (!doc.RootElement.GetProperty("nativeCallInvoked").GetBoolean()
                || doc.RootElement.GetProperty("effectObserved").GetBoolean())
            {
                throw new InvalidOperationException("Native panel action result must distinguish a dispatched call from an observed effect.");
            }
        }
    }),
    ("gameplay command director dispatches active skill commands across Self, Target, and Position modes", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var caster = new EntityController
            {
                name = "test_arc_caster",
                EntityId = "unit:arc-caster-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(15f, 0f, 20f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(caster);

            var enemyTarget = new EntityController
            {
                name = "test_enemy_mech",
                EntityId = "unit:enemy-mech-01",
                IsControlledByAi = true,
                IsUnit = true,
                Position = new Vector3(40f, 0f, 60f)
            };
            ExistingControllers.Instance.AiUnits().Add(enemyTarget);

            // Self-targeted active skill
            ActiveSkillOrderResult selfRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01");
            if (!selfRes.Success || selfRes.SkillTargetMode != "Self" || selfRes.TargetX != 15f || selfRes.TargetZ != 20f)
            {
                throw new InvalidOperationException("Self-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillWithoutTarget)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillWithoutTarget command.");
            }

            // Target-entity active skill
            ActiveSkillOrderResult targetRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetEntityKeyId: "unit:enemy-mech-01");
            if (!targetRes.Success || targetRes.SkillTargetMode != "Target" || targetRes.TargetX != 40f || targetRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("Entity-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillOnTarget ||
                caster.LastExecutedCommand.Value.target != enemyTarget)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillOnTarget command with target.");
            }

            caster.SkillInfo = new SkillInfo { targetOrigin = TargetOrigin.ChosenLocation };
            enemyTarget.Position = new Vector3(44f, 0f, 65f);
            ActiveSkillOrderResult locationTargetRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill(
                "unit:arc-caster-01", targetEntityKeyId: "unit:enemy-mech-01");
            if (!locationTargetRes.Success || locationTargetRes.SkillTargetMode != "Position" ||
                caster.LastExecutedCommand?.command != EntityCommand.Command.ActiveSkillAtPosition ||
                caster.LastExecutedCommand.Value.destination.x != 44f ||
                caster.LastExecutedCommand.Value.destination.z != 65f ||
                locationTargetRes.TargetX != 44f || locationTargetRes.TargetZ != 65f)
            {
                throw new InvalidOperationException("A location skill aimed at an entity must use its live position.");
            }

            caster.SkillInfo.targetOrigin = TargetOrigin.Self;
            ActiveSkillOrderResult selfWithTarget = RuntimeGameplayCommandDirector.TryOrderActiveSkill(
                "unit:arc-caster-01", targetEntityKeyId: "unit:enemy-mech-01");
            if (!selfWithTarget.Success || selfWithTarget.SkillTargetMode != "Self" ||
                caster.LastExecutedCommand?.command != EntityCommand.Command.ActiveSkillWithoutTarget ||
                selfWithTarget.TargetX != caster.Position.x || selfWithTarget.TargetZ != caster.Position.z)
            {
                throw new InvalidOperationException("A native self skill must use the untargeted activation route.");
            }
            caster.SkillInfo = null;

            // Ground position active skill
            ActiveSkillOrderResult posRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetX: 75f, targetZ: 85f);
            if (!posRes.Success || posRes.SkillTargetMode != "Position" || posRes.TargetX != 75f || posRes.TargetZ != 85f)
            {
                throw new InvalidOperationException("Position-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillAtPosition ||
                caster.LastExecutedCommand.Value.destination.x != 75f ||
                caster.LastExecutedCommand.Value.destination.z != 85f)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillAtPosition command with coords.");
            }

            // Failure handling
            ActiveSkillOrderResult missingCaster = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:missing-caster");
            if (missingCaster.Success)
            {
                throw new InvalidOperationException("Skill command must fail when caster is missing.");
            }

            ActiveSkillOrderResult missingTarget = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetEntityKeyId: "unit:missing-target");
            if (missingTarget.Success)
            {
                throw new InvalidOperationException("Skill command must fail when target entity is missing.");
            }

            // Mismatched and non-finite coordinate validation
            ActiveSkillOrderResult partialX = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetX: 75f);
            if (partialX.Success || !partialX.Message.Contains("Both targetX and targetZ"))
            {
                throw new InvalidOperationException("Skill command must fail when targetZ is omitted for positioned skill.");
            }

            ActiveSkillOrderResult partialZ = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetZ: 85f);
            if (partialZ.Success || !partialZ.Message.Contains("Both targetX and targetZ"))
            {
                throw new InvalidOperationException("Skill command must fail when targetX is omitted for positioned skill.");
            }

            ActiveSkillOrderResult nanCoord = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetX: float.NaN, targetZ: 85f);
            if (nanCoord.Success || !nanCoord.Message.Contains("finite"))
            {
                throw new InvalidOperationException("Skill command must fail when coordinates are non-finite.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeActiveSkillOrderResult(posRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("skillTargetMode").GetString() != "Position" ||
                    doc.RootElement.GetProperty("targetX").GetSingle() != 75f)
                {
                    throw new InvalidOperationException("SerializeActiveSkillOrderResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("gameplay command director adjusts vitality: Heal, RepairArmor, ChargeShield, Damage", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var unit = new EntityController
            {
                name = "test_vitality_unit",
                EntityId = "unit:vitality-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 200f,
                CurrentArmor = 50f,
                MaxArmor = 100f,
                CurrentShield = 20f,
                MaxShield = 100f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            // Heal
            VitalityAdjustmentResult healRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Heal", 40f);
            if (!healRes.Success || healRes.NewHealth != 140f)
            {
                throw new InvalidOperationException("Heal adjustment must succeed and increase health to 140.");
            }

            // RepairArmor
            VitalityAdjustmentResult repairRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "RepairArmor", 30f);
            if (!repairRes.Success || repairRes.NewArmor != 80f)
            {
                throw new InvalidOperationException("RepairArmor adjustment must succeed and increase armor to 80.");
            }

            // ChargeShield
            VitalityAdjustmentResult shieldRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "ChargeShield", 50f);
            if (!shieldRes.Success || shieldRes.NewShield != 70f)
            {
                throw new InvalidOperationException("ChargeShield adjustment must succeed and increase shield to 70.");
            }

            // Damage with Armor Absorption (60 damage: armor drops from 80 to 20, health untouched at 140)
            VitalityAdjustmentResult damageRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Damage", 60f, ignoreArmor: false);
            if (!damageRes.Success || damageRes.NewArmor != 20f || damageRes.NewHealth != 140f)
            {
                throw new InvalidOperationException("Damage adjustment must reduce armor first before health.");
            }

            // Damage Bypassing Armor (50 damage: armor remains 20, health drops from 140 to 90)
            VitalityAdjustmentResult directDmgRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Damage", 50f, ignoreArmor: true);
            if (!directDmgRes.Success || directDmgRes.NewHealth != 90f || directDmgRes.NewArmor != 20f)
            {
                throw new InvalidOperationException("Armor-bypassing damage must directly reduce health.");
            }

            // Negative amount validation
            VitalityAdjustmentResult negativeRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Heal", -20f);
            if (negativeRes.Success)
            {
                throw new InvalidOperationException("Negative vitality adjustment must fail.");
            }

            // Invalid type validation
            VitalityAdjustmentResult invalidTypeRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Explode", 50f);
            if (invalidTypeRes.Success)
            {
                throw new InvalidOperationException("Unsupported adjustment type must fail.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeVitalityAdjustmentResult(healRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("adjustmentType").GetString() != "Heal" ||
                    doc.RootElement.GetProperty("amountApplied").GetSingle() != 40f ||
                    doc.RootElement.GetProperty("newHealth").GetSingle() != 140f)
                {
                    throw new InvalidOperationException("SerializeVitalityAdjustmentResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("gameplay command director applies and clears status effects on units", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var unit = new EntityController
            {
                name = "test_status_unit",
                EntityId = "unit:status-01",
                IsControlledByPlayer = true,
                IsUnit = true
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            // Apply OnFire
            StatusEffectOrderResult fireRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "OnFire", 15f);
            if (!fireRes.Success || !fireRes.IsApplied || fireRes.DurationSeconds != 15f || !unit.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("ApplyStatusEffect for OnFire must succeed.");
            }

            // Apply Stun
            StatusEffectOrderResult stunRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "Stun", 5f);
            if (!stunRes.Success || !unit.HasActiveStatusEffect(StatusEffect.Stun))
            {
                throw new InvalidOperationException("ApplyStatusEffect for Stun must succeed.");
            }

            // Clear OnFire
            StatusEffectOrderResult clearFireRes = RuntimeGameplayCommandDirector.TryClearStatusEffect("unit:status-01", "OnFire");
            if (!clearFireRes.Success || clearFireRes.IsApplied || unit.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("ClearStatusEffect for OnFire must succeed.");
            }
            if (!unit.HasActiveStatusEffect(StatusEffect.Stun))
            {
                throw new InvalidOperationException("Stun effect must remain active after clearing OnFire.");
            }

            // Unknown status effect name
            StatusEffectOrderResult unknownRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "NonExistentEffect");
            if (unknownRes.Success)
            {
                throw new InvalidOperationException("ApplyStatusEffect must fail for unknown effect name.");
            }

            // Missing entity
            StatusEffectOrderResult missingRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:missing", "OnFire");
            if (missingRes.Success)
            {
                throw new InvalidOperationException("ApplyStatusEffect must fail for missing entity.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeStatusEffectOrderResult(fireRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("statusEffectName").GetString() != "OnFire" ||
                    doc.RootElement.GetProperty("durationSeconds").GetSingle() != 15f ||
                    !doc.RootElement.GetProperty("isApplied").GetBoolean())
                {
                    throw new InvalidOperationException("SerializeStatusEffectOrderResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime grid census extracts map bounds and evaluates coordinates against world grid", () =>
    {
        GridMetricsCensus metrics = RuntimeGridCensus.Current();
        if (!metrics.IsGridReady || metrics.WorldWidth != 200f || metrics.WorldHeight != 200f ||
            metrics.HorizontalCellCount != 100 || metrics.VerticalCellCount != 100 || metrics.CellSize != 2f ||
            metrics.PlayerStartX != 10 || metrics.PlayerStartY != 10 ||
            metrics.AiStartX != 90 || metrics.AiStartY != 90)
        {
            throw new InvalidOperationException("RuntimeGridCensus.Current must extract accurate world dimensions and start cells.");
        }

        // Evaluate In-Bounds Position
        GridCellEvaluationResult inBounds = RuntimeGridCensus.EvaluatePosition(50f, 60f);
        if (!inBounds.IsWithinWorld || inBounds.ClampedX != 50f || inBounds.ClampedZ != 60f ||
            inBounds.CellX != 25 || inBounds.CellY != 30)
        {
            throw new InvalidOperationException("EvaluatePosition for in-bounds coordinates (50, 60) must succeed with cell (25, 30).");
        }

        // Evaluate Out-of-Bounds Position
        GridCellEvaluationResult outBounds = RuntimeGridCensus.EvaluatePosition(250f, -10f);
        if (outBounds.IsWithinWorld || outBounds.ClampedX != 200f || outBounds.ClampedZ != 0f)
        {
            throw new InvalidOperationException("EvaluatePosition for out-of-bounds coordinates must clamp to grid edge.");
        }

        // Serialization
        string metricsJson = AgentDiagnosticsFormatter.SerializeGridMetricsCensus(metrics);
        using (var doc = JsonDocument.Parse(metricsJson))
        {
            if (!doc.RootElement.GetProperty("isGridReady").GetBoolean() ||
                doc.RootElement.GetProperty("worldWidth").GetSingle() != 200f ||
                doc.RootElement.GetProperty("playerStartX").GetInt32() != 10)
            {
                throw new InvalidOperationException("SerializeGridMetricsCensus JSON mismatch.");
            }
        }

        string evalJson = AgentDiagnosticsFormatter.SerializeGridCellEvaluationResult(inBounds);
        using (var doc = JsonDocument.Parse(evalJson))
        {
            if (!doc.RootElement.GetProperty("isWithinWorld").GetBoolean() ||
                doc.RootElement.GetProperty("cellX").GetInt32() != 25 ||
                doc.RootElement.GetProperty("cellY").GetInt32() != 30)
            {
                throw new InvalidOperationException("SerializeGridCellEvaluationResult JSON mismatch.");
            }
        }
    }),
    ("runtime selection census and tactical command director manage unit selection and stance orders", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();
            PlayerUnitController.Instance.DeselectAllUnits();

            var unit1 = new EntityController { EntityId = "unit_alpha", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
            unit1.gameObject.name = "unit_alpha";
            var unit2 = new EntityController { EntityId = "unit_beta", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
            unit2.gameObject.name = "unit_beta";
            var harvester = new EntityController { EntityId = "harvester_1", tag = "Player", CurrentHealth = 80f, MaxHealth = 80f };
            harvester.gameObject.name = "harvester_1";

            ExistingControllers.Instance.PlayerUnits().Add(unit1);
            ExistingControllers.Instance.PlayerUnits().Add(unit2);
            ExistingControllers.Instance.Harvesters.Add(harvester);

            // Select units by keys
            SelectionCommandResult selectRes = RuntimeGameplayCommandDirector.TrySelectEntities(new[] { "unit_alpha", "unit_beta" });
            if (!selectRes.Success || selectRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TrySelectEntities should select 2 entities.");
            }

            SelectionCensus census = RuntimeSelectionCensus.Current();
            if (!census.IsSelectionReady || census.SelectedCount != 2 || census.SelectedEntityKeys.Count != 2)
            {
                throw new InvalidOperationException("RuntimeSelectionCensus.Current should report 2 selected units.");
            }

            // Assign Control Group 1
            SelectionCommandResult assignGroupRes = RuntimeGameplayCommandDirector.TryAssignControlGroup(1);
            if (!assignGroupRes.Success || assignGroupRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TryAssignControlGroup should bind 2 units to group 1.");
            }

            census = RuntimeSelectionCensus.Current();
            if (census.ControlGroupCounts[1] != 2)
            {
                throw new InvalidOperationException("ControlGroupCounts[1] should report 2 units.");
            }

            // Deselect all
            SelectionCommandResult deselectRes = RuntimeGameplayCommandDirector.TryDeselectAll();
            if (!deselectRes.Success)
            {
                throw new InvalidOperationException("TryDeselectAll should succeed.");
            }

            census = RuntimeSelectionCensus.Current();
            if (census.SelectedCount != 0)
            {
                throw new InvalidOperationException("Selection count should be 0 after DeselectAll.");
            }

            // Select Control Group 1
            SelectionCommandResult recallGroupRes = RuntimeGameplayCommandDirector.TrySelectControlGroup(1);
            if (!recallGroupRes.Success || recallGroupRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TrySelectControlGroup should recall 2 units.");
            }

            census = RuntimeSelectionCensus.Current();

            // Select Category Harvesters
            SelectionCommandResult catRes = RuntimeGameplayCommandDirector.TrySelectCategory("Harvesters");
            if (!catRes.Success)
            {
                throw new InvalidOperationException("TrySelectCategory Harvesters should succeed.");
            }

            // Orders Stop & Hold Position
            UnitCommandResult stopRes = RuntimeGameplayCommandDirector.TryOrderStop("unit_alpha");
            if (!stopRes.Success || stopRes.CommandType != "Stop")
            {
                throw new InvalidOperationException("TryOrderStop should succeed.");
            }

            UnitCommandResult holdRes = RuntimeGameplayCommandDirector.TryOrderHoldPosition();
            if (!holdRes.Success || holdRes.CommandType != "HoldPosition")
            {
                throw new InvalidOperationException("TryOrderHoldPosition should succeed.");
            }

            UnitCommandResult attackMoveRes = RuntimeGameplayCommandDirector.TryOrderAttackMove("unit_alpha", 50f, 60f);
            if (!attackMoveRes.Success || attackMoveRes.TargetX != 50f || attackMoveRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("TryOrderAttackMove should succeed with target coords (50, 60).");
            }

            // Serialization check
            string json = AgentDiagnosticsFormatter.SerializeSelectionCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("isSelectionReady").GetBoolean() ||
                    doc.RootElement.GetProperty("selectedCount").GetInt32() != 2 ||
                    doc.RootElement.GetProperty("controlGroupCounts")[1].GetInt32() != 2)
                {
                    throw new InvalidOperationException("SerializeSelectionCensus JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            PlayerUnitController.Instance.DeselectAllUnits();
        }
    }),
    ("runtime fog census and fog command director safely toggle and reveal fog of war", () =>
    {
        FogStateCensus state = RuntimeFogCensus.Current();
        if (!state.IsAvailable || !state.IsActive || state.IsDisabled)
        {
            throw new InvalidOperationException("RuntimeFogCensus.Current should report available and active Fog of War.");
        }

        // Disable Fog of War
        FogCommandResult disableRes = RuntimeGameplayCommandDirector.TrySetFogOfWar(true);
        if (!disableRes.Success || !disableRes.IsDisabled || !UpdateFogOfWar.RevealedStaticCalled)
        {
            throw new InvalidOperationException("TrySetFogOfWar(true) should disable fog and call Reveal_Static.");
        }

        state = RuntimeFogCensus.Current();
        if (!state.IsDisabled || state.IsActive)
        {
            throw new InvalidOperationException("RuntimeFogCensus should report fog disabled.");
        }

        // Reveal Fog of War
        FogCommandResult revealRes = RuntimeGameplayCommandDirector.TryRevealFogOfWar();
        if (!revealRes.Success)
        {
            throw new InvalidOperationException("TryRevealFogOfWar should succeed.");
        }

        // Re-enable Fog of War
        RuntimeGameplayCommandDirector.TrySetFogOfWar(false);

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeFogStateCensus(state);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAvailable").GetBoolean() ||
                !doc.RootElement.GetProperty("isDisabled").GetBoolean())
            {
                throw new InvalidOperationException("SerializeFogStateCensus JSON mismatch.");
            }
        }
    }),
    ("runtime game speed match control and entity spawning directors execute commands without GUI", () =>
    {
        try
        {
            // Game Speed must fail closed for non-finite input.
            UnityEngine.Time.timeScale = 1.0f;
            GameSpeedCommandResult invalidScale = RuntimeGameplayCommandDirector.TrySetGameTimeScale(float.NaN);
            if (invalidScale.Success || RuntimeGameplayCommandDirector.GetGameTimeScale() != 1.0f)
            {
                throw new InvalidOperationException("TrySetGameTimeScale must reject NaN without changing Time.timeScale.");
            }

            invalidScale = RuntimeGameplayCommandDirector.TrySetGameTimeScale(float.PositiveInfinity);
            if (invalidScale.Success || RuntimeGameplayCommandDirector.GetGameTimeScale() != 1.0f)
            {
                throw new InvalidOperationException("TrySetGameTimeScale must reject infinity without changing Time.timeScale.");
            }

            // Game Speed
            GameSpeedCommandResult speedRes = RuntimeGameplayCommandDirector.TrySetGameTimeScale(2.5f);
            if (!speedRes.Success || speedRes.CurrentTimeScale != 2.5f || RuntimeGameplayCommandDirector.GetGameTimeScale() != 2.5f)
            {
                throw new InvalidOperationException("TrySetGameTimeScale should update Time.timeScale to 2.5f.");
            }

            RuntimeGameplayCommandDirector.TrySetGameTimeScale(1.0f);

            // Match Control
            FinishLevel.Reset();
            MatchControlResult winRes = RuntimeGameplayCommandDirector.TryTriggerMatchEnd(true);
            if (!winRes.Success || winRes.Outcome != "Win" || !FinishLevel.WonCalled)
            {
                throw new InvalidOperationException("TryTriggerMatchEnd(true) should invoke victory sequence.");
            }

            FinishLevel.Reset();

            // Entity Spawning
            EntitySpawnResult spawnRes = RuntimeGameplayCommandDirector.TrySpawnEntity("SampleScout", "Player", 45f, 55f, "SpawnedScout");
            if (!spawnRes.Success || spawnRes.EntityId != "SampleScout" || spawnRes.Side != "Player" ||
                spawnRes.SpawnedX != 45f || spawnRes.SpawnedZ != 55f || spawnRes.Health != 100f)
            {
                throw new InvalidOperationException("TrySpawnEntity should spawn SampleScout at (45, 55) with 100 health.");
            }

            // Serialization checks
            string speedJson = AgentDiagnosticsFormatter.SerializeGameSpeedCommandResult(speedRes);
            using (var doc = JsonDocument.Parse(speedJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("currentTimeScale").GetSingle() != 2.5f)
                {
                    throw new InvalidOperationException("SerializeGameSpeedCommandResult JSON mismatch.");
                }
            }

            string spawnJson = AgentDiagnosticsFormatter.SerializeEntitySpawnResult(spawnRes);
            using (var doc = JsonDocument.Parse(spawnJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("entityId").GetString() != "SampleScout" ||
                    doc.RootElement.GetProperty("side").GetString() != "Player")
                {
                    throw new InvalidOperationException("SerializeEntitySpawnResult JSON mismatch.");
                }
            }
        }
        finally
        {
            FinishLevel.Reset();
        }
    }),
    ("runtime entity catalog census queries registered balancing units buildings and factories", () =>
    {
        EntityBalancingStore.RolesOf["Unit_Prowler"] = UnitRole.Unit;
        EntityBalancingStore.RolesOf["Unit_Striker"] = UnitRole.Scout;
        EntityBalancingStore.BuildingEntityIds.Add("Building_Turret");
        EntityBalancingStore.BuildingEntityIds.Add("Building_Radar");
        EntityBalancingStore.FactoryEntityIdOf["Factory_Vehicles"] = "Building_Factory";

        EntityCatalogCensus catalog = RuntimeEntityCatalogCensus.Current();
        if (!catalog.IsCatalogReady || catalog.TotalUnits < 2 || catalog.TotalBuildings < 2 || catalog.TotalFactories < 1)
        {
            throw new InvalidOperationException("RuntimeEntityCatalogCensus should report registered units, buildings, and factories.");
        }

        if (!catalog.UnitIds.Contains("Unit_Prowler") || !catalog.BuildingIds.Contains("Building_Turret"))
        {
            throw new InvalidOperationException("EntityCatalogCensus should contain Unit_Prowler and Building_Turret.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeEntityCatalogCensus(catalog);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isCatalogReady").GetBoolean() ||
                doc.RootElement.GetProperty("totalUnits").GetInt32() < 2 ||
                doc.RootElement.GetProperty("totalBuildings").GetInt32() < 2)
            {
                throw new InvalidOperationException("SerializeEntityCatalogCensus JSON mismatch.");
            }
        }

        // Verify Diagnostic Catalog includes new getters
        string catalogJson = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(catalogJson))
        {
            var items = doc.RootElement.GetProperty("diagnostics");
            var names = new HashSet<string>();
            foreach (var item in items.EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetSelectionCensusJson") ||
                !names.Contains("GetFogStateJson") ||
                !names.Contains("GetEntityCatalogJson") ||
                !names.Contains("SelectUnitsJson") ||
                !names.Contains("DeselectAllUnitsJson") ||
                !names.Contains("SetGameTimeScaleJson") ||
                !names.Contains("SpawnEntityJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog should include newly exposed getters.");
            }
        }
    }),
("runtime ai director census and command director observe and manipulate attack waves and debug visuals", () =>
    {
        AiDirectorCensus census = RuntimeAiDirectorCensus.Current();
        if (!census.IsAiDirectorAvailable || census.CurrentAttackWaveNumber < 0)
        {
            throw new InvalidOperationException("RuntimeAiDirectorCensus.Current should report available AI director.");
        }

        // Set wave number
        AiDirectorCommandResult setRes = RuntimeGameplayCommandDirector.TrySetAttackWaveNumber(7);
        if (!setRes.Success || setRes.WaveNumber != 7)
        {
            throw new InvalidOperationException("TrySetAttackWaveNumber(7) should update wave number to 7.");
        }

        census = RuntimeAiDirectorCensus.Current();
        if (census.CurrentAttackWaveNumber != 7)
        {
            throw new InvalidOperationException("CurrentAttackWaveNumber should be 7.");
        }

        // Increment wave
        AiDirectorCommandResult incRes = RuntimeGameplayCommandDirector.TryIncrementAttackWave();
        if (!incRes.Success || incRes.WaveNumber != 8)
        {
            throw new InvalidOperationException("TryIncrementAttackWave should increment wave to 8.");
        }

        // Toggle debug visuals
        AiDirectorCommandResult visRes = RuntimeGameplayCommandDirector.TryToggleAiDebugVisuals(true, true);
        if (!visRes.Success)
        {
            throw new InvalidOperationException("TryToggleAiDebugVisuals should succeed.");
        }

        census = RuntimeAiDirectorCensus.Current();
        if (!census.ShowAiTags || !census.ShowCurrentCommands)
        {
            throw new InvalidOperationException("ShowAiTags and ShowCurrentCommands should be true.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeAiDirectorCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAiDirectorAvailable").GetBoolean() ||
                doc.RootElement.GetProperty("currentAttackWaveNumber").GetInt32() != 8 ||
                !doc.RootElement.GetProperty("showAiTags").GetBoolean())
            {
                throw new InvalidOperationException("SerializeAiDirectorCensus JSON mismatch.");
            }
        }
    }),
    ("runtime camera census and director move focus and zoom camera viewport without GUI", () =>
    {
        CameraCensus census = RuntimeCameraCensus.Current();
        if (!census.IsCameraAvailable)
        {
            throw new InvalidOperationException("RuntimeCameraCensus.Current should report available camera.");
        }

        // Move camera
        CameraCommandResult moveRes = RuntimeGameplayCommandDirector.TryMoveCamera(120f, 180f);
        if (!moveRes.Success || moveRes.TargetX != 120f || moveRes.TargetZ != 180f)
        {
            throw new InvalidOperationException("TryMoveCamera should report moved target coords.");
        }

        // Set camera zoom
        CameraCommandResult zoomRes = RuntimeGameplayCommandDirector.TrySetCameraZoom(55f);
        if (!zoomRes.Success || zoomRes.FieldOfView != 55f)
        {
            throw new InvalidOperationException("TrySetCameraZoom should update camera FOV to 55f.");
        }

        // Focus camera on entity
        var scout = new EntityController { EntityId = "scout_rover", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
        scout.gameObject.name = "scout_rover";
        scout.transform.position = new Vector3(40f, 0f, 60f);
        ExistingControllers.Instance.PlayerUnits().Add(scout);

        try
        {
            CameraCommandResult focusRes = RuntimeGameplayCommandDirector.TryFocusCameraOnEntity("scout_rover");
            if (!focusRes.Success || focusRes.TargetX != 40f || focusRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("TryFocusCameraOnEntity should succeed focusing scout_rover.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeCameraCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isCameraAvailable").GetBoolean())
            {
                throw new InvalidOperationException("SerializeCameraCensus JSON mismatch.");
            }
        }
    }),
    ("runtime harvester census and director track crystal payloads and dispatch mining orders", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();
            Harvest.ClaimCalled = false;
            Harvest.RefineryCalled = false;

            var harvester = new EntityController { EntityId = "harvester_mining_1", tag = "Player", CurrentHealth = 120f, MaxHealth = 120f };
            harvester.gameObject.name = "harvester_mining_1";
            var harvestComp = harvester.gameObject.AddComponent<Harvest>();
            harvestComp._currentLoad = 40f;
            harvestComp.MaxValue = 100;
            harvestComp._loadFraction = 0.4f;

            ExistingControllers.Instance.Harvesters.Add(harvester);

            HarvesterCensus census = RuntimeHarvesterCensus.Current();
            if (census.TotalHarvesterCount != 1 || census.PlayerHarvesterCount != 1 || census.TotalCrystalsCarried != 40f)
            {
                throw new InvalidOperationException("RuntimeHarvesterCensus should report 1 player harvester with 40 crystals carried.");
            }

            if (census.HarvesterRows.Count != 1 || census.HarvesterRows[0].EntityKeyId != "harvester_mining_1")
            {
                throw new InvalidOperationException("HarvesterRows should include harvester_mining_1.");
            }

            // Command harvest
            HarvesterCommandResult claimRes = RuntimeGameplayCommandDirector.TryOrderHarvesterHarvest("harvester_mining_1");
            if (!claimRes.Success || !Harvest.ClaimCalled)
            {
                throw new InvalidOperationException("TryOrderHarvesterHarvest should invoke ClaimClosestCrystalAndMoveToIt.");
            }

            // Command return to refinery
            HarvesterCommandResult refRes = RuntimeGameplayCommandDirector.TryOrderHarvesterReturn("harvester_mining_1");
            if (!refRes.Success || !Harvest.RefineryCalled)
            {
                throw new InvalidOperationException("TryOrderHarvesterReturn should invoke MoveToClosestRefinery.");
            }

            // Serialization check
            string json = AgentDiagnosticsFormatter.SerializeHarvesterCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("totalHarvesterCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("totalCrystalsCarried").GetSingle() != 40f ||
                    doc.RootElement.GetProperty("omittedHarvesterCount").GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeHarvesterCensus JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            Harvest.ClaimCalled = false;
            Harvest.RefineryCalled = false;
        }
    }),
    ("runtime audio census and volume director query and adjust audio mixer sliders", () =>
    {
        AudioCensus census = RuntimeAudioCensus.Current();
        if (!census.IsAudioAvailable)
        {
            throw new InvalidOperationException("RuntimeAudioCensus.Current should report available audio.");
        }

        // Set master volume
        AudioCommandResult masterRes = RuntimeGameplayCommandDirector.TrySetAudioVolume("Master", 0.55f);
        if (!masterRes.Success || masterRes.VolumeSlider != 0.55f || AudioMixerHelper.MasterSlider != 0.55f)
        {
            throw new InvalidOperationException("TrySetAudioVolume(Master, 0.55) should update Master slider to 0.55.");
        }

        // Set music volume
        AudioCommandResult musicRes = RuntimeGameplayCommandDirector.TrySetAudioVolume("Music", 0.35f);
        if (!musicRes.Success || musicRes.VolumeSlider != 0.35f || AudioMixerHelper.MusicSlider != 0.35f)
        {
            throw new InvalidOperationException("TrySetAudioVolume(Music, 0.35) should update Music slider to 0.35.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeAudioCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAudioAvailable").GetBoolean())
            {
                throw new InvalidOperationException("SerializeAudioCensus JSON mismatch.");
            }
        }
    }),
    ("runtime visual surface census reports renderers materials and vertex-wear channels", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "unit:visual-surface",
                UniqueEntityId = 861,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject { name = "VisualSurfaceUnit" };
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { vertexCount = 4, colors = new[] { new Color(0.3f, 0.8f, 0.1f, 1f), new Color(0.4f, 0.7f, 0.2f, 1f) } };
            mf.sharedMesh = mesh;
            var mat = new Material { name = "VehiclePBR", shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" } };
            mr.sharedMaterials = new[] { mat };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualSurfaceCensus census = RuntimeVisualSurfaceCensus.Current();
            if (census.Rows.Count != 1)
            {
                throw new InvalidOperationException("Visual surface census should report one living unit.");
            }

            VisualSurfaceRow row = census.Rows[0];
            if (row.EntityKeyId != "861" || row.RendererCount != 1 ||
                row.MaterialCount != 1 || row.VertexCount != 4 || !row.HasVertexColors ||
                row.ShaderNames.Count != 1 || row.ShaderNames[0] != "RogueCommand/VehiclePBR_VertexWear")
            {
                throw new InvalidOperationException("Visual surface row should report renderer, material, vertex and shader telemetry.");
            }

            string json = AgentDiagnosticsFormatter.SerializeVisualSurfaceCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "visualSurfaceCensus" ||
                    doc.RootElement.GetProperty("rowCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("omittedRowCount").GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeVisualSurfaceCensus JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime visual quality census reports URP bindings and vertex-wear contract metrics", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:visual-quality",
                UniqueEntityId = 862,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject { name = "VisualQualityUnit" };
            var meshFilter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = new Mesh
            {
                name = "VisualQualityMesh",
                vertexCount = 4,
                colors = new[]
                {
                    new Color(0.30f, 0.80f, 0.60f, 1f),
                    new Color(0.40f, 0.70f, 0.20f, 1f),
                }
            };
            var material = new Material
            {
                name = "VisualQualityPbr",
                shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                shaderKeywords = new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" },
                enableInstancing = true
            };
            material.SetTexture("_MetallicGlossMap", new Texture { name = "Quality_MetallicGloss" });
            material.SetTexture("_BumpMap", new Texture { name = "Quality_Normal" });
            material.SetTexture("_EmissionMap", new Texture { name = "Quality_Emission" });
            renderer.sharedMaterials = new[] { material };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualQualityCensus census = RuntimeVisualQualityCensus.Current();
            if (census.Rows.Count != 1 || census.LoaderOwnedUnitCount != 1 ||
                census.PbrContractPassCount != 1 || census.PbrContractIssueCount != 0)
            {
                throw new InvalidOperationException("Visual quality census should pass the URP PBR contract for the namespaced unit.");
            }

            VisualQualityRow row = census.Rows[0];
            if (!row.IsLoaderOwned || !row.ContractPass || row.PbrMaterialCount != 1 ||
                row.MetallicGlossMapCount != 1 || row.NormalMapCount != 1 ||
                row.EmissionMapCount != 1 || row.VertexColorSampleCount != 2 ||
                row.EdgeWearFraction <= 0f || row.ThermalSootFraction <= 0f ||
                row.TeamMaskCoverage != 1f || row.GpuInstancedMaterialCount != 1)
            {
                throw new InvalidOperationException("Visual quality row should expose URP bindings, instancing, and vertex-wear distribution metrics.");
            }

            string json = AgentDiagnosticsFormatter.SerializeVisualQualityCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "visualQualityCensus" ||
                    doc.RootElement.GetProperty("omittedRowCount").GetInt32() != 0 ||
                    doc.RootElement.GetProperty("pbrContractIssueCount").GetInt32() != 0 ||
                    doc.RootElement.GetProperty("rows").GetArrayLength() != 1)
                {
                    throw new InvalidOperationException("SerializeVisualQualityCensus JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime visual quality census accepts active URP rigged overlay materials", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:rigged-urp-overlay",
                UniqueEntityId = 864,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject { name = "RiggedUrpOverlay" };
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                new Material
                {
                    name = "RiggedUrpMaterial",
                    shader = new Shader { name = "Universal Render Pipeline/Lit" }
                }
            };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualQualityCensus census = RuntimeVisualQualityCensus.Current();
            if (census.Rows.Count != 1 || census.Rows[0].UnsupportedShaderCount != 0 ||
                census.Rows[0].QualityIssues.Count != 0)
            {
                throw new InvalidOperationException("Active URP rigged overlay materials must not be classified as unsupported shaders.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime visual quality census ignores disabled native renderer materials", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:disabled-native-materials",
                UniqueEntityId = 863,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject { name = "DisabledNativeVisual" };
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMaterials = new[]
            {
                new Material
                {
                    name = "DisabledNativeMaterial",
                    shader = new Shader { name = "Universal Render Pipeline/Lit" }
                }
            };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualQualityCensus census = RuntimeVisualQualityCensus.Current();
            if (census.Rows.Count != 1 || census.Rows[0].UnsupportedShaderCount != 0 ||
                census.Rows[0].MaterialCount != 0 || census.Rows[0].VertexColorSampleCount != 0 ||
                census.Rows[0].QualityIssues.Count != 0)
            {
                throw new InvalidOperationException("Disabled native renderers must not create active visual quality failures for loader-owned units.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("independent visual censuses deduplicate units shared by player and AI rosters", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var shared = new EntityController
            {
                entityId = "sample-native:shared-visual-roster",
                UniqueEntityId = 8641,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f),
                gameObject = new GameObject("SharedVisualRosterUnit")
            };
            shared.gameObject.AddComponent<MeshRenderer>();
            ExistingControllers.Instance.PlayerUnits().Add(shared);
            ExistingControllers.Instance.AiUnits().Add(shared);

            VisualQualityCensus quality = RuntimeVisualQualityCensus.Current(8);
            VisualSurfaceCensus surface = RuntimeVisualSurfaceCensus.Current(8);
            VisualMaterialCensus material = RuntimeVisualMaterialCensus.Current(8);
            SocketAuditCensus sockets = RuntimeSocketAuditCensus.Current(8);

            if (quality.Rows.Count != 1 || surface.Rows.Count != 1 ||
                material.Rows.Count != 1 || sockets.Rows.Count != 1 ||
                quality.OmittedRowCount != 0 || surface.OmittedRowCount != 0 ||
                material.OmittedRowCount != 0 || sockets.OmittedRowCount != 0)
            {
                throw new InvalidOperationException("Independent visual censuses must process a unit shared by player and AI rosters once.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("standalone visual censuses ignore disabled renderer materials and mesh work", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:disabled-standalone-visual",
                UniqueEntityId = 8642,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject("DisabledStandaloneVisual");
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = new Mesh
            {
                vertexCount = 4,
                colors = new[] { new Color(0.2f, 0.8f, 0.6f, 1f) }
            };
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMaterials = new[]
            {
                new Material
                {
                    name = "DisabledStandaloneMaterial",
                    shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                    mainTexture = new Texture { name = "DisabledStandaloneTexture" }
                }
            };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualSurfaceCensus surface = RuntimeVisualSurfaceCensus.Current(1);
            VisualMaterialCensus material = RuntimeVisualMaterialCensus.Current(1);
            if (surface.Rows.Count != 1 || surface.Rows[0].RendererCount != 1 ||
                surface.Rows[0].RendererEnabledCount != 0 || surface.Rows[0].MaterialCount != 0 ||
                surface.Rows[0].VertexColorSampleCount != 0)
            {
                throw new InvalidOperationException("Disabled renderers must not cause standalone surface material or mesh work.");
            }
            if (material.Rows.Count != 1 || material.Rows[0].RendererCount != 1 ||
                material.Rows[0].MaterialCount != 0 || material.Rows[0].HasMainTextureCount != 0)
            {
                throw new InvalidOperationException("Disabled renderers must not cause standalone material or texture work.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("standalone visual censuses bound mesh color sampling", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:bounded-standalone-meshes",
                UniqueEntityId = 8643,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var root = new GameObject("BoundedStandaloneMeshes");
            for (int i = 0; i < 33; i++)
            {
                var child = new GameObject("MeshChild" + i);
                child.transform.parent = root.transform;
                var meshFilter = child.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = new Mesh
                {
                    vertexCount = 4,
                    colors = new[] { new Color(0.3f, 0.8f, 0.6f, 1f) }
                };
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[]
                {
                    new Material
                    {
                        shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                        shaderKeywords = new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" }
                    }
                };
            }
            unit.gameObject = root;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualSurfaceRow surface = RuntimeVisualSurfaceCensus.Current(1).Rows[0];
            VisualQualityRow quality = RuntimeVisualQualityCensus.Current(1).Rows[0];
            if (surface.VertexColorSampleCount != 32 || !surface.VertexColorSamplingBounded ||
                quality.VertexColorSampleCount != 32 || !quality.VertexColorSamplingBounded)
            {
                throw new InvalidOperationException("Standalone visual censuses must cap mesh color inspections at the bounded limit.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("standalone material census caches binding reflection for shared materials", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var sharedMaterial = new Material
            {
                name = "SharedMaterial",
                shader = new Shader { name = "Standard" },
                shaderKeywords = new[] { "_EMISSION" }
            };
            var sharedTexture = new Texture { name = "SharedTexture" };
            sharedMaterial.mainTexture = sharedTexture;
            sharedMaterial.SetTexture("_EmissionMap", sharedTexture);

            for (int i = 0; i < 2; i++)
            {
                var unit = new EntityController
                {
                    entityId = "sample-native:shared-material",
                    UniqueEntityId = 9100 + i,
                    IsControlledByPlayer = true,
                    IsUnit = true,
                    Position = new Vector3(i, 0f, 0f)
                };
                var root = new GameObject("SharedMaterialUnit" + i);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { sharedMaterial };
                unit.gameObject = root;
                ExistingControllers.Instance.PlayerUnits().Add(unit);
            }

            VisualMaterialCensus census = RuntimeVisualMaterialCensus.Current(2);
            if (census.Rows.Count != 2 || sharedMaterial.HasPropertyCallCount != 4 ||
                sharedMaterial.GetTextureCallCount != 1 ||
                !census.Rows.Any(row => row.EntityKeyId == "9100") ||
                !census.Rows.Any(row => row.EntityKeyId == "9101"))
            {
                throw new InvalidOperationException("Standalone material census must inspect each shared material binding once per sweep.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("standalone material census skips native binding detail when not needed", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var nativeMaterial = new Material
            {
                name = "NativeMaterial",
                shader = new Shader { name = "Shader Graphs/EntityColorShader" },
                shaderKeywords = new[] { "_EMISSION" }
            };
            nativeMaterial.SetTexture("_EmissionMap", new Texture { name = "NativeEmission" });
            var unit = new EntityController
            {
                entityId = "Engineer",
                UniqueEntityId = 9200,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = Vector3.zero
            };
            var root = new GameObject("NativeMaterialUnit");
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { nativeMaterial };
            unit.gameObject = root;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualMaterialRow row = RuntimeVisualMaterialCensus.Current(1).Rows[0];
            if (row.MaterialCount != 1 || row.MaterialNames.Count != 0 ||
                !row.MaterialSamplingBounded || !row.NameSamplingBounded ||
                nativeMaterial.HasPropertyCallCount != 0 || nativeMaterial.GetTextureCallCount != 0)
            {
                throw new InvalidOperationException("Native material rows must avoid detailed binding reflection and expose partial coverage.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime tech catalog census queries registered upgrades research branches and relics", () =>
    {
        TechCatalogCensus census = RuntimeTechCatalogCensus.Current();
        if (!census.IsTechCatalogReady || census.TotalUpgrades < 3 || census.TotalResearch < 2 || census.TotalRelics < 3)
        {
            throw new InvalidOperationException("RuntimeTechCatalogCensus should report registered tech items.");
        }

        if (!census.UpgradeIds.Contains("Upgrade_HullReinforcement") ||
            !census.ResearchIds.Contains("Research_AdvancedRobotics") ||
            !census.RelicIds.Contains("Relic_FoundryCounterweight"))
        {
            throw new InvalidOperationException("Tech catalog should contain expected sample IDs.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeTechCatalogCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isTechCatalogReady").GetBoolean() ||
                doc.RootElement.GetProperty("totalUpgrades").GetInt32() < 3 ||
                doc.RootElement.GetProperty("omittedUpgradeCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("SerializeTechCatalogCensus JSON mismatch.");
            }
        }

        // Verify Diagnostic Catalog includes newest endpoints
        string catalogJson = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(catalogJson))
        {
            var items = doc.RootElement.GetProperty("diagnostics");
            var names = new HashSet<string>();
            foreach (var item in items.EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetAiDirectorCensusJson") ||
                !names.Contains("SetAttackWaveNumberJson") ||
                !names.Contains("IncrementAttackWaveJson") ||
                !names.Contains("ToggleAiDebugVisualsJson") ||
                !names.Contains("GetCameraCensusJson") ||
                !names.Contains("MoveCameraJson") ||
                !names.Contains("FocusCameraOnEntityJson") ||
                !names.Contains("SetCameraZoomJson") ||
                !names.Contains("GetHarvesterCensusJson") ||
                !names.Contains("OrderHarvesterHarvestJson") ||
                !names.Contains("OrderHarvesterReturnJson") ||
                !names.Contains("GetAudioCensusJson") ||
                !names.Contains("SetAudioVolumeJson") ||
                !names.Contains("GetTechCatalogJson") ||
                !names.Contains("GetVeterancyCensusJson") ||
                !names.Contains("OrderGrantCombatXpJson") ||
                !names.Contains("OrderSetVeterancyLevelJson") ||
                !names.Contains("GetCommandAuraCensusJson") ||
                !names.Contains("OrderCommandAuraPulseJson") ||
                !names.Contains("OrderClearCommandAuraLinksJson") ||
                !names.Contains("GetHazardCensusJson") ||
                !names.Contains("OrderSpawnHazardZoneJson") ||
                !names.Contains("OrderClearHazardZonesJson") ||
                !names.Contains("OrderUnitTacticalPulseJson") ||
                !names.Contains("OrderDirectorThreatSweepJson") ||
                !names.Contains("OrderCoordinatedAssaultJson") ||
                !names.Contains("OrderRetreatJson") ||
                !names.Contains("GetUpgradeCensusJson") ||
                !names.Contains("GetRelicCensusJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog should include all newly exposed endpoints.");
            }
        }
    }),
    ("runtime veterancy census and director track XP, promote ranks, and format JSON", () =>
    {
        try
        {
            var pUnit = new EntityController
            {
                entityId = "unit:hero_mech",
                UniqueEntityId = 555,
                IsControlledByPlayer = true,
                IsUnit = true,
                MaxRank = 5,
                CurrentRank = 1,
            };
            ExistingControllers.Instance.PlayerUnits().Add(pUnit);

            VeterancyCensus census = RuntimeVeterancyCensus.Current();
            if (census == null || census.OmittedUnitCount != 0 || census.TotalVeterancyUnitsCount < 1)
            {
                throw new InvalidOperationException("RuntimeVeterancyCensus should report registered unit with rank > 0.");
            }

            // Grant XP command
            VeterancyCommandResult xpRes = RuntimeGameplayCommandDirector.TryGrantEntityCombatXp("555", 3);
            if (!xpRes.Success || xpRes.CurrentRank < 1)
            {
                throw new InvalidOperationException("TryGrantEntityCombatXp should grant XP successfully.");
            }

            // Set Rank command
            VeterancyCommandResult rankRes = RuntimeGameplayCommandDirector.TrySetEntityVeterancyLevel("555", 3);
            if (!rankRes.Success || rankRes.CurrentRank != 3)
            {
                throw new InvalidOperationException("TrySetEntityVeterancyLevel should advance unit to Rank 3.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeVeterancyCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedUnitCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("units", out var unitsProp) ||
                    unitsProp.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeVeterancyCensus JSON format error.");
                }
            }

            string cmdJson = AgentDiagnosticsFormatter.SerializeVeterancyCommandResult(rankRes);
            using (var doc = JsonDocument.Parse(cmdJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("currentRank").GetInt32() != 3)
                {
                    throw new InvalidOperationException("SerializeVeterancyCommandResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime command aura census and director manage captain linkage and squad pulses", () =>
    {
        try
        {
            var captain = new EntityController
            {
                entityId = "unit:squad_captain",
                UniqueEntityId = 601,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(10f, 0f, 10f),
            };
            var follower = new EntityController
            {
                entityId = "unit:squad_grunt",
                UniqueEntityId = 602,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(12f, 0f, 11f),
            };
            ExistingControllers.Instance.PlayerUnits().Add(captain);
            ExistingControllers.Instance.PlayerUnits().Add(follower);

            // Dispatch command aura pulse
            CommandAuraResult pulseRes = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                "601", "Inspire", 15.0f, 30.0f);
            if (!pulseRes.Success || pulseRes.LinkedFollowersCount < 1 || follower.CaptainOfThisEntity != captain)
            {
                throw new InvalidOperationException("TryDispatchCommandAuraPulse should link nearby follower to captain.");
            }

            // Query census
            CommandAuraCensus census = RuntimeCommandAuraCensus.Current();
            if (census == null || census.OmittedAuraCount != 0 || census.TotalActiveAurasCount < 1 || census.TotalLinkedFollowersCount < 1)
            {
                throw new InvalidOperationException("RuntimeCommandAuraCensus should report active aura and linked follower.");
            }

            // Clear links
            CommandAuraResult clearRes = RuntimeGameplayCommandDirector.TryClearCommandAuraLinks("601");
            if (!clearRes.Success || clearRes.LinkedFollowersCount < 1 || follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("TryClearCommandAuraLinks should remove captain link from follower.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeCommandAuraCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedAuraCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("auras", out var aurasProp) ||
                    aurasProp.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeCommandAuraCensus JSON format error.");
                }
            }

            string cmdJson = AgentDiagnosticsFormatter.SerializeCommandAuraResult(pulseRes);
            using (var doc = JsonDocument.Parse(cmdJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("auraType").GetString() != "Inspire")
                {
                    throw new InvalidOperationException("SerializeCommandAuraResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime tactical pulse shockwave damages and stuns nearby enemy units", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            var caster = new EntityController
            {
                entityId = "unit:pulse-caster",
                UniqueEntityId = 801,
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "unit:pulse-enemy",
                UniqueEntityId = 802,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new Vector3(4f, 0f, 0f)
            };
            var farEnemy = new EntityController
            {
                entityId = "unit:pulse-far-enemy",
                UniqueEntityId = 803,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new Vector3(100f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(caster);
            ExistingControllers.Instance.AiUnits().Add(enemy);
            ExistingControllers.Instance.AiUnits().Add(farEnemy);

            TacticalPulseResult result = RuntimeGameplayCommandDirector.TryExecuteTacticalPulse(
                "801", "Shockwave", 10f, 12f, 2f, 4);
            if (!result.Success || result.PulseType != "Shockwave" || result.AffectedEntityCount != 1 ||
                result.DamageApplied != 12f || result.StatusEffectApplicationCount < 2 ||
                enemy.CurrentHealth != 88f || farEnemy.CurrentHealth != 100f ||
                !enemy.HasActiveStatusEffect(StatusEffect.Stun) ||
                !enemy.HasActiveStatusEffect(StatusEffect.SlowedDown))
            {
                throw new InvalidOperationException("Tactical Shockwave should affect only the nearby enemy with bounded damage and crowd-control effects.");
            }

            string json = AgentDiagnosticsFormatter.SerializeTacticalPulseResult(result);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "tacticalPulseResult" ||
                    !doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("affectedEntityCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("statusEffectApplicationCount").GetInt32() < 2)
                {
                    throw new InvalidOperationException("SerializeTacticalPulseResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime director threat sweep marks only nearby hostile units", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var source = new EntityController
            {
                entityId = "unit:sweep-source",
                UniqueEntityId = 821,
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "unit:sweep-enemy",
                UniqueEntityId = 822,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(4f, 0f, 0f)
            };
            var farEnemy = new EntityController
            {
                entityId = "unit:sweep-far-enemy",
                UniqueEntityId = 823,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(100f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(source);
            ExistingControllers.Instance.AiUnits().Add(enemy);
            ExistingControllers.Instance.AiUnits().Add(farEnemy);

            ThreatSweepResult result = RuntimeGameplayCommandDirector.TryExecuteThreatSweep(
                "821", "Mark", 10f, 4f, 4);
            if (!result.Success || result.SweepType != "Mark" || result.StatusEffectName != "Marked" ||
                result.AffectedEntityCount != 1 || result.TargetEntityKeyIds.Count != 1 ||
                result.TargetEntityKeyIds[0] != "unit:sweep-enemy" ||
                !enemy.HasActiveStatusEffect(StatusEffect.Marked) ||
                farEnemy.HasActiveStatusEffect(StatusEffect.Marked) ||
                source.HasActiveStatusEffect(StatusEffect.Marked))
            {
                throw new InvalidOperationException("Threat sweep should mark only the nearby hostile unit.");
            }

            string json = AgentDiagnosticsFormatter.SerializeThreatSweepResult(result);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "directorThreatSweepResult" ||
                    !doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("affectedEntityCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("targetEntityKeyIds").GetArrayLength() != 1)
                {
                    throw new InvalidOperationException("SerializeThreatSweepResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime director threat sweep rejects non-finite bounds and keeps JSON valid", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var source = new EntityController
            {
                entityId = "unit:sweep-finite-source",
                UniqueEntityId = 824,
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "unit:sweep-finite-enemy",
                UniqueEntityId = 825,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(2f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(source);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            ThreatSweepResult invalidRadius = RuntimeGameplayCommandDirector.TryExecuteThreatSweep(
                "824", "Mark", float.NaN, 5f, 2);
            ThreatSweepResult invalidDuration = RuntimeGameplayCommandDirector.TryExecuteThreatSweep(
                "824", "Suppress", 5f, float.PositiveInfinity, 2);
            if (invalidRadius.Success || invalidDuration.Success ||
                invalidRadius.AffectedEntityCount != 0 || invalidDuration.AffectedEntityCount != 0 ||
                !invalidRadius.Message.Contains("finite") || !invalidDuration.Message.Contains("finite") ||
                enemy.HasActiveStatusEffect(StatusEffect.Marked) ||
                enemy.HasActiveStatusEffect(StatusEffect.SlowedDown))
            {
                throw new InvalidOperationException("Threat sweep must reject non-finite bounds without affecting targets.");
            }

            using (var radiusDoc = JsonDocument.Parse(AgentDiagnosticsFormatter.SerializeThreatSweepResult(invalidRadius)))
            using (var durationDoc = JsonDocument.Parse(AgentDiagnosticsFormatter.SerializeThreatSweepResult(invalidDuration)))
            {
                if (radiusDoc.RootElement.GetProperty("radius").ValueKind != JsonValueKind.Null ||
                    durationDoc.RootElement.GetProperty("durationSeconds").ValueKind != JsonValueKind.Null ||
                    radiusDoc.RootElement.GetProperty("success").GetBoolean() ||
                    durationDoc.RootElement.GetProperty("success").GetBoolean())
                {
                    throw new InvalidOperationException("Non-finite threat sweep results must serialize as valid JSON null values.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime director coordinated assault dispatches only hostile combat attackers", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var target = new EntityController
            {
                entityId = "unit:assault-target",
                UniqueEntityId = 831,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(12f, 0f, 8f)
            };
            var attacker = new EntityController
            {
                entityId = "unit:assault-attacker",
                UniqueEntityId = 832,
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(4f, 0f, 8f)
            };
            var friendlyAttacker = new EntityController
            {
                entityId = "unit:assault-friendly",
                UniqueEntityId = 833,
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new Vector3(6f, 0f, 8f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(attacker);
            ExistingControllers.Instance.AiUnits().Add(target);
            ExistingControllers.Instance.AiUnits().Add(friendlyAttacker);

            CoordinatedAssaultResult result = RuntimeGameplayCommandDirector.TryExecuteCoordinatedAssault(
                "unit:assault-target",
                new[] { "unit:assault-attacker", "unit:assault-friendly", "unit:missing", "unit:assault-attacker" },
                3);
            if (!result.Success || result.TargetEntityKeyId != "unit:assault-target" ||
                result.RequestedAttackerCount != 4 || result.DispatchedAttackerCount != 1 ||
                result.OmittedAttackerCount != 3 || result.DispatchedAttackerEntityKeyIds.Count != 1 ||
                result.DispatchedAttackerEntityKeyIds[0] != "unit:assault-attacker" ||
                attacker.LastExecutedCommand?.command != EntityCommand.Command.AttackMoveOnTarget ||
                attacker.LastExecutedCommand?.target != target ||
                attacker.CurrentCommandTag != "rcl-coordinated-assault" ||
                friendlyAttacker.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("Coordinated assault must dispatch only hostile combat attackers and reject friendly or missing targets.");
            }

            string json = AgentDiagnosticsFormatter.SerializeCoordinatedAssaultResult(result);
            using (var doc = JsonDocument.Parse(json))
            {
                int omissionIndex = json.IndexOf("\"omittedAttackerCount\"", StringComparison.Ordinal);
                int detailIndex = json.IndexOf("\"dispatchedAttackerEntityKeyIds\"", StringComparison.Ordinal);
                if (doc.RootElement.GetProperty("kind").GetString() != "coordinatedAssaultResult" ||
                    doc.RootElement.GetProperty("dispatchedAttackerCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("omittedAttackerCount").GetInt32() != 3 ||
                    doc.RootElement.GetProperty("dispatchedAttackerEntityKeyIds").GetArrayLength() != 1 ||
                    omissionIndex < 0 || detailIndex < 0 || omissionIndex > detailIndex)
                {
                    throw new InvalidOperationException("SerializeCoordinatedAssaultResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime director retreat moves unit away from threat and rejects non-finite bounds", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "unit:retreat-unit",
                UniqueEntityId = 841,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(10f, 0f, 10f)
            };
            var originalPosition = unit.Position;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            RetreatResult invalid = RuntimeGameplayCommandDirector.TryOrderRetreat(
                "unit:retreat-unit", float.NaN, 5f, 8f);
            if (invalid.Success || !invalid.Message.Contains("finite"))
            {
                throw new InvalidOperationException("Retreat must reject non-finite threat coordinates.");
            }

            RetreatResult result = RuntimeGameplayCommandDirector.TryOrderRetreat(
                "unit:retreat-unit", 0f, 0f, 8f);
            if (!result.Success || result.EntityKeyId != "unit:retreat-unit" ||
                result.RetreatX <= originalPosition.x || result.RetreatZ <= originalPosition.z ||
                result.Distance < 3f || result.Distance > 50f ||
                unit.LastExecutedCommand?.command != EntityCommand.Command.Move ||
                unit.CurrentCommandTag != "rcl-retreat")
            {
                throw new InvalidOperationException("Retreat must move the unit away from the threat.");
            }

            string json = AgentDiagnosticsFormatter.SerializeRetreatResult(result);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "retreatResult" ||
                    doc.RootElement.GetProperty("success").GetBoolean() != true ||
                    doc.RootElement.GetProperty("distance").GetSingle() != result.Distance)
                {
                    throw new InvalidOperationException("SerializeRetreatResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime tactical pulse rally and repair affect only nearby allied units", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            var source = new EntityController
            {
                entityId = "unit:pulse-support",
                UniqueEntityId = 811,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var ally = new EntityController
            {
                entityId = "unit:pulse-ally",
                UniqueEntityId = 812,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 50f,
                MaxHealth = 100f,
                Position = new Vector3(3f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "unit:pulse-enemy-rally",
                UniqueEntityId = 813,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new Vector3(3f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(source);
            ExistingControllers.Instance.PlayerUnits().Add(ally);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            TacticalPulseResult rally = RuntimeGameplayCommandDirector.TryExecuteTacticalPulse(
                "811", "Rally", 10f, 20f, 3f, 4);
            if (!rally.Success || rally.AffectedEntityCount != 1 || rally.HealingApplied != 20f ||
                ally.CurrentHealth != 70f || enemy.CurrentHealth != 100f ||
                !ally.HasActiveStatusEffect(StatusEffect.CombatBoost) ||
                !ally.HasActiveStatusEffect(StatusEffect.Turbo))
            {
                throw new InvalidOperationException("Tactical Rally should heal and buff only the nearby allied unit.");
            }

            ally.CurrentHealth = 40f;
            TacticalPulseResult repair = RuntimeGameplayCommandDirector.TryExecuteTacticalPulse(
                "811", "Repair", 10f, 15f, 3f, 4);
            if (!repair.Success || repair.AffectedEntityCount != 1 || repair.HealingApplied != 15f ||
                ally.CurrentHealth != 55f || enemy.CurrentHealth != 100f ||
                !ally.HasActiveStatusEffect(StatusEffect.Robust))
            {
                throw new InvalidOperationException("Tactical Repair should restore only the nearby allied unit and apply Robust.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime command aura expiry removes only the follower links it created", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 20f;
        try
        {
            var captain = new EntityController
            {
                entityId = "test:aura-expiry-captain",
                UniqueEntityId = 761,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var follower = new EntityController
            {
                entityId = "test:aura-expiry-follower",
                UniqueEntityId = 762,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(1f, 0f, 0f)
            };
            var existingCaptain = new EntityController
            {
                entityId = "test:native-captain",
                UniqueEntityId = 763,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(2f, 0f, 0f)
            };
            var existingFollower = new EntityController
            {
                entityId = "test:native-follower",
                UniqueEntityId = 764,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(3f, 0f, 0f),
                CaptainOfThisEntity = existingCaptain
            };
            ExistingControllers.Instance.PlayerUnits().Add(captain);
            ExistingControllers.Instance.PlayerUnits().Add(follower);
            ExistingControllers.Instance.PlayerUnits().Add(existingCaptain);
            ExistingControllers.Instance.PlayerUnits().Add(existingFollower);

            CommandAuraResult pulse = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                "761", "Inspire", 10f, 1f);
            if (!pulse.Success || !ReferenceEquals(follower.CaptainOfThisEntity, captain) ||
                !ReferenceEquals(existingFollower.CaptainOfThisEntity, existingCaptain))
            {
                throw new InvalidOperationException("The initial command aura should link the nearby follower.");
            }

            UnityEngine.Time.time = 20.5f;
            CommandAuraResult renewed = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                "761", "Inspire", 10f, 3f);
            if (!renewed.Success || !ReferenceEquals(follower.CaptainOfThisEntity, captain))
            {
                throw new InvalidOperationException("Renewing a command aura should preserve follower links owned by the prior pulse.");
            }

            UnityEngine.Time.time = 24f;
            CommandAuraCensus expired = RuntimeCommandAuraCensus.Current();
            if (follower.CaptainOfThisEntity != null || expired.TotalActiveAurasCount != 0 ||
                !ReferenceEquals(existingFollower.CaptainOfThisEntity, existingCaptain))
            {
                throw new InvalidOperationException("An expired command aura should release only links it created and leave no active census row.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("runtime hazard commands reject non-finite and invalid bounds without registering zones", () =>
    {
        RuntimeHazardManager.ClearAll();
        try
        {
            int initialCount = RuntimeHazardManager.ActiveZoneCount;
            HazardCommandResult[] invalidResults =
            {
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", float.NaN, 0f, 10f, 10f, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, float.PositiveInfinity, 10f, 10f, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, float.NaN, 10f, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, 10f, float.PositiveInfinity, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, 10f, 10f, float.NaN),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, 0f, 10f, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, 10f, 0f, 1f),
                RuntimeGameplayCommandDirector.TrySpawnHazardZone("Firestorm", 0f, 0f, 10f, 10f, -1f)
            };

            for (int i = 0; i < invalidResults.Length; i++)
            {
                if (invalidResults[i].Success || RuntimeHazardManager.ActiveZoneCount != initialCount)
                {
                    throw new InvalidOperationException("Invalid hazard input was accepted or registered a zone at index " + i + ".");
                }
            }

            string directZoneId;
            if (RuntimeHazardManager.TrySpawnHazardZone("Firestorm", float.NaN, 0f, 10f, 10f, 1f, out directZoneId) ||
                RuntimeHazardManager.ActiveZoneCount != initialCount)
            {
                throw new InvalidOperationException("The hazard manager must reject invalid direct API input without registering a zone.");
            }
        }
        finally
        {
            RuntimeHazardManager.ClearAll();
        }
    }),
    ("runtime hazard ticks ignore non-finite clock values", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeHazardManager.ClearAll();
        try
        {
            var enemy = new EntityController
            {
                entityId = "unit:hazard-clock-enemy",
                UniqueEntityId = 703,
                IsControlledByAi = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f),
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
            ExistingControllers.Instance.AiUnits().Add(enemy);

            HazardCommandResult spawn = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                "Firestorm", 0f, 0f, 10f, 15f, 10f);
            if (!spawn.Success)
            {
                throw new InvalidOperationException("The valid hazard setup did not spawn.");
            }

            RuntimeHazardManager.UpdateTick(0.6f);
            float healthAfterValidTick = enemy.CurrentHealth;
            int activeZonesAfterValidTick = RuntimeHazardManager.ActiveZoneCount;

            RuntimeHazardManager.UpdateTick(float.NaN);
            RuntimeHazardManager.UpdateTick(float.PositiveInfinity);
            if (enemy.CurrentHealth != healthAfterValidTick ||
                RuntimeHazardManager.ActiveZoneCount != activeZonesAfterValidTick)
            {
                throw new InvalidOperationException("Non-finite hazard clock values must not apply effects or expire zones.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeHazardManager.ClearAll();
        }
    }),
    ("runtime battlefield environmental hazard zones enforce trigger colliders and amortized tick rates", () =>

    {
        try
        {
            var enemy = new EntityController
            {
                entityId = "unit:enemy_tank",
                UniqueEntityId = 701,
                IsControlledByPlayer = false,
                IsControlledByAi = true,
                IsUnit = true,
                Position = new Vector3(20f, 0f, 20f),
                CurrentHealth = 100f,
                MaxHealth = 100f,
            };
            var ally = new EntityController
            {
                entityId = "unit:ally_tank",
                UniqueEntityId = 702,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(21f, 0f, 20f),
                CurrentHealth = 50f,
                MaxHealth = 100f,
                CurrentArmor = 10f,
                MaxArmor = 50f,
            };
            ExistingControllers.Instance.AiUnits().Add(enemy);
            ExistingControllers.Instance.PlayerUnits().Add(ally);

            // Spawn firestorm
            HazardCommandResult fireRes = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                "Firestorm", 20f, 20f, 10f, 15f, 20f);
            if (!fireRes.Success || string.IsNullOrEmpty(fireRes.ZoneId))
            {
                throw new InvalidOperationException("TrySpawnHazardZone should succeed and assign zone ID.");
            }

            // Tick firestorm -> enemy should take damage and catch fire
            RuntimeHazardManager.UpdateTick(Time.time + 0.6f);
            if (enemy.CurrentHealth >= 100f || !enemy.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("Firestorm hazard tick should deal damage and apply OnFire status.");
            }

            // Spawn NaniteField
            HazardCommandResult nanoRes = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                "NaniteField", 20f, 20f, 10f, 15f, 15f);
            if (!nanoRes.Success)
            {
                throw new InvalidOperationException("Spawn NaniteField should succeed.");
            }

            // Tick nanite -> ally should heal, repair armor, and receive Regen
            float allyHpBefore = ally.CurrentHealth;
            float allyArmorBefore = ally.CurrentArmor;
            RuntimeHazardManager.UpdateTick(Time.time + 1.2f);
            if (ally.CurrentHealth <= allyHpBefore ||
                ally.CurrentArmor <= allyArmorBefore ||
                !ally.HasActiveStatusEffect(StatusEffect.Regen))
            {
                throw new InvalidOperationException("NaniteField hazard tick should heal, repair armor, and apply Regen to friendly units.");
            }

            // Census check
            HazardZoneCensus census = RuntimeHazardCensus.Current();
            if (census == null || census.OmittedZoneCount != 0 || census.TotalActiveZonesCount < 2)
            {
                throw new InvalidOperationException("RuntimeHazardCensus should report 2 active hazard zones.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeHazardCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedZoneCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("activeZones", out var zonesProp) ||
                    zonesProp.GetArrayLength() < 2)
                {
                    throw new InvalidOperationException("SerializeHazardCensus JSON format error.");
                }
            }

            // Clear hazards
            HazardCommandResult clearRes = RuntimeGameplayCommandDirector.TryClearHazardZones();
            if (!clearRes.Success || RuntimeHazardManager.ActiveZoneCount != 0)
            {
                throw new InvalidOperationException("TryClearHazardZones should remove all active hazard zones.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeHazardManager.ClearAll();
        }
    }),
    ("runtime agent coordinator manages sessions, pings, advisory locks, and clean-room serialization", () =>
    {
        RuntimeAgentCoordinator.ResetForTests();
        UnityEngine.Time.time = 50f;

        try
        {
            // Register Hermes session
            bool regHermes = RuntimeAgentCoordinator.TryRegisterSession(
                "agent:hermes", "Coordinator", "Primary test agent", 50f, out string regMsg);
            if (!regHermes || RuntimeAgentCoordinator.RegisteredSessionCount != 1)
            {
                throw new InvalidOperationException($"Register Hermes agent failed: {regMsg}");
            }

            // Ping from Hermes
            bool pingHermes = RuntimeAgentCoordinator.TryPing("agent:hermes", "Heartbeat 1", 51f, out string pingReply);
            if (!pingHermes || !pingReply.Contains("Pong"))
            {
                throw new InvalidOperationException($"Ping Hermes failed: {pingReply}");
            }

            // Acquire advisory lock
            bool lock1 = RuntimeAgentCoordinator.TryAcquireLock("agent:hermes", "unit:command_tank", 15f, 51f, out string lockMsg1);
            if (!lock1 || RuntimeAgentCoordinator.ActiveLockCount != 1)
            {
                throw new InvalidOperationException($"Acquire lock 1 failed: {lockMsg1}");
            }

            // Second agent fails to lock same resource
            bool lockConflict = RuntimeAgentCoordinator.TryAcquireLock("agent:antigravity", "unit:command_tank", 10f, 52f, out string lockMsg2);
            if (lockConflict)
            {
                throw new InvalidOperationException("Agent antigravity should not acquire locked resource.");
            }

            // Ping from antigravity auto-registers
            bool pingAg = RuntimeAgentCoordinator.TryPing("agent:antigravity", "Hello runtime", 52f, out string pingReply2);
            if (!pingAg || RuntimeAgentCoordinator.RegisteredSessionCount != 2)
            {
                throw new InvalidOperationException("Ping should auto-register second agent.");
            }

            // Antigravity locks different resource
            bool lock2 = RuntimeAgentCoordinator.TryAcquireLock("agent:antigravity", "camera:tactical", 20f, 52f, out string lockMsg3);
            if (!lock2 || RuntimeAgentCoordinator.ActiveLockCount != 2)
            {
                throw new InvalidOperationException($"Acquire lock 2 failed: {lockMsg3}");
            }

            // Snapshot check
            AgentCoordinationSnapshot snapshot = RuntimeAgentCoordinator.GetSnapshot(53f);
            if (snapshot.OmittedSessionCount != 0 || snapshot.OmittedLockCount != 0 ||
                snapshot.ActiveSessions.Count != 2 || snapshot.ActiveLocks.Count != 2)
            {
                throw new InvalidOperationException("Snapshot counts mismatch.");
            }

            // Clean-room JSON serialization
            string json = AgentDiagnosticsFormatter.SerializeAgentCoordinationCensus(snapshot);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var kindProp) ||
                    kindProp.GetString() != "agentCoordinationCensus" ||
                    !doc.RootElement.TryGetProperty("omittedSessionCount", out var omittedSess) ||
                    omittedSess.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("sessions", out var sessArr) ||
                    sessArr.GetArrayLength() != 2 ||
                    !doc.RootElement.TryGetProperty("locks", out var locksArr) ||
                    locksArr.GetArrayLength() != 2)
                {
                    throw new InvalidOperationException($"SerializeAgentCoordinationCensus JSON format error: {json}");
                }
            }

            // Release lock by Hermes
            bool rel1 = RuntimeAgentCoordinator.TryReleaseLock("agent:hermes", "unit:command_tank", out string relMsg);
            if (!rel1 || RuntimeAgentCoordinator.ActiveLockCount != 1)
            {
                throw new InvalidOperationException($"Release lock failed: {relMsg}");
            }

            // Antigravity can now acquire unit:command_tank
            bool lockTakeover = RuntimeAgentCoordinator.TryAcquireLock("agent:antigravity", "unit:command_tank", 10f, 54f, out string takeMsg);
            if (!lockTakeover || RuntimeAgentCoordinator.ActiveLockCount != 2)
            {
                throw new InvalidOperationException($"Takeover lock failed: {takeMsg}");
            }
        }
        finally
        {
            RuntimeAgentCoordinator.ResetForTests();
        }
    }),
    ("runtime mechanics cleans up applied boost keys and enforces veterancy rank bounds", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        try
        {
            var unit = new EntityController
            {
                entityId = "test:veterancy-cap-unit",
                UniqueEntityId = 987,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                CurrentRank = 3,
                MaxRank = 3
            };

            var profile = new UnitRuntimeMechanicsProfile
            {
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 2,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f,
                    levelUpBonus = 0.1f
                }
            };
            RuntimeUnitMechanicsDirector.RegisterProfile("test:veterancy-cap-unit", profile);

            var enemy = new EntityController
            {
                entityId = "test:hostile-target",
                UniqueEntityId = 988,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 50f,
                MaxHealth = 50f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 10f, unit);

            if (unit.CurrentRank != 3)
            {
                throw new InvalidOperationException($"Rank should remain at max (3), was: {unit.CurrentRank}");
            }
            if (RuntimeUnitMechanicsDirector.GetCombatXpForEntity(unit) != 0)
            {
                throw new InvalidOperationException("A unit at its veterancy cap must not accumulate unusable combat XP.");
            }

            RuntimeUnitMechanicsDirector.OnEntityDied(unit);
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime unit loadouts and prespawn pool warming apply dynamic combat variants and pool quotas", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        try
        {
            // Empty census check
            UnitLoadoutAndPoolCensus emptyCensus = RuntimeUnitLoadoutCensus.Current();
            if (emptyCensus == null || emptyCensus.TotalTrackedUnits != 0 || emptyCensus.TotalPoolCount != 0)
            {
                throw new InvalidOperationException("Initial unit loadout and pool census should be empty.");
            }

            // Spawn test unit
            var unit = new EntityController
            {
                entityId = "unit:combat_skirmisher",
                UniqueEntityId = 881,
                IsControlledByPlayer = true,
                IsUnit = true,
                Damage1 = 10f,
                WeaponRange = 10f,
                MoveSpeed = 10f,
                Attack1Cooldown = 1.0f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            // Apply AP loadout
            UnitLoadoutResult apResult = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("881", "AP");
            if (!apResult.Success || !string.Equals(apResult.LoadoutId, "AP", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("TryApplyUnitLoadout should succeed and return AP loadout.");
            }

            // Warm pre-spawn pool
            PrespawnWarmResult warmResult = RuntimeGameplayCommandDirector.TryWarmPrespawnPool("sample-native:sandlance-scout", 5);
            if (!warmResult.Success || warmResult.RequestedCount != 5)
            {
                throw new InvalidOperationException("TryWarmPrespawnPool should record the quota and report success.");
            }
            // Offline, the game assembly is absent, so no instances are actually prespawned;
            // the warmed count must be reported honestly as 0 rather than optimistically as the request.
            if (warmResult.WarmedCount != 0)
            {
                throw new InvalidOperationException("TryWarmPrespawnPool must report warmed counts honestly (0 when the game assembly is unavailable).");
            }

            // Census populated check
            UnitLoadoutAndPoolCensus populated = RuntimeUnitLoadoutCensus.Current();
            if (populated == null || populated.TotalTrackedUnits < 1 || populated.TotalPoolCount < 1)
            {
                throw new InvalidOperationException("Populated census should report living units and pre-spawn pools.");
            }
            if (populated.OmittedUnitCount != 0 || populated.OmittedPoolCount != 0)
            {
                throw new InvalidOperationException("Small pool/unit lists should have 0 omitted counts.");
            }

            // Serialization check
            string json = AgentDiagnosticsFormatter.SerializeUnitLoadoutAndPoolCensus(populated);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "unitLoadoutAndPoolCensus")
                {
                    throw new InvalidOperationException("SerializeUnitLoadoutAndPoolCensus kind must be 'unitLoadoutAndPoolCensus'.");
                }
                if (!doc.RootElement.TryGetProperty("omittedUnitCount", out var oUnit) || oUnit.GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeUnitLoadoutAndPoolCensus omittedUnitCount must be 0.");
                }
                if (!doc.RootElement.TryGetProperty("omittedPoolCount", out var oPool) || oPool.GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeUnitLoadoutAndPoolCensus omittedPoolCount must be 0.");
                }
                if (!doc.RootElement.TryGetProperty("units", out var unitsArray) || unitsArray.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeUnitLoadoutAndPoolCensus units array must not be empty.");
                }
                if (!doc.RootElement.TryGetProperty("pools", out var poolsArray) || poolsArray.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeUnitLoadoutAndPoolCensus pools array must not be empty.");
                }
            }

            // Fail-closed checks
            UnitLoadoutResult missingUnit = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("nonexistent_999", "AP");
            if (missingUnit.Success)
            {
                throw new InvalidOperationException("TryApplyUnitLoadout on missing unit must return false.");
            }

            PrespawnWarmResult emptyPool = RuntimeGameplayCommandDirector.TryWarmPrespawnPool("", 5);
            if (emptyPool.Success)
            {
                throw new InvalidOperationException("TryWarmPrespawnPool on empty entityId must return false.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        }
    }),
    ("runtime unit loadout switching replaces stale modifiers and accepts Standard reset", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:loadout-switch-test",
                UniqueEntityId = 882,
                IsControlledByPlayer = true,
                IsUnit = true,
                MoveSpeed = 10f,
                WeaponRange = 15f,
                Damage1 = 10f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            UnitLoadoutResult ap = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("882", "AP");
            if (!ap.Success)
            {
                throw new InvalidOperationException("AP loadout should apply before testing replacement.");
            }

            UnitLoadoutResult he = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("882", "HE");
            if (!he.Success)
            {
                throw new InvalidOperationException("HE loadout should replace the previous AP loadout.");
            }

            var changesAfterHe = unit.AppliedSpecificValueChanges;
            int damageChanges = changesAfterHe.Count(c => c.Value == EntityController.ChangeableValue.Damage);
            int cooldownChanges = changesAfterHe.Count(c => c.Value == EntityController.ChangeableValue.AttackCooldown);
            int radiusChanges = changesAfterHe.Count(c => c.Value == EntityController.ChangeableValue.EffectRadius);
            if (damageChanges != 1 || cooldownChanges != 1 || radiusChanges != 1)
            {
                throw new InvalidOperationException("Switching loadouts must leave one replacement entry per affected stat, not stale AP and HE entries.");
            }

            var cooldown = changesAfterHe.Single(c => c.Value == EntityController.ChangeableValue.AttackCooldown);
            if (Math.Abs(cooldown.Change.ValueToAdd) > 0.0001f)
            {
                throw new InvalidOperationException("The AP-only cooldown modifier must be neutralized when switching to HE.");
            }

            UnitLoadoutResult standard = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("882", "Standard");
            if (!standard.Success || !string.Equals(standard.LoadoutId, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Standard must be an explicit loadout that resets the active variant.");
            }

            foreach (var change in unit.AppliedSpecificValueChanges)
            {
                if (Math.Abs(change.Change.ValueToAdd) > 0.0001f)
                {
                    throw new InvalidOperationException("Standard loadout must neutralize every prior runtime modifier.");
                }
            }

            if (RuntimeGameplayCommandDirector.GetActiveUnitVariant("882") != "Standard")
            {
                throw new InvalidOperationException("The loadout census state must report Standard after reset.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        }
    }),
    ("runtime declared unit variants are selectable through the loadout director and replace stale modifiers", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        RuntimeUnitVariantRegistry.Clear();
        try
        {
            RuntimeUnitVariantRegistry.Register(
                "sample-native:declared-variant",
                new[]
                {
                    new RuntimeUnitVariant(
                        "ap",
                        "AP",
                        new[]
                        {
                            new RuntimeUnitVariantOverride("damage1", "20"),
                            new RuntimeUnitVariantOverride("attack1Cooldown", "0.5")
                        }),
                    new RuntimeUnitVariant(
                        "scout",
                        "",
                        new[]
                        {
                            new RuntimeUnitVariantOverride("moveSpeed", "15"),
                            new RuntimeUnitVariantOverride("weaponRange", "20")
                        })
                });

            var unit = new EntityController
            {
                entityId = "sample-native:declared-variant",
                UniqueEntityId = 883,
                IsControlledByPlayer = true,
                IsUnit = true,
                Damage1 = 10f,
                Attack1Cooldown = 1f,
                MoveSpeed = 10f,
                WeaponRange = 10f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            UnitLoadoutResult ap = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("883", "ap");
            if (!ap.Success || !string.Equals(ap.LoadoutId, "ap", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A declared unit variant should be selectable through TryApplyUnitLoadout.");
            }

            var apDamage = unit.AppliedSpecificValueChanges.Single(c => c.Value == EntityController.ChangeableValue.Damage);
            var apCooldown = unit.AppliedSpecificValueChanges.Single(c => c.Value == EntityController.ChangeableValue.AttackCooldown);
            if (Math.Abs(apDamage.Change.ValueToAdd - 1f) > 0.0001f || Math.Abs(apCooldown.Change.ValueToAdd + 0.5f) > 0.0001f)
            {
                throw new InvalidOperationException("Declared absolute balance overrides must be converted to relative runtime modifiers.");
            }

            UnitLoadoutResult scout = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("883", "scout");
            if (!scout.Success || !string.Equals(RuntimeGameplayCommandDirector.GetActiveUnitVariant("883"), "scout", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Switching to a declared variant should update the active loadout census state.");
            }

            if (unit.AppliedSpecificValueChanges.Count(c => c.Value == EntityController.ChangeableValue.Damage) != 1 ||
                unit.AppliedSpecificValueChanges.Any(c => c.Value == EntityController.ChangeableValue.Damage && Math.Abs(c.Change.ValueToAdd) > 0.0001f))
            {
                throw new InvalidOperationException("Switching declared variants must neutralize the previous damage modifier.");
            }

            UnitLoadoutResult unsupported = RuntimeGameplayCommandDirector.TryApplyUnitLoadout("883", "unsupported");
            if (unsupported.Success || unsupported.Message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("Unknown declared variants must fail closed rather than becoming generic presets.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeGameplayCommandDirector.ClearUnitLoadouts();
            RuntimeUnitVariantRegistry.Clear();
        }
    }),
    ("runtime AI doctrine coordinator and deck reinforcement dynamically modulate doctrine and base quotas", () =>
    {
        RuntimeGameplayCommandDirector.ClearAiDoctrineAndDeckEntries();
        try
        {
            // Initial state check
            AiDoctrineAndDeckCensus initialCensus = RuntimeAiDoctrineCensus.Current();
            if (initialCensus == null || initialCensus.ActiveDoctrine != "Standard" || initialCensus.AggressionMultiplier != 1.0f)
            {
                throw new InvalidOperationException("Initial AI doctrine census should be Standard with 1.0 aggression multiplier.");
            }

            // Set doctrine to Assault
            AiDoctrineResult doctrineRes = RuntimeGameplayCommandDirector.TrySetAiDoctrine("Assault", 2.5f);
            if (!doctrineRes.Success || doctrineRes.DoctrineName != "Assault" || doctrineRes.AggressionMultiplier != 2.5f)
            {
                throw new InvalidOperationException("TrySetAiDoctrine should succeed and set Assault doctrine.");
            }

            // Inject AI deck unit
            AiDeckInjectionResult injectRes = RuntimeGameplayCommandDirector.TryInjectAiDeckUnit(
                0, "sample-native:heavy-artillery", 2, 4);
            if (!injectRes.Success || injectRes.BaseIndex != 0 || injectRes.UnitId != "sample-native:heavy-artillery" ||
                injectRes.CountAtStart != 2 || injectRes.MaxLiving != 4)
            {
                throw new InvalidOperationException("TryInjectAiDeckUnit should succeed and report parameters.");
            }

            // Census populated check
            AiDoctrineAndDeckCensus populated = RuntimeAiDoctrineCensus.Current();
            if (populated == null || populated.ActiveDoctrine != "Assault" || populated.AggressionMultiplier != 2.5f ||
                populated.TotalDeckEntries < 1)
            {
                throw new InvalidOperationException("Populated AI doctrine census should reflect Assault and injected deck entries.");
            }
            if (populated.OmittedDeckEntryCount != 0)
            {
                throw new InvalidOperationException("Omitted deck entry count must be 0 for small collections.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeAiDoctrineAndDeckCensus(populated);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "aiDoctrineAndDeckCensus")
                {
                    throw new InvalidOperationException("SerializeAiDoctrineAndDeckCensus kind must be 'aiDoctrineAndDeckCensus'.");
                }
                if (!doc.RootElement.TryGetProperty("omittedDeckEntryCount", out var oDeck) || oDeck.GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeAiDoctrineAndDeckCensus omittedDeckEntryCount must be 0.");
                }
                if (!doc.RootElement.TryGetProperty("activeDoctrine", out var actDoc) || actDoc.GetString() != "Assault")
                {
                    throw new InvalidOperationException("SerializeAiDoctrineAndDeckCensus activeDoctrine must be 'Assault'.");
                }
                if (!doc.RootElement.TryGetProperty("deckEntries", out var deckArray) || deckArray.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeAiDoctrineAndDeckCensus deckEntries must not be empty.");
                }
            }

            string doctrineJson = AgentDiagnosticsFormatter.SerializeAiDoctrineResult(doctrineRes);
            using (var doc = JsonDocument.Parse(doctrineJson))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "aiDoctrineResult")
                {
                    throw new InvalidOperationException("SerializeAiDoctrineResult kind must be 'aiDoctrineResult'.");
                }
            }

            string injectJson = AgentDiagnosticsFormatter.SerializeAiDeckInjectionResult(injectRes);
            using (var doc = JsonDocument.Parse(injectJson))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "aiDeckInjectionResult")
                {
                    throw new InvalidOperationException("SerializeAiDeckInjectionResult kind must be 'aiDeckInjectionResult'.");
                }
            }

            // Fail-closed checks
            AiDoctrineResult invalidDoc = RuntimeGameplayCommandDirector.TrySetAiDoctrine("", 1.0f);
            if (invalidDoc.Success)
            {
                throw new InvalidOperationException("TrySetAiDoctrine with empty doctrine name must return false.");
            }

            AiDeckInjectionResult invalidInject = RuntimeGameplayCommandDirector.TryInjectAiDeckUnit(-1, "unit", 1, 1);
            if (invalidInject.Success)
            {
                throw new InvalidOperationException("TryInjectAiDeckUnit with negative base index must return false.");
            }
        }
        finally
        {
            RuntimeGameplayCommandDirector.ClearAiDoctrineAndDeckEntries();
        }
    }),
    ("runtime unit patrol seeds native waypoints and issues a tagged patrol command", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        var unit = new EntityController
        {
            entityId = "unit:patrol_test",
            UniqueEntityId = 911,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        ExistingControllers.Instance.PlayerUnits().Add(unit);

        UnitPatrolResult emptyRoute = RuntimeGameplayCommandDirector.TryOrderUnitPatrol("911", new float[0], new float[0]);
        if (emptyRoute.Success)
        {
            throw new InvalidOperationException("TryOrderUnitPatrol with no waypoints must return false.");
        }

        UnitPatrolResult mismatched = RuntimeGameplayCommandDirector.TryOrderUnitPatrol("911", new[] { 1f, 2f }, new[] { 3f });
        if (mismatched.Success)
        {
            throw new InvalidOperationException("TryOrderUnitPatrol with mismatched waypoint lengths must return false.");
        }

        UnitPatrolResult nonFinite = RuntimeGameplayCommandDirector.TryOrderUnitPatrol(
            "911", new[] { float.NaN }, new[] { float.PositiveInfinity });
        if (nonFinite.Success || unit.PatrolPositions.Count != 0 || unit.LastExecutedCommand != null)
        {
            throw new InvalidOperationException("TryOrderUnitPatrol must reject non-finite waypoints without mutating the route or dispatching a command.");
        }

        UnitPatrolResult ok = RuntimeGameplayCommandDirector.TryOrderUnitPatrol("911", new[] { 10f, 20f, 30f }, new[] { 40f, 50f, 60f });
        if (!ok.Success || ok.WaypointCount != 3 || ok.EntityKeyId != "911")
        {
            throw new InvalidOperationException("TryOrderUnitPatrol should seed 3 waypoints and dispatch a patrol command.");
        }

        if (unit.PatrolPositions == null || unit.PatrolPositions.Count != 3)
        {
            throw new InvalidOperationException("The native patrol route should contain exactly 3 seeded positions.");
        }

        if (!unit.CurrentCommandIsTaggedWith("rcl-command-director") || unit.LastExecutedCommand == null ||
            unit.LastExecutedCommand.Value.command != EntityCommand.Command.Patrol)
        {
            throw new InvalidOperationException("The dispatched command must be a tagged EntityCommand.Patrol.");
        }

        string json = AgentDiagnosticsFormatter.SerializeUnitPatrolResult(ok);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "unitPatrolResult")
            {
                throw new InvalidOperationException("SerializeUnitPatrolResult kind must be 'unitPatrolResult'.");
            }
            if (!doc.RootElement.TryGetProperty("waypointCount", out var wp) || wp.GetInt32() != 3)
            {
                throw new InvalidOperationException("SerializeUnitPatrolResult waypointCount must be 3.");
            }
        }
    }),
    ("runtime group command fans a single command type to every targeted unit", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        var unitA = new EntityController
        {
            entityId = "unit:group_alpha",
            UniqueEntityId = 921,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        var unitB = new EntityController
        {
            entityId = "unit:group_beta",
            UniqueEntityId = 922,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        ExistingControllers.Instance.PlayerUnits().Add(unitA);
        ExistingControllers.Instance.PlayerUnits().Add(unitB);

        GroupCommandResult unsupported = RuntimeGameplayCommandDirector.TryOrderGroupCommand(
            new[] { "921", "922" }, "Barrage", 0f, 0f);
        if (unsupported.Success)
        {
            throw new InvalidOperationException("TryOrderGroupCommand with an unsupported command type must return false.");
        }

        GroupCommandResult empty = RuntimeGameplayCommandDirector.TryOrderGroupCommand(
            new[] { "does_not_exist" }, "Stop", 0f, 0f);
        if (empty.Success || empty.DispatchedCount != 0 || empty.TargetedCount != 1)
        {
            throw new InvalidOperationException("TryOrderGroupCommand should target 1 unit and dispatch to 0 when none exist.");
        }

        GroupCommandResult stop = RuntimeGameplayCommandDirector.TryOrderGroupCommand(
            new[] { "921", "922" }, "Stop", 0f, 0f);
        if (!stop.Success || stop.TargetedCount != 2 || stop.DispatchedCount != 2)
        {
            throw new InvalidOperationException("TryOrderGroupCommand Stop should dispatch to both targeted units.");
        }

        GroupCommandResult move = RuntimeGameplayCommandDirector.TryOrderGroupCommand(
            new[] { "921", "922" }, "Move", 15f, 25f);
        if (!move.Success || move.DispatchedCount != 2)
        {
            throw new InvalidOperationException("TryOrderGroupCommand Move should dispatch to both targeted units.");
        }

        string json = AgentDiagnosticsFormatter.SerializeGroupCommandResult(stop);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "groupCommandResult")
            {
                throw new InvalidOperationException("SerializeGroupCommandResult kind must be 'groupCommandResult'.");
            }
            if (!doc.RootElement.TryGetProperty("dispatchedCount", out var dc) || dc.GetInt32() != 2)
            {
                throw new InvalidOperationException("SerializeGroupCommandResult dispatchedCount must be 2.");
            }
        }
    }),
    ("synthetic gameplay seam plan validates bounded CSV units, formations, and finite coordinates", () =>
    {
        if (!SyntheticGameplayScenarioPlan.TryCreate(
                "unit:alpha, unit:beta",
                "Wedge",
                10f,
                20f,
                4f,
                out SyntheticGameplayScenarioPlan plan,
                out string diagnostic))
        {
            throw new InvalidOperationException("A valid synthetic gameplay plan was rejected: " + diagnostic);
        }

        if (plan.UnitIds.Count != 2 || plan.FormationType != "Wedge" ||
            plan.TargetX != 10f || plan.TargetZ != 20f || plan.Spacing != 4f)
        {
            throw new InvalidOperationException("Synthetic gameplay plan did not preserve its bounded primitive inputs.");
        }

        if (SyntheticGameplayScenarioPlan.TryCreate(
                "unit:alpha,,unit:beta",
                "Wedge",
                10f,
                20f,
                4f,
                out _,
                out diagnostic) || !diagnostic.Contains("empty"))
        {
            throw new InvalidOperationException("Synthetic gameplay plan must reject empty CSV entries.");
        }

        if (SyntheticGameplayScenarioPlan.TryCreate(
                "unit:alpha",
                "Unsupported",
                float.NaN,
                20f,
                4f,
                out _,
                out diagnostic) || !diagnostic.Contains("finite"))
        {
            throw new InvalidOperationException("Synthetic gameplay plan must fail closed for non-finite coordinates.");
        }
    }),
    ("synthetic gameplay scenario result serializes omission counters before bounded evidence", () =>
    {
        var result = new SyntheticGameplayScenarioResult(
            true,
            true,
            2,
            2,
            2,
            2,
            2,
            2,
            2,
            1,
            2,
            1,
            3,
            1,
            0,
            0,
            0,
            "Wedge",
            new[] { "unit:alpha", "unit:beta" },
            new[] { "none" },
            "synthetic scenario complete");
        string json = AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(result);
        using (var doc = JsonDocument.Parse(json))
        {
            var properties = doc.RootElement.EnumerateObject().Select(property => property.Name).ToList();
            if (properties[0] != "kind" || properties[1] != "omittedFailureCount")
            {
                throw new InvalidOperationException("Synthetic scenario omission counter must precede bounded evidence.");
            }
            if (doc.RootElement.GetProperty("kind").GetString() != "syntheticGameplayScenario")
            {
                throw new InvalidOperationException("Synthetic scenario result kind is incorrect.");
            }
            if (doc.RootElement.GetProperty("createdEntityKeys").GetArrayLength() != 2)
            {
                throw new InvalidOperationException("Synthetic scenario created entity keys were not serialized.");
            }
        }
    }),
    ("runtime formation move dispatches calculated offset positions for Line, Wedge, and Box formations", () =>

    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearUnitLoadouts();
        var unitA = new EntityController
        {
            entityId = "unit:form_alpha",
            UniqueEntityId = 931,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        var unitB = new EntityController
        {
            entityId = "unit:form_beta",
            UniqueEntityId = 932,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        var unitC = new EntityController
        {
            entityId = "unit:form_gamma",
            UniqueEntityId = 933,
            IsControlledByPlayer = true,
            IsUnit = true,
        };
        ExistingControllers.Instance.PlayerUnits().Add(unitA);
        ExistingControllers.Instance.PlayerUnits().Add(unitB);
        ExistingControllers.Instance.PlayerUnits().Add(unitC);

        // Empty keys
        FormationOrderResult empty = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new string[0], "Line", 50f, 50f, 4f);
        if (empty.Success || empty.TargetedCount != 0)
        {
            throw new InvalidOperationException("TryOrderFormationMove with empty keys must fail.");
        }

        // Line formation
        FormationOrderResult line = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "Line", 50f, 50f, 4f);
        if (!line.Success || line.TargetedCount != 3 || line.DispatchedCount != 3)
        {
            throw new InvalidOperationException("TryOrderFormationMove Line must dispatch to all 3 units.");
        }

        // Wedge formation
        FormationOrderResult wedge = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "Wedge", 100f, 100f, 5f);
        if (!wedge.Success || wedge.DispatchedCount != 3)
        {
            throw new InvalidOperationException("TryOrderFormationMove Wedge must dispatch to all 3 units.");
        }

        // Box formation
        FormationOrderResult box = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "Box", 0f, 0f, 3f);
        if (!box.Success || box.DispatchedCount != 3)
        {
            throw new InvalidOperationException("TryOrderFormationMove Box must dispatch to all 3 units.");
        }

        // Non-finite coordinate and spacing validation
        FormationOrderResult nanCoord = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "Line", float.NaN, 50f, 4f);
        if (nanCoord.Success || !nanCoord.Message.Contains("finite"))
        {
            throw new InvalidOperationException("TryOrderFormationMove must fail with non-finite targetX.");
        }

        FormationOrderResult infSpacing = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "Line", 50f, 50f, float.PositiveInfinity);
        if (infSpacing.Success || !infSpacing.Message.Contains("finite"))
        {
            throw new InvalidOperationException("TryOrderFormationMove must fail with non-finite spacing.");
        }

        FormationOrderResult invalidFormation = RuntimeGameplayCommandDirector.TryOrderFormationMove(
            new[] { "931", "932", "933" }, "NotAFormation", 50f, 50f, 4f);
        if (invalidFormation.Success || !invalidFormation.Message.Contains("Unsupported"))
        {
            throw new InvalidOperationException("TryOrderFormationMove must fail closed for unsupported formation types.");
        }

        // Serialization
        string json = AgentDiagnosticsFormatter.SerializeFormationOrderResult(line);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "formationOrderResult")
            {
                throw new InvalidOperationException("SerializeFormationOrderResult kind must be 'formationOrderResult'.");
            }
            if (!doc.RootElement.TryGetProperty("formationType", out var fProp) || fProp.GetString() != "Line")
            {
                throw new InvalidOperationException("SerializeFormationOrderResult formationType must be 'Line'.");
            }
            if (!doc.RootElement.TryGetProperty("dispatchedCount", out var dc) || dc.GetInt32() != 3)
            {
                throw new InvalidOperationException("SerializeFormationOrderResult dispatchedCount must be 3.");
            }
        }
    }),
    ("runtime tactical combat stances assigns stance modes and serializes tactical stance census with omission first", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        var unitA = new EntityController
        {
            entityId = "unit:stance_alpha",
            UniqueEntityId = 941,
            IsControlledByPlayer = true,
            IsUnit = true,
            MoveSpeed = 5.0f,
            WeaponRange = 10.0f,
        };
        var unitB = new EntityController
        {
            entityId = "unit:stance_beta",
            UniqueEntityId = 942,
            IsControlledByPlayer = false,
            IsUnit = true,
            MoveSpeed = 4.0f,
            WeaponRange = 12.0f,
        };
        ExistingControllers.Instance.PlayerUnits().Add(unitA);
        ExistingControllers.Instance.AiUnits().Add(unitB);

        // Assign Aggressive to unit A
        UnitTacticalStanceResult resA = RuntimeGameplayCommandDirector.TrySetUnitTacticalStance("941", "Aggressive");
        if (!resA.Success || resA.Stance != "Aggressive" || resA.AffectedUnitCount != 1)
        {
            throw new InvalidOperationException("TrySetUnitTacticalStance should assign Aggressive to unit 941.");
        }

        // Assign Kiting to unit B
        UnitTacticalStanceResult resB = RuntimeGameplayCommandDirector.TrySetUnitTacticalStance("942", "Kiting");
        if (!resB.Success || resB.Stance != "Kiting")
        {
            throw new InvalidOperationException("TrySetUnitTacticalStance should assign Kiting to unit 942.");
        }

        // Invalid stance
        UnitTacticalStanceResult invalid = RuntimeGameplayCommandDirector.TrySetUnitTacticalStance("941", "Berserk");
        if (invalid.Success)
        {
            throw new InvalidOperationException("TrySetUnitTacticalStance with unsupported stance must fail.");
        }

        // Query Census
        TacticalStanceCensus census = RuntimeGameplayCommandDirector.GetTacticalStanceCensus(32);
        if (census.TotalTrackedUnits != 2 || census.AggressiveCount != 1 || census.KitingCount != 1)
        {
            throw new InvalidOperationException("TacticalStanceCensus should track 2 units (1 Aggressive, 1 Kiting).");
        }

        // Serialize census
        string json = AgentDiagnosticsFormatter.SerializeTacticalStanceCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "tacticalStanceCensus")
            {
                throw new InvalidOperationException("SerializeTacticalStanceCensus kind must be 'tacticalStanceCensus'.");
            }
            if (!doc.RootElement.TryGetProperty("omittedUnitCount", out var om) || om.GetInt32() != 0)
            {
                throw new InvalidOperationException("SerializeTacticalStanceCensus omittedUnitCount must be 0.");
            }
            if (!doc.RootElement.TryGetProperty("aggressiveCount", out var agg) || agg.GetInt32() != 1)
            {
                throw new InvalidOperationException("SerializeTacticalStanceCensus aggressiveCount must be 1.");
            }
            if (!doc.RootElement.TryGetProperty("stanceRows", out var rows) || rows.GetArrayLength() != 2)
            {
                throw new InvalidOperationException("SerializeTacticalStanceCensus stanceRows must have 2 elements.");
            }
        }

        // Group stance assignment
        UnitTacticalStanceResult groupRes = RuntimeGameplayCommandDirector.TrySetGroupTacticalStance(
            new[] { "941", "942" }, "Defensive");
        if (!groupRes.Success || groupRes.AffectedUnitCount != 2)
        {
            throw new InvalidOperationException("TrySetGroupTacticalStance should assign Defensive to both units.");
        }
    }),
    ("runtime unit elite affixes applies and removes traits and serializes unit affix census with omission first", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        var unitA = new EntityController
        {
            entityId = "unit:affix_alpha",
            UniqueEntityId = 951,
            IsControlledByPlayer = true,
            IsUnit = true,
            CurrentHealth = 50f,
            MaxHealth = 100f,
            CurrentShield = 0f,
            MaxShield = 2f,
        };
        ExistingControllers.Instance.PlayerUnits().Add(unitA);

        // Apply Swift
        UnitAffixResult swift = RuntimeGameplayCommandDirector.TryApplyUnitAffix("unit:affix_alpha", "Swift");
        if (!swift.Success || swift.ActiveAffixCount != 1 || swift.Affix != "Swift")
        {
            throw new InvalidOperationException("TryApplyUnitAffix Swift should succeed with 1 active affix.");
        }

        // Apply Bulwark (charges shield)
        UnitAffixResult bulwark = RuntimeGameplayCommandDirector.TryApplyUnitAffix("951", "Bulwark");
        if (!bulwark.Success || bulwark.ActiveAffixCount != 2 || unitA.CurrentShield < 1f)
        {
            throw new InvalidOperationException("TryApplyUnitAffix Bulwark should charge shield and report 2 active affixes.");
        }

        // Apply Juggernaut (heals)
        UnitAffixResult jugg = RuntimeGameplayCommandDirector.TryApplyUnitAffix("951", "Juggernaut");
        if (!jugg.Success || jugg.ActiveAffixCount != 3 || unitA.CurrentHealth <= 50f)
        {
            throw new InvalidOperationException("TryApplyUnitAffix Juggernaut should heal and report 3 active affixes.");
        }

        // Query Census
        UnitAffixCensus census = RuntimeGameplayCommandDirector.GetUnitAffixCensus(32);
        if (census.TotalTrackedUnits != 1 || census.TotalActiveAffixes != 3)
        {
            throw new InvalidOperationException("UnitAffixCensus should report 1 unit and 3 total active affixes.");
        }

        // Serialize census
        string json = AgentDiagnosticsFormatter.SerializeUnitAffixCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "unitAffixCensus")
            {
                throw new InvalidOperationException("SerializeUnitAffixCensus kind must be 'unitAffixCensus'.");
            }
            if (!doc.RootElement.TryGetProperty("omittedUnitCount", out var om) || om.GetInt32() != 0)
            {
                throw new InvalidOperationException("SerializeUnitAffixCensus omittedUnitCount must be 0.");
            }
            if (!doc.RootElement.TryGetProperty("totalActiveAffixes", out var ta) || ta.GetInt32() != 3)
            {
                throw new InvalidOperationException("SerializeUnitAffixCensus totalActiveAffixes must be 3.");
            }
        }

        // Remove Swift
        UnitAffixResult remSwift = RuntimeGameplayCommandDirector.TryRemoveUnitAffix("unit:affix_alpha", "Swift");
        if (!remSwift.Success || remSwift.ActiveAffixCount != 2)
        {
            throw new InvalidOperationException("TryRemoveUnitAffix Swift should leave 2 active affixes.");
        }

        // Remove All
        UnitAffixResult remAll = RuntimeGameplayCommandDirector.TryRemoveUnitAffix("unit:affix_alpha", "All");
        if (!remAll.Success || remAll.ActiveAffixCount != 0)
        {
            throw new InvalidOperationException("TryRemoveUnitAffix All should clear all affixes.");
        }
    }),
    ("runtime AI assault wave coordinates attack wave escalation and dispatches orders to living AI combat units", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearAiDoctrineAndDeckEntries();
        var hq = new EntityController
        {
            entityId = "building:player_hq",
            UniqueEntityId = 101,
            IsControlledByPlayer = true,
            IsBuilding = true,
            Position = new UnityEngine.Vector3(100f, 0f, 100f),
        };
        ExistingControllers.Instance.EngineeringHqEntity = hq;

        var aiTank1 = new EntityController
        {
            entityId = "unit:ai_assault_tank_1",
            UniqueEntityId = 961,
            IsControlledByAi = true,
            IsControlledByPlayer = false,
            IsUnit = true,
            IsCombatUnit = true,
            CanAttackAndIsAttackEnabled = true,
            Position = new UnityEngine.Vector3(0f, 0f, 0f),
        };
        var aiTank2 = new EntityController
        {
            entityId = "unit:ai_assault_tank_2",
            UniqueEntityId = 962,
            IsControlledByAi = true,
            IsControlledByPlayer = false,
            IsUnit = true,
            IsCombatUnit = true,
            CanAttackAndIsAttackEnabled = true,
            Position = new UnityEngine.Vector3(5f, 0f, 5f),
        };
        ExistingControllers.Instance.AiUnits().Add(aiTank1);
        ExistingControllers.Instance.AiUnits().Add(aiTank2);

        // Dispatch assault wave 4 with aggression boost 2.0
        AiWaveDispatchResult res = RuntimeGameplayCommandDirector.TryDispatchAiAssaultWave(4, 2.0f, 0);
        if (!res.Success || res.WaveNumber != 4 || res.DispatchedUnitCount != 2)
        {
            throw new InvalidOperationException("TryDispatchAiAssaultWave should dispatch wave 4 to both living AI units.");
        }

        // Serialization
        string json = AgentDiagnosticsFormatter.SerializeAiWaveDispatchResult(res);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("kind", out var kindProp) || kindProp.GetString() != "aiWaveDispatchResult")
            {
                throw new InvalidOperationException("SerializeAiWaveDispatchResult kind must be 'aiWaveDispatchResult'.");
            }
            if (!doc.RootElement.TryGetProperty("waveNumber", out var wn) || wn.GetInt32() != 4)
            {
                throw new InvalidOperationException("SerializeAiWaveDispatchResult waveNumber must be 4.");
            }
            if (!doc.RootElement.TryGetProperty("dispatchedUnitCount", out var dc) || dc.GetInt32() != 2)
            {
                throw new InvalidOperationException("SerializeAiWaveDispatchResult dispatchedUnitCount must be 2.");
            }
        }
    }),
    ("runtime agent coordinator provides multi-agent blackboard messaging, batch locking, contention tracking, and session deregistration", () =>
    {
        RuntimeAgentCoordinator.ResetForTests();
        UnityEngine.Time.time = 100f;

        try
        {
            // 1. Register agent with sector and capabilities
            bool reg = RuntimeAgentCoordinator.TryRegisterSession(
                "agent:tactician",
                "Tactician",
                "Coordinates frontline micro",
                "NorthFlank",
                new[] { "MicroCombat", "FocusFire" },
                100f,
                out string regMsg);
            if (!reg || RuntimeAgentCoordinator.RegisteredSessionCount != 1)
            {
                throw new InvalidOperationException($"Register agent failed: {regMsg}");
            }

            // 2. Track command execution
            RuntimeAgentCoordinator.RecordCommandDispatched("agent:tactician");
            RuntimeAgentCoordinator.RecordCommandDispatched("agent:tactician");

            // 3. Post blackboard message
            bool post = RuntimeAgentCoordinator.TryPostMessage(
                "agent:tactician",
                "TacticalIntent",
                "Engaging northern artillery cluster",
                "all",
                101f,
                out string postMsg);
            if (!post || RuntimeAgentCoordinator.TotalMessageCount != 1)
            {
                throw new InvalidOperationException($"Post message failed: {postMsg}");
            }

            // 4. Lock contention tracking
            bool lock1 = RuntimeAgentCoordinator.TryAcquireLock("agent:tactician", "unit:artillery_target", 30f, 101f, out string lockMsg1);
            if (!lock1)
            {
                throw new InvalidOperationException($"Acquire lock failed: {lockMsg1}");
            }

            // Second agent fails -> increments contention
            bool lockConflict = RuntimeAgentCoordinator.TryAcquireLock("agent:observer", "unit:artillery_target", 15f, 102f, out string lockMsg2);
            if (lockConflict)
            {
                throw new InvalidOperationException("Lock conflict should have failed.");
            }

            // 5. Batch lock acquisition and release
            bool batchAcq = RuntimeAgentCoordinator.TryAcquireLockBatch(
                "agent:observer",
                new[] { "camera:tactical", "factory:heavy_mech" },
                30f,
                102f,
                out string batchMsg);
            if (!batchAcq || RuntimeAgentCoordinator.ActiveLockCount != 3)
            {
                throw new InvalidOperationException($"Batch lock acquisition failed: {batchMsg}");
            }

            bool batchRel = RuntimeAgentCoordinator.TryReleaseLockBatch(
                "agent:observer",
                new[] { "camera:tactical", "factory:heavy_mech" },
                out string relBatchMsg);
            if (!batchRel || RuntimeAgentCoordinator.ActiveLockCount != 1)
            {
                throw new InvalidOperationException($"Batch lock release failed: {relBatchMsg}");
            }

            // 6. Snapshot inspection
            AgentCoordinationSnapshot snap = RuntimeAgentCoordinator.GetSnapshot(103f);
            if (snap.OmittedMessageCount != 0 || snap.ActiveSessions.Count != 1 || snap.RecentMessages.Count != 1)
            {
                throw new InvalidOperationException("Snapshot counts mismatch.");
            }

            AgentSessionRecord sess = snap.ActiveSessions[0];
            if (sess.CommandsDispatchedCount != 2 || sess.AssignedSector != "NorthFlank" || sess.Status != "Active")
            {
                throw new InvalidOperationException($"Session record fields incorrect: commands={sess.CommandsDispatchedCount}, sector={sess.AssignedSector}, status={sess.Status}");
            }

            AgentAdvisoryLock lk = snap.ActiveLocks[0];
            if (lk.ContentionCount != 1 || lk.ResourceCategory != "Unit")
            {
                throw new InvalidOperationException($"Lock record fields incorrect: contention={lk.ContentionCount}, category={lk.ResourceCategory}");
            }

            // 7. Full clean-room serialization
            string json = AgentDiagnosticsFormatter.SerializeAgentCoordinationCensus(snap);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("kind", out var k) || k.GetString() != "agentCoordinationCensus" ||
                    !doc.RootElement.TryGetProperty("omittedMessageCount", out var omMsg) || omMsg.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("messages", out var msgs) || msgs.GetArrayLength() != 1)
                {
                    throw new InvalidOperationException($"JSON schema validation failed: {json}");
                }
            }

            // 8. Session deregistration and automatic lock release
            bool dereg = RuntimeAgentCoordinator.TryDeregisterSession("agent:tactician", out string deregMsg);
            if (!dereg || RuntimeAgentCoordinator.RegisteredSessionCount != 0 || RuntimeAgentCoordinator.ActiveLockCount != 0)
            {
                throw new InvalidOperationException($"Deregister session failed to release locks: {deregMsg}");
            }
        }
        finally
        {
            RuntimeAgentCoordinator.ResetForTests();
        }
    }),
    ("content unit and AI profile definitions validate default tactical stances, elite affixes, AI doctrines, and aggression multipliers", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            var unit = pack.units[0];
            unit.defaultTacticalStance = "Kiting";
            unit.startingAffixes = new[] { "Swift", "Overcharged" };

            // Clone check
            var cloneMethod = typeof(ContentPackCopy).GetMethod("CloneUnit", BindingFlags.NonPublic | BindingFlags.Static);
            var cloned = (ContentUnitDefinition)cloneMethod!.Invoke(null, new object[] { unit })!;
            if (cloned.defaultTacticalStance != "Kiting" || cloned.startingAffixes == null || cloned.startingAffixes.Length != 2 ||
                cloned.startingAffixes[0] != "Swift" || cloned.startingAffixes[1] != "Overcharged")
            {
                throw new InvalidOperationException("Cloned unit definition failed to preserve defaultTacticalStance or startingAffixes.");
            }
            if (ReferenceEquals(cloned.startingAffixes, unit.startingAffixes))
            {
                throw new InvalidOperationException("Cloned unit definition must not share startingAffixes array instance.");
            }

            // Valid package passes
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            // Invalid stance
            unit.defaultTacticalStance = "SniperRush";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.defaultTacticalStance-invalid");
            unit.defaultTacticalStance = "Kiting";

            // Empty affix
            unit.startingAffixes = new[] { "   " };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.startingAffixes-empty");

            // Invalid affix
            unit.startingAffixes = new[] { "MegaBlast" };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.startingAffixes-invalid");

            // Duplicate affix
            unit.startingAffixes = new[] { "Swift", "Swift" };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.startingAffixes-duplicate");
            unit.startingAffixes = new[] { "Swift" };

            // AI profile validation & cloning
            if (pack.aiProfiles != null && pack.aiProfiles.Length > 0)
            {
                var ai = pack.aiProfiles[0];
                ai.doctrine = "Assault";
                ai.aggressionMultiplier = 1.8f;

                var cloneAiMethod = typeof(ContentPackCopy).GetMethod("CloneAiProfile", BindingFlags.NonPublic | BindingFlags.Static);
                var clonedAi = (ContentAiProfileDefinition)cloneAiMethod!.Invoke(null, new object[] { ai })!;
                if (clonedAi.doctrine != "Assault" || clonedAi.aggressionMultiplier != 1.8f)
                {
                    throw new InvalidOperationException("Cloned AI profile failed to preserve doctrine or aggressionMultiplier.");
                }

                // Invalid doctrine
                ai.doctrine = "ChaosDoctrine";
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-profile.doctrine-invalid");
                ai.doctrine = "Assault";

                // Out-of-range aggression multiplier (< 0.1)
                ai.aggressionMultiplier = 0.05f;
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-profile.aggressionMultiplier-range");

                // Out-of-range aggression multiplier (> 5.0)
                ai.aggressionMultiplier = 6.0f;
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-profile.aggressionMultiplier-range");

                // Non-finite aggression multiplier
                ai.aggressionMultiplier = float.NaN;
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-profile.aggressionMultiplier-range");
            }
        });
    }),
    ("unit runtime mechanics director applies default tactical stance and starting affixes on spawn and cleans up on reset", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        RuntimeUnitMechanicsDirector.ClearProfiles();

        var go = new GameObject("TestUnitStanceAffix");
        var unit = go.AddComponent<EntityController>();
        unit.entityId = "sample:custom-stance-tank";
        unit.UniqueEntityId = 980;
        unit.IsUnit = true;
        unit.IsControlledByPlayer = true;
        unit.CurrentHealth = 80f;
        unit.MaxHealth = 100f;
        unit.CurrentShield = 0f;
        unit.MaxShield = 5f;
        ExistingControllers.Instance.PlayerUnits().Add(unit);

        RuntimeUnitMechanicsDirector.RegisterProfile("sample:custom-stance-tank", new UnitRuntimeMechanicsProfile
        {
            EntityId = "sample:custom-stance-tank",
            DefaultTacticalStance = "Aggressive",
            StartingAffixes = new[] { "Swift", "Bulwark" }
        });

        // 1. Summary formatting check
        string? summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("sample:custom-stance-tank");
        if (summary == null || !summary.Contains("Postura: Aggressive") || !summary.Contains("Afijos: Swift, Bulwark"))
        {
            throw new InvalidOperationException($"Mechanics summary did not contain expected stance and affixes: '{summary}'");
        }

        // 2. Attach mechanics and tick
        RuntimeUnitMechanicsDirector.AttachIfApplicable(unit);
        var director = go.GetComponent<RuntimeUnitMechanicsDirector>();
        if (director == null)
        {
            throw new InvalidOperationException("RuntimeUnitMechanicsDirector should be attached to unit with stance and affixes.");
        }

        director.TickMechanics();

        // 3. Verify tactical stance census
        TacticalStanceCensus stanceCensus = RuntimeGameplayCommandDirector.GetTacticalStanceCensus(32);
        if (stanceCensus.TotalTrackedUnits != 1 || stanceCensus.AggressiveCount != 1)
        {
            throw new InvalidOperationException($"Stance census expected 1 Aggressive unit, got {stanceCensus.AggressiveCount} / {stanceCensus.TotalTrackedUnits}");
        }

        // 4. Verify unit affix census
        UnitAffixCensus affixCensus = RuntimeGameplayCommandDirector.GetUnitAffixCensus(32);
        if (affixCensus.TotalTrackedUnits != 1 || affixCensus.TotalActiveAffixes != 2 ||
            affixCensus.AffixRows.Count != 1 || !affixCensus.AffixRows[0].Affixes.Contains("Swift") || !affixCensus.AffixRows[0].Affixes.Contains("Bulwark"))
        {
            throw new InvalidOperationException($"Affix census expected Swift and Bulwark, got total={affixCensus.TotalActiveAffixes}");
        }

        // 5. Clear runtime mechanics and verify cleanup
        var clearMethod = typeof(RuntimeUnitMechanicsDirector).GetMethod("ClearRuntimeMechanics", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
        clearMethod!.Invoke(null, new object[] { go });

        TacticalStanceCensus clearedStance = RuntimeGameplayCommandDirector.GetTacticalStanceCensus(32);
        if (clearedStance.AggressiveCount != 0 || clearedStance.StandardCount != 1)
        {
            throw new InvalidOperationException($"Tactical stance should be reset to Standard upon clear. Aggressive={clearedStance.AggressiveCount}, Standard={clearedStance.StandardCount}");
        }

        UnitAffixCensus clearedAffix = RuntimeGameplayCommandDirector.GetUnitAffixCensus(32);
        if (clearedAffix.TotalTrackedUnits != 0 || clearedAffix.TotalActiveAffixes != 0)
        {
            throw new InvalidOperationException("Unit affixes should be removed upon clear.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        RuntimeUnitMechanicsDirector.ClearProfiles();
    }),
    ("content pack runtime loader applies AI doctrine on commit and initializes profile", () =>
    {
        RuntimeGameplayCommandDirector.TrySetAiDoctrine("Standard", 1.0f);
        AiDoctrineAndDeckCensus initialCensus = RuntimeAiDoctrineCensus.Current();
        if (initialCensus.ActiveDoctrine != "Standard" || Math.Abs(initialCensus.AggressionMultiplier - 1.0f) > 0.01f)
        {
            throw new InvalidOperationException("Initial doctrine census should be Standard.");
        }

        // Apply dynamic doctrine via TrySetAiDoctrine
        AiDoctrineResult result = RuntimeGameplayCommandDirector.TrySetAiDoctrine("Siege", 2.2f);
        if (!result.Success || result.DoctrineName != "Siege" || Math.Abs(result.AggressionMultiplier - 2.2f) > 0.01f)
        {
            throw new InvalidOperationException("Failed to set AI doctrine Siege.");
        }

        AiDoctrineAndDeckCensus updatedCensus = RuntimeAiDoctrineCensus.Current();
        if (updatedCensus.ActiveDoctrine != "Siege" || Math.Abs(updatedCensus.AggressionMultiplier - 2.2f) > 0.01f)
        {
            throw new InvalidOperationException($"Updated doctrine census expected Siege 2.2, got {updatedCensus.ActiveDoctrine} {updatedCensus.AggressionMultiplier}");
        }

        // Verify bounds clamping: multiplier clamped between 0.1 and 5.0
        AiDoctrineResult clampedLow = RuntimeGameplayCommandDirector.TrySetAiDoctrine("Kiting", 0.01f);
        if (!clampedLow.Success || Math.Abs(clampedLow.AggressionMultiplier - 0.1f) > 0.01f)
        {
            throw new InvalidOperationException("Aggression multiplier below 0.1 must be clamped to 0.1.");
        }

        AiDoctrineResult clampedHigh = RuntimeGameplayCommandDirector.TrySetAiDoctrine("Assault", 10.0f);
        if (!clampedHigh.Success || Math.Abs(clampedHigh.AggressionMultiplier - 5.0f) > 0.01f)
        {
            throw new InvalidOperationException("Aggression multiplier above 5.0 must be clamped to 5.0.");
        }

        RuntimeGameplayCommandDirector.TrySetAiDoctrine("Standard", 1.0f);
    }),
    ("locomotion animation policy loops idle only while stationary", () =>
    {
        if (LocomotionAnimationPolicy.IsMoving(0f, 0.016f, 0f))
        {
            throw new InvalidOperationException("A stationary unit must keep its idle loop active.");
        }
        if (!LocomotionAnimationPolicy.IsMoving(0.0001f, 0.016f, 0f))
        {
            throw new InvalidOperationException("A measurable position delta must stop the idle loop and enter locomotion.");
        }
        if (!LocomotionAnimationPolicy.IsMoving(0f, 0.016f, 0.000002f))
        {
            throw new InvalidOperationException("A sampled movement velocity must stop the idle loop even when position is quantized.");
        }
        if (LocomotionAnimationPolicy.IsMoving(0f, 0f, 1f))
        {
            throw new InvalidOperationException("A zero-delta-time sample must not activate locomotion.");
        }

        float movementHoldRemainingSeconds = 0f;
        if (!LocomotionAnimationPolicy.EvaluateWithMovementGrace(
                0.0001f,
                0.016f,
                0f,
                ref movementHoldRemainingSeconds))
        {
            throw new InvalidOperationException("A movement sample must immediately activate locomotion.");
        }

        for (int frame = 0; frame < 7; frame++)
        {
            if (!LocomotionAnimationPolicy.EvaluateWithMovementGrace(
                    0f,
                    0.016f,
                    0f,
                    ref movementHoldRemainingSeconds))
            {
                throw new InvalidOperationException("Intermittent movement samples must keep the locomotion clip active during the release grace window.");
            }
        }

        if (LocomotionAnimationPolicy.EvaluateWithMovementGrace(
                0f,
                0.016f,
                0f,
                ref movementHoldRemainingSeconds))
        {
            throw new InvalidOperationException("The locomotion clip must return to idle after the release grace window expires.");
        }

        movementHoldRemainingSeconds = 0.08f;
        if (LocomotionAnimationPolicy.EvaluateWithMovementGrace(
                0f,
                0f,
                0f,
                ref movementHoldRemainingSeconds) || movementHoldRemainingSeconds != 0f)
        {
            throw new InvalidOperationException("A paused zero-delta-time sample must clear the movement hold and allow idle.");
        }
    }),
    ("turret aim policy only allows enemies within weapon range", () =>
    {
        if (!TurretAimPolicy.IsInWeaponRange(30f, 3.8f, null))
        {
            throw new InvalidOperationException("An enemy inside weapon range (3.8 cells = 38 world units) must authorize turret aim.");
        }
        if (TurretAimPolicy.IsInWeaponRange(39f, 3.8f, null))
        {
            throw new InvalidOperationException("An enemy beyond weapon range must not authorize turret aim.");
        }
        if (TurretAimPolicy.IsInWeaponRange(30f, 0f, null))
        {
            throw new InvalidOperationException("A unit with no weapon range must not aim its turret.");
        }
        if (TurretAimPolicy.IsInWeaponRange(20f, 14f, 5f))
        {
            throw new InvalidOperationException("An enemy inside the min-range deadzone (5 cells = 50 world units) must not authorize turret aim.");
        }
        if (!TurretAimPolicy.IsInWeaponRange(80f, 14f, 5f))
        {
            throw new InvalidOperationException("An enemy between min and max range must authorize turret aim.");
        }
    }),
    ("runtime visual census discovers skinned mesh, skeletal armature, animator parameters, and canonical sockets", () =>
    {
        ExistingControllers.Instance.Reset();

        // 1. Setup Skeletal Armature Unit with Animator, SkinnedMeshRenderer, and canonical sockets
        var goA = new GameObject("RCL_Unit_ArachnidTank");
        var unitA = new EntityController
        {
            entityId = "RCL_Unit_ArachnidTank",
            UniqueEntityId = 991,
            IsControlledByPlayer = true,
            IsUnit = true,
            gameObject = goA
        };

        var visualRoot = new GameObject("RCL_Visual_Arachnid");
        visualRoot.transform.parent = goA.transform;

        var anim = visualRoot.AddComponent<Animator>();
        anim.parameters = new[]
        {
            new AnimatorControllerParameter { name = "RCL_IsMoving", type = AnimatorControllerParameterType.Bool, defaultBool = true },
            new AnimatorControllerParameter { name = "RCL_Fire", type = AnimatorControllerParameterType.Trigger },
            new AnimatorControllerParameter { name = "RCL_Death", type = AnimatorControllerParameterType.Trigger }
        };
        anim.SetBool("RCL_IsMoving", true);
        anim.activeClip = new AnimationClip { name = "Action_Walk", length = 1.25f };
        anim.activeStateInfo = new AnimatorStateInfo("Walk", 0.45f, 1f);

        // Canonical sockets and anchors
        var muzzleSock = new GameObject("Socket_MuzzlePrimary");
        muzzleSock.transform.parent = goA.transform;
        var firePoint = new GameObject("FirePoint_0");
        firePoint.transform.parent = muzzleSock.transform;
        var audioSock = new GameObject("Socket_EngineAudio");
        audioSock.transform.parent = goA.transform;

        // Armature bones
        var boneRoot = new GameObject("Bone_Root");
        boneRoot.transform.parent = goA.transform;
        var bonePelvis = new GameObject("Bone_Pelvis");
        bonePelvis.transform.parent = boneRoot.transform;
        var boneThigh = new GameObject("Bone_Thigh_L1");
        boneThigh.transform.parent = bonePelvis.transform;

        // SkinnedMeshRenderer
        var smrObj = new GameObject("Arachnid_MeshObject");
        smrObj.transform.parent = visualRoot.transform;
        var smr = smrObj.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = new Mesh { name = "Arachnid_Mesh", vertexCount = 3500, subMeshCount = 1 };
        smr.rootBone = boneRoot.transform;
        smr.bones = new[] { boneRoot.transform, bonePelvis.transform, boneThigh.transform };
        smr.sharedMaterials = new[]
        {
            new Material
            {
                name = "M_Arachnid_Atlas",
                shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                shaderKeywords = new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" },
                enableInstancing = true
            }
        };

        // 2. Setup NativeTransformRig Unit
        var goB = new GameObject("sample_native_walker");
        var unitB = new EntityController
        {
            entityId = "sample:native-walker",
            UniqueEntityId = 992,
            IsControlledByPlayer = false,
            IsUnit = true,
            gameObject = goB
        };
        var rotHor = new GameObject("RotHor");
        rotHor.transform.parent = goB.transform;
        var rotVert = new GameObject("RotVert");
        rotVert.transform.parent = rotHor.transform;
        var legs = new GameObject("LEGS");
        legs.transform.parent = goB.transform;

        var mf = goB.AddComponent<MeshFilter>();
        mf.sharedMesh = new Mesh { name = "Walker_Mesh", vertexCount = 1200, subMeshCount = 1 };
        var mr = goB.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[]
        {
            new Material
            {
                name = "M_Walker",
                shader = new Shader { name = "Shader Graphs/EntityColorShader" }
            }
        };

        ExistingControllers.Instance.PlayerUnits().Add(unitA);
        ExistingControllers.Instance.AiUnits().Add(unitB);

        // 3. Collect Visual Census
        VisualRenderCensus census = RuntimeGameplayCommandDirector.GetVisualRenderCensus(32);
        if (census.TotalTrackedUnits != 2)
        {
            throw new InvalidOperationException($"Expected 2 total tracked units, got {census.TotalTrackedUnits}");
        }
        if (census.SkinnedMeshCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 skinned mesh unit, got {census.SkinnedMeshCount}");
        }
        if (census.StaticMeshCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 static mesh unit, got {census.StaticMeshCount}");
        }
        if (census.AnimatedUnitCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 animated unit, got {census.AnimatedUnitCount}");
        }
        if (census.PbrShaderCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 PBR shader unit, got {census.PbrShaderCount}");
        }

        // Verify Unit A row
        VisualUnitDiagnosticRow rowA = census.UnitRows[0];
        if (rowA.RigType != "SkeletalArmature")
        {
            throw new InvalidOperationException($"Unit A expected SkeletalArmature rig, got {rowA.RigType}");
        }
        if (!rowA.HasAnimator || !rowA.IsMovingParam)
        {
            throw new InvalidOperationException("Unit A should have active animator with RCL_IsMoving = true");
        }
        if (rowA.ActiveClipName != "Action_Walk")
        {
            throw new InvalidOperationException($"Unit A expected active clip Action_Walk, got {rowA.ActiveClipName}");
        }
        if (rowA.TotalVertexCount != 3500)
        {
            throw new InvalidOperationException($"Unit A expected 3500 vertices, got {rowA.TotalVertexCount}");
        }
        if (rowA.PrimaryShaderName != "RogueCommand/VehiclePBR_VertexWear" || !rowA.HasPbrKeywords || !rowA.IsGpuInstanced)
        {
            throw new InvalidOperationException("Unit A expected PBR shader with keywords and GPU instancing.");
        }
        if (!rowA.DiscoveredSockets.Contains("Socket_MuzzlePrimary") || !rowA.DiscoveredSockets.Contains("FirePoint_0") || !rowA.DiscoveredSockets.Contains("Socket_EngineAudio"))
        {
            throw new InvalidOperationException("Unit A expected canonical sockets Socket_MuzzlePrimary, FirePoint_0, and Socket_EngineAudio.");
        }

        // Verify Unit B row
        VisualUnitDiagnosticRow rowB = census.UnitRows[1];
        if (rowB.RigType != "NativeTransformRig")
        {
            throw new InvalidOperationException($"Unit B expected NativeTransformRig, got {rowB.RigType}");
        }
        if (rowB.TotalVertexCount != 1200)
        {
            throw new InvalidOperationException($"Unit B expected 1200 vertices, got {rowB.TotalVertexCount}");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("detailed entity visual inspection extracts renderer details, bone names, and socket ambiguity", () =>
    {
        ExistingControllers.Instance.Reset();

        var go = new GameObject("RCL_Unit_AmbiguousTest");
        var unit = new EntityController
        {
            entityId = "RCL_Unit_AmbiguousTest",
            UniqueEntityId = 993,
            IsControlledByPlayer = true,
            IsUnit = true,
            gameObject = go
        };

        // Add duplicate canonical socket transforms to trigger socket ambiguity detection
        var sock1 = new GameObject("Socket_MuzzlePrimary");
        sock1.transform.parent = go.transform;
        sock1.transform.localPosition = new Vector3(0f, 1f, 2f);

        var sock2 = new GameObject("Socket_MuzzlePrimary");
        sock2.transform.parent = go.transform;
        sock2.transform.localPosition = new Vector3(1f, 1f, 2f);

        // Add LODGroup
        var lod = go.AddComponent<LODGroup>();
        lod.lodCount = 3;

        ExistingControllers.Instance.PlayerUnits().Add(unit);

        DetailedEntityVisualDiagnostic diag = RuntimeGameplayCommandDirector.InspectEntityVisual("993");
        if (!diag.Success)
        {
            throw new InvalidOperationException($"Expected inspection success for unit 993, message: {diag.Message}");
        }
        if (!diag.HasLODGroup || diag.LODCount != 3)
        {
            throw new InvalidOperationException($"Expected LODGroup with 3 levels, got hasLod={diag.HasLODGroup}, count={diag.LODCount}");
        }

        bool foundAmbiguousSocket = false;
        for (int s = 0; s < diag.Sockets.Count; s++)
        {
            if (diag.Sockets[s].CanonicalRole == "Socket_MuzzlePrimary" && diag.Sockets[s].IsAmbiguous && diag.Sockets[s].DuplicateCount == 2)
            {
                foundAmbiguousSocket = true;
                break;
            }
        }
        if (!foundAmbiguousSocket)
        {
            throw new InvalidOperationException("Detailed visual inspection should detect ambiguous duplicate Socket_MuzzlePrimary.");
        }

        // Test non-existent unit inspection
        DetailedEntityVisualDiagnostic missing = RuntimeGameplayCommandDirector.InspectEntityVisual("unknown_nonexistent_unit");
        if (missing.Success)
        {
            throw new InvalidOperationException("Inspecting non-existent unit should report failure.");
        }
        if (!missing.Message.Contains("not found"))
        {
            throw new InvalidOperationException($"Failure message should explain unit not found, got: '{missing.Message}'");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("diagnostics formatter serializes visual render census and detailed entity diagnostics", () =>
    {
        var row = new VisualUnitDiagnosticRow(
            "995", "unit:test", true, "SkeletalArmature",
            true, true, "Action_Walk", 0.5f, 1.0f,
            true, false, false, 1, 2400,
            "RogueCommand/VehiclePBR_VertexWear", true, true, true,
            new[] { "Socket_MuzzlePrimary", "FirePoint_0" }, 0);

        var census = new VisualRenderCensus(
            omittedUnitCount: 2,
            totalTrackedUnits: 3,
            skinnedMeshCount: 1,
            staticMeshCount: 2,
            animatedUnitCount: 1,
            pbrShaderCount: 1,
            new[] { row });

        string json = AgentDiagnosticsFormatter.SerializeVisualRenderCensus(census);
        if (!json.Contains("\"kind\":\"visualRenderCensus\""))
        {
            throw new InvalidOperationException($"JSON should have kind visualRenderCensus, got: {json}");
        }

        int omittedPos = json.IndexOf("\"omittedUnitCount\":2", StringComparison.Ordinal);
        int rowsPos = json.IndexOf("\"unitRows\"", StringComparison.Ordinal);
        if (omittedPos < 0 || rowsPos < 0 || omittedPos > rowsPos)
        {
            throw new InvalidOperationException("Omission counter omittedUnitCount must strictly precede unitRows in serialized JSON.");
        }

        // Test detailed entity diagnostic serialization
        var diag = new DetailedEntityVisualDiagnostic(
            true, "995", "unit:test", "Inspected successfully",
            1, 2400, 1, 2.0f, 1.5f, 3.0f, true, 2,
            new[] { new VisualRendererDetailRow("TestMesh", "SkinnedMeshRenderer", true, true, "Mesh0", 2400, 1, new[] { "M_PBR" }, new[] { "RogueCommand/VehiclePBR_VertexWear" }, new[] { "_NORMALMAP" }, true) },
            "SkeletalArmature", "Bone_Root", 3, new[] { "Bone_Root", "Bone_Pelvis", "Bone_Foot" },
            new[] { new VisualSocketDetailRow("Socket_MuzzlePrimary", "Socket_MuzzlePrimary", 0f, 1f, 2f, 0f, 0f, 0f, false, 1) },
            true, true, "Controller_Walker", "Avatar_Walker", true,
            "Walk", "Action_Walk", 1.0f, 0.5f, 1.0f,
            new[] { new VisualAnimationParameterRow("RCL_IsMoving", "Bool", "true") },
            true, false, false, false, 0f, 0f,
            new VisualNativeRigRetargetState(true, true, false, 8, 8));

        string diagJson = AgentDiagnosticsFormatter.SerializeDetailedEntityVisualDiagnostic(diag);
        if (!diagJson.Contains("\"kind\":\"detailedEntityVisualDiagnostic\"") ||
            !diagJson.Contains("\"render\":") || !diagJson.Contains("\"rig\":") ||
            !diagJson.Contains("\"animation\":") || !diagJson.Contains("\"nativeRigRetarget\":") ||
            !diagJson.Contains("\"configuredMappingCount\":8") || !diagJson.Contains("\"boundMappingCount\":8") ||
            !diagJson.Contains("\"director\":"))
        {
            throw new InvalidOperationException($"Detailed diagnostic JSON missing required sections: {diagJson}");
        }
    }),
    ("visual render census exposes living visual metrics and formats correctly", () =>
    {
        ExistingControllers.Instance.Reset();

        var go = new GameObject("RCL_Scalar_Test");
        var unit = new EntityController
        {
            entityId = "RCL_Scalar_Test",
            UniqueEntityId = 998,
            IsControlledByPlayer = true,
            IsUnit = true,
            gameObject = go
        };

        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = new Mesh { name = "MeshScalar", vertexCount = 500 };
        smr.sharedMaterials = new[]
        {
            new Material
            {
                name = "M_Pbr",
                shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" }
            }
        };

        var anim = go.AddComponent<Animator>();
        anim.activeClip = new AnimationClip { name = "Action_Walk", length = 1.0f };

        Transform nativeRoot = new GameObject("Native").transform;
        nativeRoot.parent = go.transform;
        Transform sourceBone = new GameObject("Bone").transform;
        sourceBone.parent = nativeRoot;
        sourceBone.localPosition = new Vector3(1f, 2f, 3f);
        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.parent = go.transform;
        Transform rig = new GameObject("Rig").transform;
        rig.parent = visualRoot;
        Transform targetBone = new GameObject("Bone").transform;
        targetBone.parent = rig;
        targetBone.localPosition = new Vector3(10f, 20f, 30f);
        float retargetHalfSqrt = (float)Math.Sqrt(0.5f);
        targetBone.localRotation = new Quaternion(0f, retargetHalfSqrt, 0f, retargetHalfSqrt);
        NativeRigRetargetProfile retargetProfile = go.AddComponent<NativeRigRetargetProfile>();
        retargetProfile.Configure(new[] { "Native/Bone" }, new[] { "Rig/Bone" }, "Visual", 0.5f);
        if (!retargetProfile.Bind(unit))
        {
            throw new InvalidOperationException("The census fixture must bind its declared retarget mapping.");
        }
        sourceBone.localPosition = new Vector3(3f, 4f, 7f);
        sourceBone.localRotation = new Quaternion(0f, 0f, retargetHalfSqrt, retargetHalfSqrt);
        MethodInfo? retargetLateUpdate = typeof(NativeRigRetargetProfile).GetMethod(
            "LateUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (retargetLateUpdate == null)
        {
            throw new InvalidOperationException("The census fixture requires the retarget LateUpdate entry point.");
        }
        retargetLateUpdate.Invoke(retargetProfile, null);

        ExistingControllers.Instance.PlayerUnits().Add(unit);

        VisualRenderCensus census = RuntimeVisualCensus.Current(32);
        if (census.SkinnedMeshCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 live skinned mesh unit, got {census.SkinnedMeshCount}");
        }
        if (census.AnimatedUnitCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 live animated unit, got {census.AnimatedUnitCount}");
        }
        if (census.PbrShaderCount != 1)
        {
            throw new InvalidOperationException($"Expected 1 live PBR shader unit, got {census.PbrShaderCount}");
        }

        string censusJson = AgentDiagnosticsFormatter.SerializeVisualRenderCensus(census);
        if (!censusJson.Contains("\"kind\":\"visualRenderCensus\"") || !censusJson.Contains("RCL_Scalar_Test"))
        {
            throw new InvalidOperationException($"Census JSON missing expected content: {censusJson}");
        }

        DetailedEntityVisualDiagnostic diag = RuntimeGameplayCommandDirector.InspectEntityVisual("998");
        string inspectJson = AgentDiagnosticsFormatter.SerializeDetailedEntityVisualDiagnostic(diag);
        if (!inspectJson.Contains("\"success\":true") || !inspectJson.Contains("MeshScalar") ||
            !inspectJson.Contains("\"nativeRigRetarget\":{\"present\":true,\"isBound\":true") ||
            !inspectJson.Contains("\"configuredMappingCount\":1") || !inspectJson.Contains("\"boundMappingCount\":1") ||
            !inspectJson.Contains("\"omittedMappingCount\":0") || !inspectJson.Contains("\"sourcePath\":\"Native/Bone\"") ||
            !inspectJson.Contains("\"targetPath\":\"Rig/Bone\"") || !inspectJson.Contains("\"expectedTargetLocalPose\""))
        {
            throw new InvalidOperationException($"Inspect JSON missing expected content: {inspectJson}");
        }
        if (diag.NativeRigRetargetMappings.Count != 1)
        {
            throw new InvalidOperationException("Detailed inspection must contain the mapped retarget pose sample.");
        }
        VisualNativeRigRetargetMappingRow mappingRow = diag.NativeRigRetargetMappings[0];
        if (!mappingRow.SourceAvailable || !mappingRow.TargetAvailable ||
            Math.Abs(mappingRow.SourcePose.PositionX - 3f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.PositionX - 11f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.PositionY - 21f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.PositionZ - 32f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.PositionX - 11f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.PositionY - 21f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.PositionZ - 32f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.RotationX - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.RotationY - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.RotationZ - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.TargetPose.RotationW - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.RotationX - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.RotationY - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.RotationZ - 0.5f) > 0.0001f ||
            Math.Abs(mappingRow.ExpectedTargetPose.RotationW - 0.5f) > 0.0001f)
        {
            throw new InvalidOperationException("The inspect DTO must report source, actual target, and computed target local poses.");
        }

        retargetProfile.SuspendForDeath();
        DetailedEntityVisualDiagnostic suspendedDiag = RuntimeGameplayCommandDirector.InspectEntityVisual("998");
        string suspendedJson = AgentDiagnosticsFormatter.SerializeDetailedEntityVisualDiagnostic(suspendedDiag);
        if (!suspendedJson.Contains("\"isSuspended\":true"))
        {
            throw new InvalidOperationException($"Inspect JSON must expose death-suspended retarget state: {suspendedJson}");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("native rig retarget visual diagnostics cap mapping samples and report omissions", () =>
    {
        ExistingControllers.Instance.Reset();
        var go = new GameObject("RCL_RetargetSampleCap");
        var unit = new EntityController
        {
            entityId = "RCL_RetargetSampleCap",
            UniqueEntityId = 999,
            IsControlledByPlayer = true,
            IsUnit = true,
            gameObject = go
        };
        Transform nativeRoot = new GameObject("Native").transform;
        nativeRoot.parent = go.transform;
        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.parent = go.transform;
        Transform rigRoot = new GameObject("Rig").transform;
        rigRoot.parent = visualRoot;

        var sourcePaths = new string[20];
        var targetPaths = new string[20];
        for (int i = 0; i < sourcePaths.Length; i++)
        {
            string suffix = i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            Transform source = new GameObject("Source" + suffix).transform;
            source.parent = nativeRoot;
            Transform target = new GameObject("Target" + suffix).transform;
            target.parent = rigRoot;
            sourcePaths[i] = "Native/Source" + suffix;
            targetPaths[i] = "Rig/Target" + suffix;
        }

        NativeRigRetargetProfile profile = go.AddComponent<NativeRigRetargetProfile>();
        profile.Configure(sourcePaths, targetPaths, "Visual", 1f);
        if (!profile.Bind(unit))
        {
            throw new InvalidOperationException("All unique retarget mapping paths should bind in the cap fixture.");
        }
        ExistingControllers.Instance.PlayerUnits().Add(unit);

        DetailedEntityVisualDiagnostic diag = RuntimeVisualCensus.InspectEntity("999");
        if (!diag.Success || diag.NativeRigRetargetMappings.Count != NativeRigRetargetProfile.MaxDiagnosticMappingSamples ||
            diag.NativeRigRetargetOmittedMappingCount != 4)
        {
            throw new InvalidOperationException("Retarget diagnostics must cap samples at 16 and count all four omitted mappings.");
        }
        string json = AgentDiagnosticsFormatter.SerializeDetailedEntityVisualDiagnostic(diag);
        int omittedIndex = json.IndexOf("\"omittedMappingCount\":4", StringComparison.Ordinal);
        int mappingsIndex = json.IndexOf("\"mappings\":[", StringComparison.Ordinal);
        if (omittedIndex < 0 || mappingsIndex < 0 || omittedIndex > mappingsIndex ||
            !json.Contains("\"targetPath\":\"Rig/Target15\""))
        {
            throw new InvalidOperationException("Serialized retarget diagnostics must put omission counts before the bounded mapping details.");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("unit loader validates and applies defaultLoadout and startingVeterancyRank on spawn and cleans up on reuse", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample:test-loadout-vet",
            defaultLoadout = "AP",
            startingVeterancyRank = 3,
            variants = new[]
            {
                new ContentUnitVariantDefinition
                {
                    id = "custom_siege"
                }
            }
        };

        var cloneMethod = typeof(ContentPackCopy).GetMethod("CloneUnit", BindingFlags.NonPublic | BindingFlags.Static);
        var cloned = (ContentUnitDefinition)cloneMethod!.Invoke(null, new object[] { unit })!;

        if (cloned.defaultLoadout != "AP" || cloned.startingVeterancyRank != 3)
        {
            throw new InvalidOperationException("ContentPackCopy.CloneUnit must deep-copy defaultLoadout and startingVeterancyRank.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();

        var profile = new UnitRuntimeMechanicsProfile
        {
            EntityId = "sample:test-loadout-vet",
            DefaultTacticalStance = "Aggressive",
            StartingAffixes = new[] { "Swift" },
            DefaultLoadout = "AP",
            StartingVeterancyRank = 2
        };
        if (!profile.HasAnyMechanics)
        {
            throw new InvalidOperationException("Profile with DefaultLoadout and StartingVeterancyRank must report HasAnyMechanics=true.");
        }

        RuntimeUnitMechanicsDirector.RegisterProfile("sample:test-loadout-vet", profile);

        string? summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("sample:test-loadout-vet");
        if (summary == null || !summary.Contains("Loadout: AP") || !summary.Contains("Rango Inicial: 2"))
        {
            throw new InvalidOperationException($"Mechanics summary did not include Loadout and Rango Inicial: '{summary}'");
        }

        var go = new GameObject("DirectorHostLoadoutVet");
        var entity = go.AddComponent<EntityController>();
        entity.entityId = "sample:test-loadout-vet";
        entity.UniqueEntityId = 1042;
        entity.IsControlledByPlayer = true;
        entity.IsUnit = true;
        entity.IsCombatUnit = true;
        entity.CanAttackAndIsAttackEnabled = true;
        entity.CanMoveAndMovementIsEnabled = true;
        entity.MaxHealth = 500f;
        entity.CurrentHealth = 500f;
        entity.MaxRank = 5;
        entity.CurrentRank = 0;
        ExistingControllers.Instance.PlayerUnits().Add(entity);

        RuntimeUnitMechanicsDirector.AttachIfApplicable(entity);
        var director = go.GetComponent<RuntimeUnitMechanicsDirector>();
        if (director == null)
        {
            throw new InvalidOperationException("RuntimeUnitMechanicsDirector should be attached to unit with defaultLoadout and startingVeterancyRank.");
        }

        UnityEngine.Time.time = 5.0f;
        director.TickMechanics();

        string keyStr = entity.UniqueEntityId.ToString();
        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance(keyStr) != "Aggressive")
        {
            throw new InvalidOperationException("Unit spawn should apply DefaultTacticalStance.");
        }

        VeterancyCensus vetCensus = RuntimeVeterancyCensus.Current();
        bool foundRank2 = false;
        foreach (var row in vetCensus.Units)
        {
            if (row.EntityKeyId == keyStr && row.CurrentRank == 2)
            {
                foundRank2 = true;
                break;
            }
        }
        if (!foundRank2)
        {
            throw new InvalidOperationException("Unit spawn should apply StartingVeterancyRank=2.");
        }

        UnitLoadoutAndPoolCensus loadoutCensus = RuntimeUnitLoadoutCensus.Current();
        bool foundApLoadout = false;
        foreach (var row in loadoutCensus.UnitRows)
        {
            if (row.UnitKeyId == keyStr && row.LoadoutName == "AP")
            {
                foundApLoadout = true;
                break;
            }
        }
        if (!foundApLoadout)
        {
            throw new InvalidOperationException("Unit spawn should apply DefaultLoadout='AP'.");
        }

        var clearMethod = typeof(RuntimeUnitMechanicsDirector).GetMethod("ClearRuntimeMechanics", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
        clearMethod!.Invoke(null, new object[] { go });
        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance(keyStr) != "Standard")
        {
            throw new InvalidOperationException("ClearRuntimeState must reset stance to Standard.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
    }),
    ("load-time prefab visual audit registers prefabs, formats JSON with omission counter first, and supports InspectEntity fallback", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeVisualCensus.ClearLoadedPrefabAudits();

        var prefabGo = new GameObject("RCL_Custom_sample_arachnid");
        var smr = prefabGo.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = new Mesh { name = "ArachnidMesh", vertexCount = 3200, subMeshCount = 2 };
        smr.bones = new[] { new GameObject("Bone_Root").transform, new GameObject("Bone_Pelvis").transform };
        smr.sharedMaterials = new[]
        {
            new Material
            {
                name = "M_Arachnid_PBR",
                shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                enableInstancing = true,
                shaderKeywords = new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" }
            }
        };

        var anim = prefabGo.AddComponent<Animator>();
        anim.runtimeAnimatorController = new RuntimeAnimatorController { name = "AC_Arachnid" };
        anim.avatar = new Avatar { name = "Av_Arachnid", isValid = true };

        var muzzle = new GameObject("Socket_MuzzlePrimary");
        muzzle.transform.parent = prefabGo.transform;
        var fp0 = new GameObject("FirePoint_0");
        fp0.transform.parent = muzzle.transform;

        RuntimeVisualCensus.RegisterLoadedPrefabVisualAudit("sample:arachnid", prefabGo);
        if (RuntimeVisualCensus.AuditedPrefabCount != 1)
        {
            throw new InvalidOperationException($"Expected AuditedPrefabCount=1, got {RuntimeVisualCensus.AuditedPrefabCount}");
        }

        LoadedPrefabVisualAuditCensus auditCensus = RuntimeGameplayCommandDirector.GetLoadedPrefabVisualAuditCensus(64);
        if (auditCensus.TotalAuditedPrefabs != 1 ||
            auditCensus.SkinnedPrefabCount != 1 ||
            auditCensus.AnimatedPrefabCount != 1 ||
            auditCensus.PbrPrefabCount != 1 ||
            auditCensus.AmbiguousSocketPrefabCount != 0)
        {
            throw new InvalidOperationException("LoadedPrefabVisualAuditCensus aggregate counters did not match expected values.");
        }

        LoadedPrefabVisualAuditRow row = auditCensus.PrefabRows[0];
        if (row.EntityId != "sample:arachnid" ||
            row.RigType != "SkeletalArmature" ||
            !(row.HasMuzzlePrimary && row.HasFirePoint0) ||
            row.TotalVertexCount != 3200)
        {
            throw new InvalidOperationException("LoadedPrefabVisualAuditRow did not capture expected rig/socket/mesh metadata.");
        }

        string auditJson = AgentDiagnosticsFormatter.SerializeLoadedPrefabVisualAuditCensus(auditCensus);
        int omittedIdx = auditJson.IndexOf("\"omittedUnitCount\":0", StringComparison.Ordinal);
        int rowsIdx = auditJson.IndexOf("\"prefabRows\"", StringComparison.Ordinal);
        if (omittedIdx < 0 || rowsIdx < 0 || omittedIdx > rowsIdx)
        {
            throw new InvalidOperationException("omittedUnitCount must strictly precede prefabRows in loadedPrefabVisualAuditCensus JSON.");
        }

        DetailedEntityVisualDiagnostic prefabInspect = RuntimeGameplayCommandDirector.InspectEntityVisual("sample:arachnid");
        if (!prefabInspect.Success ||
            prefabInspect.EntityKeyId != "prefab:sample:arachnid" ||
            prefabInspect.RigType != "SkeletalArmature" ||
            prefabInspect.TotalVertexCount != 3200)
        {
            throw new InvalidOperationException($"InspectEntity prefab fallback failed: success={prefabInspect.Success}, msg={prefabInspect.Message}");
        }

        RuntimeVisualCensus.ClearLoadedPrefabAudits();
    }),
    ("AI profile squad defaults validate, clone, apply to non-player units while respecting unit-level overrides, and loader audit census + reconciliation operate cleanly", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        RuntimeVisualCensus.ClearLoadedPrefabAudits();

        var invalidPack = new ContentPackDefinition
        {
            schemaVersion = 1,
            packId = "sample:invalid-ai-defaults",
            assetBundles = Array.Empty<ContentAssetBundleDefinition>(),
            units = Array.Empty<ContentUnitDefinition>(),
            stages = Array.Empty<ContentStageDefinition>(),
            aiProfiles = new[]
            {
                new ContentAiProfileDefinition
                {
                    id = "sample:invalid-ai",
                    nativeTemplateId = "EnemyBase",
                    defaultUnitStance = "Berserk",
                    defaultUnitLoadout = "Nuclear",
                    startingUnitAffixes = new[] { "Swift", "Swift", "InvalidAffix" },
                    startingUnitVeterancyRank = 9,
                    unitEntries = new[]
                    {
                        new ContentAiUnitEntryDefinition { unitId = "PCXBaseTank", baseIndex = 0, countAtStart = 1, maxLiving = 2 }
                    },
                    productionBuildingEntries = Array.Empty<ContentAiProductionBuildingEntryDefinition>(),
                    modules = Array.Empty<ContentAiModuleDefinition>()
                }
            }
        };

        var invalidResult = ContentPackValidator.Validate(invalidPack, repositoryRoot, new EmptySnapshot(), new KnownModuleValidator());
        bool hasStanceErr = false, hasLoadoutErr = false, hasAffixDupErr = false, hasAffixInvErr = false, hasVetErr = false;
        foreach (var d in invalidResult.Diagnostics)
        {
            if (d.Code == "ai-profile.defaultUnitStance-invalid") hasStanceErr = true;
            if (d.Code == "ai-profile.defaultUnitLoadout-invalid") hasLoadoutErr = true;
            if (d.Code == "ai-profile.startingUnitAffixes-duplicate") hasAffixDupErr = true;
            if (d.Code == "ai-profile.startingUnitAffixes-invalid") hasAffixInvErr = true;
            if (d.Code == "ai-profile.startingUnitVeterancyRank-invalid") hasVetErr = true;
        }
        if (!hasStanceErr || !hasLoadoutErr || !hasAffixDupErr || !hasAffixInvErr || !hasVetErr)
        {
            throw new InvalidOperationException("Expected AI profile squad default validation diagnostics were not emitted.");
        }

        var validPack = new ContentPackDefinition
        {
            schemaVersion = 1,
            packId = "sample:valid-ai-defaults",
            assetBundles = Array.Empty<ContentAssetBundleDefinition>(),
            units = Array.Empty<ContentUnitDefinition>(),
            stages = Array.Empty<ContentStageDefinition>(),
            aiProfiles = new[]
            {
                new ContentAiProfileDefinition
                {
                    id = "sample:valid-ai",
                    nativeTemplateId = "EnemyBase",
                    doctrine = "Assault",
                    aggressionMultiplier = 1.25f,
                    defaultUnitStance = "Aggressive",
                    defaultUnitLoadout = "AP",
                    startingUnitAffixes = new[] { "Swift", "Bulwark" },
                    startingUnitVeterancyRank = 2,
                    unitEntries = new[]
                    {
                        new ContentAiUnitEntryDefinition { unitId = "sample:ai-grunt", baseIndex = 0, countAtStart = 1, maxLiving = 2 }
                    },
                    productionBuildingEntries = Array.Empty<ContentAiProductionBuildingEntryDefinition>(),
                    modules = Array.Empty<ContentAiModuleDefinition>()
                }
            }
        };

        var clonedPack = ContentPackCopy.DeepClone(validPack);
        var clonedAi = clonedPack.aiProfiles[0];
        if (clonedAi.defaultUnitStance != "Aggressive" ||
            clonedAi.defaultUnitLoadout != "AP" ||
            clonedAi.startingUnitVeterancyRank != 2 ||
            clonedAi.startingUnitAffixes == null ||
            clonedAi.startingUnitAffixes.Length != 2 ||
            clonedAi.startingUnitAffixes[0] != "Swift" ||
            clonedAi.startingUnitAffixes[1] != "Bulwark")
        {
            throw new InvalidOperationException("ContentPackCopy.DeepClone did not deep-copy AI profile squad defaults.");
        }

        RuntimeGameplayCommandDirector.RegisterAiProfileUnitDefaults(
            "sample:valid-ai",
            "Assault",
            1.25f,
            "Aggressive",
            "AP",
            new[] { "Swift", "Bulwark" },
            2,
            setActive: true);

        var aiUnitGo = new GameObject("AiGruntUnit");
        var aiUnit = aiUnitGo.AddComponent<EntityController>();
        aiUnit.entityId = "sample:ai-grunt";
        aiUnit.UniqueEntityId = 2001;
        aiUnit.IsControlledByPlayer = false;
        aiUnit.IsUnit = true;
        aiUnit.IsCombatUnit = true;
        aiUnit.CanAttackAndIsAttackEnabled = true;
        aiUnit.CanMoveAndMovementIsEnabled = true;
        aiUnit.MaxHealth = 300f;
        aiUnit.CurrentHealth = 300f;
        aiUnit.MaxRank = 5;
        aiUnit.CurrentRank = 0;
        ExistingControllers.Instance.AiUnits().Add(aiUnit);

        var playerUnitGo = new GameObject("PlayerGruntUnit");
        var playerUnit = playerUnitGo.AddComponent<EntityController>();
        playerUnit.entityId = "sample:ai-grunt";
        playerUnit.UniqueEntityId = 2002;
        playerUnit.IsControlledByPlayer = true;
        playerUnit.IsUnit = true;
        playerUnit.IsCombatUnit = true;
        playerUnit.MaxHealth = 300f;
        playerUnit.CurrentHealth = 300f;
        playerUnit.MaxRank = 5;
        playerUnit.CurrentRank = 0;
        ExistingControllers.Instance.PlayerUnits().Add(playerUnit);

        RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(aiUnit);
        RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(playerUnit);

        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance("2001") != "Aggressive")
        {
            throw new InvalidOperationException("AI squad default stance was not applied to AI unit.");
        }
        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance("2002") != "Standard")
        {
            throw new InvalidOperationException("AI squad default stance should not apply to player-controlled units.");
        }

        UnitAffixCensus affixCensus = RuntimeGameplayCommandDirector.GetUnitAffixCensus(32);
        bool hasAffixesOnAiUnit = false;
        foreach (var row in affixCensus.AffixRows)
        {
            if (row.EntityKeyId == "2001" && row.Affixes.Count == 2)
            {
                hasAffixesOnAiUnit = true;
                break;
            }
        }
        if (!hasAffixesOnAiUnit)
        {
            throw new InvalidOperationException("AI squad starting affixes were not applied to AI unit.");
        }

        var snapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9",
            true,
            true,
            1,
            0,
            0L,
            Array.Empty<PackDiagnosticReport>(),
            new[]
            {
                new UnitDiagnosticReport("sample:ai-grunt", "sample:valid-ai-defaults", "prefab", "img", true)
            },
            Array.Empty<StageDiagnosticReport>(),
            new[]
            {
                new AiProfileDiagnosticReport("sample:valid-ai", "sample:valid-ai-defaults", "prefab", new[] { "sample:adaptive-commander" }, 1)
            },
            new[]
            {
                new UnitFactoryDiagnosticReport("sample:ai-grunt", "sample:ai-grunt-factory", true, "OK")
            },
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            Array.Empty<string>());

        LoaderRuntimeAuditCensus auditCensus = RuntimeGameplayCommandDirector.GetLoaderRuntimeAuditCensus(snapshot, 64, 32);
        if (auditCensus.TotalRegisteredUnits != 1 || auditCensus.TotalRegisteredAiProfiles != 1)
        {
            throw new InvalidOperationException("LoaderRuntimeAuditCensus did not report expected unit and AI profile counts.");
        }
        if (auditCensus.AiProfileRows[0].DefaultUnitStance != "Aggressive" ||
            auditCensus.AiProfileRows[0].DefaultUnitLoadout != "AP" ||
            auditCensus.AiProfileRows[0].StartingUnitVeterancyRank != 2)
        {
            throw new InvalidOperationException("LoaderAiProfileAuditRow did not surface registered AI squad defaults.");
        }

        string auditJson = AgentDiagnosticsFormatter.SerializeLoaderRuntimeAuditCensus(auditCensus);
        int omittedUnitIdx = auditJson.IndexOf("\"omittedUnitCount\":0", StringComparison.Ordinal);
        int omittedAiIdx = auditJson.IndexOf("\"omittedAiProfileCount\":0", StringComparison.Ordinal);
        int unitRowsIdx = auditJson.IndexOf("\"units\"", StringComparison.Ordinal);
        int aiRowsIdx = auditJson.IndexOf("\"aiProfiles\"", StringComparison.Ordinal);
        if (omittedUnitIdx < 0 || omittedAiIdx < 0 || unitRowsIdx < 0 || aiRowsIdx < 0 || omittedUnitIdx > unitRowsIdx || omittedAiIdx > unitRowsIdx)
        {
            throw new InvalidOperationException("Omission counters must strictly precede units and aiProfiles in loaderRuntimeAuditCensus JSON.");
        }

        LoaderReconciliationResult reconcileResult = RuntimeGameplayCommandDirector.TryReconcileLoaderRuntimeState(snapshot);
        if (!reconcileResult.Success || reconcileResult.ReconciledUnitCount < 1)
        {
            throw new InvalidOperationException($"TryReconcileLoaderRuntimeState failed: success={reconcileResult.Success}, msg={reconcileResult.Message}");
        }

        string reconcileJson = AgentDiagnosticsFormatter.SerializeLoaderReconciliationResult(reconcileResult);
        if (!reconcileJson.Contains("\"kind\":\"loaderReconciliationResult\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SerializeLoaderReconciliationResult did not include expected kind.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
    }),
    ("unit mechanics census and visual feedback director command integration", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();

        var unitGo = new GameObject("TestMechanicsUnit");
        var ec = unitGo.AddComponent<EntityController>();
        ec.UniqueEntityId = 3001;
        ec.entityId = "test:scout";
        ec.IsUnit = true;
        ec.IsCombatUnit = true;
        ec.IsControlledByPlayer = true;
        ec.MaxHealth = 200f;
        ec.CurrentHealth = 150f;
        ec.MaxShield = 50f;
        ec.CurrentShield = 30f;
        ec.CurrentRank = 2;
        ExistingControllers.Instance.PlayerUnits().Add(ec);

        var rvd = unitGo.AddComponent<RuntimeVisualDirector>();
        rvd.SetTurretJammedOverride(true);
        if (!rvd.IsTurretJammed)
        {
            throw new InvalidOperationException("SetTurretJammedOverride(true) did not set IsTurretJammed.");
        }
        rvd.SetDoorOpenOverride(0.75f);
        if (Math.Abs(rvd.DoorOpenProgress - 0.75f) > 0.01f)
        {
            throw new InvalidOperationException("SetDoorOpenOverride(0.75f) did not set DoorOpenProgress.");
        }
        string summary = rvd.GetVisualStatusSummary();
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("GetVisualStatusSummary returned empty string.");
        }

        var census = RuntimeGameplayCommandDirector.GetUnitMechanicsCensus(16);
        if (census.TotalTrackedUnits < 1)
        {
            throw new InvalidOperationException("GetUnitMechanicsCensus did not report living test unit.");
        }

        string censusJson = AgentDiagnosticsFormatter.SerializeUnitMechanicsCensus(census);
        int omittedIdx = censusJson.IndexOf("\"omittedUnitCount\":0", StringComparison.Ordinal);
        int unitsIdx = censusJson.IndexOf("\"units\"", StringComparison.Ordinal);
        if (omittedIdx < 0 || unitsIdx < 0 || omittedIdx > unitsIdx)
        {
            throw new InvalidOperationException("Omission counter must strictly precede units collection in unitMechanicsCensus JSON.");
        }

        string ecKey = ec.UniqueEntityId.ToString();
        var fbResult = RuntimeGameplayCommandDirector.TryOrderUnitVisualFeedback(ecKey, "CritImpact", 0.5f);
        if (!fbResult.Success)
        {
            throw new InvalidOperationException($"TryOrderUnitVisualFeedback failed: {fbResult.Message}");
        }

        string fbJson = AgentDiagnosticsFormatter.SerializeVisualFeedbackResult(fbResult);
        if (!fbJson.Contains("\"feedbackType\":\"CritImpact\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SerializeVisualFeedbackResult missing feedbackType.");
        }

        var attachResult = RuntimeGameplayCommandDirector.TryOrderUnitAttachment(ecKey, "Armor", true);
        if (!attachResult.Success)
        {
            throw new InvalidOperationException($"TryOrderUnitAttachment failed: {attachResult.Message}");
        }

        var invalidFb = RuntimeGameplayCommandDirector.TryOrderUnitVisualFeedback("non-existent-key", "InvalidType", 1f);
        if (invalidFb.Success)
        {
            throw new InvalidOperationException("TryOrderUnitVisualFeedback should fail for non-existent unit.");
        }

        var invalidMagnitude = RuntimeGameplayCommandDirector.TryOrderUnitVisualFeedback(ecKey, "CritImpact", float.NaN);
        if (invalidMagnitude.Success || !invalidMagnitude.Message.Contains("finite"))
        {
            throw new InvalidOperationException("TryOrderUnitVisualFeedback must reject non-finite magnitude before reflection.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
    }),
    ("artillery minAttackRange dead-zone scales from cells to world coordinates in EntityAttackIsTargetInRangePostfix", () =>
    {
        try
        {
            var attacker = new EntityController
            {
                entityId = "test:siege-artillery",
                UniqueEntityId = 701,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var insideDeadzoneTarget = new EntityController
            {
                entityId = "test:deadzone-target",
                UniqueEntityId = 702,
                Position = new UnityEngine.Vector3(30f, 0f, 0f)
            };
            var outsideDeadzoneTarget = new EntityController
            {
                entityId = "test:outside-target",
                UniqueEntityId = 703,
                Position = new UnityEngine.Vector3(50f, 0f, 0f)
            };

            // 4.5 cells = 45 world units. At 30m, target is inside the dead-zone (30 < 45).
            RuntimeHooks.MinAttackRangeResolver = id => id == "test:siege-artillery" ? 4.5f : null;

            var attackComponent = new TestEntityAttackStub(attacker);

            bool resultInside = true;
            RuntimeHooks.EntityAttackIsTargetInRangePostfix(attackComponent, insideDeadzoneTarget, ref resultInside);
            if (resultInside)
            {
                throw new InvalidOperationException("Target at 30m should be rejected within 4.5-cell (45m) dead-zone.");
            }

            bool resultOutside = true;
            RuntimeHooks.EntityAttackIsTargetInRangePostfix(attackComponent, outsideDeadzoneTarget, ref resultOutside);
            if (!resultOutside)
            {
                throw new InvalidOperationException("Target at 50m should be accepted outside 4.5-cell (45m) dead-zone.");
            }
        }
        finally
        {
            RuntimeHooks.MinAttackRangeResolver = null;
        }
    }),
    ("TryOrderActiveSkill validates HasActiveSkill on caster unit", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var caster = new EntityController
            {
                entityId = "test:passive-caster",
                UniqueEntityId = 710,
                IsControlledByPlayer = true,
                IsUnit = true,
                HasActiveSkill = false,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(caster);

            string key = caster.UniqueEntityId.ToString();
            var failResult = RuntimeGameplayCommandDirector.TryOrderActiveSkill(key);
            if (failResult.Success)
            {
                throw new InvalidOperationException("TryOrderActiveSkill should fail when HasActiveSkill is false.");
            }
            if (!failResult.Message.Contains("does not possess an active skill", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected lack of active skill message, got: {failResult.Message}");
            }

            caster.HasActiveSkill = true;
            var successResult = RuntimeGameplayCommandDirector.TryOrderActiveSkill(key);
            if (!successResult.Success)
            {
                throw new InvalidOperationException($"TryOrderActiveSkill should succeed when HasActiveSkill is true, got: {successResult.Message}");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("TrySetUnitVeterancyRank resolves profile maxLevel when clamping rank", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        ExistingControllers.Instance.Reset();
        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:custom-vet-unit", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:custom-vet-unit",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 2,
                    maxLevel = 2
                }
            });

            var unit = new EntityController
            {
                entityId = "test:custom-vet-unit",
                UniqueEntityId = 720,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };

            bool success = RuntimeUnitMechanicsDirector.TrySetUnitVeterancyRank(unit, 3, out int resultingRank);
            if (!success || resultingRank != 2)
            {
                throw new InvalidOperationException($"TrySetUnitVeterancyRank should clamp against profile.Experience.maxLevel=2, got resultingRank={resultingRank}");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("tactical pulse triggers visual recoil and applies radius effect", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unitGo = new UnityEngine.GameObject("RCL_PulseUnit");
            var source = unitGo.AddComponent<EntityController>();
            source.entityId = "test:pulse-source";
            source.UniqueEntityId = 730;
            source.IsControlledByPlayer = true;
            source.IsUnit = true;
            source.Position = new UnityEngine.Vector3(0f, 0f, 0f);

            var ally = new EntityController
            {
                entityId = "test:pulse-ally",
                UniqueEntityId = 731,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 50f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(source);
            ExistingControllers.Instance.PlayerUnits().Add(ally);

            bool pulseOk = RuntimeUnitMechanicsDirector.TryExecuteTacticalPulse(
                source, "Rally", 15f, 20f, 4f, 8,
                out string normType, out int affectedCount, out float dmgApplied,
                out float healingApplied, out int statusCount, out string msg);

            if (!pulseOk || affectedCount < 1)
            {
                throw new InvalidOperationException($"Tactical pulse failed or affected 0 units: {msg}");
            }
            if (healingApplied <= 0f)
            {
                throw new InvalidOperationException("Tactical pulse Rally should heal injured ally.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime visual director transitions tactical stance visuals and deactivates attachments when leaving defensive stance", () =>
    {
        var director = new RuntimeVisualDirector();

        director.SetTacticalStanceVisual("Defensive");
        if (!director.ActiveTacticalStanceVisual.Equals("Defensive", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ActiveTacticalStanceVisual was not updated to Defensive.");
        }
        if (!director.AttachmentStates.TryGetValue("Armor", out bool armorActive) || !armorActive ||
            !director.AttachmentStates.TryGetValue("Shield", out bool shieldActive) || !shieldActive ||
            !director.AttachmentStates.TryGetValue("APS", out bool apsActive) || !apsActive)
        {
            throw new InvalidOperationException("Defensive stance did not activate Armor, Shield, and APS attachments.");
        }

        director.SetTacticalStanceVisual("Aggressive");
        if (!director.ActiveTacticalStanceVisual.Equals("Aggressive", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ActiveTacticalStanceVisual was not updated to Aggressive.");
        }
        if (director.AttachmentStates["Armor"] || director.AttachmentStates["Shield"] || director.AttachmentStates["APS"])
        {
            throw new InvalidOperationException("Leaving Defensive stance did not deactivate Armor, Shield, and APS attachments.");
        }
    }),
    ("runtime gameplay command director clears unit affixes and cleans up destroyed entity dictionaries", () =>
    {
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "test:tank-8802",
                UniqueEntityId = 8802,
                IsUnit = true,
                IsControlledByPlayer = true,
                MaxHealth = 100f,
                CurrentHealth = 100f,
                MaxShield = 50f,
                CurrentShield = 50f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            string entityKey = "8802";
            var stanceRes = RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(entityKey, "Aggressive");
            if (!stanceRes.Success) throw new InvalidOperationException("Failed to set tactical stance.");

            var affixRes1 = RuntimeGameplayCommandDirector.TryApplyUnitAffix(entityKey, "Swift");
            var affixRes2 = RuntimeGameplayCommandDirector.TryApplyUnitAffix(entityKey, "Bulwark");
            if (!affixRes1.Success || !affixRes2.Success) throw new InvalidOperationException("Failed to apply affixes.");

            var rem1 = RuntimeGameplayCommandDirector.TryRemoveUnitAffix(entityKey, "Swift");
            if (!rem1.Success || rem1.ActiveAffixCount != 1)
            {
                throw new InvalidOperationException($"Removing single affix failed or active count wrong: {rem1.ActiveAffixCount}");
            }

            var rem2 = RuntimeGameplayCommandDirector.TryRemoveUnitAffix(entityKey, "Bulwark");
            if (!rem2.Success || rem2.ActiveAffixCount != 0)
            {
                throw new InvalidOperationException($"Removing final affix failed or active count wrong: {rem2.ActiveAffixCount}");
            }

            RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(entityKey, "Defensive");
            RuntimeGameplayCommandDirector.TryApplyUnitAffix(entityKey, "Volatile");

            RuntimeGameplayCommandDirector.OnEntityDestroyed(entityKey);

            string currentStance = RuntimeGameplayCommandDirector.GetUnitTacticalStance(entityKey);
            if (!string.Equals(currentStance, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"OnEntityDestroyed did not reset stance to Standard: {currentStance}");
            }

            var remAfterDestroy = RuntimeGameplayCommandDirector.TryRemoveUnitAffix(entityKey, "Volatile");
            if (remAfterDestroy.Success)
            {
                throw new InvalidOperationException("Affix still reported present after entity was destroyed.");
            }
        }
        finally
        {
            RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("test stubs enforce shield capability on charge and prune removed status effects", () =>
    {
        var unitWithoutShield = new EntityController
        {
            entityId = "test:no-shield",
            MaxShield = 0f,
            CurrentShield = 0f
        };
        unitWithoutShield.ChargeShield(50f);
        if (unitWithoutShield.CurrentShield > 0f)
        {
            throw new InvalidOperationException("Unit without shield capacity should not gain shield from ChargeShield.");
        }

        var unitWithStatus = new EntityController
        {
            entityId = "test:status-unit"
        };
        unitWithStatus.SetActiveStatusEffect(StatusEffect.Electrified, SetStatusEffect.DurationType.Seconds, 5f);
        if (!unitWithStatus.HasActiveStatusEffect(StatusEffect.Electrified) || unitWithStatus.AppliedStatusEffects.Count == 0)
        {
            throw new InvalidOperationException("Failed to apply status effect in test stub.");
        }
        unitWithStatus.RemoveStatusEffect(StatusEffect.Electrified);
        if (unitWithStatus.HasActiveStatusEffect(StatusEffect.Electrified) || unitWithStatus.AppliedStatusEffects.Exists(e => e.Effect == StatusEffect.Electrified))
        {
            throw new InvalidOperationException("RemoveStatusEffect did not clear AppliedStatusEffects entry in test stub.");
        }
    }),
    ("FindRuntimeVisualDirector locates director on root and child hierarchy", () =>
    {
        var rootUnit = new EntityController { entityId = "test:child-vd-unit" };
        var rootGo = new GameObject("Root");
        rootUnit.gameObject = rootGo;

        if (RuntimeGameplayCommandDirector.FindRuntimeVisualDirector(rootUnit) != null)
        {
            throw new InvalidOperationException("FindRuntimeVisualDirector should return null when no director is attached.");
        }

        var childGo = new GameObject("Child_Visual");
        childGo.transform.parent = rootGo.transform;
        var rvd = childGo.AddComponent<RuntimeVisualDirector>();

        var found = RuntimeGameplayCommandDirector.FindRuntimeVisualDirector(rootUnit);
        if (found == null)
        {
            throw new InvalidOperationException("FindRuntimeVisualDirector failed to locate child-attached RuntimeVisualDirector.");
        }
    }),
    ("RuntimeCardChangeApplier SetField converts collections and numeric types", () =>
    {
        var dummy = new TestCardChangeTarget();
        MethodInfo? setFieldMethod = typeof(RuntimeCardChangeApplier).GetMethod("SetField", BindingFlags.NonPublic | BindingFlags.Static);
        if (setFieldMethod == null)
        {
            throw new InvalidOperationException("SetField method not found via reflection.");
        }

        // Test array to List<string>
        setFieldMethod.Invoke(null, new object[] { dummy, "ListField", new string[] { "unit:a", "unit:b" } });
        if (dummy.ListField == null || dummy.ListField.Count != 2 || dummy.ListField[0] != "unit:a")
        {
            throw new InvalidOperationException("SetField failed to convert array to List<string>.");
        }

        // Test List<string> to string[]
        setFieldMethod.Invoke(null, new object[] { dummy, "ArrayField", new List<string> { "unit:c", "unit:d" } });
        if (dummy.ArrayField == null || dummy.ArrayField.Length != 2 || dummy.ArrayField[1] != "unit:d")
        {
            throw new InvalidOperationException("SetField failed to convert List<string> to string[].");
        }

        // Test int to float conversion
        setFieldMethod.Invoke(null, new object[] { dummy, "FloatField", 42 });
        if (Math.Abs(dummy.FloatField - 42f) > 0.001f)
        {
            throw new InvalidOperationException("SetField failed to convert int to float.");
        }
    }),
    ("SetTacticalStanceVisual modulates stance state on Aggressive and Defensive", () =>
    {
        var go = new GameObject("TestStanceDirector");
        var rvd = go.AddComponent<RuntimeVisualDirector>();

        rvd.SetTacticalStanceVisual("Defensive");
        if (rvd.ActiveTacticalStanceVisual != "Defensive")
        {
            throw new InvalidOperationException("SetTacticalStanceVisual did not transition to Defensive.");
        }

        rvd.SetTacticalStanceVisual("Aggressive");
        if (rvd.ActiveTacticalStanceVisual != "Aggressive")
        {
            throw new InvalidOperationException("SetTacticalStanceVisual did not transition to Aggressive.");
        }

        rvd.SetTacticalStanceVisual("Standard");
        if (rvd.ActiveTacticalStanceVisual != "Standard")
        {
            throw new InvalidOperationException("SetTacticalStanceVisual did not transition to Standard.");
        }
    }),
    ("AI profile defaults target the declared unit roster", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();

        var firstGo = new GameObject("AiProfileUnitA");
        var first = firstGo.AddComponent<EntityController>();
        first.entityId = "sample:unit-a";
        first.UniqueEntityId = 9101;
        first.IsControlledByPlayer = false;
        first.IsUnit = true;
        ExistingControllers.Instance.AiUnits().Add(first);

        var secondGo = new GameObject("AiProfileUnitB");
        var second = secondGo.AddComponent<EntityController>();
        second.entityId = "sample:unit-b";
        second.UniqueEntityId = 9102;
        second.IsControlledByPlayer = false;
        second.IsUnit = true;
        ExistingControllers.Instance.AiUnits().Add(second);

        RuntimeGameplayCommandDirector.RegisterAiProfileUnitDefaults(
            "sample:profile-a", "Assault", 1.0f, "Aggressive", string.Empty,
            Array.Empty<string>(), 0, setActive: false, targetUnitIds: new[] { "sample:unit-a" });
        RuntimeGameplayCommandDirector.RegisterAiProfileUnitDefaults(
            "sample:profile-b", "Defensive", 1.0f, "Defensive", string.Empty,
            Array.Empty<string>(), 0, setActive: false, targetUnitIds: new[] { "sample:unit-b" });

        RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(first);
        RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(second);
        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance("9101") != "Aggressive" ||
            RuntimeGameplayCommandDirector.GetUnitTacticalStance("9102") != "Defensive")
        {
            throw new InvalidOperationException("AI defaults must follow each profile's declared unit roster instead of the last registered profile.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
    }),
    ("visual health summary aggregates quality surface material and sockets", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:visual-health",
                UniqueEntityId = 9201,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var go = new GameObject { name = "VisualHealthUnit" };
            var meshFilter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = new Mesh
            {
                name = "VisualHealthMesh",
                vertexCount = 4,
                colors = new[]
                {
                    new Color(0.30f, 0.80f, 0.60f, 1f),
                    new Color(0.40f, 0.70f, 0.20f, 1f),
                }
            };
            var material = new Material
            {
                name = "VisualHealthPbr",
                shader = new Shader { name = "RogueCommand/VehiclePBR_VertexWear" },
                shaderKeywords = new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" },
                enableInstancing = true
            };
            material.SetTexture("_MetallicGlossMap", new Texture { name = "Health_MetallicGloss" });
            material.SetTexture("_BumpMap", new Texture { name = "Health_Normal" });
            material.SetTexture("_EmissionMap", new Texture { name = "Health_Emission" });
            renderer.sharedMaterials = new[] { material };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualHealthSummary summary = RuntimeVisualHealthCensus.Current(64);
            if (summary.TrackedUnitCount != 1)
            {
                throw new InvalidOperationException("Visual health summary must track the single loader-owned unit.");
            }
            if (summary.Rows.Count != 1 || summary.Rows[0].EntityKeyId != "sample-native:visual-health")
            {
                throw new InvalidOperationException("Visual health summary row must identify the loader-owned unit.");
            }
            VisualHealthRow healthRow = summary.Rows[0];
            if (healthRow.RendererCount != 1 || healthRow.MaterialCount != 1 ||
                healthRow.PbrMaterialCount != 1 || healthRow.VertexColorSampleCount != 2 ||
                healthRow.SurfaceVertexCount != 4 || healthRow.MaterialTextureCount != 3 ||
                !healthRow.ContractPass)
            {
                throw new InvalidOperationException("Visual health summary must retain the single-pass render, material, surface, and contract metrics.");
            }

            string json = AgentDiagnosticsFormatter.SerializeVisualHealthSummary(summary);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("kind").GetString() != "visualHealthSummary" ||
                    doc.RootElement.GetProperty("omittedUnitCount").GetInt32() != 0 ||
                    doc.RootElement.GetProperty("trackedUnitCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("rows").GetArrayLength() != 1)
                {
                    throw new InvalidOperationException("SerializeVisualHealthSummary JSON format error.");
                }
                // Omission counters must precede unbounded detail collections.
                int omittedIndex = json.IndexOf("\"omittedUnitCount\"", StringComparison.Ordinal);
                int rowsIndex = json.IndexOf("\"rows\"", StringComparison.Ordinal);
                if (omittedIndex < 0 || rowsIndex < 0 || omittedIndex > rowsIndex)
                {
                    throw new InvalidOperationException("Visual health summary must place omittedUnitCount before rows.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("visual health summary counts bounded roster omissions once", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                ExistingControllers.Instance.PlayerUnits().Add(new EntityController
                {
                    entityId = "sample-native:bounded-visual-health-" + i,
                    UniqueEntityId = 9210 + i,
                    IsControlledByPlayer = true,
                    IsUnit = true,
                    Position = new Vector3(i, 0f, 0f)
                });
            }

            VisualHealthSummary summary = RuntimeVisualHealthCensus.Current(1);
            if (summary.TrackedUnitCount != 1 || summary.Rows.Count != 1)
            {
                throw new InvalidOperationException("Visual health summary must enforce the requested row bound.");
            }
            if (summary.OmittedUnitCount != 1)
            {
                throw new InvalidOperationException("Visual health summary must count each omitted unit once.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("visual health summary counts only bound material textures", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var unit = new EntityController
            {
                entityId = "sample-native:texture-health",
                UniqueEntityId = 9220,
                IsControlledByPlayer = true,
                IsUnit = true
            };
            var go = new GameObject("TextureHealthUnit");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                new Material
                {
                    name = "TextureOnlyMaterial",
                    shader = new Shader { name = "Standard" },
                    mainTexture = new Texture { name = "TextureOnlyAlbedo" }
                }
            };
            unit.gameObject = go;
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            VisualHealthSummary summary = RuntimeVisualHealthCensus.Current(1);
            if (summary.Rows.Count != 1 || summary.Rows[0].MaterialTextureCount != 1)
            {
                throw new InvalidOperationException("Visual health summary must not treat a main texture as an unbound metallic, normal, or emission map.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("performance budget policy classifies healthy degraded and critical frames", () =>
    {
        if (PerformanceBudgetPolicy.Classify(60f, 16.7f, 50f) != PerformanceVerdict.Healthy)
        {
            throw new InvalidOperationException("A 60 FPS, 16.7ms frame must be classified healthy.");
        }

        if (PerformanceBudgetPolicy.Classify(30f, 33.3f, 120f) != PerformanceVerdict.Degraded)
        {
            throw new InvalidOperationException("A 30 FPS frame must be classified degraded.");
        }

        if (PerformanceBudgetPolicy.Classify(10f, 100f, 400f) != PerformanceVerdict.Critical)
        {
            throw new InvalidOperationException("A 10 FPS frame with high GC pressure must be classified critical.");
        }

        if (PerformanceBudgetPolicy.Classify(float.NaN, float.NaN, float.NaN) != PerformanceVerdict.Critical)
        {
            throw new InvalidOperationException("Non-finite frame metrics must fail closed as critical, never healthy.");
        }

        if (PerformanceBudgetPolicy.Classify(60f, -1f, 50f) != PerformanceVerdict.Critical)
        {
            throw new InvalidOperationException("A negative frame time must fail closed as critical.");
        }

        (PerformanceVerdict verdict, string reason) = PerformanceBudgetPolicy.Evaluate(30f, 33.3f, 120f);
        if (verdict != PerformanceVerdict.Degraded || string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Evaluate must return a reason alongside the verdict.");
        }
    }),
    ("performance census serializer puts omission counters before recommendations", () =>
    {
        var census = new PerformanceCensus(
            120, 16.7f, 60f, 1f, 60, 48f, 512f, "2021.3.45f2",
            PerformanceVerdict.Healthy, "frame budget met", 0, 0.0f,
            new[] { "profile-ok" });
        string json = AgentDiagnosticsFormatter.SerializePerformanceCensus(census);
        int omittedIndex = json.IndexOf("\"omittedSampleCount\"", StringComparison.Ordinal);
        int recommendationsIndex = json.IndexOf("\"recommendations\"", StringComparison.Ordinal);
        if (omittedIndex < 0 || recommendationsIndex < 0 || omittedIndex > recommendationsIndex)
        {
            throw new InvalidOperationException("Performance census JSON must place omittedSampleCount before the unbounded recommendations list.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("verdict").GetString() != "Healthy" ||
                doc.RootElement.GetProperty("frameCount").GetInt32() != 120 ||
                doc.RootElement.GetProperty("fps").GetSingle() < 59.9f)
            {
                throw new InvalidOperationException("Performance census JSON fields do not match the census DTO.");
            }
        }

        string empty = AgentDiagnosticsFormatter.SerializePerformanceCensus(null);
        using (var doc = JsonDocument.Parse(empty))
        {
            if (doc.RootElement.GetProperty("verdict").GetString() != "Critical" ||
                doc.RootElement.GetProperty("frameCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("A null performance census must serialize as critical defaults, never healthy.");
            }
        }
    }),
    ("snapshot diff records added removed and changed keys with bounded omissions", () =>
    {
        var before = new Dictionary<string, object>
        {
            ["packs"] = 3,
            ["units"] = 18,
            ["stale"] = true,
            ["mode"] = "paused",
        };
        var after = new Dictionary<string, object>
        {
            ["packs"] = 3,
            ["units"] = 19,
            ["mode"] = "running",
            ["newKey"] = "yes",
        };
        SnapshotDiffResult diff = RuntimeSnapshotDiff.DiffDictionaries(before, after, 16);
        if (!diff.IsValid || diff.AddedKeyCount != 1 || diff.RemovedKeyCount != 1 || diff.ChangedKeyCount != 2)
        {
            throw new InvalidOperationException("Snapshot diff must count one added, one removed, and two changed top-level keys.");
        }

        var bounded = new Dictionary<string, object>();
        var expanded = new Dictionary<string, object>(before);
        for (int i = 0; i < 10; i++)
        {
            expanded["extra-" + i] = i;
        }
        SnapshotDiffResult boundedDiff = RuntimeSnapshotDiff.DiffDictionaries(bounded, expanded, 4);
        if (boundedDiff.AddedKeyCount != 14 || boundedDiff.OmittedAddedCount != 10 ||
            boundedDiff.AddedKeys.Count != 4)
        {
            throw new InvalidOperationException("Snapshot diff must bound detail rows and report omitted added keys.");
        }

        SnapshotDiffResult nullDiff = RuntimeSnapshotDiff.DiffDictionaries(null, null, 0);
        if (!nullDiff.IsValid || nullDiff.TotalChangeCount != 0)
        {
            throw new InvalidOperationException("A null snapshot diff must be a valid empty diff, not an exception.");
        }
    }),
    ("snapshot diff serializer puts omission counters before detail lists", () =>
    {
        var result = new SnapshotDiffResult(true, "", 1, 1, 1, 0, 0, 0, 1, 1, 1,
            new[] { "newKey" }, new[] { "stale" },
            new[] { new SnapshotDiffChange("units", "18", "19") });
        string json = AgentDiagnosticsFormatter.SerializeSnapshotDiff(result);
        int omittedIndex = json.IndexOf("\"omittedAddedCount\"", StringComparison.Ordinal);
        int addedIndex = json.IndexOf("\"addedKeys\"", StringComparison.Ordinal);
        if (omittedIndex < 0 || addedIndex < 0 || omittedIndex > addedIndex)
        {
            throw new InvalidOperationException("Snapshot diff JSON must place omission counters before detail lists.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("addedKeyCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("changedKeys")[0].GetProperty("key").GetString() != "units")
            {
                throw new InvalidOperationException("Snapshot diff JSON fields do not match the diff DTO.");
            }
        }
    }),
    ("frame timing policy classifies smooth minor and major stutter", () =>
    {
        if (FrameTimingPolicy.Classify(10f, 12f, 0, 10) != FrameTimingVerdict.Smooth)
        {
            throw new InvalidOperationException("A 10ms average frame must be smooth.");
        }

        if (FrameTimingPolicy.Classify(25f, 40f, 2, 10) != FrameTimingVerdict.MinorStutter)
        {
            throw new InvalidOperationException("A 25ms average frame must be minor stutter.");
        }

        if (FrameTimingPolicy.Classify(40f, 120f, 5, 10) != FrameTimingVerdict.MajorStutter)
        {
            throw new InvalidOperationException("A 40ms average with severe hitches must be major stutter.");
        }

        if (FrameTimingPolicy.Classify(10f, 12f, 0, 0) != FrameTimingVerdict.MajorStutter)
        {
            throw new InvalidOperationException("Empty frame samples must fail closed as major stutter.");
        }

        if (FrameTimingPolicy.Classify(float.NaN, 12f, 0, 10) != FrameTimingVerdict.MajorStutter)
        {
            throw new InvalidOperationException("Non-finite frame metrics must fail closed as major stutter.");
        }

        (FrameTimingVerdict verdict, string reason) = FrameTimingPolicy.Evaluate(25f, 40f, 2, 10);
        if (verdict != FrameTimingVerdict.MinorStutter || string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Evaluate must return a reason alongside the verdict.");
        }

        if (FrameTimingPolicy.ComputePercentile(new[] { 8f, 16f, 24f, 32f }, 0.95f) < 31f)
        {
            throw new InvalidOperationException("Percentile computation must approximate the 95th percentile.");
        }
    }),
    ("frame timing census aggregates samples and omits invalid and overflow entries", () =>
    {
        var samples = new List<float> { 10f, 12f, 14f, 40f, 110f, float.NaN, -1f };
        FrameTimingCensus census = RuntimeFrameTimingCensus.Aggregate(samples);
        if (census.SampleCount != 5 || census.OmittedSampleCount != 2)
        {
            throw new InvalidOperationException("Frame timing census must count valid samples and omit invalid ones.");
        }

        if (census.HitchCount != 1)
        {
            throw new InvalidOperationException("Frame timing census must count hitches above 100ms.");
        }

        if (census.Verdict != FrameTimingVerdict.MajorStutter)
        {
            throw new InvalidOperationException("A window with a 110ms hitch must be major stutter.");
        }

        FrameTimingCensus empty = RuntimeFrameTimingCensus.Aggregate(new List<float>());
        if (empty != FrameTimingCensus.Empty)
        {
            throw new InvalidOperationException("An empty frame window must return the empty census.");
        }
    }),
    ("frame timing census serializer puts omission counters before recommendations", () =>
    {
        var census = new FrameTimingCensus(
            10, 16.7f, 8f, 110f, 33.3f, 2, 1,
            FrameTimingVerdict.MinorStutter, "frame time above smooth budget", 3,
            new[] { "reduce non-essential visual effects" });
        string json = AgentDiagnosticsFormatter.SerializeFrameTimingCensus(census);
        int omittedIndex = json.IndexOf("\"omittedSampleCount\"", StringComparison.Ordinal);
        int recommendationsIndex = json.IndexOf("\"recommendations\"", StringComparison.Ordinal);
        if (omittedIndex < 0 || recommendationsIndex < 0 || omittedIndex > recommendationsIndex)
        {
            throw new InvalidOperationException("Frame timing census JSON must place omittedSampleCount before recommendations.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("sampleCount").GetInt32() != 10 ||
                doc.RootElement.GetProperty("hitchCount").GetInt32() != 1)
            {
                throw new InvalidOperationException("Frame timing census JSON fields do not match the DTO.");
            }
        }

        string empty = AgentDiagnosticsFormatter.SerializeFrameTimingCensus(null);
        using (var doc = JsonDocument.Parse(empty))
        {
            if (doc.RootElement.GetProperty("verdict").GetString() != "MajorStutter" ||
                doc.RootElement.GetProperty("sampleCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("A null frame timing census must serialize as major stutter defaults.");
            }
        }
    }),
    ("performance census current samples live unity frame data", () =>
    {
        UnityEngine.Time.deltaTime = 0.0167f;
        UnityEngine.Time.timeScale = 1.0f;
        UnityEngine.Time.frameCount = 42;
        UnityEngine.Application.targetFrameRate = 60;
        PerformanceCensus census = RuntimePerformanceCensus.Current();
        if (census.FrameCount != 42 || census.Fps <= 0f)
        {
            throw new InvalidOperationException("Performance census must sample live Unity frame data.");
        }
    }),
    ("runtime upgrade census and relic census serialize valid schema and respect omission bounds", () =>
    {
        // 1. Upgrade census tests
        UpgradeCensus emptyUpgrade = RuntimeUpgradeCensus.Current();
        string emptyUpgradeJson = AgentDiagnosticsFormatter.SerializeUpgradeCensus(emptyUpgrade);
        using (var doc = JsonDocument.Parse(emptyUpgradeJson))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "upgradeCensus" ||
                doc.RootElement.GetProperty("omittedUpgradeCount").GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("upgrades", out var upgradesProp))
            {
                throw new InvalidOperationException("Empty upgrade census JSON mismatch.");
            }
        }

        var dummyUpgrades = new List<UpgradeDetailEntry>
        {
            new UpgradeDetailEntry(
                id: "Upgrade_TestHull",
                rarity: "Rare",
                tech: "Armor",
                coinsAmount: 50,
                neededExperienceLevel: 2,
                isAllowedForDemo: true,
                isForSpecialists: false,
                inactive: false,
                offeredSystemTags: "Mech;Heavy",
                neededSystemTags: "",
                applyToFactoryWhereApplicable: true,
                cardChangeCount: 2,
                isModded: true)
        };
        var populatedUpgrade = new UpgradeCensus(
            omittedUpgradeCount: 0,
            totalUpgrades: 1,
            activeUpgrades: 1,
            moddedUpgrades: 1,
            upgrades: dummyUpgrades,
            isReady: true);

        string populatedUpgradeJson = AgentDiagnosticsFormatter.SerializeUpgradeCensus(populatedUpgrade);
        using (var doc = JsonDocument.Parse(populatedUpgradeJson))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "upgradeCensus" ||
                doc.RootElement.GetProperty("totalUpgrades").GetInt32() != 1 ||
                doc.RootElement.GetProperty("activeUpgrades").GetInt32() != 1 ||
                doc.RootElement.GetProperty("moddedUpgrades").GetInt32() != 1 ||
                !doc.RootElement.GetProperty("isReady").GetBoolean())
            {
                throw new InvalidOperationException("Populated upgrade census header JSON mismatch.");
            }
            var first = doc.RootElement.GetProperty("upgrades")[0];
            if (first.GetProperty("id").GetString() != "Upgrade_TestHull" ||
                first.GetProperty("rarity").GetString() != "Rare" ||
                first.GetProperty("coinsAmount").GetInt32() != 50 ||
                !first.GetProperty("isModded").GetBoolean() ||
                !first.GetProperty("applyToFactoryWhereApplicable").GetBoolean() ||
                first.GetProperty("cardChangeCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Populated upgrade entry JSON mismatch.");
            }
        }

        // 2. Relic census tests
        RelicCensus emptyRelic = RuntimeRelicCensus.Current();
        string emptyRelicJson = AgentDiagnosticsFormatter.SerializeRelicCensus(emptyRelic);
        using (var doc = JsonDocument.Parse(emptyRelicJson))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "relicCensus" ||
                doc.RootElement.GetProperty("omittedRelicCount").GetInt32() != 0 ||
                !doc.RootElement.TryGetProperty("relics", out var relicsProp))
            {
                throw new InvalidOperationException("Empty relic census JSON mismatch.");
            }
        }

        var dummyRelics = new List<RelicDetailEntry>
        {
            new RelicDetailEntry(
                id: "Relic_TestReactor",
                rarity: "Epic",
                tech: "Energy",
                coinsAmount: 120,
                neededExperienceLevel: 3,
                isAllowedForDemo: false,
                isForSpecialists: true,
                inactive: false,
                offeredSystemTags: "Energy;Power",
                neededSystemTags: "",
                cardChangeCount: 3,
                isModded: true)
        };
        var populatedRelic = new RelicCensus(
            omittedRelicCount: 0,
            totalRelics: 1,
            activeRelics: 1,
            moddedRelics: 1,
            relics: dummyRelics,
            isReady: true);

        string populatedRelicJson = AgentDiagnosticsFormatter.SerializeRelicCensus(populatedRelic);
        using (var doc = JsonDocument.Parse(populatedRelicJson))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "relicCensus" ||
                doc.RootElement.GetProperty("totalRelics").GetInt32() != 1 ||
                doc.RootElement.GetProperty("activeRelics").GetInt32() != 1 ||
                doc.RootElement.GetProperty("moddedRelics").GetInt32() != 1 ||
                !doc.RootElement.GetProperty("isReady").GetBoolean())
            {
                throw new InvalidOperationException("Populated relic census header JSON mismatch.");
            }
            var first = doc.RootElement.GetProperty("relics")[0];
            if (first.GetProperty("id").GetString() != "Relic_TestReactor" ||
                first.GetProperty("rarity").GetString() != "Epic" ||
                first.GetProperty("coinsAmount").GetInt32() != 120 ||
                !first.GetProperty("isModded").GetBoolean() ||
                !first.GetProperty("isForSpecialists").GetBoolean() ||
                first.GetProperty("cardChangeCount").GetInt32() != 3)
            {
                throw new InvalidOperationException("Populated relic entry JSON mismatch.");
            }
        }
    }),
    ("intermission census clamps rows and puts omission counter before node list", () =>
    {
        var rows = new List<IntermissionNodeRow>();
        for (int i = 0; i < 100; i++)
        {
            rows.Add(new IntermissionNodeRow(
                level: i % 10,
                field: i / 10,
                isChoosable: i % 2 == 0,
                isCurrentNodeOfPath: i == 0,
                hasCardReward: i % 3 == 0,
                position: i + ",0,0",
                instanceId: 1000 + i));
        }

        IntermissionCensus census = IntermissionCensus.Build(rows, true, 2, 3, 64);
        if (census.NodeCount != 64 || census.OmittedNodeCount != 36 || census.Nodes.Count != 64)
        {
            throw new InvalidOperationException(
                "Intermission census must keep maxRows rows and report the remainder as omitted.");
        }

        // Sorted by level asc then field asc; first kept row must be (0,0), last kept (6,0)... verify ordering broadly.
        for (int i = 1; i < census.Nodes.Count; i++)
        {
            IntermissionNodeRow prev = census.Nodes[i - 1];
            IntermissionNodeRow current = census.Nodes[i];
            if (prev.Level > current.Level || (prev.Level == current.Level && prev.Field > current.Field))
            {
                throw new InvalidOperationException("Intermission census rows must be sorted by level then field.");
            }
        }

        string json = AgentDiagnosticsFormatter.SerializeIntermissionCensus(census);
        int omittedIndex = json.IndexOf("\"omittedNodeCount\"", StringComparison.Ordinal);
        int nodesIndex = json.IndexOf("\"nodes\"", StringComparison.Ordinal);
        if (omittedIndex < 0 || nodesIndex < 0 || omittedIndex > nodesIndex)
        {
            throw new InvalidOperationException("The omission counter must appear before the nodes detail list.");
        }

        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "intermissionCensus"
                || !root.GetProperty("isIntermissionActive").GetBoolean()
                || root.GetProperty("omittedNodeCount").GetInt32() != 36
                || root.GetProperty("nodeCount").GetInt32() != 64
                || root.GetProperty("currentStage").GetInt32() != 2
                || root.GetProperty("currentLevel").GetInt32() != 3
                || root.GetProperty("nodes").GetArrayLength() != 64)
            {
                throw new InvalidOperationException("Intermission census JSON header mismatch.");
            }

            JsonElement first = root.GetProperty("nodes")[0];
            if (first.GetProperty("level").GetInt32() != 0 || first.GetProperty("field").GetInt32() != 0
                || !first.GetProperty("isChoosable").GetBoolean()
                || first.GetProperty("position").GetString() != "0,0,0")
            {
                throw new InvalidOperationException("Intermission census first-row JSON mismatch.");
            }
        }

        // Default clamp: non-positive maxRows falls back to 64.
        IntermissionCensus defaulted = IntermissionCensus.Build(rows, true, 0, 0, 0);
        if (defaulted.NodeCount != 64 || defaulted.OmittedNodeCount != 36)
        {
            throw new InvalidOperationException("Non-positive maxRows must fall back to the 64-row default.");
        }

        // Verify FindChoosableNodeIndex and TryPickNode integration
        if (IntermissionCensus.FindChoosableNodeIndex(null, 0, 0) != -1)
        {
            throw new InvalidOperationException("FindChoosableNodeIndex must return -1 on null rows.");
        }
        int foundIndex = IntermissionCensus.FindChoosableNodeIndex(census.Nodes, 0, 0);
        if (foundIndex < 0 || census.Nodes[foundIndex].Level != 0 || census.Nodes[foundIndex].Field != 0 || !census.Nodes[foundIndex].IsChoosable)
        {
            throw new InvalidOperationException("FindChoosableNodeIndex must locate the choosable node matching level and field.");
        }
        if (IntermissionCensus.FindChoosableNodeIndex(census.Nodes, 99, 99) != -1)
        {
            throw new InvalidOperationException("FindChoosableNodeIndex must return -1 when no matching choosable node exists.");
        }
        IntermissionNodeRow picked;
        if (!RuntimeIntermissionDirector.TryPickNode(census.Nodes, 0, 0, out picked) || picked.Level != 0 || picked.Field != 0)
        {
            throw new InvalidOperationException("TryPickNode must resolve choosable node matching level and field via FindChoosableNodeIndex.");
        }

        // Null input must not throw and must report an empty, inactive census.
        string emptyJson = AgentDiagnosticsFormatter.SerializeIntermissionCensus(
            IntermissionCensus.Build(null, false, 0, 0, 64));
        using (JsonDocument doc = JsonDocument.Parse(emptyJson))
        {
            if (doc.RootElement.GetProperty("nodeCount").GetInt32() != 0
                || doc.RootElement.GetProperty("nodes").GetArrayLength() != 0)
            {
                throw new InvalidOperationException("Null intermission rows must serialize as an empty census.");
            }
        }
    }),
    ("TryEnterLevelNode fails closed when the level node is unknown", () =>
    {
        EnterLevelNodeResult result = RuntimeIntermissionEnter.TryEnterLevelNode(999, 999);
        if (result.Success || result.Level != 999 || result.Field != 999)
        {
            throw new InvalidOperationException("Unknown intermission nodes must fail closed without entering.");
        }

        if (result.Message.IndexOf("999", StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("The unknown-node failure must state the requested level/field.");
        }

        string json = AgentDiagnosticsFormatter.SerializeEnterLevelNodeResult(result);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("success").GetBoolean()
                || doc.RootElement.GetProperty("level").GetInt32() != 999
                || doc.RootElement.GetProperty("field").GetInt32() != 999)
            {
                throw new InvalidOperationException("Enter-level unknown-node JSON mismatch.");
            }
        }
    }),
    ("SetNavigatorPaused fails closed when Navigator is absent", () =>
    {
        NavigatorPauseResult result = RuntimeNavigatorState.TrySetPaused(true);
        if (result.Success)
        {
            throw new InvalidOperationException("Pausing without a Navigator must fail closed in the offline harness.");
        }

        string json = AgentDiagnosticsFormatter.SerializeNavigatorPauseResult(result);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("success").GetBoolean()
                || !doc.RootElement.GetProperty("isPaused").GetBoolean())
            {
                throw new InvalidOperationException("Navigator fail-closed JSON mismatch.");
            }
        }

        NavigatorStateSnapshot snapshot = RuntimeNavigatorState.Current();
        string stateJson = AgentDiagnosticsFormatter.SerializeNavigatorState(snapshot);
        using (JsonDocument doc = JsonDocument.Parse(stateJson))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("isPaused").GetBoolean()
                || root.GetProperty("isDebugPaused").GetBoolean())
            {
                throw new InvalidOperationException("Navigator state without a Navigator must report unpaused.");
            }
        }
    }),
    ("intermission and navigator serializers escape strings safely", () =>
    {
        string trickyMessage = "node \"quoted\" \\ backslash \n newline \t tab";
        string json = AgentDiagnosticsFormatter.SerializeEnterLevelNodeResult(
            new EnterLevelNodeResult(false, trickyMessage, 3, 7));
        if (json.IndexOf("\"quoted\"", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Serializer must escape embedded quotes instead of emitting them raw.");
        }

        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            string parsed = doc.RootElement.GetProperty("message").GetString() ?? string.Empty;
            if (!string.Equals(parsed, trickyMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Escaped serializer output must round-trip through a JSON parser.");
            }
        }

        var rows = new[]
        {
            new IntermissionNodeRow(1, 2, true, false, true, "1.5,\"x\",\\3", 42),
        };
        string censusJson = AgentDiagnosticsFormatter.SerializeIntermissionCensus(
            IntermissionCensus.Build(rows, true, 4, 5, 64));
        using (JsonDocument doc = JsonDocument.Parse(censusJson))
        {
            string parsedPosition = doc.RootElement.GetProperty("nodes")[0].GetProperty("position").GetString() ?? string.Empty;
            if (!string.Equals(parsedPosition, "1.5,\"x\",\\3", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Intermission node position strings must be escaped safely.");
                }
                }
                }),
                ("deep intermission choosable-field count mirrors LevelMap.IsChoosable", () =>
    {
        // Fresh run: CurrentLevel == -1 -> every field of level 0 is choosable.
        int fresh = RuntimeIntermissionDeepReads.CountChoosableFields(
            -1, 0, new[] { 1, 2, 3 }, 0, new List<(int, int)>());
        if (fresh != 3)
        {
            throw new InvalidOperationException("CountChoosableFields must mark every level-0 field choosable on a fresh run.");
        }

        // Mid-map: only (CurrentField, field) pairs in Connections(next) count.
        var fields = new[] { 1, 2, 3 };
        var connections = new List<(int, int)> { (1, 2), (1, 3), (2, 1), (2, 3) };
        int atField1 = RuntimeIntermissionDeepReads.CountChoosableFields(3, 4, fields, 1, connections);
        if (atField1 != 2)
        {
            throw new InvalidOperationException("CountChoosableFields must count only edges out of CurrentField.");
        }

        // Wrong level: nothing is choosable.
        int wrongLevel = RuntimeIntermissionDeepReads.CountChoosableFields(3, 2, fields, 1, connections);
        if (wrongLevel != 0)
        {
            throw new InvalidOperationException("CountChoosableFields must return 0 outside CurrentLevel+1.");
        }

        // Empty inputs are safe.
        if (RuntimeIntermissionDeepReads.CountChoosableFields(-1, 0, null, 0, null) != 0)
        {
            throw new InvalidOperationException("CountChoosableFields must return 0 for null fields.");
        }
    }),
    ("deep intermission route and node detail serializers emit ordered fields", () =>
    {
        var state = new IntermissionRouteState(
            true, 3, 1, true, false, true, false, 4, 5, 1, 1, 0, 16, "BossAi", "BossWorld", "BossMap");
        string stateJson = AgentDiagnosticsFormatter.SerializeLevelMapRouteState(state);
        using (JsonDocument doc = JsonDocument.Parse(stateJson))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "levelMapRouteState"
                || !root.GetProperty("isRouteAvailable").GetBoolean()
                || root.GetProperty("currentLevel").GetInt32() != 3
                || root.GetProperty("currentField").GetInt32() != 1
                || root.GetProperty("choosableFieldCount").GetInt32() != 0
                || !root.GetProperty("isLastLevelBeforeBoss").GetBoolean()
                || root.GetProperty("bossAiPrefabName").GetString() != "BossAi")
            {
                throw new InvalidOperationException("Route state serializer mismatch: " + stateJson);
            }
        }

        string unavailableJson = AgentDiagnosticsFormatter.SerializeLevelMapRouteState(IntermissionRouteState.Unavailable);
        using (JsonDocument doc = JsonDocument.Parse(unavailableJson))
        {
            if (doc.RootElement.GetProperty("isRouteAvailable").GetBoolean()
                || doc.RootElement.GetProperty("currentLevel").GetInt32() != -1)
            {
                throw new InvalidOperationException("Unavailable route state must emit neutral defaults.");
            }
        }

        var detail = new IntermissionNodeDetail(
            true, 3, 1, false, false, true, false, true, false, true, false, true, false, false, true,
            "pool:bs", 2, 1, 4);
        string detailJson = AgentDiagnosticsFormatter.SerializeIntermissionNodeDetail(detail);
        using (JsonDocument doc = JsonDocument.Parse(detailJson))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "intermissionNodeDetail"
                || !root.GetProperty("found").GetBoolean()
                || root.GetProperty("isChoosable").GetBoolean()
                || root.GetProperty("isBoss").GetBoolean()
                || !root.GetProperty("hasCardReward").GetBoolean()
                || !root.GetProperty("hasRelicReward").GetBoolean()
                || root.GetProperty("blueprintPoolId").GetString() != "pool:bs"
                || root.GetProperty("outgoingConnectionCount").GetInt32() != 2
                || root.GetProperty("incomingConnectionCount").GetInt32() != 1)
            {
                throw new InvalidOperationException("Node detail serializer mismatch: " + detailJson);
            }
        }

        string notFoundJson = AgentDiagnosticsFormatter.SerializeIntermissionNodeDetail(
            IntermissionNodeDetail.NotFound(3, 1));
        using (JsonDocument doc = JsonDocument.Parse(notFoundJson))
        {
            if (doc.RootElement.GetProperty("found").GetBoolean())
            {
                throw new InvalidOperationException("NotFound node detail must report found=false.");
            }
        }

        if (!RuntimeIntermissionDeepReads.IsBossNodeCoordinate(4, 0, 4)
            || RuntimeIntermissionDeepReads.IsBossNodeCoordinate(4, 1, 4)
            || RuntimeIntermissionDeepReads.IsBossNodeCoordinate(4, 0, 0))
        {
            throw new InvalidOperationException("The special intermission boss node must be levelCount:0 only.");
        }

        IntermissionNodeDetail bossDetail = RuntimeIntermissionDeepReads.CreateBossNodeDetail(4, 0, 4, true, 4);
        string bossJson = AgentDiagnosticsFormatter.SerializeIntermissionNodeDetail(bossDetail);
        using (JsonDocument doc = JsonDocument.Parse(bossJson))
        {
            JsonElement root = doc.RootElement;
            if (!root.GetProperty("found").GetBoolean()
                || !root.GetProperty("isBoss").GetBoolean()
                || !root.GetProperty("isChoosable").GetBoolean()
                || root.GetProperty("incomingConnectionCount").GetInt32() != 4
                || root.GetProperty("fieldCountInLevel").GetInt32() != 1
                || root.GetProperty("hasCardReward").GetBoolean()
                || root.GetProperty("blueprintPoolId").GetString() != string.Empty)
            {
                throw new InvalidOperationException("Special boss node detail mismatch: " + bossJson);
            }
        }
    }),
    ("deep intermission already-selected predicate requires the current flag", () =>
    {
        var rows = new[]
        {
            new IntermissionNodeRow(0, 1, true, true, true, "0,0,0", 42),
            new IntermissionNodeRow(0, 2, true, false, true, "0,0,0", 43),
            new IntermissionNodeRow(1, 1, true, false, false, "0,0,0", 44),
        };
        if (!RuntimeIntermissionDirector.IsAlreadySelected(rows, 0, 1))
        {
            throw new InvalidOperationException("A node marked IsCurrentNodeOfPath must be reported as already selected.");
        }
        if (RuntimeIntermissionDirector.IsAlreadySelected(rows, 0, 2))
        {
            throw new InvalidOperationException("A choosable node without the current flag must not count as selected.");
        }
        if (RuntimeIntermissionDirector.IsAlreadySelected(rows, 1, 1)
            || RuntimeIntermissionDirector.IsAlreadySelected(rows, 99, 99)
            || RuntimeIntermissionDirector.IsAlreadySelected(null, 0, 1))
        {
            throw new InvalidOperationException("Unknown or null inputs must never count as selected.");
        }
    }),
    ("UI click serializer puts the clicked outcome before detail", () =>
                {
                    string json = AgentDiagnosticsFormatter.SerializeUiClickResult(
                        new UiClickResult(true, RuntimeUiClickDirector.ViaEventSystem, "StartButton", "StartButton",
                            100f, 100f, new[] { "pointerEnter", "pointerDown", "pointerUp", "pointerClick" },
                            "clicked StartButton by real coordinates (pointerEnter→pointerDown→pointerUp→pointerClick)."));
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.GetProperty("kind").GetString() != "uiClickResult" || !root.GetProperty("clicked").GetBoolean())
                        {
                            throw new InvalidOperationException("UI click serializer must expose the clicked outcome first.");
                        }

                        if (root.GetProperty("via").GetString() != "EventSystemRaycast"
                            || root.GetProperty("targetName").GetString() != "StartButton"
                            || root.GetProperty("screenX").GetSingle() != 100f
                            || root.GetProperty("phases").GetArrayLength() != 4)
                        {
                            throw new InvalidOperationException("UI click serializer mismatch: " + json);
                        }

                        int clickedIndex = json.IndexOf("\"clicked\"", StringComparison.Ordinal);
                        int messageIndex = json.IndexOf("\"message\"", StringComparison.Ordinal);
                        if (clickedIndex > messageIndex)
                        {
                            throw new InvalidOperationException("UI click serializer must place the outcome before the message detail.");
                        }
                    }
                }),
                ("UI click at real screen coordinates replays the input-module sequence", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateUiClickScene(out GameObject button,
                        out TestUiClickHandler handler);
                    try
                    {
                        UiClickResult result = RuntimeUiClickDirector.TryClickGameObject(button);

                        if (!result.Clicked)
                        {
                            throw new InvalidOperationException("A UI click on a raycast-reachable button must be delivered.");
                        }

                        if (result.Via != RuntimeUiClickDirector.ViaEventSystem)
                        {
                            throw new InvalidOperationException("UI clicks must report the EventSystem raycast path.");
                        }

                        if (result.ScreenX != 100f || result.ScreenY != 100f)
                        {
                            throw new InvalidOperationException("The click must target the button's real screen center (100, 100).");
                        }

                        string[] expected = { "pointerEnter", "pointerDown", "pointerUp", "pointerClick" };
                        if (!string.Join(",", result.Phases).Equals(string.Join(",", expected), StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("The replayed sequence must be pointerEnter→pointerDown→pointerUp→pointerClick, got " + string.Join(",", result.Phases) + ".");
                        }

                        if (!string.Join(",", handler.Events).Equals("OnPointerEnter,OnPointerDown,OnPointerUp,OnPointerClick", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("The button must receive the full pointer sequence, got " + string.Join(",", handler.Events) + ".");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("UI click fails closed when an unrelated element occludes the target", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateUiClickScene(out GameObject button,
                        out TestUiClickHandler handler);
                    var occluder = new GameObject("Occluder");
                    UnityEngine.EventSystems.EventSystem.TestRaycastResults = new List<UnityEngine.EventSystems.RaycastResult>
                    {
                        new UnityEngine.EventSystems.RaycastResult { gameObject = occluder, screenPoint = new Vector2(100f, 100f) },
                    };
                    try
                    {
                        UiClickResult result = RuntimeUiClickDirector.TryClickGameObject(button);

                        if (result.Clicked || result.Phases.Length != 0)
                        {
                            throw new InvalidOperationException("An occluded UI click must fail closed with no dispatched phases.");
                        }

                        if (!result.Message.Contains("Occluder") || !result.Message.Contains("occlusion gate"))
                        {
                            throw new InvalidOperationException("The occlusion failure must name the occluder and the stop point, got: " + result.Message);
                        }

                        if (handler.Events.Count != 0)
                        {
                            throw new InvalidOperationException("No pointer events may reach the button while occluded.");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("UI click fails closed when the target center projects off-screen", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateUiClickScene(out GameObject button,
                        out TestUiClickHandler handler);
                    try
                    {
                        RectTransformUtility.WorldToScreenPointHook = world => new Vector2(-50f, 5000f);
                        UiClickResult result = RuntimeUiClickDirector.TryClickGameObject(button);

                        if (result.Clicked || result.Phases.Length != 0)
                        {
                            throw new InvalidOperationException("An off-screen UI click must fail closed with no dispatched phases.");
                        }

                        if (!result.Message.Contains("off-screen"))
                        {
                            throw new InvalidOperationException("The off-screen failure must carry a stop point, got: " + result.Message);
                        }

                        if (handler.Events.Count != 0)
                        {
                            throw new InvalidOperationException("No pointer events may be dispatched past the off-screen gate.");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("click-only widget receives pointerClick through the input-module click-handler fallback", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateUiClickScene(out GameObject button,
                        out TestUiClickHandler handler);
                    // Michsky-style widgets implement only enter + click handlers:
                    // the input module falls back from the null down handler to
                    // the IPointerClickHandler object (pointerPress = click handler).
                    button.DestroyImmediate(handler);
                    TestUiClickClickOnlyHandler clickOnly = button.AddComponent<TestUiClickClickOnlyHandler>();
                    try
                    {
                        UiClickResult result = RuntimeUiClickDirector.TryClickGameObject(button);

                        if (!result.Clicked)
                        {
                            throw new InvalidOperationException("A click-only widget must be clicked via the input-module fallback: " + result.Message);
                        }

                        if (!string.Join(",", clickOnly.Events).Equals("OnPointerEnter,OnPointerClick", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Click-only widget must receive Enter+Click (no Down/Up), got " + string.Join(",", clickOnly.Events) + ".");
                        }

                        if (!string.Join(",", result.Phases).Equals("pointerEnter,pointerClick", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Click-only phases must be pointerEnter,pointerClick, got " + string.Join(",", result.Phases) + ".");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("instance-ID resolver covers component IDs (intermission census space)", () =>
                {
                    var node = new GameObject("OverWorldMapEnemy(Clone)");
                    Component behaviour = node.AddComponent<MeshRenderer>();
                    int componentId = behaviour.GetInstanceID();
                    int gameObjectId = node.GetInstanceID();

                    GameObject byComponent = RuntimeUiClickDirector.ResolveGameObjectByInstanceId(
                        componentId,
                        () => new Component[] { behaviour },
                        () => new Transform[] { node.transform });
                    GameObject byTransform = RuntimeUiClickDirector.ResolveGameObjectByInstanceId(
                        gameObjectId,
                        () => new Component[] { behaviour },
                        () => new Transform[] { node.transform });
                    GameObject missing = RuntimeUiClickDirector.ResolveGameObjectByInstanceId(
                        -999999,
                        () => new Component[] { behaviour },
                        () => new Transform[] { node.transform });

                    if (byComponent != node || byTransform != node || missing != null)
                    {
                        throw new InvalidOperationException("The instance-ID resolver must resolve component IDs (census space) and GameObject IDs, and null otherwise.");
                    }
                }),
                ("point click delivers the full sequence to the top element even without an expected target", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateUiClickScene(out GameObject button,
                        out TestUiClickHandler handler);
                    try
                    {
                        UiClickResult result = RuntimeUiClickDirector.TryClickAtScreenPoint(new Vector2(100f, 100f));

                        if (!result.Clicked || result.TargetName != string.Empty)
                        {
                            throw new InvalidOperationException("Point mode must click the top element without a target name.");
                        }

                        if (!string.Join(",", handler.Events).Equals("OnPointerEnter,OnPointerDown,OnPointerUp,OnPointerClick", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Point mode must hit the button's handlers, got " + string.Join(",", handler.Events) + ".");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("world click at the node's real screen position dispatches engine messages", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateWorldClickScene(out GameObject node);
                    try
                    {
                        Physics.TestHasHit = true;
                        Physics.TestHit = new RaycastHit { transform = node.transform, collider = null, distance = 5f };

                        UiClickResult result = RuntimeUiClickDirector.TryClickWorldObject(node);

                        if (!result.Clicked || result.Via != RuntimeUiClickDirector.ViaPhysics)
                        {
                            throw new InvalidOperationException("A world click must succeed when physics pick hits the node: " + result.Message);
                        }

                        if (!string.Join(",", node.SentMessages).Equals("OnMouseEnter,OnMouseDown", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("The node must receive OnMouseEnter+OnMouseDown via SendMessage, got " + string.Join(",", node.SentMessages) + ".");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("world click fails closed when physics picks another object in front", () =>
                {
                    EventSystemsHarnessScope scope = EventSystemsHarnessScope.CreateWorldClickScene(out GameObject node);
                    var occluder = new GameObject("ForegroundRubble");
                    try
                    {
                        Physics.TestHasHit = true;
                        Physics.TestHit = new RaycastHit { transform = occluder.transform, collider = null, distance = 5f };

                        UiClickResult result = RuntimeUiClickDirector.TryClickWorldObject(node);

                        if (result.Clicked || result.Phases.Length != 0)
                        {
                            throw new InvalidOperationException("A world click must fail closed when physics hits another object first.");
                        }

                        if (!result.Message.Contains("occlusion gate") || !result.Message.Contains("ForegroundRubble"))
                        {
                            throw new InvalidOperationException("The world occlusion failure must name the occluder, got: " + result.Message);
                        }

                        if (node.SentMessages.Count != 0 || occluder.SentMessages.Count != 0)
                        {
                            throw new InvalidOperationException("No mouse messages may be dispatched past the occlusion gate.");
                        }
                    }
                    finally { scope.Dispose(); }
                }),
                ("hierarchy gate accepts self and descendants only", () =>
                {
                    var parent = new GameObject("Parent");
                    var child = new GameObject("Child");
                    child.transform.parent = parent.transform;
                    var unrelated = new GameObject("Unrelated");

                    if (!RuntimeUiClickDirector.IsInHierarchyOrSelf(parent, parent)
                        || !RuntimeUiClickDirector.IsInHierarchyOrSelf(child, parent))
                    {
                        throw new InvalidOperationException("The hierarchy gate must accept self and descendants.");
                    }

                    if (RuntimeUiClickDirector.IsInHierarchyOrSelf(unrelated, parent)
                        || RuntimeUiClickDirector.IsInHierarchyOrSelf(parent, child))
                    {
                        throw new InvalidOperationException("The hierarchy gate must reject unrelated and parent hits.");
                    }
                }),
                ("intermission director picks exact choosable node before frontier", () =>
                {
                var rows = new List<IntermissionNodeRow>
                {
                new IntermissionNodeRow(0, 0, true, true, false, "0,0,0", 101),
                new IntermissionNodeRow(2, 1, true, false, true, "2,1,0", 202),
                new IntermissionNodeRow(2, 1, false, true, false, "2,1,0", 303),
                new IntermissionNodeRow(0, 1, true, false, false, "0,1,0", 404),
                };

                IntermissionNodeRow picked;
                if (!RuntimeIntermissionDirector.TryPickNode(rows, 2, 1, out picked)
                || picked.Level != 2 || picked.Field != 1 || picked.InstanceId != 202 || !picked.IsChoosable)
                {
                throw new InvalidOperationException("TryPickNode must prefer the exact choosable match over the frontier candidate.");
                }
                }),
                ("intermission director picks frontier node for wildcard selection", () =>
                {
                var rows = new List<IntermissionNodeRow>
                {
                new IntermissionNodeRow(3, 0, true, false, false, "3,0,0", 501),
                new IntermissionNodeRow(1, 5, true, false, false, "1,5,0", 502),
                new IntermissionNodeRow(2, 1, true, true, false, "2,1,0", 503),
                new IntermissionNodeRow(0, 0, false, true, false, "0,0,0", 504),
                };

                IntermissionNodeRow picked;
                if (!RuntimeIntermissionDirector.TryPickNode(rows, -1, -1, out picked)
                || picked.InstanceId != 503 || picked.Level != 2)
                {
                throw new InvalidOperationException("Wildcard pick must rank current-path nodes first (frontier preference).");
                }

                IntermissionNodeRow frontier;
                if (!RuntimeIntermissionDirector.TryPickChoosableFrontier(rows, out frontier)
                || frontier.InstanceId != 503)
                {
                throw new InvalidOperationException("TryPickChoosableFrontier must follow the same frontier ranking.");
                }

                var noCurrent = new List<IntermissionNodeRow>
                {
                new IntermissionNodeRow(4, 1, true, false, false, "4,1,0", 601),
                new IntermissionNodeRow(1, 9, true, false, false, "1,9,0", 602),
                new IntermissionNodeRow(1, 2, true, false, false, "1,2,0", 603),
                };
                if (!RuntimeIntermissionDirector.TryPickChoosableFrontier(noCurrent, out frontier)
                || frontier.InstanceId != 603 || frontier.Level != 1 || frontier.Field != 2)
                {
                throw new InvalidOperationException("Without a current-path node the frontier must be lowest level then lowest field.");
                }
                }),
                ("intermission director fails closed on empty or unknown census", () =>
                {
                IntermissionNodeRow picked;
                if (RuntimeIntermissionDirector.TryPickNode(null, 0, 0, out picked))
                {
                throw new InvalidOperationException("Null census rows must fail closed in TryPickNode.");
                }
                if (RuntimeIntermissionDirector.TryPickNode(new List<IntermissionNodeRow>(), 0, 0, out picked))
                {
                throw new InvalidOperationException("Empty census rows must fail closed in TryPickNode.");
                }
                if (RuntimeIntermissionDirector.TryPickNode(new List<IntermissionNodeRow>(), -1, -1, out picked))
                {
                throw new InvalidOperationException("Wildcard pick on empty census must fail closed.");
                }
                if (RuntimeIntermissionDirector.TryPickChoosableFrontier(null, out picked)
                || RuntimeIntermissionDirector.TryPickChoosableFrontier(new List<IntermissionNodeRow>(), out picked))
                {
                throw new InvalidOperationException("Frontier pick on null/empty census must fail closed.");
                }

                var rows = new List<IntermissionNodeRow>
                {
                new IntermissionNodeRow(1, 1, true, false, false, "1,1,0", 701),
                new IntermissionNodeRow(9, 9, false, false, false, "9,9,0", 702),
                };
                if (RuntimeIntermissionDirector.TryPickNode(rows, 999, 999, out picked))
                {
                throw new InvalidOperationException("Unknown level/field with other choosable nodes must fail closed (exact match required).");
                }
                if (RuntimeIntermissionDirector.TryPickNode(rows, 9, 9, out picked))
                {
                throw new InvalidOperationException("A non-choosable exact cell must fail closed.");
                }
                }),
                ("intermission director selection fails closed without scene and serializers escape safely", () =>
                {
                IntermissionNodeSelection selection = RuntimeIntermissionDirector.TrySelectLevelNode(999, 999);
                if (selection.Selected || selection.Level != 999 || selection.Field != 999)
                {
                throw new InvalidOperationException("TrySelectLevelNode without a live scene must fail closed.");
                }
                if (selection.Reason.IndexOf("999", StringComparison.Ordinal) < 0)
                {
                throw new InvalidOperationException("Selection fail-closed reason must state the requested level/field.");
                }

                IntermissionNodeSelection selectionOut;
                bool selectedOk = RuntimeIntermissionDirector.TrySelectLevelNode(999, 999, out selectionOut);
                if (selectedOk || selectionOut.Selected)
                {
                throw new InvalidOperationException("TrySelectLevelNode out-overload must report false without a scene.");
                }

                // Harness cannot compile LoaderDiagnosticsComponent (Newtonsoft/MonoBehaviour);
                // exercise the exact body of OrderSelectLevelNodeJson here: Serialize(TrySelect(...)).
                string orderSelectJson = AgentDiagnosticsFormatter.SerializeIntermissionSelectionResult(
                    RuntimeIntermissionDirector.TrySelectLevelNode(999, 999));
                using (JsonDocument doc = JsonDocument.Parse(orderSelectJson))
                {
                    if (doc.RootElement.GetProperty("selected").GetBoolean()
                        || doc.RootElement.GetProperty("level").GetInt32() != 999
                        || doc.RootElement.GetProperty("field").GetInt32() != 999)
                    {
                        throw new InvalidOperationException("OrderSelectLevelNodeJson body must fail closed without a scene.");
                    }
                }

                string trickyReason = "select \"node\" \\ slash\nnewline\t tab";
                string trickyJson = AgentDiagnosticsFormatter.SerializeIntermissionSelectionResult(
                new IntermissionNodeSelection(3, 7, 42, false, trickyReason));
                if (trickyJson.IndexOf("\"node\"", StringComparison.Ordinal) >= 0)
                {
                throw new InvalidOperationException("Selection serializer must escape embedded quotes.");
                }
                using (JsonDocument doc = JsonDocument.Parse(trickyJson))
                {
                string parsed = doc.RootElement.GetProperty("reason").GetString() ?? string.Empty;
                if (!string.Equals(parsed, trickyReason, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Selection serializer output must round-trip through a JSON parser.");
                }
                if (doc.RootElement.GetProperty("kind").GetString() != "intermissionNodeSelection"
                    || doc.RootElement.GetProperty("instanceId").GetInt32() != 42)
                {
                    throw new InvalidOperationException("Selection serializer header mismatch.");
                }
                }
                }),
                ("intermission director entry fails closed on unknown node via order wrapper", () =>
                {
                IntermissionLevelEntry entry = RuntimeIntermissionDirector.TryEnterLevelNodeFlow(999, 999);
                if (entry.Success || entry.Entered || entry.Level != 999 || entry.Field != 999)
                {
                throw new InvalidOperationException("TryEnterLevelNodeFlow without a scene must fail closed.");
                }
                if (entry.Message.IndexOf("999", StringComparison.Ordinal) < 0)
                {
                throw new InvalidOperationException("Entry fail-closed message must state the requested level/field.");
                }

                IntermissionLevelEntry entryOut;
                bool entryOk = RuntimeIntermissionDirector.TryEnterLevelNodeFlow(999, 999, out entryOut);
                if (entryOk || entryOut.Success || entryOut.Entered)
                {
                throw new InvalidOperationException("TryEnterLevelNodeFlow out-overload must report false without a scene.");
                }

                // Harness cannot compile LoaderDiagnosticsComponent; exercise the exact
                // bodies of OrderEnterLevelNodeJson and TryEnterLevelNodeJson delegations.
                string orderEnterJson = AgentDiagnosticsFormatter.SerializeIntermissionLevelEntryResult(
                    RuntimeIntermissionDirector.TryEnterLevelNodeFlow(999, 999));
                using (JsonDocument doc = JsonDocument.Parse(orderEnterJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("success").GetBoolean()
                        || root.GetProperty("entered").GetBoolean()
                        || root.GetProperty("selected").GetBoolean()
                        || root.GetProperty("level").GetInt32() != 999
                        || root.GetProperty("field").GetInt32() != 999)
                    {
                        throw new InvalidOperationException("OrderEnterLevelNodeJson body must fail closed on an unknown node.");
                    }
                    int successIndex = orderEnterJson.IndexOf("\"success\"", StringComparison.Ordinal);
                    int messageIndex = orderEnterJson.IndexOf("\"message\"", StringComparison.Ordinal);
                    if (successIndex < 0 || messageIndex < 0 || successIndex > messageIndex)
                    {
                        throw new InvalidOperationException("Entry serializer must place status counters before the message detail.");
                    }
                }

                string trickyMessage = "enter \"level\" \\ path\nnext";
                string trickyEntryJson = AgentDiagnosticsFormatter.SerializeIntermissionLevelEntryResult(
                new IntermissionLevelEntry(4, 5, 77, false, trickyMessage, false, false));
                using (JsonDocument doc = JsonDocument.Parse(trickyEntryJson))
                {
                string parsed = doc.RootElement.GetProperty("message").GetString() ?? string.Empty;
                if (!string.Equals(parsed, trickyMessage, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Entry serializer must escape strings safely and round-trip.");
                }
                if (doc.RootElement.GetProperty("kind").GetString() != "intermissionLevelEntry"
                    || doc.RootElement.GetProperty("instanceId").GetInt32() != 77)
                {
                    throw new InvalidOperationException("Entry serializer header mismatch.");
                }
                }

                // Compat shim: TryEnterLevelNodeJson reimplemented over the director must
                // stay fail-closed on unknown nodes (same body: director -> EnterLevelNodeResult).
                IntermissionLevelEntry compatEntry = RuntimeIntermissionDirector.TryEnterLevelNodeFlow(999, 999);
                string compatJson = AgentDiagnosticsFormatter.SerializeEnterLevelNodeResult(
                    new EnterLevelNodeResult(compatEntry.Success, compatEntry.Message, compatEntry.Level, compatEntry.Field));
                using (JsonDocument doc = JsonDocument.Parse(compatJson))
                {
                    if (doc.RootElement.GetProperty("success").GetBoolean())
                    {
                        throw new InvalidOperationException("TryEnterLevelNodeJson delegation must stay fail-closed.");
                    }
                }

                // Intermission graph & forward route planner:
                if (RuntimeIntermissionDeepReads.ClassifyPrimaryRewardType(
                        true, false, false, false, false, false, false, false, false, false, false) != "Boss"
                    || RuntimeIntermissionDeepReads.ClassifyPrimaryRewardType(
                        false, true, false, false, false, false, false, false, false, false, false) != "RestSite"
                    || RuntimeIntermissionDeepReads.ClassifyPrimaryRewardType(
                        false, false, true, false, false, false, false, false, false, false, false) != "Shop"
                    || RuntimeIntermissionDeepReads.ClassifyPrimaryRewardType(
                        false, false, false, true, false, false, false, false, false, false, false) != "CardReward"
                    || RuntimeIntermissionDeepReads.ClassifyPrimaryRewardType(
                        false, false, false, false, false, true, false, false, false, false, false) != "RelicReward")
                {
                    throw new InvalidOperationException("ClassifyPrimaryRewardType priority mismatch.");
                }

                var graphNodes = new List<IntermissionGraphNode>
                {
                    new IntermissionGraphNode(0, 0, "CardReward", "Pool_0_0", true, false, new[] { 0, 1 }),
                    new IntermissionGraphNode(0, 1, "RelicReward", "Pool_0_1", true, false, new[] { 1 }),
                    new IntermissionGraphNode(1, 0, "Shop", "Pool_1_0", false, false, new[] { 0 }),
                    new IntermissionGraphNode(1, 1, "RelicReward", "Pool_1_1", false, false, new[] { 0 }),
                    new IntermissionGraphNode(2, 0, "Boss", "Boss", false, false, Array.Empty<int>()),
                };
                IntermissionGraphSnapshot graphSnap = RuntimeIntermissionDeepReads.BuildIntermissionGraph(
                    -1, -1, 2, graphNodes, "RelicReward", maxRoutes: 2, maxNodes: 3);
                if (!graphSnap.IsAvailable || graphSnap.NodeCount != 3 || graphSnap.OmittedNodeCount != 2)
                {
                    throw new InvalidOperationException("BuildIntermissionGraph must clamp nodes and track omittedNodeCount.");
                }
                if (graphSnap.RouteCount != 2 || graphSnap.OmittedRouteCount != 1)
                {
                    throw new InvalidOperationException("BuildIntermissionGraph must enumerate 3 routes and clamp to maxRoutes=2.");
                }
                if (graphSnap.Routes[0].MatchedPreferredCount != 2
                    || graphSnap.Routes[0].Steps[0] != "0:1"
                    || graphSnap.Routes[0].Steps[1] != "1:1"
                    || graphSnap.Routes[0].Steps[2] != "2:0")
                {
                    throw new InvalidOperationException("Top route for RelicReward preference should be 0:1 -> 1:1 -> 2:0.");
                }

                string graphJson = AgentDiagnosticsFormatter.SerializeIntermissionGraph(graphSnap);
                using (JsonDocument doc = JsonDocument.Parse(graphJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("kind").GetString() != "intermissionGraph"
                        || !root.GetProperty("isAvailable").GetBoolean()
                        || root.GetProperty("omittedNodeCount").GetInt32() != 2
                        || root.GetProperty("omittedRouteCount").GetInt32() != 1
                        || root.GetProperty("nodes").GetArrayLength() != 3
                        || root.GetProperty("routes").GetArrayLength() != 2)
                    {
                        throw new InvalidOperationException("SerializeIntermissionGraph contract mismatch.");
                    }
                    int omittedNodeIdx = graphJson.IndexOf("\"omittedNodeCount\"", StringComparison.Ordinal);
                    int omittedRouteIdx = graphJson.IndexOf("\"omittedRouteCount\"", StringComparison.Ordinal);
                    int nodesIdx = graphJson.IndexOf("\"nodes\"", StringComparison.Ordinal);
                    int routesIdx = graphJson.IndexOf("\"routes\"", StringComparison.Ordinal);
                    if (omittedNodeIdx < 0 || omittedRouteIdx < 0 || nodesIdx < 0 || routesIdx < 0
                        || omittedNodeIdx > nodesIdx || omittedRouteIdx > routesIdx)
                    {
                        throw new InvalidOperationException("SerializeIntermissionGraph must place omission counters before detail arrays.");
                    }
                }

                // Intermission rewards & sub-screens:
                if (RuntimeIntermissionRewards.ResolveActiveRewardCategory(false, 1, 0, 0, 0, 2, 1, 0, 0) != "Engineer"
                    || RuntimeIntermissionRewards.ResolveActiveRewardCategory(false, 0, 0, 0, 0, 2, 1, 0, 0) != "Card"
                    || RuntimeIntermissionRewards.ResolveActiveRewardCategory(true, 0, 0, 0, 0, 2, 1, 0, 0) != "Relic"
                    || RuntimeIntermissionRewards.ResolveActiveRewardCategory(false, 0, 0, 0, 0, 0, 0, 1, 1) != "Upgrade"
                    || RuntimeIntermissionRewards.ResolveActiveRewardCategory(false, 0, 0, 0, 0, 0, 0, 0, 1) != "Research"
                    || RuntimeIntermissionRewards.ResolveActiveRewardCategory(false, 0, 0, 0, 0, 0, 0, 0, 0) != "None")
                {
                    throw new InvalidOperationException("ResolveActiveRewardCategory priority mismatch.");
                }

                var sampleOffered = new List<IntermissionOfferedCardRow>
                {
                    new IntermissionOfferedCardRow(0, "Card", "Factory_Tank", "Unit_Tank", "Heavy Tank", 101),
                    new IntermissionOfferedCardRow(1, "Card", "Factory_Bot", "Unit_Bot", "Scout Bot", 102),
                };
                var sampleDeck = new List<IntermissionDeckCardRow>
                {
                    new IntermissionDeckCardRow("Factory_Tank", "Unit_Tank", 1, new[] { "Upg_Armor" }, true),
                    new IntermissionDeckCardRow("Factory_Arty", "Unit_Arty", 0, Array.Empty<string>(), false),
                };
                IntermissionRewardState rewardState = IntermissionRewardState.Build(
                    true, "Intermission", "ChooseCard", true,
                    150, 200, 1, 2, 3, 5,
                    0, 0, 0, 0, 1, 0, 0, 0,
                    true, "Card", true, true, true,
                    false, false, string.Empty, false, 75,
                    sampleOffered, sampleDeck, maxCards: 1);
                string rewardJson = AgentDiagnosticsFormatter.SerializeIntermissionRewardState(rewardState);
                using (JsonDocument doc = JsonDocument.Parse(rewardJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("kind").GetString() != "intermissionRewardState"
                        || !root.GetProperty("isAvailable").GetBoolean()
                        || root.GetProperty("omittedOfferedCardCount").GetInt32() != 1
                        || root.GetProperty("omittedDeckCardCount").GetInt32() != 1
                        || root.GetProperty("offeredCards").GetArrayLength() != 1
                        || root.GetProperty("deckCards").GetArrayLength() != 1)
                    {
                        throw new InvalidOperationException("SerializeIntermissionRewardState contract mismatch.");
                    }
                    int omittedOfferedIdx = rewardJson.IndexOf("\"omittedOfferedCardCount\"", StringComparison.Ordinal);
                    int omittedDeckIdx = rewardJson.IndexOf("\"omittedDeckCardCount\"", StringComparison.Ordinal);
                    int offeredIdx = rewardJson.IndexOf("\"offeredCards\"", StringComparison.Ordinal);
                    int deckIdx = rewardJson.IndexOf("\"deckCards\"", StringComparison.Ordinal);
                    if (omittedOfferedIdx < 0 || omittedDeckIdx < 0 || offeredIdx < 0 || deckIdx < 0
                        || omittedOfferedIdx > offeredIdx || omittedDeckIdx > deckIdx)
                    {
                        throw new InvalidOperationException("SerializeIntermissionRewardState must place omission counters before detail arrays.");
                    }
                }

                // Offline fail-closed checks for live intermission reward/action directors:
                if (RuntimeIntermissionDeepReads.CurrentGraph().IsAvailable
                    || RuntimeIntermissionRewards.GetState().IsAvailable
                    || RuntimeIntermissionRewards.TryTriggerLoadLevel().Success
                    || RuntimeIntermissionRewards.TryChooseReward("Factory_Tank", 0).Success
                    || RuntimeIntermissionRewards.TrySkipReward().Success
                    || RuntimeIntermissionRewards.TryRerollReward().Success
                    || RuntimeIntermissionRewards.TryAssignUpgradeToCard("Unit_Tank", false).Success
                    || RuntimeIntermissionRewards.TrySetDeckOverviewOpen(true).Success)
                {
                    throw new InvalidOperationException("Intermission graph/reward readers and actions must fail closed when game singletons are absent.");
                }

                string actionJson = AgentDiagnosticsFormatter.SerializeIntermissionActionResult(
                    RuntimeIntermissionRewards.TryChooseReward("Factory_Tank", 0));
                using (JsonDocument doc = JsonDocument.Parse(actionJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("kind").GetString() != "intermissionActionResult"
                        || root.GetProperty("success").GetBoolean()
                        || !string.Equals(root.GetProperty("action").GetString(), "ChooseReward", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("SerializeIntermissionActionResult header mismatch.");
                    }
                    int sIdx = actionJson.IndexOf("\"success\"", StringComparison.Ordinal);
                    int mIdx = actionJson.IndexOf("\"message\"", StringComparison.Ordinal);
                    if (sIdx < 0 || mIdx < 0 || sIdx > mIdx)
                    {
                        throw new InvalidOperationException("SerializeIntermissionActionResult must place success before message.");
                    }
                }
                }),
                ("two-level visual variant registry and director lifecycle handles primary and tactical variants", () =>
                {
                    RuntimeUnitVisualVariantRegistry.Clear();
                    try
                    {
                        if (RuntimeUnitVisualVariantRegistry.GetRegisteredUnitCount() != 0 ||
                            RuntimeUnitVisualVariantRegistry.GetTotalVariantCount() != 0)
                        {
                            throw new InvalidOperationException("Registry must be empty after Clear().");
                        }

                        var primaryVariant = new RuntimeUnitVisualVariant(
                            "primary",
                            "sample:heavy-tank",
                            "sample:heavy-tank/desert-pbr",
                            "#E6B800",
                            1.0f,
                            "Assets/SampleUnits/HeavyTank_Desert.prefab",
                            "VisualRoot",
                            isPrimary: true);

                        var arcticVariant = new RuntimeUnitVisualVariant(
                            "arctic-ops",
                            "sample:heavy-tank",
                            "sample:heavy-tank/arctic-pbr",
                            "#D8E2E8",
                            1.0f,
                            "Assets/SampleUnits/HeavyTank_Arctic.prefab",
                            "VisualRoot",
                            isPrimary: false);

                        var stealthVariant = new RuntimeUnitVisualVariant(
                            "stealth-night",
                            "sample:heavy-tank",
                            "sample:heavy-tank/stealth-pbr",
                            "#1A1A1A",
                            1.0f,
                            "Assets/SampleUnits/HeavyTank_Stealth.prefab",
                            "VisualRoot",
                            isPrimary: false);

                        RuntimeUnitVisualVariantRegistry.Register("sample:heavy-tank", new[] { primaryVariant, arcticVariant, stealthVariant });

                        if (!RuntimeUnitVisualVariantRegistry.HasVariants("sample:heavy-tank"))
                        {
                            throw new InvalidOperationException("Registry must report variants exist for registered unit.");
                        }

                        if (!RuntimeUnitVisualVariantRegistry.TryGetPrimary("sample:heavy-tank", out var foundPrimary) ||
                            !foundPrimary.IsPrimary ||
                            foundPrimary.PresetName != "sample:heavy-tank/desert-pbr")
                        {
                            throw new InvalidOperationException("Registry must resolve Level 1 Primary chassis preset.");
                        }

                        if (!RuntimeUnitVisualVariantRegistry.TryGet("sample:heavy-tank", "arctic-ops", out var foundArctic) ||
                            foundArctic.IsPrimary ||
                            foundArctic.PresetName != "sample:heavy-tank/arctic-pbr")
                        {
                            throw new InvalidOperationException("Registry must resolve Level 2 Tactical variant.");
                        }

                        Color tint = foundArctic.ColorTint;
                        if (tint.r < 0.8f || tint.g < 0.8f || tint.b < 0.8f)
                        {
                            throw new InvalidOperationException("Hex color parser must accurately decode RGB values.");
                        }

                        string censusJson = AgentDiagnosticsFormatter.SerializeVisualVariantCensus();
                        using (JsonDocument doc = JsonDocument.Parse(censusJson))
                        {
                            JsonElement root = doc.RootElement;
                            if (root.GetProperty("kind").GetString() != "visualVariantCensus" ||
                                root.GetProperty("totalUnits").GetInt32() < 1 ||
                                root.GetProperty("totalVariants").GetInt32() < 3)
                            {
                                throw new InvalidOperationException("Visual variant census serializer failed contract check.");
                            }
                        }

                        var successResult = new UnitVisualVariantResult(true, "unit_123", "arctic-ops", "Sub-Zero Tundra", "Applied variant successfully");
                        string resultJson = AgentDiagnosticsFormatter.SerializeUnitVisualVariantResult(successResult);
                        using (JsonDocument doc = JsonDocument.Parse(resultJson))
                        {
                            JsonElement root = doc.RootElement;
                            if (!root.GetProperty("success").GetBoolean() ||
                                root.GetProperty("variantId").GetString() != "arctic-ops" ||
                                root.GetProperty("presetName").GetString() != "Sub-Zero Tundra")
                            {
                                throw new InvalidOperationException("UnitVisualVariantResult serializer failed contract check.");
                            }
                        }

                        UnitVisualVariantResult failResult = RuntimeGameplayCommandDirector.TryApplyUnitVisualVariant("nonexistent_entity", "arctic-ops");
                        if (failResult.Success)
                        {
                            throw new InvalidOperationException("TryApplyUnitVisualVariant must fail closed on missing entity.");
                        }

                        UnitVisualVariantResult invalidResult = RuntimeGameplayCommandDirector.TryApplyUnitVisualVariant("", "arctic-ops");
                        if (invalidResult.Success)
                        {
                            throw new InvalidOperationException("TryApplyUnitVisualVariant must fail closed on empty entity ID.");
                        }
                    }
                    finally
                    {
                        RuntimeUnitVisualVariantRegistry.Clear();
                    }
                }),
                ("RuntimeBridgeArgumentParser parses CSV strings, floats, bounds, and roundtrips with JoinCsv", () =>
                {
                    IReadOnlyList<string> strings;
                    string diag;
                    if (!RuntimeBridgeArgumentParser.TryParseStringCsv("alpha, beta; gamma", 1, 5, out strings, out diag) || strings.Count != 3)
                    {
                        throw new InvalidOperationException("TryParseStringCsv must parse comma and semicolon separated strings.");
                    }
                    if (strings[0] != "alpha" || strings[1] != "beta" || strings[2] != "gamma")
                    {
                        throw new InvalidOperationException("Parsed strings must be trimmed.");
                    }
                    if (RuntimeBridgeArgumentParser.TryParseStringCsv("", 1, 5, out strings, out diag))
                    {
                        throw new InvalidOperationException("TryParseStringCsv must reject empty input.");
                    }
                    if (RuntimeBridgeArgumentParser.TryParseStringCsv("a, , c", 1, 5, out strings, out diag))
                    {
                        throw new InvalidOperationException("TryParseStringCsv must reject empty entries.");
                    }
                    if (RuntimeBridgeArgumentParser.TryParseStringCsv("a, b", 3, 5, out strings, out diag))
                    {
                        throw new InvalidOperationException("TryParseStringCsv must reject count below minimum.");
                    }

                    IReadOnlyList<float> floats;
                    if (!RuntimeBridgeArgumentParser.TryParseFloatCsv("1.5, -2.5; 3.0", 1, 4, out floats, out diag) || floats.Count != 3)
                    {
                        throw new InvalidOperationException("TryParseFloatCsv must parse valid float strings.");
                    }
                    if (floats[0] != 1.5f || floats[1] != -2.5f || floats[2] != 3.0f)
                    {
                        throw new InvalidOperationException("Parsed floats must match input values.");
                    }
                    if (RuntimeBridgeArgumentParser.TryParseFloatCsv("1.0, not-a-number", 1, 4, out floats, out diag))
                    {
                        throw new InvalidOperationException("TryParseFloatCsv must reject non-numeric values.");
                    }

                    string joined = RuntimeBridgeArgumentParser.JoinCsv(new[] { "x", " y ", "z" });
                    if (joined != "x,y,z")
                    {
                        throw new InvalidOperationException("JoinCsv must trim and join with comma: " + joined);
                    }
                    if (RuntimeBridgeArgumentParser.JoinCsv(null) != string.Empty)
                    {
                        throw new InvalidOperationException("JoinCsv must return empty string on null input.");
                    }
                }),
                ("core Harmony hooks for unit creation, pool reuse, battlefield registration, and command loop execute and serialize cleanly", () =>
                {
                    ExistingControllers.Instance.Reset();
                    RuntimeUnitMechanicsDirector.ClearProfiles();
                    RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
                    RuntimeHooks.ResetCoreHookTelemetry();

                    try
                    {
                        RuntimeUnitMechanicsDirector.RegisterProfile("test:core-hook-unit", new UnitRuntimeMechanicsProfile
                        {
                            EntityId = "test:core-hook-unit",
                            BaselineStatusEffects = new[] { "Robust" },
                            DefaultTacticalStance = "Defensive",
                            StartingAffixes = new[] { "Swift" },
                            Experience = new ContentUnitExperienceDefinition
                            {
                                experiencePerLevel = 1,
                                maxLevel = 3,
                                experienceGainMultiplier = 1f
                            },
                            ShieldRegen = new ContentUnitShieldRegenDefinition
                            {
                                rechargeRate = 2f,
                                rechargeDelay = 1f
                            }
                        });

                        // 1. Placement Ghost hook & InitEntityController hook
                        var ghostGo = new GameObject("PlacementGhost");
                        RuntimeHarmonyPatches.CreateBuildingPlacementGhostPrefix();
                        RuntimeHarmonyPatches.CreateBuildingPlacementGhostPostfix("test:core-hook-factory", ghostGo);
                        if (RuntimeHooks.PlacementGhostHookCount != 1 ||
                            RuntimeGameplayCommandDirector.GetLastPlacementGhostEntityId() != "test:core-hook-factory")
                        {
                            throw new InvalidOperationException("Expected PlacementGhostHookCount == 1 and GetLastPlacementGhostEntityId == test:core-hook-factory.");
                        }

                        var unit = new EntityController
                        {
                            entityId = "test:core-hook-unit",
                            UniqueEntityId = 8801,
                            IsControlledByPlayer = true,
                            IsUnit = true,
                            CurrentRank = 0,
                            MaxRank = 3
                        };
                        var enemy = new EntityController
                        {
                            entityId = "test:core-hook-enemy",
                            UniqueEntityId = 8802,
                            IsControlledByAi = true,
                            IsUnit = true,
                            CurrentHealth = 100f,
                            MaxHealth = 100f
                        };
                        var factory = new EntityController
                        {
                            entityId = "test:core-hook-factory",
                            UniqueEntityId = 8803,
                            IsControlledByPlayer = true,
                            IsBuilding = true,
                            IsFactory = true
                        };

                        RuntimeHarmonyPatches.InitEntityControllerPrefix();
                        RuntimeHarmonyPatches.InitEntityControllerPostfix(unit);
                        if (RuntimeHooks.UnitCreationHookCount != 1)
                        {
                            throw new InvalidOperationException("Expected UnitCreationHookCount == 1 after InitEntityControllerPostfix.");
                        }

                        var mechDir = unit.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
                        if (mechDir == null || !unit.HasActiveStatusEffect(StatusEffect.Robust))
                        {
                            throw new InvalidOperationException("InitEntityControllerPostfix must attach RuntimeUnitMechanicsDirector and baseline status effects.");
                        }

                        // 2. Battlefield Registration hooks (ExistingControllers)
                        ExistingControllers.Instance.PlayerUnits().Add(unit);
                        ExistingControllers.Instance.PlayerBuildings().Add(factory);
                        ExistingControllers.Instance.AiUnits().Add(enemy);
                        RuntimeHarmonyPatches.ExistingControllersOnEntityCreatedPostfix(unit, false);
                        RuntimeHarmonyPatches.ExistingControllersOnEntityCreatedPostfix(factory, false);
                        if (RuntimeHooks.EntityRegisteredCount != 2 || RuntimeHooks.ActiveRegisteredEntityCount != 2 ||
                            !RuntimeHooks.IsEntityRegisteredInBattlefield(unit))
                        {
                            throw new InvalidOperationException("ExistingControllersOnEntityCreatedPostfix must track registered battlefield entities.");
                        }

                        mechDir.TickMechanics();
                        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance("8801") != "Defensive")
                        {
                            throw new InvalidOperationException("InitEntityControllerPostfix + TickMechanics must apply default Defensive stance.");
                        }

                        // Factory production hook
                        factory.ProductionInfo = new ProductionInfo { entityId = "unit:produced-tank" };
                        RuntimeHarmonyPatches.EntityControllerProducePrefix();
                        RuntimeHarmonyPatches.EntityControllerProducePostfix(factory, false, false, false);
                        if (RuntimeHooks.FactoryProduceHookCount != 1 ||
                            RuntimeGameplayCommandDirector.GetFactoryProducedCount("8803") != 1 ||
                            RuntimeGameplayCommandDirector.GetFactoryProducedCountByEntityId("test:core-hook-factory") != 1)
                        {
                            throw new InvalidOperationException("EntityControllerProducePostfix must record factory production telemetry and entityId count.");
                        }

                        // 3. Command Loop hooks (ExecuteCommand, ExecuteNextCommand, ResetCurrentCommand, DeleteCommandChain)
                        var moveCmd = EntityCommand.Move(new Vector3(24f, 0f, 36f));
                        moveCmd.tag = "director.tactical-move";
                        RuntimeHarmonyPatches.EntityControllerExecuteCommandPostfix(
                            unit,
                            new object[] { moveCmd, EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand });
                        if (RuntimeHooks.CommandExecutedCount != 1 ||
                            RuntimeHooks.LastExecutedCommandKind != "Move" ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandKind("8801") != "Move" ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandTag("8801") != "director.tactical-move" ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandProcessingType("8801") != "ExecuteAndCancelCurrentCommand")
                        {
                            throw new InvalidOperationException("EntityControllerExecuteCommandPostfix must record command kind, tag, and processing type.");
                        }

                        RuntimeHarmonyPatches.EntityControllerExecuteNextCommandPostfix(unit);
                        if (RuntimeHooks.CommandNextStepCount != 1 ||
                            RuntimeGameplayCommandDirector.GetUnitCommandStepCount("8801") != 1)
                        {
                            throw new InvalidOperationException("EntityControllerExecuteNextCommandPostfix must increment unit command step count.");
                        }

                        RuntimeHarmonyPatches.EntityControllerResetCurrentCommandPostfix(unit);
                        if (RuntimeHooks.CommandResetCount != 1 ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandKind("8801") != "DoNothing" ||
                            !string.IsNullOrEmpty(RuntimeGameplayCommandDirector.GetLastUnitCommandTag("8801")))
                        {
                            throw new InvalidOperationException("EntityControllerResetCurrentCommandPostfix must reset command kind to DoNothing and clear tag.");
                        }

                        var attackCmd = EntityCommand.Attack(enemy);
                        attackCmd.tag = "director.tactical-attack";
                        RuntimeHarmonyPatches.EntityControllerExecuteCommandPostfix(
                            unit,
                            new object[] { attackCmd, EntityController.CommandProcessingType.AppendToCommandChain });
                        if (RuntimeHooks.LastExecutedCommandKind != "Attack" ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandTag("8801") != "director.tactical-attack")
                        {
                            throw new InvalidOperationException("Expected LastExecutedCommandKind == Attack and command tag director.tactical-attack.");
                        }

                        RuntimeHarmonyPatches.EntityControllerDeleteCommandChainPostfix(unit);
                        if (RuntimeHooks.CommandChainDeletedCount != 1 ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandKind("8801") != "DoNothing" ||
                            !string.IsNullOrEmpty(RuntimeGameplayCommandDirector.GetLastUnitCommandTag("8801")) ||
                            RuntimeGameplayCommandDirector.GetLastUnitCommandProcessingType("8801") != "Cleared")
                        {
                            throw new InvalidOperationException("EntityControllerDeleteCommandChainPostfix must mark command chain cleared and clear tag.");
                        }

                        // 4. Pool Reuse hooks (ResetAndInitForReuse -> OnHasBeenInstantiatedForReuse)
                        RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 10f, unit);
                        if (RuntimeUnitMechanicsDirector.GetCombatXpForEntity(unit) <= 0)
                        {
                            throw new InvalidOperationException("Expected unit to accumulate combat XP before pool reuse.");
                        }

                        RuntimeHarmonyPatches.EntityControllerResetAndInitForReusePrefix(unit);
                        if (RuntimeHooks.UnitReuseHookCount != 1 ||
                            RuntimeUnitMechanicsDirector.GetCombatXpForEntity(unit) != 0 ||
                            unit.HasActiveStatusEffect(StatusEffect.Robust))
                        {
                            throw new InvalidOperationException("EntityControllerResetAndInitForReusePrefix must scrub stale XP and baseline effects before reuse.");
                        }

                        RuntimeHarmonyPatches.EntityControllerOnHasBeenInstantiatedForReusePostfix(unit);
                        if (RuntimeHooks.UnitCreationHookCount != 2 || !unit.HasActiveStatusEffect(StatusEffect.Robust))
                        {
                            throw new InvalidOperationException("EntityControllerOnHasBeenInstantiatedForReusePostfix must re-initialize pooled entity mechanics.");
                        }

                        // 5. Unregistration & Registry Reset hooks
                        RuntimeHarmonyPatches.ExistingControllersOnEntityWillBeDestroyedPostfix(unit, enemy);
                        if (RuntimeHooks.EntityUnregisteredCount != 1 ||
                            RuntimeHooks.ActiveRegisteredEntityCount != 1 ||
                            RuntimeHooks.IsEntityRegisteredInBattlefield(unit))
                        {
                            throw new InvalidOperationException("ExistingControllersOnEntityWillBeDestroyedPostfix must unregister destroyed entity.");
                        }

                        string hooksJson = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(
                            true, 53, "RogueCommand.ExpansionLoader.Runtime", RuntimeUnitMechanicsDirector.RegisteredCombatXpCount);
                        using (JsonDocument doc = JsonDocument.Parse(hooksJson))
                        {
                            JsonElement root = doc.RootElement;
                            if (!root.GetProperty("hooksInstalled").GetBoolean() ||
                                root.GetProperty("unitCreationHookCount").GetInt32() != 2 ||
                                root.GetProperty("unitReuseHookCount").GetInt32() != 1 ||
                                root.GetProperty("placementGhostHookCount").GetInt32() != 1 ||
                                root.GetProperty("factoryProduceHookCount").GetInt32() != 1 ||
                                root.GetProperty("entityRegisteredCount").GetInt32() != 2 ||
                                root.GetProperty("entityUnregisteredCount").GetInt32() != 1 ||
                                root.GetProperty("activeRegisteredEntityCount").GetInt32() != 1 ||
                                root.GetProperty("commandExecutedCount").GetInt32() != 2 ||
                                root.GetProperty("commandNextStepCount").GetInt32() != 1 ||
                                root.GetProperty("commandResetCount").GetInt32() != 1 ||
                                root.GetProperty("commandChainDeletedCount").GetInt32() != 1 ||
                                root.GetProperty("lastExecutedCommandKind").GetString() != "Attack")
                            {
                                throw new InvalidOperationException("SerializeRuntimeHooksState JSON mismatch: " + hooksJson);
                            }
                        }

                        RuntimeHarmonyPatches.ExistingControllersInitPostfix(ExistingControllers.Instance);
                        if (RuntimeHooks.EntityRegistryResetCount != 1 || RuntimeHooks.ActiveRegisteredEntityCount != 0 ||
                            RuntimeGameplayCommandDirector.GetFactoryProducedCount("8803") != 0 ||
                            RuntimeGameplayCommandDirector.GetFactoryProducedCountByEntityId("test:core-hook-factory") != 0)
                        {
                            throw new InvalidOperationException("ExistingControllersInitPostfix must clear active battlefield registration and per-match counters.");
                        }
                    }
                    finally
                    {
                        RuntimeHooks.ResetCoreHookTelemetry();
                        RuntimeUnitMechanicsDirector.ClearProfiles();
                        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
                        ExistingControllers.Instance.Reset();
                    }
                }),
        ("RuntimeHooks.CoreTranspilerExecutionAndTelemetryLifecycle", () =>
        {
            try
            {
                RuntimeHooks.ResetCoreHookTelemetry();
                RuntimeUnitMechanicsDirector.ClearProfiles();
                RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
                ExistingControllers.Instance.Reset();

                if (RuntimeHooks.UnitCreationTranspilerCount != 0 ||
                    RuntimeHooks.EntityRegisteredTranspilerCount != 0 ||
                    RuntimeHooks.EntityUnregisteredTranspilerCount != 0 ||
                    RuntimeHooks.UnitReuseTranspilerCount != 0 ||
                    RuntimeHooks.InitEntityControllerTranspilerCount != 0 ||
                    RuntimeHooks.EntityOnInstantiatedTranspilerCount != 0 ||
                    RuntimeHooks.EntityReuseOnInstantiatedTranspilerCount != 0 ||
                    RuntimeHooks.FactoryProduceTranspilerCount != 0 ||
                    RuntimeHooks.EntityRegistryResetTranspilerCount != 0 ||
                    RuntimeHooks.PlacementGhostTranspilerCount != 0 ||
                    RuntimeHooks.EntityInitTranspilerCount != 0 ||
                    RuntimeHooks.EntityDestroyTranspilerCount != 0 ||
                    RuntimeHooks.CommandExecutedTranspilerCount != 0 ||
                    RuntimeHooks.CommandNextStepTranspilerCount != 0 ||
                    RuntimeHooks.CommandResetTranspilerCount != 0 ||
                    RuntimeHooks.CommandChainDeletedTranspilerCount != 0)
                {
                    throw new InvalidOperationException("Initial transpiler telemetry counters must be 0 after reset.");
                }

                var unit = new EntityController
                {
                    entityId = "test:transpiler-unit",
                    UniqueEntityId = 9901,
                    IsControlledByPlayer = true,
                    CurrentHealth = 150f,
                    MaxHealth = 150f
                };

                var factoryUnit = new EntityController
                {
                    entityId = "test:transpiler-factory",
                    UniqueEntityId = 9902,
                    IsControlledByPlayer = true,
                    ProductionInfo = new ProductionInfo { entityId = "unit:produced-tank" }
                };

                // 1. Unit Creation Transpiler Hook (EntityFactory.InstantiateEntity)
                object? factoryVisualResetEntity = null;
                RuntimeVisualDirector.OnEntityReusedAction = entity => factoryVisualResetEntity = entity;
                try
                {
                    RuntimeHarmonyPatches.OnEntityInstantiatedTranspilerHook(unit, false);
                }
                finally
                {
                    RuntimeVisualDirector.OnEntityReusedAction = null;
                }

                if (RuntimeHooks.UnitCreationTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityInstantiatedTranspilerHook must increment UnitCreationTranspilerCount.");
                }
                if (!ReferenceEquals(factoryVisualResetEntity, unit))
                {
                    throw new InvalidOperationException("OnEntityInstantiatedTranspilerHook must reset visual lifecycle state for factory stack-pool reuse.");
                }

                // 2. Init Entity Controller Transpiler Hook (EntityFactory.InitEntityController)
                RuntimeHarmonyPatches.OnInitEntityControllerTranspilerHook(unit);
                if (RuntimeHooks.InitEntityControllerTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnInitEntityControllerTranspilerHook must increment InitEntityControllerTranspilerCount.");
                }

                // 3. Entity OnInstantiated Transpiler Hook (EntityController.OnHasBeenInstantiated)
                RuntimeHarmonyPatches.OnEntityHasBeenInstantiatedTranspilerHook(unit, false);
                if (RuntimeHooks.EntityOnInstantiatedTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityHasBeenInstantiatedTranspilerHook must increment EntityOnInstantiatedTranspilerCount.");
                }

                // 4. Entity Registered Transpiler Hook (ExistingControllers.OnEntityHasBeenCreated)
                ExistingControllers.Instance.PlayerUnits().Add(unit);
                RuntimeHarmonyPatches.OnEntityRegisteredTranspilerHook(ExistingControllers.Instance, unit, false);
                if (RuntimeHooks.EntityRegisteredTranspilerCount != 1 ||
                    !RuntimeHooks.IsEntityRegisteredInBattlefield(unit))
                {
                    throw new InvalidOperationException("OnEntityRegisteredTranspilerHook must increment EntityRegisteredTranspilerCount and track active entity.");
                }

                // 5. Entity Reset For Reuse Transpiler Hook (EntityController.ResetAndInitForReuse)
                RuntimeHarmonyPatches.OnEntityResetForReuseTranspilerHook(unit);
                if (RuntimeHooks.UnitReuseTranspilerCount != 1 ||
                    RuntimeHooks.IsEntityRegisteredInBattlefield(unit))
                {
                    throw new InvalidOperationException("OnEntityResetForReuseTranspilerHook must increment UnitReuseTranspilerCount and remove entity from tracking.");
                }

                // 6. Entity OnInstantiated For Reuse Transpiler Hook (EntityController.OnHasBeenInstantiatedForReuse)
                object? reusedVisualEntity = null;
                RuntimeVisualDirector.OnEntityReusedAction = entity => reusedVisualEntity = entity;
                RuntimeHarmonyPatches.OnEntityHasBeenInstantiatedForReuseTranspilerHook(unit, false);
                RuntimeVisualDirector.OnEntityReusedAction = null;
                if (RuntimeHooks.EntityReuseOnInstantiatedTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityHasBeenInstantiatedForReuseTranspilerHook must increment EntityReuseOnInstantiatedTranspilerCount.");
                }
                if (!ReferenceEquals(reusedVisualEntity, unit))
                {
                    throw new InvalidOperationException("OnEntityHasBeenInstantiatedForReuseTranspilerHook must notify the visual director with the reused entity.");
                }

                // 7. Factory Produce Transpiler Hook (EntityController.Produce)
                RuntimeHarmonyPatches.OnEntityProduceTranspilerHook(factoryUnit, false, true, false);
                if (RuntimeHooks.FactoryProduceTranspilerCount != 1 ||
                    RuntimeGameplayCommandDirector.GetFactoryProducedCount("9902") != 1)
                {
                    throw new InvalidOperationException("OnEntityProduceTranspilerHook must increment FactoryProduceTranspilerCount and record production.");
                }

                // 8. Entity Will Be Destroyed Transpiler Hook (ExistingControllers.OnEntityWillBeDestroyed)
                RuntimeHarmonyPatches.OnEntityWillBeDestroyedTranspilerHook(ExistingControllers.Instance, unit, null);
                if (RuntimeHooks.EntityUnregisteredTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityWillBeDestroyedTranspilerHook must increment EntityUnregisteredTranspilerCount.");
                }

                // 9. Registry Reset Transpiler Hook (ExistingControllers.Init)
                RuntimeHarmonyPatches.OnEntityRegistryResetTranspilerHook(ExistingControllers.Instance);
                if (RuntimeHooks.EntityRegistryResetTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityRegistryResetTranspilerHook must increment EntityRegistryResetTranspilerCount.");
                }

                // 10. Placement Ghost Transpiler Hook (EntityFactory.CreateBuildingPlacementGhost)
                var ghostObj = new GameObject();
                RuntimeHarmonyPatches.OnCreateBuildingPlacementGhostTranspilerHook(ghostObj, "building:barracks", false);
                if (RuntimeHooks.PlacementGhostTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnCreateBuildingPlacementGhostTranspilerHook must increment PlacementGhostTranspilerCount.");
                }

                // 11. Entity Init Transpiler Hook (EntityController.Init)
                RuntimeHarmonyPatches.OnEntityInitTranspilerHook(unit, null);
                if (RuntimeHooks.EntityInitTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityInitTranspilerHook must increment EntityInitTranspilerCount.");
                }

                // 12. Entity Destroy Transpiler Hook (EntityController.Destroy)
                RuntimeVisualDirector destroyVisualDirector = unit.gameObject.AddComponent<RuntimeVisualDirector>();
                RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook(unit, false, null);
                if (RuntimeHooks.EntityDestroyTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityDestroyTranspilerHook must increment EntityDestroyTranspilerCount.");
                }
                if (!destroyVisualDirector.DeathAnimationTriggeredForDestruction)
                {
                    throw new InvalidOperationException("Entity destruction must trigger the unit death animation before retarget suspension.");
                }

                // The terminal native destroy call may be held only after the entry hook issued
                // the destruction death trigger; no-trigger destruction remains immediate.
                UnityEngine.Object.ResetDestroyTelemetry();
                var heldDestroyEntity = new EntityController
                {
                    IsDestroyed = true,
                    DestroyHasBeenStarted = true
                };
                RuntimeVisualDirector heldDestroyDirector =
                    heldDestroyEntity.gameObject.AddComponent<RuntimeVisualDirector>();
                heldDestroyDirector.TriggerDeathAnimationForDestruction();
                heldDestroyDirector.CanBeginDeathVisualRetirement = true;
                RuntimeHarmonyPatches.OnEntityDestroyGameObjectTranspilerHook(
                    heldDestroyEntity.gameObject);
                if (!heldDestroyDirector.DeathVisualRetirementPending ||
                    UnityEngine.Object.DestroyCallCount != 0 ||
                    RuntimeHooks.EntityDestroyGameObjectTranspilerCount != 1 ||
                    RuntimeHooks.DeathVisualRetirementCount != 1)
                {
                    throw new InvalidOperationException("The terminal destroy hook must defer destruction only after a death visual retirement is armed.");
                }

                var immediateDestroyEntity = new EntityController
                {
                    IsDestroyed = true,
                    DestroyHasBeenStarted = true
                };
                RuntimeVisualDirector immediateDestroyDirector =
                    immediateDestroyEntity.gameObject.AddComponent<RuntimeVisualDirector>();
                immediateDestroyDirector.CanBeginDeathVisualRetirement = true;
                RuntimeHarmonyPatches.OnEntityDestroyGameObjectTranspilerHook(
                    immediateDestroyEntity.gameObject);
                if (immediateDestroyDirector.DeathVisualRetirementPending ||
                    UnityEngine.Object.DestroyCallCount != 1 ||
                    UnityEngine.Object.LastDestroyedObject != immediateDestroyEntity.gameObject ||
                    RuntimeHooks.EntityDestroyGameObjectTranspilerCount != 2 ||
                    RuntimeHooks.DeathVisualRetirementCount != 1)
                {
                    throw new InvalidOperationException("The no-actions destroy path must preserve native immediate GameObject destruction.");
                }

                // 13. Entity Execute Command Transpiler Hook (EntityController.ExecuteCommand)
                RuntimeHarmonyPatches.OnEntityExecuteCommandTranspilerHook(unit);
                if (RuntimeHooks.CommandExecutedTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityExecuteCommandTranspilerHook must increment CommandExecutedTranspilerCount.");
                }

                // 14. Entity Execute Next Command Transpiler Hook (EntityController.ExecuteNextCommand)
                RuntimeHarmonyPatches.OnEntityExecuteNextCommandTranspilerHook(unit);
                if (RuntimeHooks.CommandNextStepTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityExecuteNextCommandTranspilerHook must increment CommandNextStepTranspilerCount.");
                }

                // 15. Entity Reset Current Command Transpiler Hook (EntityController.ResetCurrentCommand)
                RuntimeHarmonyPatches.OnEntityResetCurrentCommandTranspilerHook(unit);
                if (RuntimeHooks.CommandResetTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityResetCurrentCommandTranspilerHook must increment CommandResetTranspilerCount.");
                }

                // 16. Entity Delete Command Chain Transpiler Hook (EntityController.DeleteCommandChain)
                RuntimeHarmonyPatches.OnEntityDeleteCommandChainTranspilerHook(unit);
                if (RuntimeHooks.CommandChainDeletedTranspilerCount != 1)
                {
                    throw new InvalidOperationException("OnEntityDeleteCommandChainTranspilerHook must increment CommandChainDeletedTranspilerCount.");
                }

                // 17. Diagnostics JSON serialization verification
                string json = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(
                    true, 65, "RogueCommand.ExpansionLoader.Runtime", 0);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetProperty("unitCreationTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityRegisteredTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityUnregisteredTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("unitReuseTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("initEntityControllerTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityOnInstantiatedTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityReuseOnInstantiatedTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("factoryProduceTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityRegistryResetTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("placementGhostTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityInitTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityDestroyTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("entityDestroyGameObjectTranspilerCount").GetInt32() != 2 ||
                        root.GetProperty("deathVisualRetirementCount").GetInt32() != 1 ||
                        root.GetProperty("commandExecutedTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("commandNextStepTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("commandResetTranspilerCount").GetInt32() != 1 ||
                        root.GetProperty("commandChainDeletedTranspilerCount").GetInt32() != 1)
                    {
                        throw new InvalidOperationException("SerializeRuntimeHooksState must serialize all 18 transpiler counters correctly: " + json);
                    }
                }

                // 18. Reset telemetry verification
                RuntimeHooks.ResetCoreHookTelemetry();
                if (RuntimeHooks.UnitCreationTranspilerCount != 0 ||
                    RuntimeHooks.EntityRegisteredTranspilerCount != 0 ||
                    RuntimeHooks.EntityUnregisteredTranspilerCount != 0 ||
                    RuntimeHooks.UnitReuseTranspilerCount != 0 ||
                    RuntimeHooks.InitEntityControllerTranspilerCount != 0 ||
                    RuntimeHooks.EntityOnInstantiatedTranspilerCount != 0 ||
                    RuntimeHooks.EntityReuseOnInstantiatedTranspilerCount != 0 ||
                    RuntimeHooks.FactoryProduceTranspilerCount != 0 ||
                    RuntimeHooks.EntityRegistryResetTranspilerCount != 0 ||
                    RuntimeHooks.PlacementGhostTranspilerCount != 0 ||
                    RuntimeHooks.EntityInitTranspilerCount != 0 ||
                    RuntimeHooks.EntityDestroyTranspilerCount != 0 ||
                    RuntimeHooks.EntityDestroyGameObjectTranspilerCount != 0 ||
                    RuntimeHooks.DeathVisualRetirementCount != 0 ||
                    RuntimeHooks.CommandExecutedTranspilerCount != 0 ||
                    RuntimeHooks.CommandNextStepTranspilerCount != 0 ||
                    RuntimeHooks.CommandResetTranspilerCount != 0 ||
                    RuntimeHooks.CommandChainDeletedTranspilerCount != 0)
                {
                    throw new InvalidOperationException("ResetCoreHookTelemetry must reset all transpiler counters to 0.");
                }
            }
            finally
            {
                RuntimeHooks.ResetCoreHookTelemetry();
                RuntimeUnitMechanicsDirector.ClearProfiles();
                RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
                ExistingControllers.Instance.Reset();
            }
        }),
        ("RuntimeHooks.AiCommandArbiterAndLifecycleSynchronization", () =>
        {
            try
            {
                // 1. AiCommandArbiterState lease lifecycle and tag comparison
                var state = new AiCommandArbiterState();
                state.SetLease(901, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = "sample:skirmish-kiting",
                    CommandTag = "kite.tactical-run",
                    TargetEntityId = 902,
                    Category = AiIntentCategory.SurvivalKite,
                    LastApprovedScore = 100f,
                    LockoutExpiresAt = 50f
                });

                if (state.ActiveLeaseCount != 1)
                {
                    throw new InvalidOperationException("Expected ActiveLeaseCount == 1.");
                }

                // Same tag: lease must NOT be released
                if (state.ReleaseLeaseIfTagDiffers(901, "kite.tactical-run"))
                {
                    throw new InvalidOperationException("ReleaseLeaseIfTagDiffers must return false when commandTag matches.");
                }
                if (state.ActiveLeaseCount != 1)
                {
                    throw new InvalidOperationException("Active lease should be retained when tag matches.");
                }

                // Different tag: lease MUST be released
                if (!state.ReleaseLeaseIfTagDiffers(901, "player.manual-move"))
                {
                    throw new InvalidOperationException("ReleaseLeaseIfTagDiffers must return true when commandTag differs.");
                }
                if (state.ActiveLeaseCount != 0)
                {
                    throw new InvalidOperationException("Active lease should be removed when tag differs.");
                }

                // Re-set lease and test ReleaseLease
                state.SetLease(901, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = "sample:skirmish-kiting",
                    CommandTag = "kite.tactical-run",
                    Category = AiIntentCategory.SurvivalKite,
                    LockoutExpiresAt = 50f
                });
                if (!state.ReleaseLease(901) || state.ActiveLeaseCount != 0)
                {
                    throw new InvalidOperationException("ReleaseLease must remove active lease.");
                }

                // 2. CompositeAiRuntimeBehaviour notify methods with active composite
                var events = new List<string>();
                var recordingBehaviour = new RecordingAiRuntimeBehaviour(events, "test:recording");
                var composite = new CompositeAiRuntimeBehaviour(
                    "test:composite-profile",
                    new[] { "test:recording" },
                    new IRogueCommandAiRuntimeBehaviour[] { recordingBehaviour });
                composite.OnInitialized();

                // Seed lease in composite arbiter state
                composite.ArbiterState.SetLease(903, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = "test:recording",
                    CommandTag = "dummy.hold",
                    Category = AiIntentCategory.CombatTargeting,
                    LockoutExpiresAt = 100f
                });

                if (composite.ArbiterState.ActiveLeaseCount != 1)
                {
                    throw new InvalidOperationException("Expected composite arbiter state to have 1 lease.");
                }

                // NotifyUnitCommandExecuted with same tag -> lease kept
                CompositeAiRuntimeBehaviour.NotifyUnitCommandExecuted(903, "dummy.hold");
                if (composite.ArbiterState.ActiveLeaseCount != 1)
                {
                    throw new InvalidOperationException("NotifyUnitCommandExecuted with same tag must retain lease.");
                }

                // NotifyUnitCommandExecuted with different tag -> lease released
                CompositeAiRuntimeBehaviour.NotifyUnitCommandExecuted(903, "external.override");
                if (composite.ArbiterState.ActiveLeaseCount != 0)
                {
                    throw new InvalidOperationException("NotifyUnitCommandExecuted with different tag must invalidate lease.");
                }

                // Seed again, test NotifyUnitLeaseInvalidated
                composite.ArbiterState.SetLease(903, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = "test:recording",
                    CommandTag = "dummy.hold",
                    Category = AiIntentCategory.CombatTargeting,
                    LockoutExpiresAt = 100f
                });
                CompositeAiRuntimeBehaviour.NotifyUnitLeaseInvalidated(903);
                if (composite.ArbiterState.ActiveLeaseCount != 0)
                {
                    throw new InvalidOperationException("NotifyUnitLeaseInvalidated must remove unit lease.");
                }

                // Test NotifyBattlefieldRegistryReset
                composite.ArbiterState.SetLease(904, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = "test:recording",
                    CommandTag = "dummy.hold",
                    Category = AiIntentCategory.CombatTargeting,
                    LockoutExpiresAt = 100f
                });
                CompositeAiRuntimeBehaviour.NotifyBattlefieldRegistryReset();
                if (composite.ArbiterState.ActiveLeaseCount != 0)
                {
                    throw new InvalidOperationException("NotifyBattlefieldRegistryReset must clear all leases in active composites.");
                }

                composite.OnDestroyed();

                // 3. Dynamic Command Aura cleanup on entity death/destruction
                var captain = new EntityController
                {
                    entityId = "unit:aura-captain",
                    UniqueEntityId = 905,
                    IsControlledByPlayer = true,
                    IsUnit = true,
                    Position = new Vector3(10f, 0f, 10f)
                };
                var follower = new EntityController
                {
                    entityId = "unit:aura-follower",
                    UniqueEntityId = 906,
                    IsControlledByPlayer = true,
                    IsUnit = true,
                    Position = new Vector3(12f, 0f, 10f)
                };
                ExistingControllers.Instance.PlayerUnits().Add(captain);
                ExistingControllers.Instance.PlayerUnits().Add(follower);

                CommandAuraResult pulseRes = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                    "905", "Leadership", 15.0f, 30.0f);
                if (!pulseRes.Success || follower.CaptainOfThisEntity != captain)
                {
                    throw new InvalidOperationException("TryDispatchCommandAuraPulse must establish follower link.");
                }

                // Destroying or killing the captain must release follower link and aura state
                RuntimeUnitMechanicsDirector.OnEntityDestroyed(captain);
                if (follower.CaptainOfThisEntity != null)
                {
                    throw new InvalidOperationException("OnEntityDestroyed on captain must unlink follower.");
                }

                CommandAuraCensus auraCensus = RuntimeCommandAuraCensus.Current();
                if (auraCensus.TotalActiveAurasCount != 0)
                {
                    throw new InvalidOperationException("OnEntityDestroyed must clear captain's active dynamic command aura.");
                }
            }
            finally
            {
                RuntimeHooks.ResetCoreHookTelemetry();
                RuntimeUnitMechanicsDirector.ClearProfiles();
                RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
                ExistingControllers.Instance.Reset();
            }
        }),
    };

    private sealed class TestCardChangeTarget
    {
#pragma warning disable CS0649, CS8618
        public List<string>? ListField;
        public string[]? ArrayField;
        public float FloatField;
#pragma warning restore CS0649, CS8618
    }

    private sealed class TestEntityAttackStub
    {
        public EntityController _entityController;
        public TestEntityAttackStub(EntityController ec) { _entityController = ec; }
    }

    private sealed class TestUiClickHandler : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IPointerUpHandler,
        UnityEngine.EventSystems.IPointerClickHandler
    {
        public readonly List<string> Events = new List<string>();

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerEnter");
        }

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerDown");
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerUp");
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerClick");
        }
    }

    private sealed class TestUiClickClickOnlyHandler : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerClickHandler
    {
        public readonly List<string> Events = new List<string>();

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerEnter");
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Events.Add("OnPointerClick");
        }
    }

    private sealed class EventSystemsHarnessScope : IDisposable
    {
        private readonly UnityEngine.EventSystems.EventSystem _eventSystem;
        private readonly Camera _previousCamera;

        private EventSystemsHarnessScope(UnityEngine.EventSystems.EventSystem eventSystem, Camera previousCamera)
        {
            _eventSystem = eventSystem;
            _previousCamera = previousCamera;
        }

        public static EventSystemsHarnessScope CreateUiClickScene(out GameObject button, out TestUiClickHandler handler)
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.AddComponent<Canvas>();
            var panelGo = new GameObject("Panel");
            panelGo.transform.parent = canvasGo.transform;
            button = new GameObject("StartButton");
            button.transform.parent = panelGo.transform;
            button.transform.position = new Vector3(100f, 100f, 0f);

            handler = button.AddComponent<TestUiClickHandler>();
            RectTransform rectTransform = button.AddComponent<RectTransform>();
            rectTransform.rect = new Rect(-100f, -50f, 200f, 100f);

            var system = new UnityEngine.EventSystems.EventSystem { gameObject = canvasGo, currentSelectedGameObject = null };
            UnityEngine.EventSystems.EventSystem.current = system;
            UnityEngine.EventSystems.EventSystem.TestRaycastResults = new List<UnityEngine.EventSystems.RaycastResult>
            {
                new UnityEngine.EventSystems.RaycastResult { gameObject = button, screenPoint = new Vector2(100f, 100f) },
            };
            RectTransformUtility.WorldToScreenPointHook = world => new Vector2(world.x, world.y);
            Screen.width = 1920;
            Screen.height = 1080;

            return new EventSystemsHarnessScope(system, Camera.main);
        }

        public static EventSystemsHarnessScope CreateWorldClickScene(out GameObject node)
        {
            node = new GameObject("LevelNode");
            node.AddComponent<MeshRenderer>();
            Camera camera = Camera.main ?? new Camera();
            Camera.main = camera;
            Camera.WorldToScreenPointHook = position => new Vector3(position.x, position.y, 1f);
            Screen.width = 1920;
            Screen.height = 1080;
            Physics.TestHasHit = false;
            var system = new UnityEngine.EventSystems.EventSystem { gameObject = new GameObject("EventSystem") };
            UnityEngine.EventSystems.EventSystem.current = system;
            UnityEngine.EventSystems.EventSystem.TestRaycastResults = new List<UnityEngine.EventSystems.RaycastResult>();

            return new EventSystemsHarnessScope(system, camera);
        }

        public void Dispose()
        {
            UnityEngine.EventSystems.EventSystem.current = null;
            UnityEngine.EventSystems.EventSystem.TestRaycastResults = new List<UnityEngine.EventSystems.RaycastResult>();
            RectTransformUtility.WorldToScreenPointHook = null;
            Camera.WorldToScreenPointHook = null;
            Physics.TestHasHit = false;
            Physics.TestHit = default(RaycastHit);
        }
    }
}
