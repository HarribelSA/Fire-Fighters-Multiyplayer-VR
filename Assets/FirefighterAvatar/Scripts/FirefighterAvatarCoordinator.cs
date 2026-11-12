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
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FloorY);serializer.SerializeValue(ref OriginY);
            serializer.SerializeValue(ref BodyYaw);serializer.SerializeValue(ref StandingHeight);
            serializer.SerializeValue(ref AvatarScale);serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref LeftGrip);serializer.SerializeValue(ref LeftTrigger);
            serializer.SerializeValue(ref RightGrip);serializer.SerializeValue(ref RightTrigger);
        }
        public bool IsValid => float.IsFinite(FloorY)&&float.IsFinite(OriginY)&&float.IsFinite(BodyYaw)
            &&float.IsFinite(StandingHeight)&&float.IsFinite(AvatarScale)&&float.IsFinite(Speed)
            &&StandingHeight>=.8f&&StandingHeight<=2.4f&&AvatarScale>=.5f&&AvatarScale<=1.5f&&Speed>=0&&Speed<=3.1f;
        public bool Changed(FirefighterAvatarState other) => Mathf.Abs(FloorY-other.FloorY)>.005f
            ||Mathf.Abs(OriginY-other.OriginY)>.005f||Mathf.Abs(Mathf.DeltaAngle(BodyYaw,other.BodyYaw))>1
            ||Mathf.Abs(StandingHeight-other.StandingHeight)>.005f||Mathf.Abs(AvatarScale-other.AvatarScale)>.005f
            ||Mathf.Abs(Speed-other.Speed)>.03f||(Speed==0)!=(other.Speed==0)
            ||LeftGrip!=other.LeftGrip||LeftTrigger!=other.LeftTrigger||RightGrip!=other.RightGrip||RightTrigger!=other.RightTrigger;
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
            driver.headSource=binding.player.head;driver.leftSource=binding.player.leftHand;driver.rightSource=binding.player.rightHand;
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
            if(!localAvatar||!localAvatar.trackingReady||Time.unscaledTime<nextSend)return;
            var player=XRINetworkPlayer.LocalPlayer;
            if(!player||!player.IsOwner||!bindings.ContainsKey(player.OwnerClientId))return;
            nextSend=Time.unscaledTime+1/Mathf.Max(2,StateUpdatesPerSecond);
            var state=new FirefighterAvatarState
            {
                FloorY=localAvatar.floorY,OriginY=localAvatar.trackingOriginY,
                BodyYaw=localAvatar.placement.eulerAngles.y,StandingHeight=localAvatar.StandingHMDHeight,
                AvatarScale=localAvatar.placement.localScale.y,Speed=localAvatar.movementSpeed>.08f?localAvatar.movementSpeed:0,
                LeftGrip=Quantize(localAvatar.leftGripValue),LeftTrigger=Quantize(localAvatar.leftTriggerValue),
                RightGrip=Quantize(localAvatar.rightGripValue),RightTrigger=Quantize(localAvatar.rightTriggerValue)
            };
            if(!sentState||state.Changed(lastState)||Time.unscaledTime-lastSend>=1)
            {PublishStateRpc(state);lastState=state;lastSend=Time.unscaledTime;sentState=true;}
        }
        static byte Quantize(float value)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
        [Rpc(SendTo.Everyone,InvokePermission=RpcInvokePermission.Everyone,Delivery=RpcDelivery.Unreliable)]
        void PublishStateRpc(FirefighterAvatarState state,RpcParams rpcParams=default)
        {
            if(!state.IsValid)return;
            if(!bindings.ContainsKey(rpcParams.Receive.SenderClientId))RefreshPlayers();
            if(!bindings.TryGetValue(rpcParams.Receive.SenderClientId,out var binding))return;
            binding.state=state;binding.hasState=true;binding.receivedAt=Time.unscaledTime;
        }
        void Release(Binding binding)
        {
            for(int i=0;i<binding.defaults.Length;i++)if(binding.defaults[i])binding.defaults[i].enabled=binding.defaultEnabled[i];
            if(binding.visual&&binding.visual!=localAvatar)Destroy(binding.visual.placement.gameObject);
            if(binding.origin)Destroy(binding.origin.gameObject);
        }
        public override void OnNetworkDespawn()
        {
            if(gameManager)gameManager.OnPlayerStateChanged-=PlayerChanged;
            foreach(var binding in bindings.Values)Release(binding);
            bindings.Clear();sentState=false;
        }
    }
}
