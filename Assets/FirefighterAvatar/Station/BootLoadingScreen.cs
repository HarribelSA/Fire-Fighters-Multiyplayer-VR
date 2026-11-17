using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FirefighterVR.Station
{
    // First scene of the build: a tiny scene (black camera + the same loading card as the mission
    // transitions) that loads the merged lobby scene (S1Test) asynchronously and shows real progress,
    // so players see a loading screen instead of a frozen/black headset while the lobby loads.
    public sealed class BootLoadingScreen : MonoBehaviour
    {
        public string targetScene = "Assets/S1Test.unity";
        public Camera viewCamera;
        public Sprite driverMark, ring, firefighter, fire, barFill, white;
        public Material uiMaterial, textMaterial;
        public TMP_FontAsset font;
        public float distance = 1.6f;

        RectTransform markPivot, runner, fireRect, fillRect;
        Image markImage, ringImage, fillImage, fireImage;
        TMP_Text percentText;
        CanvasGroup group;
        AsyncOperation load;
        float shown;

        void Start()
        {
            Application.backgroundLoadingPriority = ThreadPriority.High;
            Build();
            load = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            load.allowSceneActivation = false;
        }

        void Update()
        {
            if (load == null) return;
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            float target = Mathf.Clamp01(load.progress / .9f);
            shown = Mathf.MoveTowards(shown, target, dt * (target >= 1 ? 1.5f : .6f));
            group.alpha = Mathf.MoveTowards(group.alpha, 1, dt / .4f);
            markPivot.localRotation = Quaternion.Euler(0, 0, -t * 90 % 360);
            var c = Color.Lerp(new Color(.95f, .2f, .18f), new Color(.2f, .5f, 1f), .5f + .5f * Mathf.Sin(t * 2.2f));
            markImage.color = c; ringImage.color = c;
            fillImage.fillAmount = shown;
            float width = fillRect.rect.width; runner.anchoredPosition = new Vector2(-width * .5f + width * shown, runner.anchoredPosition.y);
            float flame = Mathf.Clamp01(1 - shown);
            fireRect.localScale = Vector3.one * (flame * (.85f + .15f * Mathf.Sin(t * 18)));
            fireImage.enabled = flame > .01f;
            percentText.text = Mathf.FloorToInt(shown * 100) + "%";
            if (shown >= .999f && !load.allowSceneActivation) load.allowSceneActivation = true;
        }

        void Build()
        {
            if (!viewCamera) viewCamera = Camera.main;
            var go = new GameObject("Boot Loading Card", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 32000;
            group = go.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1600, 900); rt.localScale = Vector3.one * .001f * distance / 1.6f;
            if (viewCamera) { rt.SetParent(viewCamera.transform, false); rt.localPosition = Vector3.forward * distance; rt.localRotation = Quaternion.identity; }
            Img("Background", rt, Vector2.zero, new Vector2(1700, 1000), white, Color.black);
            markPivot = new GameObject("MarkPivot", typeof(RectTransform)).GetComponent<RectTransform>(); markPivot.SetParent(rt, false); markPivot.anchoredPosition = new Vector2(0, 80);
            ringImage = Img("Ring", markPivot, Vector2.zero, new Vector2(300, 300), ring, Color.white);
            markImage = Img("DriverMark", markPivot, Vector2.zero, new Vector2(190, 190), driverMark, Color.white);
            Text("Status", rt, new Vector2(0, -150), new Vector2(1200, 90), 40, "Loading fire center…");
            var track = Img("BarTrack", rt, new Vector2(0, -300), new Vector2(1200, 34), white, new Color(.12f, .12f, .12f));
            fillImage = Img("BarFill", track.rectTransform, Vector2.zero, new Vector2(1200, 34), barFill, Color.white); fillImage.type = Image.Type.Filled; fillImage.preserveAspect = false; fillImage.fillMethod = Image.FillMethod.Horizontal; fillImage.fillAmount = 0; fillRect = fillImage.rectTransform;
            runner = Img("Firefighter", track.rectTransform, new Vector2(-600, 62), new Vector2(84, 84), firefighter, Color.white).rectTransform;
            fireImage = Img("Fire", track.rectTransform, new Vector2(645, 58), new Vector2(86, 96), fire, Color.white); fireRect = fireImage.rectTransform; fireRect.pivot = new Vector2(.5f, 0); fireRect.anchoredPosition = new Vector2(645, 18);
            percentText = Text("Percent", rt, new Vector2(0, -370), new Vector2(400, 60), 34, "0%");
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
    }
}
