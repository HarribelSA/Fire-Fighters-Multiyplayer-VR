using UnityEngine;
using UnityEngine.UI;

namespace FireCenter
{
    /// <summary>Pulses a Graphic's alpha (used for the incoming-emergency alert banner).</summary>
    [RequireComponent(typeof(Graphic))]
    public class UIPulse : MonoBehaviour
    {
        [SerializeField] float m_Speed = 4f;
        [SerializeField] float m_MinAlpha = 0.55f;
        Graphic m_Graphic;
        float m_BaseAlpha;

        void Awake()
        {
            m_Graphic = GetComponent<Graphic>();
            m_BaseAlpha = m_Graphic.color.a;
        }

        void Update()
        {
            var c = m_Graphic.color;
            c.a = Mathf.Lerp(m_MinAlpha, m_BaseAlpha, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * m_Speed));
            m_Graphic.color = c;
        }
    }
}
