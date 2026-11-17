using System;
using UnityEngine;
using UnityEngine.UI;

namespace FireCenter
{
    /// <summary>
    /// A row of buttons where exactly one is selected (Easy | Medium | Hard, ON | OFF ...).
    /// Large targets work better with VR controller rays than drop-down lists.
    /// </summary>
    public class SegmentedControl : MonoBehaviour
    {
        [SerializeField] Button[] m_Options;
        [SerializeField] Color m_Selected = new(0.86f, 0.24f, 0.16f, 1f);
        [SerializeField] Color m_Unselected = new(0.18f, 0.21f, 0.26f, 1f);

        int m_Index = -1;
        bool m_Interactable = true;

        public event Action<int> valueChanged;
        public int value => m_Index;
        public int count => m_Options?.Length ?? 0;

        void Awake()
        {
            for (int i = 0; i < m_Options.Length; i++)
            {
                int captured = i;
                if (m_Options[i] != null) m_Options[i].onClick.AddListener(() => Select(captured, true));
            }
        }

        public void SetValueWithoutNotify(int index) => Select(index, false);

        public bool interactable
        {
            get => m_Interactable;
            set
            {
                m_Interactable = value;
                foreach (var b in m_Options) if (b != null) b.interactable = value;
            }
        }

        void Select(int index, bool notify)
        {
            index = Mathf.Clamp(index, 0, m_Options.Length - 1);
            bool changed = index != m_Index;
            m_Index = index;
            for (int i = 0; i < m_Options.Length; i++)
                if (m_Options[i] != null && m_Options[i].targetGraphic != null)
                    m_Options[i].targetGraphic.color = i == m_Index ? m_Selected : m_Unselected;
            if (notify && changed) valueChanged?.Invoke(m_Index);
        }
    }
}
