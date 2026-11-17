using System.Collections.Generic;
using UnityEngine;

namespace FireCenter
{
    /// <summary>
    /// Keeps a root object (XR rig, network game manager, settings systems, EventSystem...) alive across
    /// the synchronized mission scene load, so the multiplayer connection, the local XR Origin and
    /// the player's settings survive the transition. Objects are keyed by <see cref="m_PersistenceKey"/>;
    /// if a scene containing the same key is loaded again, the newer copy removes itself so no
    /// duplicate rigs or managers ever exist.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class PersistAcrossScenes : MonoBehaviour
    {
        [SerializeField, Tooltip("Unique key. A second object with the same key is destroyed.")]
        string m_PersistenceKey;

        static readonly Dictionary<string, GameObject> s_Persisted = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Persisted.Clear();

        public string persistenceKey { get => m_PersistenceKey; set => m_PersistenceKey = value; }

        void Awake()
        {
            string key = string.IsNullOrEmpty(m_PersistenceKey) ? gameObject.name : m_PersistenceKey;

            if (s_Persisted.TryGetValue(key, out var existing) && existing != null && existing != gameObject)
            {
                // A persistent copy already exists - deactivate first so this copy never initializes.
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            if (transform.parent != null)
                transform.SetParent(null, true);

            s_Persisted[key] = gameObject;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            string key = string.IsNullOrEmpty(m_PersistenceKey) ? gameObject.name : m_PersistenceKey;
            if (s_Persisted.TryGetValue(key, out var existing) && existing == gameObject)
                s_Persisted.Remove(key);
        }
    }
}
