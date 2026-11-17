using System;
using Unity.Netcode;
using UnityEngine;

namespace CityNightPresentation.Fireground
{
    public enum FiregroundPhase : byte { Inactive, ArrivalHold, Dismount, Operation, Recovery, Complete }

    // One compact authoritative snapshot. Rope points and particles are never replicated:
    // every peer simulates the hose locally from payout + nozzle pose.
    public struct FiregroundState : INetworkSerializable, IEquatable<FiregroundState>
    {
        public const ulong NoHolder = ulong.MaxValue;
        public const int MaxZones = 4;
        public uint revision;
        public FiregroundPhase phase;
        public double deadline, completedAt;
        public bool pumpOn, trigger, nozzleDocked;
        public ulong nozzleHolder;
        public float payout;
        public byte dismountAcks;
        public Vector3 nozzlePosition;
        public Quaternion nozzleRotation;
        public byte zoneCount, f0, f1, f2, f3;

        public bool Spraying => pumpOn && trigger && nozzleHolder != NoHolder;
        public bool Held => nozzleHolder != NoHolder;
        public float Intensity(int i) => (i == 0 ? f0 : i == 1 ? f1 : i == 2 ? f2 : f3) / 255f;
        public void SetIntensity(int i, float value)
        {
            byte b = (byte)Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(value) * 255f), 0, 255);
            if (i == 0) f0 = b; else if (i == 1) f1 = b; else if (i == 2) f2 = b; else f3 = b;
        }
        public bool AllExtinguished
        {
            get { for (int i = 0; i < zoneCount && i < MaxZones; i++) if (Intensity(i) > 0) return false; return zoneCount > 0; }
        }
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref revision); s.SerializeValue(ref phase); s.SerializeValue(ref deadline);
            s.SerializeValue(ref pumpOn); s.SerializeValue(ref trigger); s.SerializeValue(ref nozzleDocked);
            s.SerializeValue(ref nozzleHolder); s.SerializeValue(ref payout); s.SerializeValue(ref dismountAcks);
            s.SerializeValue(ref nozzlePosition); s.SerializeValue(ref nozzleRotation);
            s.SerializeValue(ref zoneCount); s.SerializeValue(ref f0); s.SerializeValue(ref f1); s.SerializeValue(ref f2); s.SerializeValue(ref f3); s.SerializeValue(ref completedAt);
        }
        public bool Equals(FiregroundState o) => revision == o.revision;
    }
}
