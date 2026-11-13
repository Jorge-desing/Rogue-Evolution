using System.Text.Json;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class Program
{
    private static int Main()
    {
        try
        {
            SceneManager.ActiveSceneName = "IntermissionScreen";
            UiFixture fixture = CreateFixture();
            VerifyNativePointerControlCensusAndEventSystemClick(fixture);
            VerifyNonInteractableControlFailsClosed(fixture);
            VerifyAmbiguousLabelFailsClosed(fixture);
            VerifyInactiveAndNonCanvasControlsAreNotOfferedForClick(fixture);
            VerifyOccludedControlFailsClosed(fixture);
            Console.WriteLine("PASS native pointer UI census, exact-path EventSystem click, ambiguity, interactability, inactivity, canvas scope, and occlusion");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + exception.Message);
            return 1;
        }
        finally
        {
            EventSystem.current = null;
            EventSystem.TestRaycastResults = new List<RaycastResult>();
            RectTransformUtility.WorldToScreenPointHook = null;
        }
    }

    private static UiFixture CreateFixture()
    {
        var canvasObject = new GameObject("Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var panel = new GameObject("Panel");
        panel.transform.parent = canvasObject.transform;

        var button = new GameObject("ContinueButton");
        button.transform.parent = panel.transform;
        button.transform.position = new Vector3(100f, 100f, 0f);
        button.AddComponent<RectTransform>().rect = new Rect(-80f, -24f, 160f, 48f);
        TestUiPointerControl handler = button.AddComponent<TestUiPointerControl>();
        button.AddComponent<Text>().text = "Continue";

        EventSystem.current = new EventSystem { gameObject = canvasObject, currentSelectedGameObject = null };
        EventSystem.TestRaycastResults = new List<RaycastResult>
        {
            new RaycastResult { gameObject = button, screenPoint = new Vector2(100f, 100f) },
        };
        RectTransformUtility.WorldToScreenPointHook = point => new Vector2(point.x, point.y);
        Screen.width = 1920;
        Screen.height = 1080;
        return new UiFixture(canvasObject, panel, button, handler);
    }

    private static void VerifyNativePointerControlCensusAndEventSystemClick(UiFixture fixture)
    {
        string censusJson = RuntimeUiAccessibility.GetCensusJson("Continue", false, 8);
        using JsonDocument census = JsonDocument.Parse(censusJson);
        JsonElement root = census.RootElement;
        Require(root.GetProperty("success").GetBoolean(), "native UI census did not succeed");
        Require(root.GetProperty("totalCandidateCount").GetInt32() == 1, "native pointer control was not uniquely discovered");
        JsonElement control = root.GetProperty("candidates")[0];
        Require(control.GetProperty("label").GetString() == "Continue", "native pointer label was not exposed");
        Require(control.GetProperty("controlType").GetString() == nameof(TestUiPointerControl), "native pointer type was not exposed");
        string path = control.GetProperty("hierarchyPath").GetString();
        Require(path == "IntermissionScreen::Canvas/Panel/ContinueButton", "scene-qualified native control path mismatch");
        Require(control.GetProperty("interactable").GetBoolean() && control.GetProperty("onScreen").GetBoolean(), "native pointer state was not exposed");

        string clickJson = RuntimeUiAccessibility.TryClickTextJson("Continue", true, path);
        using JsonDocument click = JsonDocument.Parse(clickJson);
        Require(click.RootElement.GetProperty("success").GetBoolean(), "exact-path native pointer click failed");
        Require(click.RootElement.GetProperty("click").GetProperty("via").GetString() == RuntimeUiClickDirector.ViaEventSystem,
            "native pointer click did not use the EventSystem path");
        Require(string.Join(",", fixture.Handler.Events) == "pointerEnter,pointerDown,pointerUp,pointerClick",
            "native pointer handler did not receive the full click sequence");
    }

    private static void VerifyNonInteractableControlFailsClosed(UiFixture fixture)
    {
        fixture.Handler.interactable = false;
        string clickJson = RuntimeUiAccessibility.TryClickTextJson("Continue", true, string.Empty);
        using JsonDocument click = JsonDocument.Parse(clickJson);
        Require(!click.RootElement.GetProperty("success").GetBoolean(), "non-interactable native control was clicked");
        Require(click.RootElement.GetProperty("message").GetString() == "matched control is not interactable",
            "non-interactable native control did not fail closed with a clear reason");
        fixture.Handler.interactable = true;
    }

    private static void VerifyAmbiguousLabelFailsClosed(UiFixture fixture)
    {
        AddNativeControl(fixture.Panel, "OptionA", "Option");
        AddNativeControl(fixture.Panel, "OptionB", "Option");
        int eventsBefore = fixture.Handler.Events.Count;
        string censusJson = RuntimeUiAccessibility.GetCensusJson("Option", false, 8);
        using JsonDocument census = JsonDocument.Parse(censusJson);
        Require(census.RootElement.GetProperty("totalCandidateCount").GetInt32() == 2,
            "duplicate native labels were not both exposed for disambiguation");

        string clickJson = RuntimeUiAccessibility.TryClickTextJson("Option", true, string.Empty);
        using JsonDocument click = JsonDocument.Parse(clickJson);
        Require(!click.RootElement.GetProperty("success").GetBoolean()
            && click.RootElement.GetProperty("matchCount").GetInt32() == 2,
            "ambiguous native label did not fail closed");
        Require(fixture.Handler.Events.Count == eventsBefore, "ambiguous native label delivered an event");
    }

    private static void AddNativeControl(GameObject parent, string name, string label)
    {
        var control = new GameObject(name);
        control.transform.parent = parent.transform;
        control.AddComponent<RectTransform>().rect = new Rect(-60f, -20f, 120f, 40f);
        control.AddComponent<TestUiPointerControl>();
        control.AddComponent<Text>().text = label;
    }

    private static void VerifyInactiveAndNonCanvasControlsAreNotOfferedForClick(UiFixture fixture)
    {
        var inactive = new GameObject("InactiveNativeControl") { activeInHierarchy = false };
        inactive.transform.parent = fixture.Panel.transform;
        inactive.AddComponent<RectTransform>();
        inactive.AddComponent<TestUiPointerControl>();
        inactive.AddComponent<Text>().text = "Inactive";

        string activeOnly = RuntimeUiAccessibility.GetCensusJson("Inactive", false, 8);
        string withInactive = RuntimeUiAccessibility.GetCensusJson("Inactive", true, 8);
        using JsonDocument activeOnlyDoc = JsonDocument.Parse(activeOnly);
        using JsonDocument withInactiveDoc = JsonDocument.Parse(withInactive);
        Require(activeOnlyDoc.RootElement.GetProperty("totalCandidateCount").GetInt32() == 0,
            "inactive native UI control appeared in the default census");
        JsonElement inactiveRow = withInactiveDoc.RootElement.GetProperty("candidates")[0];
        Require(!inactiveRow.GetProperty("active").GetBoolean() && !inactiveRow.GetProperty("interactable").GetBoolean(),
            "explicit inactive census did not mark the control inactive and non-interactable");

        var worldControl = new GameObject("WorldPointerControl");
        worldControl.AddComponent<RectTransform>();
        worldControl.AddComponent<TestUiPointerControl>();
        string worldCensus = RuntimeUiAccessibility.GetCensusJson("WorldPointerControl", false, 8);
        using JsonDocument worldDoc = JsonDocument.Parse(worldCensus);
        Require(worldDoc.RootElement.GetProperty("totalCandidateCount").GetInt32() == 0,
            "non-canvas pointer handler was incorrectly exposed as native UI");
    }

    private static void VerifyOccludedControlFailsClosed(UiFixture fixture)
    {
        var occluder = new GameObject("Occluder");
        occluder.transform.parent = fixture.Panel.transform;
        occluder.AddComponent<RectTransform>();
        int eventsBefore = fixture.Handler.Events.Count;
        EventSystem.TestRaycastResults = new List<RaycastResult>
        {
            new RaycastResult { gameObject = occluder, screenPoint = new Vector2(100f, 100f) },
            new RaycastResult { gameObject = fixture.Button, screenPoint = new Vector2(100f, 100f) },
        };

        string clickJson = RuntimeUiAccessibility.TryClickTextJson("Continue", true, string.Empty);
        using JsonDocument click = JsonDocument.Parse(clickJson);
        Require(!click.RootElement.GetProperty("success").GetBoolean(), "occluded native control was clicked");
        Require(fixture.Handler.Events.Count == eventsBefore, "occluded control received pointer events");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class UiFixture
    {
        public UiFixture(GameObject canvas, GameObject panel, GameObject button, TestUiPointerControl handler)
        {
            Canvas = canvas;
            Panel = panel;
            Button = button;
            Handler = handler;
        }

        public GameObject Canvas { get; }
        public GameObject Panel { get; }
        public GameObject Button { get; }
        public TestUiPointerControl Handler { get; }
    }

    private sealed class TestUiPointerControl : MonoBehaviour,
        IPointerEnterHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler
    {
        public readonly List<string> Events = new();
        public bool interactable { get; set; } = true;

        public void OnPointerEnter(PointerEventData eventData) => Events.Add("pointerEnter");
        public void OnPointerDown(PointerEventData eventData) => Events.Add("pointerDown");
        public void OnPointerUp(PointerEventData eventData) => Events.Add("pointerUp");
        public void OnPointerClick(PointerEventData eventData) => Events.Add("pointerClick");
    }
}
