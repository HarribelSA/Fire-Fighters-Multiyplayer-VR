using UnityEngine;

namespace FireCenter
{
    // Raises LobbyPlayerState.anyStateChanged whenever the existing session/lobby/bot state changes.
    public sealed class LobbyStateTicker : MonoBehaviour
    {
        int m_Last; float m_Next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var g = new GameObject("FireCenter LobbyStateTicker"); DontDestroyOnLoad(g); g.AddComponent<LobbyStateTicker>(); }
        void Update()
        {
            if (Time.unscaledTime < m_Next) return; m_Next = Time.unscaledTime + .15f;
            int s = LobbyPlayerState.Signature(); if (s == m_Last) return; m_Last = s; LobbyPlayerState.RaiseChanged();
        }
    }
}
