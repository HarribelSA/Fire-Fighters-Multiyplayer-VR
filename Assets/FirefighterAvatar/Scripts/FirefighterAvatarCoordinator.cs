using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

namespace FirefighterVR
{
    // Head and hand poses continue to use the template's existing NetworkTransforms.
    public struct FirefighterAvatarState : INetworkSerializable
    {
        public float FloorY, OriginY, BodyYaw, StandingHeight, AvatarScale, Speed;
        public byte LeftGrip, LeftTrigger, RightGrip, RightTrigger;
        public bool Seated;
        public Vector3 SeatPosition;
        public byte SeatId;
        public float SeatHip, SeatFoot;
        public bool GripVehicleSpace;
        public byte LeftVisualWeight,RightVisualWeight;
        public Vector3 LeftVisualPosition,RightVisualPosition;
        public Quaternion LeftVisualRotation,RightVisualRotation;
        // Seated in a registered (possibly moving) seat: head/hands relative to that seat, so peers rebuild them
        // from their own copy of the seat and the vehicle's motion never shakes the remote body.
        public bool SeatSpace;
        public Vector3 HeadLocalPos,LeftLocalPos,RightLocalPos;
        public Quaternion HeadLocalRot,LeftLocalRot,RightLocalRot;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FloorY);serializer.SerializeValue(ref OriginY);
            serializer.SerializeValue(ref BodyYaw);serializer.SerializeValue(ref StandingHeight);
            serializer.SerializeValue(ref AvatarScale);serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref LeftGrip);serializer.SerializeValue(ref LeftTrigger);
            serializer.SerializeValue(ref RightGrip);serializer.SerializeValue(ref RightTrigger);
            serializer.SerializeValue(ref Seated);serializer.SerializeValue(ref SeatPosition);
            if(Seated){serializer.SerializeValue(ref SeatId);serializer.SerializeValue(ref SeatHip);serializer.SerializeValue(ref SeatFoot);serializer.SerializeValue(ref SeatSpace);
                if(SeatSpace){serializer.SerializeValue(ref HeadLocalPos);serializer.SerializeValue(ref HeadLocalRot);serializer.SerializeValue(ref LeftLocalPos);serializer.SerializeValue(ref LeftLocalRot);serializer.SerializeValue(ref RightLocalPos);serializer.SerializeValue(ref RightLocalRot);}}
            serializer.SerializeValue(ref GripVehicleSpace);serializer.SerializeValue(ref LeftVisualWeight);serializer.SerializeValue(ref RightVisualWeight);
            if(LeftVisualWeight>0){serializer.SerializeValue(ref LeftVisualPosition);serializer.SerializeValue(ref LeftVisualRotation);}
            if(RightVisualWeight>0){serializer.SerializeValue(ref RightVisualPosition);serializer.SerializeValue(ref RightVisualRotation);}
        }
        public bool IsValid => float.IsFinite(FloorY)&&float.IsFinite(OriginY)&&float.IsFinite(BodyYaw)
            &&float.IsFinite(StandingHeight)&&float.IsFinite(AvatarScale)&&float.IsFinite(Speed)
            &&float.IsFinite(SeatPosition.x)&&float.IsFinite(SeatPosition.y)&&float.IsFinite(SeatPosition.z)
            &&(!Seated||(float.IsFinite(SeatHip)&&SeatHip>=.2f&&SeatHip<=1.2f&&float.IsFinite(SeatFoot)&&SeatFoot>=.2f&&SeatFoot<=1f))&&StandingHeight>=.8f&&StandingHeight<=2.4f&&AvatarScale>=.5f&&AvatarScale<=1.5f&&Speed>=0&&Speed<=3.1f
            &&ValidGrip(LeftVisualWeight,LeftVisualPosition,LeftVisualRotation)&&ValidGrip(RightVisualWeight,RightVisualPosition,RightVisualRotation)
            &&(!SeatSpace||(ValidLocal(HeadLocalPos,HeadLocalRot)&&ValidLocal(LeftLocalPos,LeftLocalRot)&&ValidLocal(RightLocalPos,RightLocalRot)));
        static bool ValidLocal(Vector3 p,Quaternion q)=>p.sqrMagnitude<9&&ValidGrip(1,p,q);
        static bool ValidGrip(byte weight,Vector3 p,Quaternion q)=>weight==0||(float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.5f&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w<1.5f);
        public bool Changed(FirefighterAvatarState other) => Mathf.Abs(FloorY-other.FloorY)>.005f
            ||Mathf.Abs(OriginY-other.OriginY)>.005f||Mathf.Abs(Mathf.DeltaAngle(BodyYaw,other.BodyYaw))>1
            ||Mathf.Abs(StandingHeight-other.StandingHeight)>.005f||Mathf.Abs(AvatarScale-other.AvatarScale)>.005f
            ||Mathf.Abs(Speed-other.Speed)>.03f||(Speed==0)!=(other.Speed==0)
            ||LeftGrip!=other.LeftGrip||LeftTrigger!=other.LeftTrigger||RightGrip!=other.RightGrip||RightTrigger!=other.RightTrigger||Seated!=other.Seated||SeatId!=other.SeatId||Mathf.Abs(SeatHip-other.SeatHip)>.005f||SeatId==0&&(SeatPosition-other.SeatPosition).sqrMagnitude>.0001f
            ||SeatSpace!=other.SeatSpace||SeatSpace&&((HeadLocalPos-other.HeadLocalPos).sqrMagnitude>.000016f||Quaternion.Angle(HeadLocalRot,other.HeadLocalRot)>.5f||(LeftLocalPos-other.LeftLocalPos).sqrMagnitude>.000016f||Quaternion.Angle(LeftLocalRot,other.LeftLocalRot)>1||(RightLocalPos-other.RightLocalPos).sqrMagnitude>.000016f||Quaternion.Angle(RightLocalRot,other.RightLocalRot)>1)
            ||GripVehicleSpace!=other.GripVehicleSpace||LeftVisualWeight!=other.LeftVisualWeight||RightVisualWeight!=other.RightVisualWeight
            ||LeftVisualWeight>0&&((LeftVisualPosition-other.LeftVisualPosition).sqrMagnitude>.0001f||Quaternion.Angle(LeftVisualRotation,other.LeftVisualRotation)>1)
            ||RightVisualWeight>0&&((RightVisualPosition-other.RightVisualPosition).sqrMagnitude>.0001f||Quaternion.Angle(RightVisualRotation,other.RightVisualRotation)>1);
    }

    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FirefighterAvatarCoordinator : NetworkBehaviour
    {
        public FirefighterAvatarDriver localAvatar;
        public GameObject firefighterVisualPrefab;
        [Range(2,15)] public float StateUpdatesPerSecond=10;
        sealed class Binding
        {
            public XRINetworkPlayer player;
            public Renderer[] defaults;
            public bool[] defaultEnabled;
            public FirefighterAvatarDriver visual;
            public Transform origin;
            public FirefighterAvatarState state;
            public bool hasState;
            public float receivedAt;
            public Transform[] proxies;
        }
        readonly Dictionary<ulong,Binding> bindings=new Dictionary<ulong,Binding>();
        readonly List<ulong> removed=new List<ulong>();
        XRINetworkGameManager gameManager;
        float nextSend, lastSend, nextRefresh;
        FirefighterAvatarState lastState;
        bool sentState;
        public int boundPlayerCount => bindings.Count;
        public int remoteVisualCount
        {
            get {int count=0;foreach(var item in bindings.Values)if(item.visual&&item.visual!=localAvatar)count++;return count;}
        }

        public override void OnNetworkSpawn()
        {
            nextSend=0;lastSend=0;sentState=false;
            gameManager=XRINetworkGameManager.Instance;
            if(gameManager)gameManager.OnPlayerStateChanged+=PlayerChanged;
            RefreshPlayers();
        }
        void PlayerChanged(ulong id,bool joined) {RefreshPlayers();}
        void RefreshPlayers()
        {
            // Use NGO's real spawned objects, including players whose setup event ran before ours.
            foreach(var networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if(!networkObject.TryGetComponent<XRINetworkPlayer>(out var player))continue;
                if(!player.IsSpawned)continue;
                ulong id=player.OwnerClientId;
                if(bindings.TryGetValue(id,out var old))
                {
                    if(old.player==player)continue;
                    Release(old);bindings.Remove(id);
                }
                var meshes=new List<Renderer>();
                foreach(var renderer in player.GetComponentsInChildren<Renderer>(true))
                    if((renderer is MeshRenderer||renderer is SkinnedMeshRenderer)&&!renderer.GetComponentInParent<Canvas>())meshes.Add(renderer);
                var binding=new Binding{player=player,defaults=meshes.ToArray()};
                binding.defaultEnabled=new bool[binding.defaults.Length];
                for(int i=0;i<binding.defaults.Length;i++)binding.defaultEnabled[i]=binding.defaults[i].enabled;
                if(player.IsLocalPlayer||player.IsOwner)binding.visual=localAvatar;
                bindings.Add(id,binding);
            }
        }
        void CreateRemote(Binding binding)
        {
            if(!firefighterVisualPrefab||!binding.player||!binding.player.head||!binding.player.leftHand||!binding.player.rightHand)return;
            if(!binding.hasState)
            {
                // Show the remote immediately. Its own synchronized pose provides the initial floor
                // estimate until the existing posture-state heartbeat supplies calibrated values.
                float standing=localAvatar?localAvatar.StandingHMDHeight:1.65f;
                float floor=binding.player.head.position.y-standing;
                binding.state=new FirefighterAvatarState{FloorY=floor,OriginY=floor,
                    StandingHeight=standing,AvatarScale=localAvatar?localAvatar.placement.localScale.y:1,
                    BodyYaw=binding.player.head.eulerAngles.y};
            }
            var instance=Instantiate(firefighterVisualPrefab,binding.player.transform);
            instance.name="Firefighter Remote Visual";
            var driver=instance.GetComponentInChildren<FirefighterAvatarDriver>(true);
            var origin=new GameObject("Firefighter Tracking Reference").transform;
            origin.SetParent(binding.player.transform,false);
            binding.origin=origin;binding.visual=driver;
            driver.remoteTracking=true;driver.localFirstPerson=false;
            // Proxy sources: follow the networked head/hands normally, or the seat-relative pose while seated.
            Transform Proxy(string n,Transform src){var t=new GameObject(n).transform;t.SetParent(binding.player.transform,false);t.SetPositionAndRotation(src.position,src.rotation);return t;}
            binding.proxies=new[]{Proxy("Avatar Head Source",binding.player.head),Proxy("Avatar Left Source",binding.player.leftHand),Proxy("Avatar Right Source",binding.player.rightHand)};
            driver.networkHead=binding.player.head;driver.networkLeft=binding.player.leftHand;driver.networkRight=binding.player.rightHand;
            driver.headSource=binding.proxies[0];driver.leftSource=binding.proxies[1];driver.rightSource=binding.proxies[2];
            driver.playerRoot=origin;driver.SetRemoteState(binding.state);
            UpdateRemote(binding);
            instance.SetActive(true);
        }
        void UpdateRemote(Binding binding)
        {
            var state=binding.state;
            if(Time.unscaledTime-binding.receivedAt>2)state.Speed=0;
            binding.origin.SetPositionAndRotation(new Vector3(binding.player.head.position.x,state.OriginY,binding.player.head.position.z),Quaternion.Euler(0,state.BodyYaw,0));
            binding.visual.SetRemoteState(state);
        }
        void LateUpdate()
        {
            if(!IsSpawned)return;
            if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.5f;RefreshPlayers();}
            removed.Clear();
            foreach(var item in bindings)
            {
                var binding=item.Value;
                if(!binding.player||!binding.player.IsSpawned){removed.Add(item.Key);continue;}
                if(binding.visual==null&&!binding.player.IsOwner)CreateRemote(binding);
                if(binding.visual)
                {
                    if(binding.visual!=localAvatar)UpdateRemote(binding);
                    // Disable only visuals. Template tracking, ownership and interaction components stay active.
                    foreach(var renderer in binding.defaults)if(renderer)renderer.enabled=false;
                }
            }
            foreach(var id in removed){Release(bindings[id]);bindings.Remove(id);}
            if(!localAvatar||Time.unscaledTime<nextSend)return;
            // A registered seat moving with a vehicle is NOT a posture change (only a different seat is).
            bool postureChanged=!sentState||localAvatar.Seated!=lastState.Seated||localAvatar.SeatId!=lastState.SeatId||
                localAvatar.SeatId==0&&(localAvatar.SeatPosition-lastState.SeatPosition).sqrMagnitude>.0001f;
            // Mode changes must not be gated on a live headset or a lost hand pose.
            // A reliable heartbeat also repairs a missed initial posture on a late join.
            bool postureHeartbeat=Time.unscaledTime-lastSend>=1;
            if(!localAvatar.trackingReady&&!postureChanged&&!postureHeartbeat)return;
            var player=XRINetworkPlayer.LocalPlayer;
            if(!player||!player.IsOwner||!bindings.ContainsKey(player.OwnerClientId))return;
            SeatPose seatPose=null;bool seatSpace=localAvatar.Seated&&localAvatar.SeatId!=0&&SeatPose.TryGet(localAvatar.SeatId,out seatPose);
            nextSend=Time.unscaledTime+1/(seatSpace?20:Mathf.Max(2,StateUpdatesPerSecond));
            var state=new FirefighterAvatarState
            {
                FloorY=localAvatar.floorY,OriginY=localAvatar.trackingOriginY,
                BodyYaw=postureChanged?(localAvatar.Seated?localAvatar.SeatYaw:localAvatar.playerRoot.eulerAngles.y):localAvatar.placement.eulerAngles.y,StandingHeight=localAvatar.StandingHMDHeight,
                AvatarScale=localAvatar.placement.localScale.y,Speed=localAvatar.movementSpeed>.08f?localAvatar.movementSpeed:0,
                Seated=localAvatar.Seated,SeatPosition=localAvatar.SeatPosition,SeatId=localAvatar.SeatId,SeatHip=localAvatar.SeatHipHeight,SeatFoot=localAvatar.SeatFootForward,
                LeftGrip=Quantize(localAvatar.leftGripValue),LeftTrigger=Quantize(localAvatar.leftTriggerValue),
                RightGrip=Quantize(localAvatar.rightGripValue),RightTrigger=Quantize(localAvatar.rightTriggerValue)
            };
            if(seatSpace)
            {
                var st=seatPose.transform;var inv=Quaternion.Inverse(st.rotation);state.SeatSpace=true;
                state.HeadLocalPos=st.InverseTransformPoint(localAvatar.headSource.position);state.HeadLocalRot=inv*localAvatar.headSource.rotation;
                state.LeftLocalPos=st.InverseTransformPoint(localAvatar.leftSource.position);state.LeftLocalRot=inv*localAvatar.leftSource.rotation;
                state.RightLocalPos=st.InverseTransformPoint(localAvatar.rightSource.position);state.RightLocalRot=inv*localAvatar.rightSource.rotation;
            }
            state.GripVehicleSpace=localAvatar.VisualGripReference!=null;
            localAvatar.GetVisualGrip(true,out state.LeftVisualPosition,out state.LeftVisualRotation,out state.LeftVisualWeight);
            localAvatar.GetVisualGrip(false,out state.RightVisualPosition,out state.RightVisualRotation,out state.RightVisualWeight);
            if(!sentState||state.Changed(lastState)||Time.unscaledTime-lastSend>=1)
            {if(postureChanged||postureHeartbeat)PublishPostureRpc(state);else PublishStateRpc(state);lastState=state;lastSend=Time.unscaledTime;sentState=true;}
        }
        static byte Quantize(float value)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
        [Rpc(SendTo.Everyone,InvokePermission=RpcInvokePermission.Everyone,Delivery=RpcDelivery.Unreliable)]
        void PublishStateRpc(FirefighterAvatarState state,RpcParams rpcParams=default)
        {AcceptState(state,rpcParams.Receive.SenderClientId);}
        [Rpc(SendTo.Everyone,InvokePermission=RpcInvokePermission.Everyone)]
        void PublishPostureRpc(FirefighterAvatarState state,RpcParams rpcParams=default)
        {AcceptState(state,rpcParams.Receive.SenderClientId);}
        void AcceptState(FirefighterAvatarState state,ulong sender)
        {
            if(!state.IsValid)return;
            if(!bindings.ContainsKey(sender))RefreshPlayers();
            if(!bindings.TryGetValue(sender,out var binding))return;
            binding.state=state;binding.hasState=true;binding.receivedAt=Time.unscaledTime;
        }
        void Release(Binding binding)
        {
            for(int i=0;i<binding.defaults.Length;i++)if(binding.defaults[i])binding.defaults[i].enabled=binding.defaultEnabled[i];
            if(binding.visual&&binding.visual!=localAvatar)Destroy(binding.visual.placement.gameObject);
            if(binding.origin)Destroy(binding.origin.gameObject);
            if(binding.proxies!=null)foreach(var p in binding.proxies)if(p)Destroy(p.gameObject);
        }
        public override void OnNetworkDespawn()
        {
            if(gameManager)gameManager.OnPlayerStateChanged-=PlayerChanged;
            foreach(var binding in bindings.Values)Release(binding);
            bindings.Clear();sentState=false;
        }
    }
}
