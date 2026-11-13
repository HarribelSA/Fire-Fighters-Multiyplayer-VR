using UnityEngine;
namespace FirefighterVR.Station
{
    public sealed class SeatedMonitorAction:MonoBehaviour
    {
        public enum ActionKind {Page,Play,Create,Join,Copy,Exit,Volume}
        public ActionKind kind;public int side,value;public string page;
        public void Invoke(){var lobby=SeatedDispatchLobby.Instance;if(!lobby)return;switch(kind){case ActionKind.Page:lobby.Page(side,page);break;case ActionKind.Play:lobby.Play(side);break;case ActionKind.Create:lobby.Create(side);break;case ActionKind.Join:lobby.Join(side);break;case ActionKind.Copy:lobby.CopyCode();break;case ActionKind.Exit:lobby.Exit();break;case ActionKind.Volume:lobby.KeyVolume(side,value/2,value%2==0?-.1f:.1f);break;}}
    }
}
