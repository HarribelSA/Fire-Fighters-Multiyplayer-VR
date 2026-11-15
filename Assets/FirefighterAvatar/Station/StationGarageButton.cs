using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
namespace FirefighterVR.Station
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class StationGarageButton : MonoBehaviour
    {
        public StationMissionView view;
        public bool open;
        public Collider upperArrow,lowerArrow;
        public bool PressFromAssistantBot(FirefighterVR.StationBot.StationBotFollower bot)
        {
            var flow=StationSessionFlow.Instance;
            if(!flow||!flow.SoloMissionActive||!bot||bot.Role!=StationRole.Assistant||!upperArrow||!view)return false;
            if(Vector3.Distance(upperArrow.ClosestPoint(bot.LeftHandPosition),bot.LeftHandPosition)>.04f)return false;
            view.GarageOpen();return true;
        }
        void Start()=>GetComponent<XRSimpleInteractable>().selectEntered.AddListener(Pressed);
        void Pressed(SelectEnterEventArgs args)
        {
            if(!view)return;
            bool command=open;
            if(upperArrow&&lowerArrow)
            {
                Vector3 point;
                if(args.interactorObject is NearFarInteractor near&&near.selectionRegion.Value==NearFarInteractor.Region.Near)
                    point=args.interactorObject.GetAttachTransform(args.interactableObject).position;
                else if(args.interactorObject is IXRRayProvider ray)point=ray.rayEndPoint;
                else point=args.interactorObject.GetAttachTransform(args.interactableObject).position;
                if(!TryArrow(point,out command))return;
            }
            if(command)view.GarageOpen();else view.GarageClose();
        }
        public bool TryArrow(Vector3 point,out bool opening)
        {
            opening=false;if(!upperArrow||!lowerArrow)return false;
            float up=(upperArrow.ClosestPoint(point)-point).sqrMagnitude,down=(lowerArrow.ClosestPoint(point)-point).sqrMagnitude;
            if(Mathf.Min(up,down)>.04f*.04f)return false;
            opening=up<down;return true;
        }
    }
}
