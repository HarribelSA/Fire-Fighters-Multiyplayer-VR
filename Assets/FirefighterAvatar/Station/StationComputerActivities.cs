using TMPro;
using UnityEngine;
namespace FirefighterVR.Station
{
    // Local optional content on the existing monitor. Never changes mission state.
    [DefaultExecutionOrder(280)]
    public sealed class StationComputerActivities : MonoBehaviour
    {
        public StationMissionView view;
        public GameObject activityRoot,drillPanel,libraryPanel;
        public UnityEngine.UI.Button drillTab,libraryTab,restart;
        public UnityEngine.UI.Button[] cells,articles;
        public UnityEngine.UI.Slider pace;
        public TMP_Text scoreText,articleText;
        int target,score,misses;
        float targetUntil;
        readonly System.Random random=new System.Random(149);
        static readonly string[] Content={
            "LOCAL STATION LIBRARY — NO INTERNET ACCESS\n\nRESPONSE CHECKLIST\n\nConfirm Driver and Assistant profiles, tracked headset, and vehicle input. The Driver stays seated. The Assistant handles the incident tablet and garage.\n\nDo not depart until the existing garage is fully open and both crew seats are confirmed.\n\nDuring response, obey normal traffic signals unless a safe emergency priority phase has been granted. Pulling the emergency handle requests priority; it does not make every signal green.\n\nOn arrival, align with the stop zone and stop safely. Review the mission evaluation.",
            "LOCAL STATION LIBRARY — NO INTERNET ACCESS\n\nCONTROL PROFILES\n\nDRIVER\nHand Tracking + Logitech G29. Remain seated at lobby, dispatch and truck. Use pointing/pinch for computer UI and real Grab for wheel and emergency handle. Each hand releases independently. G29 alone steers the truck.\n\nASSISTANT\nMeta Quest Controllers. Select Smooth Move, Teleport, or both in Settings. Existing grab, trigger and turning controls remain available.\n\nRole is determined by selected profile, never by Host/Client identity or join order.",
            "LOCAL STATION LIBRARY — NO INTERNET ACCESS\n\nEMERGENCY PRIORITY\n\nApproach the designated red signal and stop safely. Release the right hand from the wheel, grab the red pull handle, and pull along its rail.\n\nThe siren and roof lightbar indicate emergency mode. Conflicting traffic must complete yellow and clearance intervals before your green. Verify the road is clear before proceeding.\n\nPush the handle back to reset. Without activation, traffic remains on its normal cycle, waiting time counts, and the mission can time out.",
            "LOCAL STATION LIBRARY — NO INTERNET ACCESS\n\nSEATED HAND INTERACTION\n\nPoint and pinch to activate buttons. Hold the UI press while dragging a slider or scroll area. Near-touch behavior depends on the existing XR interactor configuration.\n\nThe memory-light drill is optional. Click the highlighted square, adjust its pace, and improve your score.\n\nAn emergency alert immediately interrupts this local content and restores the dispatch report. The Assistant must still present the tablet and the Driver must accept."
        };
        void Start()
        {
            if(drillTab)drillTab.onClick.AddListener(ShowDrill);if(libraryTab)libraryTab.onClick.AddListener(ShowLibrary);if(restart)restart.onClick.AddListener(Restart);
            for(int i=0;i<cells.Length;i++){int index=i;cells[i].onClick.AddListener(()=>Hit(index));}
            for(int i=0;i<articles.Length;i++){int index=i;articles[i].onClick.AddListener(()=>OpenArticle(index));}
            OpenArticle(0);Restart();ShowDrill();
        }
        void Update()
        {
            var f=StationSessionFlow.Instance;
            bool idle=f&&f.MissionSessionActive&&f.MissionLocalRole==StationRole.Driver&&f.MissionState.stage==CrewMissionStage.Exploration;
            if(activityRoot&&activityRoot.activeSelf!=idle)activityRoot.SetActive(idle);
            if(view){if(view.computerReport)view.computerReport.gameObject.SetActive(!idle);if(view.acceptButton)view.acceptButton.gameObject.SetActive(!idle);if(view.objective)view.objective.gameObject.SetActive(!idle);}
            if(idle&&drillPanel.activeSelf&&Time.unscaledTime>=targetUntil){misses++;NextTarget();}
        }
        void ShowDrill(){drillPanel.SetActive(true);libraryPanel.SetActive(false);NextTarget();}
        void ShowLibrary(){drillPanel.SetActive(false);libraryPanel.SetActive(true);}
        void Restart(){score=misses=0;NextTarget();}
        void Hit(int index)
        {if(!activityRoot.activeInHierarchy||!drillPanel.activeSelf)return;if(index==target)score++;else misses++;NextTarget();}
        void NextTarget()
        {
            if(cells==null||cells.Length==0)return;target=random.Next(cells.Length);targetUntil=Time.unscaledTime+Mathf.Lerp(3,.7f,pace?pace.value:.4f);
            for(int i=0;i<cells.Length;i++){var image=cells[i].targetGraphic as UnityEngine.UI.Image;if(image)image.color=i==target?new Color(.08f,.8f,.4f):new Color(.04f,.12f,.18f);var text=cells[i].GetComponentInChildren<TMP_Text>();if(text)text.text=i==target?"RESPOND":"·";}
            if(scoreText)scoreText.text="RESPONSE DRILL · SCORE "+score+" · MISSES "+misses+"\nClick the lit square. Drag PACE to adjust difficulty.";
        }
        void OpenArticle(int index){if(articleText)articleText.text=Content[Mathf.Clamp(index,0,Content.Length-1)];}
    }
}
