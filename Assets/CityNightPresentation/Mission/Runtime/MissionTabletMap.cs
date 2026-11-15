using UnityEngine;
using UnityEngine.UI;

namespace CityNightPresentation.Mission
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MissionTabletMap : Graphic
    {
        public CityDrivingRoute route;
        public Transform truck;
        public Transform incident;
        public bool followTruck;
        float nextUpdate;
        protected override void Awake(){base.Awake();raycastTarget=false;}
        void Update(){if(Time.unscaledTime>=nextUpdate){nextUpdate=Time.unscaledTime+.2f;SetVerticesDirty();}}
        public void ToggleFollow(){followTruck=!followTruck;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!route||route.waypoints.Length<2)return;
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            foreach(var wp in route.waypoints){if(!wp)continue;var p=new Vector2(wp.position.x,wp.position.z);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            if(followTruck&&truck){var c=new Vector2(truck.position.x,truck.position.z);min=c-Vector2.one*55;max=c+Vector2.one*55;}
            float scale=Mathf.Min(rectTransform.rect.width/Mathf.Max(1,max.x-min.x),rectTransform.rect.height/Mathf.Max(1,max.y-min.y))*.9f;
            Vector2 center=(min+max)*.5f;
            Vector2 Map(Vector3 p)=>new Vector2(p.x-center.x,p.z-center.y)*scale+rectTransform.rect.center;
            for(int i=1;i<route.waypoints.Length;i++)
            {
                var a=Map(route.waypoints[i-1].position);var b=Map(route.waypoints[i].position);
                if(followTruck&&(!rectTransform.rect.Contains(a)||!rectTransform.rect.Contains(b)))continue;
                var normal=new Vector2(-(b-a).y,(b-a).x).normalized*2;
                Quad(vh,a-normal,a+normal,b+normal,b-normal,new Color(.2f,.7f,.85f));
            }
            if(incident||route.arrivalPoint){var c=Map(incident?incident.position:route.arrivalPoint.position);var rect=rectTransform.rect;c.x=Mathf.Clamp(c.x,rect.xMin+8,rect.xMax-8);c.y=Mathf.Clamp(c.y,rect.yMin+8,rect.yMax-8);Quad(vh,c+new Vector2(-6,-6),c+new Vector2(-6,6),c+new Vector2(6,6),c+new Vector2(6,-6),new Color(1,.28f,.12f));}
            if(truck)
            {
                var c=Map(truck.position);var f=new Vector2(truck.forward.x,truck.forward.z).normalized;var r=new Vector2(f.y,-f.x);
                int start=vh.currentVertCount;vh.AddVert(c+f*9,new Color(1,.85f,.2f),Vector2.zero);vh.AddVert(c-f*6-r*6,new Color(1,.85f,.2f),Vector2.zero);vh.AddVert(c-f*6+r*6,new Color(1,.85f,.2f),Vector2.zero);vh.AddTriangle(start,start+1,start+2);
            }
        }
        static void Quad(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {int i=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddVert(d,color,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
    }
}
