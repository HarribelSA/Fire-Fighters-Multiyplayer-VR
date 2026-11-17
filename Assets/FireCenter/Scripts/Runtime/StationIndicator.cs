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
                string who = occ.present && occ.state != null ? occ.state.playerName : (occ.isLocal ? "YOU" : "OPEN");
                m_Label.text = $"<b>{StationColors.Label(occ.color)}</b>  <size=70%>{who}</size>";
                m_Label.color = new Color(1f, 1f, 1f, occ.present ? 0.95f : 0.5f);
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
    }
}
