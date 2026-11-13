using System;
using Unity.Netcode;

namespace FirefighterVR.Station
{
    public enum StationRole : byte { None, Driver, Assistant }
    public enum StationPhase : byte { Waiting, Countdown, Fading, Loading, AwaitingSpawn, StationReady, Blocked }

    // One atomic replicated snapshot; both UI countdowns use its shared network-time deadline.
    public struct StationLobbyState : INetworkSerializable, IEquatable<StationLobbyState>
    {
        public const ulong Unassigned = ulong.MaxValue;
        public ulong Driver, Assistant;
        public bool DriverReady, AssistantReady;
        public StationPhase Phase;
        public double Deadline;
        public uint Epoch;
        public byte Arrivals;
        public static StationLobbyState Initial => new StationLobbyState { Driver=Unassigned, Assistant=Unassigned };
        public StationRole RoleOf(ulong id) => id==Unassigned?StationRole.None:id==Driver ? StationRole.Driver : id==Assistant ? StationRole.Assistant : StationRole.None;
        public bool IsReady(ulong id) => id!=Unassigned&&(id==Driver ? DriverReady : id==Assistant && AssistantReady);
        public bool BothReady => Driver!=Unassigned && Assistant!=Unassigned && Driver!=Assistant && DriverReady && AssistantReady;
        public bool Editable => Phase==StationPhase.Waiting || Phase==StationPhase.Countdown;
        public void CancelCountdown() { Deadline=0; Phase=StationPhase.Waiting; }
        public bool Claim(ulong sender, StationRole role)
        {
            if(!Editable || sender==Unassigned || (role!=StationRole.Driver && role!=StationRole.Assistant))return false;
            ulong occupied=role==StationRole.Driver?Driver:Assistant;
            if(occupied!=Unassigned && occupied!=sender)return false;
            if(RoleOf(sender)==role)return true;
            if(Driver==sender){Driver=Unassigned;DriverReady=false;}
            if(Assistant==sender){Assistant=Unassigned;AssistantReady=false;}
            if(role==StationRole.Driver){Driver=sender;DriverReady=false;}else{Assistant=sender;AssistantReady=false;}
            CancelCountdown();return true;
        }
        public bool SetReady(ulong sender,bool ready,bool inputAvailable,int connected,double now)
        {
            if(!Editable || RoleOf(sender)==StationRole.None || (ready && (!inputAvailable || connected!=2)))return false;
            if(sender==Driver)DriverReady=ready;else AssistantReady=ready;
            if(!BothReady){CancelCountdown();return true;}
            if(Phase!=StationPhase.Countdown){Phase=StationPhase.Countdown;Deadline=now+10;Epoch++;Arrivals=0;}
            return true;
        }
        public void Remove(ulong id)
        {
            if(Driver==id){Driver=Unassigned;DriverReady=false;}
            if(Assistant==id){Assistant=Unassigned;AssistantReady=false;}
            if(Editable)CancelCountdown();else Phase=StationPhase.Blocked;
            Arrivals=0;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        { s.SerializeValue(ref Driver);s.SerializeValue(ref Assistant);s.SerializeValue(ref DriverReady);s.SerializeValue(ref AssistantReady);s.SerializeValue(ref Phase);s.SerializeValue(ref Deadline);s.SerializeValue(ref Epoch);s.SerializeValue(ref Arrivals); }
        public bool Equals(StationLobbyState o) => Driver==o.Driver&&Assistant==o.Assistant&&DriverReady==o.DriverReady&&AssistantReady==o.AssistantReady&&Phase==o.Phase&&Deadline.Equals(o.Deadline)&&Epoch==o.Epoch&&Arrivals==o.Arrivals;
    }
}
