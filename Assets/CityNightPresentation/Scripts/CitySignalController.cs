using System;
using UnityEngine;

namespace CityNightPresentation
{
    // Four exclusive approach phases: E, S, W, N. Opposing left turns never share green.
    public sealed class CitySignalController : MonoBehaviour
    {
        public enum Aspect { Red, Yellow, Green }
        [Serializable] public sealed class Head
        {
            [Range(0,3)] public int approach;
            public Renderer red, yellow, green;
            public Transform stopLine;
        }
        [Min(2)] public float greenSeconds = 13;
        [Min(1)] public float yellowSeconds = 3;
        [Min(.5f)] public float allRedSeconds = 2;
        public float offsetSeconds;
        public int[] phaseOrder = { 0, 1, 2, 3 };
        public Head[] heads = new Head[0];
        public Material redOn, yellowOn, greenOn, lensOff;
        public bool externalAllRed;
        public bool responseChallenge;
        [Range(0,3)]public int responseApproach;
        [Min(1)]public float responseWaitSeconds=60;
        bool responseStarted,responsePassed;
        float responseDeadline;
        public float ResponseWaitRemaining=>!responseChallenge||responsePassed?0:responseStarted?Mathf.Max(0,responseDeadline-Clock):responseWaitSeconds;
        public void StartResponseWait(){if(responseChallenge&&!responseStarted&&!responsePassed){responseStarted=true;responseDeadline=Clock+responseWaitSeconds;}}
        public void CompleteResponsePassage(){if(!responseChallenge||responsePassed)return;responsePassed=true;ReleasePriority();}
        public float Clock { get; private set; }
        public event Action<CitySignalController> PhaseChanged;
        int lastCode = -1;
        int priorityWanted = -1, transitionApproach, priorityStage;
        float transitionDeadline;
        [NonSerialized] public int occupiedApproach = -1;
        public bool HasPriority => priorityStage != 0;
        public int ActiveApproach { get; private set; }
        public Aspect CurrentAspect { get; private set; }
        float Green => Mathf.Max(2,greenSeconds);
        float Yellow => Mathf.Max(1,yellowSeconds);
        float Clearance => Mathf.Max(.5f,allRedSeconds);
        public Aspect GetAspect(int approach) => !externalAllRed && ActiveApproach == approach ? CurrentAspect : Aspect.Red;
        public bool Allows(int approach) => GetAspect(approach) == Aspect.Green;
        public void SetClock(float seconds)
        {
            Clock = seconds;
            float phase = Green + Yellow + Clearance;
            int count = phaseOrder != null && phaseOrder.Length > 0 ? phaseOrder.Length : 4;
            float t = Mathf.Repeat(seconds + offsetSeconds, phase * count);
            int phaseIndex = Mathf.Min(count-1, Mathf.FloorToInt(t / phase));
            ActiveApproach = phaseOrder != null && phaseOrder.Length > 0 ? phaseOrder[phaseIndex] : phaseIndex;
            float local = t - phaseIndex * phase;
            CurrentAspect = externalAllRed || local >= Green + Yellow ? Aspect.Red : local >= Green ? Aspect.Yellow : Aspect.Green;
            if(responseChallenge&&!responsePassed&&priorityStage==0)
            {
                if(responseStarted&&seconds>=responseDeadline)RequestPriority(responseApproach);
                else if(ActiveApproach==responseApproach)CurrentAspect=Aspect.Red;
            }
            if(priorityStage!=0)
            {
                if(seconds>=transitionDeadline)
                {
                    if(priorityStage==1){priorityStage=2;transitionDeadline=seconds+Clearance;}
                    else if((priorityStage==2||priorityStage==5)&&(occupiedApproach==-1||priorityStage==2&&occupiedApproach==priorityWanted)){priorityStage=priorityWanted>=0?3:0;transitionDeadline=float.PositiveInfinity;}
                    else if(priorityStage==4){priorityStage=5;transitionDeadline=seconds+Clearance;}
                }
                if(priorityStage==1||priorityStage==4){ActiveApproach=transitionApproach;CurrentAspect=Aspect.Yellow;}
                else if(priorityStage==2||priorityStage==5){CurrentAspect=Aspect.Red;}
                else if(priorityStage==3){ActiveApproach=priorityWanted;CurrentAspect=Aspect.Green;}
            }
            if(CurrentAspect==Aspect.Green&&occupiedApproach!=-1&&occupiedApproach!=ActiveApproach)CurrentAspect=Aspect.Red;
            if(externalAllRed)CurrentAspect=Aspect.Red;
            DrawVisuals();
        }
        public bool RequestPriority(int approach)
        {
            if(Array.IndexOf(phaseOrder,approach)<0)return false;
            if(priorityWanted==approach&&priorityStage!=0)return true;
            priorityWanted=approach;
            if(priorityStage==0&&CurrentAspect==Aspect.Green&&ActiveApproach==approach){priorityStage=3;transitionDeadline=float.PositiveInfinity;return true;}
            if(priorityStage==0||priorityStage==3)
            {
                transitionApproach=ActiveApproach;
                priorityStage=CurrentAspect==Aspect.Red?2:1;
                transitionDeadline=Clock+(priorityStage==2?Clearance:Yellow);
            }
            return true;
        }
        public void ReleasePriority()
        {
            // An expired normal wait gets a legal green too; this does not enable emergency yielding.
            if(responseChallenge&&!responsePassed&&responseStarted&&Clock>=responseDeadline&&(priorityStage==2||priorityStage==3))return;
            priorityWanted=-1;
            if(priorityStage==3){transitionApproach=ActiveApproach;priorityStage=4;transitionDeadline=Clock+Yellow;}
        }
        public void ApplyReplicatedState(int approach,Aspect aspect)
        {ActiveApproach=approach;CurrentAspect=aspect;externalAllRed=false;DrawVisuals();}
        void DrawVisuals()
        {
            int code = externalAllRed ? 99 : ActiveApproach * 3 + (int)CurrentAspect;
            if (lastCode == code) return;
            lastCode = code;
            foreach (var h in heads)
            {
                Aspect a = GetAspect(h.approach);
                if (h.red) h.red.sharedMaterial = a == Aspect.Red ? redOn : lensOff;
                if (h.yellow) h.yellow.sharedMaterial = a == Aspect.Yellow ? yellowOn : lensOff;
                if (h.green) h.green.sharedMaterial = a == Aspect.Green ? greenOn : lensOff;
            }
            PhaseChanged?.Invoke(this);
        }
        void OnEnable() { lastCode = -1; SetClock(Clock); }
        public void RefreshVisuals() { lastCode = -1; SetClock(Clock); }
        void OnValidate() { lastCode = -1; }
    }
}
