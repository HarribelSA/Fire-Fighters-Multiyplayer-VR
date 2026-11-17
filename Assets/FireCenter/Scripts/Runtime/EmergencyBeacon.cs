using UnityEngine;

namespace FireCenter
{
    /// <summary>Rotating red emergency beacon that switches on when the dispatch alert starts.</summary>
    public class EmergencyBeacon : MonoBehaviour
    {
        [SerializeField] Light m_Light;
        [SerializeField] Transform m_Rotator;
        [SerializeField] Renderer m_LensRenderer;
        [SerializeField] float m_RotationSpeed = 360f;
        [SerializeField] float m_Intensity = 6f;
        [SerializeField] Color m_EmissionColor = new(4f, 0.2f, 0.1f);

        bool m_Active;
        MaterialPropertyBlock m_Block;
        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            SetActive(false);
            if (MissionLauncher.Instance != null) MissionLauncher.Instance.alertStarted += OnAlert;
        }

        void OnDestroy()
        {
            if (MissionLauncher.Instance != null) MissionLauncher.Instance.alertStarted -= OnAlert;
        }

        void OnAlert() => SetActive(true);

        public void SetActive(bool active)
        {
            m_Active = active;
            if (m_Light) { m_Light.enabled = active; m_Light.intensity = m_Intensity; }
            if (m_LensRenderer)
            {
                m_Block ??= new MaterialPropertyBlock();
                m_LensRenderer.GetPropertyBlock(m_Block);
                m_Block.SetColor(k_Emission, active ? m_EmissionColor : Color.black);
                m_LensRenderer.SetPropertyBlock(m_Block);
            }
        }

        void Update()
        {
            if (m_Active && m_Rotator) m_Rotator.Rotate(Vector3.up, m_RotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
