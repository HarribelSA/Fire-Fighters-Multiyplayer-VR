using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FireCenter
{
    /// <summary>
    /// Subtle floating label + light strip above a station, coloured with the occupant's colour (RED / BLUE).
    /// Dimmed while the seat is empty. Purely visual; the colour data comes from <see cref="StationSeats"/>.
    /// </summary>
    public class StationIndicator : MonoBehaviour
    {
        [SerializeField] PlayerRole m_SeatRole = PlayerRole.Firefighter;
        [SerializeField] Graphic[] m_Colored;
        [SerializeField] TMP_Text m_Label;
        [SerializeField] Renderer m_Strip;
        [SerializeField, Range(0f, 1f)] float m_EmptyAlpha = 0.35f;

        MaterialPropertyBlock m_Block;
        static readonly int k_Base = Shader.PropertyToID("_BaseColor");
        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");
        float m_Next;

        void Update()
        {
            if (Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + 0.25f;
            var occ = StationSeats.Get(m_SeatRole);
            var c = StationColors.Of(occ.color);
            float a = occ.present ? 1f : m_EmptyAlpha;
            foreach (var g in m_Colored) if (g != null) g.color = new Color(c.r, c.g, c.b, g.color.a > 0.01f ? a * 0.9f : g.color.a);
            if (m_Label)
            {
                // The player's NAME replaces the colour word (RED / BLUE); the station colour stays on the strip
                // and the label background. Own station without a name asks for one.
                string who = occ.present && occ.state != null ? occ.state.playerName : "";
                if (occ.isLocal) who = FirefighterVR.Station.SeatedDispatchLobby.HasPlayerName ? FirefighterVR.Station.SeatedDispatchLobby.SavedPlayerName : "TYPE YOUR NAME";
                if (string.IsNullOrWhiteSpace(who)) who = "OPEN";
                // Long text (the prompt or a long name) is drawn a little smaller so the pencil stays on the plate.
                m_Label.text = who.Length > 10 ? $"<b><size=78%>{who}</size></b>" : $"<b>{who}</b>";
                m_Label.color = new Color(1f, 1f, 1f, occ.present ? 0.95f : 0.5f);
                UpdateNameEditor(occ.isLocal);
            }
            if (m_Strip)
            {
                m_Block ??= new MaterialPropertyBlock();
                m_Strip.GetPropertyBlock(m_Block);
                m_Block.SetColor(k_Base, c * 0.6f);
                m_Block.SetColor(k_Emission, c * (occ.present ? 1.6f : 0.4f));
                m_Strip.SetPropertyBlock(m_Block);
            }
        }

        // ---------------- Change name (pencil) ----------------
        // On the local player's own station the label becomes a button with a pencil icon:
        // pressing it opens the spatial keyboard on the player's name field.
        Button m_EditButton;
        RectTransform m_Pencil;
        void UpdateNameEditor(bool local)
        {
            if (!m_EditButton) BuildNameEditor();
            if (!m_EditButton) return;
            if (m_EditButton.gameObject.activeSelf != local) m_EditButton.gameObject.SetActive(local);
            if (!local) return;
            m_Label.ForceMeshUpdate();
            var b = m_Label.textBounds;
            float h = Mathf.Max(10f, b.size.y);
            m_Pencil.sizeDelta = new Vector2(h * 1.15f, h * 1.15f);
            m_Pencil.localPosition = new Vector3(b.max.x + h * .85f, b.center.y, 0);
            var area = (RectTransform)m_EditButton.transform;
            area.localPosition = new Vector3(b.center.x + h * .4f, b.center.y, 0); area.sizeDelta = new Vector2(b.size.x + h * 1.8f, h * 1.6f);
        }
        void BuildNameEditor()
        {
            var canvas = m_Label.canvas; if (!canvas) return;
            if (!canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>()) canvas.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (!canvas.GetComponent<GraphicRaycaster>()) canvas.gameObject.AddComponent<GraphicRaycaster>();
            // Invisible hit area over "RED  name" + pencil (child of the label, so it uses the label's own space).
            var area = new GameObject("ChangeNameButton", typeof(RectTransform), typeof(Image), typeof(Button));
            area.layer = m_Label.gameObject.layer;
            var rt = (RectTransform)area.transform; rt.SetParent(m_Label.rectTransform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            var hit = area.GetComponent<Image>(); hit.color = new Color(1, 1, 1, .0f); hit.raycastTarget = true;
            m_EditButton = area.GetComponent<Button>(); m_EditButton.targetGraphic = hit;
            var colors = m_EditButton.colors; colors.normalColor = new Color(1, 1, 1, 0); colors.highlightedColor = new Color(1, 1, 1, .12f); colors.pressedColor = new Color(1, 1, 1, .25f); m_EditButton.colors = colors;
            m_EditButton.onClick.AddListener(() => { var lobby = FirefighterVR.Station.SeatedDispatchLobby.Instance; if (lobby) lobby.EditPlayerName(); });
            // Pencil icon built from UI shapes (body, ferrule, tip), tilted 45 degrees, beside the name.
            var pencil = new GameObject("Pencil", typeof(RectTransform)); pencil.layer = area.layer;
            m_Pencil = (RectTransform)pencil.transform; m_Pencil.SetParent(m_Label.rectTransform, false); m_Pencil.localRotation = Quaternion.Euler(0, 0, -45);
            // Plain white pencil icon (body, small gap, eraser cap, pointed tip).
            var white = Color.white;
            Part(m_Pencil, "Body", new Vector2(0, .06f), new Vector2(.24f, .62f), white);
            Part(m_Pencil, "Eraser", new Vector2(0, .46f), new Vector2(.24f, .12f), white);
            var tip = Part(m_Pencil, "Tip", new Vector2(0, -.3f), new Vector2(.17f, .17f), white); tip.localRotation = Quaternion.Euler(0, 0, 45);
        }
        static RectTransform Part(RectTransform parent, string name, Vector2 center, Vector2 size, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image)); g.layer = parent.gameObject.layer;
            var r = (RectTransform)g.transform; r.SetParent(parent, false); r.pivot = new Vector2(.5f, .5f);
            // Parts are laid out in a unit box (0..1 anchors) that scales with the pencil size.
            var c = new Vector2(.5f + center.x, .5f + center.y);
            r.anchorMin = c - size * .5f; r.anchorMax = c + size * .5f; r.sizeDelta = Vector2.zero; r.anchoredPosition = Vector2.zero;
            var img = g.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
            return r;
        }
    }
}
