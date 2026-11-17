using System;
using TMPro;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FirefighterVR.Station
{
    // Head-locked loading card drawn ON TOP of the existing black StationViewFade (per player camera).
    // It appears only once the view is fully black during a real scene transition, follows actual
    // progress milestones (scene AsyncOperation + session phases), reaches 100% only when the new
    // scene, network phase and local placement are ready, fades itself out behind black, and only
    // then releases the fade so the eyes "open" on the finished scene.
    [DefaultExecutionOrder(195)]
    public sealed class MissionLoadingScreen : MonoBehaviour
    {
        public StationViewFade fade;
        public XROrigin origin;
        public Sprite driverMark, ring, firefighter, fire, barFill, white;
        public Material uiMaterial, textMaterial;
        public TMP_FontAsset font;
        public float distance = 1.6f;

        Canvas canvas; CanvasGroup group;
        RectTransform markPivot, runner, fireRect, fillRect;
        Image markImage, ringImage, fillImage, fireImage;
        TMP_Text percentText, statusText;
        float shown, target, alpha;
        enum Mode { Idle, Loading, Outro, Failed }
        Mode mode;
        float modeTime;
        AsyncOperation networkLoad;
        NetworkSceneManager hooked;

        void Start() { Build(); SetVisible(0); }

        void Update()
        {
            if (!fade || !canvas) return;
            HookNetworkLoad();
            float dt = Time.unscaledDeltaTime; modeTime += dt;
            bool transitioning = Transitioning(out float milestone, out string label, out string failure);
            switch (mode)
            {
                case Mode.Idle:
                    if (transitioning && fade.BlackRendered) { mode = Mode.Loading; modeTime = 0; shown = target = 0; networkLoad = null; TransitionDiagnostics.ClearCurrentLoad(); fade.ExternalHold = true; }
                    break;
                case Mode.Loading:
                    if (failure != null) { mode = Mode.Failed; modeTime = 0; statusText.text = "<color=#FF6B5B>LOADING FAILED</color>\n<size=60%>" + failure + "\nReturning to the lobby safely…</size>"; break; }
                    target = Mathf.Max(target, transitioning ? Mathf.Min(milestone, .97f) : 1f);
                    if (transitioning) statusText.text = label;
                    else { statusText.text = "READY"; if (shown >= .999f) { mode = Mode.Outro; modeTime = 0; } }
                    break;
                case Mode.Outro:
                    if (modeTime > .7f) { mode = Mode.Idle; fade.ExternalHold = false; }
                    break;
                case Mode.Failed:
                    if (modeTime > 3f) { mode = Mode.Idle; fade.ExternalHold = false; }
                    break;
            }
            shown = Mathf.MoveTowards(shown, target, dt * (target >= 1 ? 1.2f : .45f));
            float wantAlpha = mode == Mode.Loading || mode == Mode.Failed ? 1 : 0;
            alpha = Mathf.MoveTowards(alpha, wantAlpha, dt / (wantAlpha > alpha ? .35f : .6f));
            SetVisible(alpha);
            if (alpha > 0) Animate(dt);
        }

        void HookNetworkLoad()
        {
            var nm = NetworkManager.Singleton; var sm = nm && nm.IsListening ? nm.SceneManager : null;
            if (sm == hooked) return;
            if (hooked != null) hooked.OnSceneEvent -= OnSceneEvent;
            hooked = sm; if (sm != null) sm.OnSceneEvent += OnSceneEvent;
        }
        void OnSceneEvent(SceneEvent e)
        { if (e.SceneEventType == SceneEventType.Load && e.ClientId == NetworkManager.Singleton.LocalClientId && e.AsyncOperation != null) networkLoad = e.AsyncOperation; }
        float LoadProgress()
        {
            var op = networkLoad ?? TransitionDiagnostics.CurrentLoad;
            return op == null ? 0 : op.isDone ? 1 : Mathf.Clamp01(op.progress / .9f);
        }

        // Milestones of the EXISTING transitions (multiplayer station/city, Solo station/city).
        bool Transitioning(out float milestone, out string label, out string failure)
        {
            milestone = 0; label = "Preparing mission…"; failure = null;
            var flow = StationSessionFlow.Instance;
            if (flow && flow.IsSpawned && !flow.SoloMissionActive)
            {
                var s = flow.Lobby.Value;
                if (s.Phase == StationPhase.Blocked && mode == Mode.Loading) { failure = flow.Failure; return true; }
                if (s.Phase == StationPhase.Fading) { milestone = .08f; label = "Synchronizing crew…"; return true; }
                if (s.Phase == StationPhase.Loading) { milestone = .12f + .7f * LoadProgress(); label = "Loading fire station…"; return true; }
                if (s.Phase == StationPhase.AwaitingSpawn) { milestone = flow.localRig && flow.localRig.StationSeated ? .95f : .88f; label = "Preparing firefighters…"; return true; }
                var m = flow.CrewMission.Value;
                if (flow.DrivingTransition.Value || m.stage == CrewMissionStage.CityTransition)
                {
                    bool cityLoaded = SceneManager.GetSceneByName(StationSessionFlow.CitySceneName).isLoaded;
                    milestone = m.cityBlack != 3 ? .08f : !cityLoaded ? .12f + .7f * LoadProgress() : m.citySeats == 3 ? .96f : .88f;
                    label = !cityLoaded ? "Loading city route…" : "Boarding fire truck…"; return true;
                }
                return false;
            }
            if (flow && flow.SoloMissionActive && flow.MissionState.stage == CrewMissionStage.CityTransition)
            { bool loaded = flow.SoloCityLoaded; milestone = loaded ? (flow.MissionState.citySeats == 3 ? .96f : .88f) : .12f + .7f * LoadProgress(); label = loaded ? "Boarding fire truck…" : "Loading city route…"; return true; }
            var solo = SoloTest();
            if (solo && solo.Transitioning) { milestone = .12f + .7f * LoadProgress(); label = "Loading fire station…"; return true; }
            return false;
        }
        StationBot.StationSoloBotTest soloCache; float nextSoloSearch;
        StationBot.StationSoloBotTest SoloTest() { if (!soloCache && Time.unscaledTime >= nextSoloSearch) { nextSoloSearch = Time.unscaledTime + 1; soloCache = FindFirstObjectByType<StationBot.StationSoloBotTest>(); } return soloCache; }

        void Animate(float dt)
        {
            float t = Time.unscaledTime;
            markPivot.localRotation = Quaternion.Euler(0, 0, -t * 90 % 360);
            var c = Color.Lerp(new Color(.95f, .2f, .18f), new Color(.2f, .5f, 1f), .5f + .5f * Mathf.Sin(t * 2.2f));
            markImage.color = c; ringImage.color = c;
            fillImage.fillAmount = shown;
            float width = fillRect.rect.width; runner.anchoredPosition = new Vector2(-width * .5f + width * shown, runner.anchoredPosition.y);
            float flame = Mathf.Clamp01(1 - shown);
            fireRect.localScale = Vector3.one * (flame * (.85f + .15f * Mathf.Sin(t * 18)));
            fireImage.enabled = flame > .01f;
            percentText.text = Mathf.FloorToInt(shown * 100) + "%";
        }
        void SetVisible(float a)
        {
            if (!canvas) return;
            group.alpha = a; bool on = a > .001f; if (canvas.enabled != on) canvas.enabled = on;
            if (on && origin && origin.Camera) { canvas.transform.SetParent(origin.Camera.transform, false); canvas.transform.localPosition = Vector3.forward * distance; canvas.transform.localRotation = Quaternion.identity; }
        }

        void Build()
        {
            if (!origin) origin = fade ? fade.origin : FindFirstObjectByType<XROrigin>();
            var go = new GameObject("Mission Loading Screen", typeof(RectTransform)); go.layer = gameObject.layer;
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 32000;
            group = go.AddComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1600, 900); rt.localScale = Vector3.one * .001f * distance / 1.6f;
            Img("Background", rt, Vector2.zero, new Vector2(1700, 1000), white, Color.black);
            markPivot = new GameObject("MarkPivot", typeof(RectTransform)).GetComponent<RectTransform>(); markPivot.SetParent(rt, false); markPivot.anchoredPosition = new Vector2(0, 80);
            ringImage = Img("Ring", markPivot, Vector2.zero, new Vector2(300, 300), ring, Color.white);
            markImage = Img("DriverMark", markPivot, Vector2.zero, new Vector2(190, 190), driverMark, Color.white);
            statusText = Text("Status", rt, new Vector2(0, -150), new Vector2(1200, 90), 40, "Preparing mission…");
            var track = Img("BarTrack", rt, new Vector2(0, -300), new Vector2(1200, 34), white, new Color(.12f, .12f, .12f));
            fillImage = Img("BarFill", track.rectTransform, Vector2.zero, new Vector2(1200, 34), barFill, Color.white); fillImage.type = Image.Type.Filled; fillImage.preserveAspect = false; fillImage.fillMethod = Image.FillMethod.Horizontal; fillRect = fillImage.rectTransform;
            var runnerImg = Img("Firefighter", track.rectTransform, new Vector2(-600, 62), new Vector2(84, 84), firefighter, Color.white); runner = runnerImg.rectTransform;
            fireImage = Img("Fire", track.rectTransform, new Vector2(645, 58), new Vector2(86, 96), fire, Color.white); fireRect = fireImage.rectTransform; fireRect.pivot = new Vector2(.5f, 0); fireRect.anchoredPosition = new Vector2(645, 18);
            percentText = Text("Percent", rt, new Vector2(0, -370), new Vector2(400, 60), 34, "0%");
            canvas.enabled = false;
        }
        Image Img(string name, RectTransform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)g.transform; r.SetParent(parent, false); r.anchoredPosition = pos; r.sizeDelta = size;
            var i = g.AddComponent<Image>(); i.sprite = sprite; i.color = color; i.material = uiMaterial; i.raycastTarget = false; i.preserveAspect = sprite && sprite != white; return i;
        }
        TMP_Text Text(string name, RectTransform parent, Vector2 pos, Vector2 size, float fontSize, string text)
        {
            var g = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)g.transform; r.SetParent(parent, false); r.anchoredPosition = pos; r.sizeDelta = size;
            var t = g.AddComponent<TextMeshProUGUI>(); if (font) t.font = font; if (textMaterial) t.fontSharedMaterial = textMaterial;
            t.fontSize = fontSize; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.text = text; t.raycastTarget = false; return t;
        }
        void OnDisable() { if (fade && mode != Mode.Idle) fade.ExternalHold = false; mode = Mode.Idle; }
        void OnDestroy() { if (hooked != null) hooked.OnSceneEvent -= OnSceneEvent; }
    }
}
