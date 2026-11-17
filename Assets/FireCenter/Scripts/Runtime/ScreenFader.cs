using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FireCenter
{
    /// <summary>
    /// VR-safe black fade. Builds a small world-space canvas parented to the headset camera
    /// (screen-space overlays do not render in XR). Because the XR Origin persists, the fader persists too.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        public static ScreenFader Instance { get; private set; }

        [SerializeField] float m_DefaultDuration = 1.2f;
        [SerializeField, Tooltip("Fade in automatically after each scene load if the screen is black.")]
        bool m_FadeInAfterSceneLoad = true;

        CanvasGroup m_Group;
        Coroutine m_Routine;

        public bool isFullyBlack => m_Group != null && m_Group.alpha >= 0.999f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_FadeInAfterSceneLoad && isFullyBlack && mode == LoadSceneMode.Single)
                FadeIn(m_DefaultDuration, 0.4f);
        }

        bool EnsureOverlay()
        {
            if (m_Group != null) return true;
            var cam = Camera.main;
            if (cam == null) return false;

            var go = new GameObject("FireCenter Fade Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.layer = cam.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(cam.transform, false);
            float distance = Mathf.Max(cam.nearClipPlane + 0.02f, 0.05f);
            rt.localPosition = new Vector3(0f, 0f, distance);
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(100f, 100f);
            rt.localScale = Vector3.one * 0.01f; // 1m x 1m, far larger than the view frustum at this distance

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = short.MaxValue;
            canvas.worldCamera = cam;

            var img = new GameObject("Black", typeof(RectTransform), typeof(Image));
            img.layer = go.layer;
            var irt = (RectTransform)img.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            var image = img.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            m_Group = go.GetComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            m_Group.blocksRaycasts = false;
            m_Group.interactable = false;
            go.SetActive(false);
            return true;
        }

        public Coroutine FadeOut(float duration = -1f) => StartFade(1f, duration, 0f);
        public Coroutine FadeIn(float duration = -1f, float delay = 0f) => StartFade(0f, duration, delay);

        Coroutine StartFade(float target, float duration, float delay)
        {
            if (!EnsureOverlay()) return null;
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = StartCoroutine(FadeRoutine(target, duration < 0f ? m_DefaultDuration : duration, delay));
            return m_Routine;
        }

        IEnumerator FadeRoutine(float target, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            m_Group.gameObject.SetActive(true);
            float start = m_Group.alpha;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                m_Group.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            m_Group.alpha = target;
            if (target <= 0f) m_Group.gameObject.SetActive(false);
            m_Routine = null;
        }
    }
}
