using System;
using UnityEngine;

namespace CityNightPresentation
{
    public sealed class CityVisibilityController : MonoBehaviour
    {
        [Serializable] public struct Item { public Renderer renderer; public float distance; }
        public Item[] items = new Item[0];
        [Min(.1f)] public float updateInterval = .35f;
        [Min(5)] public float hysteresis = 20;
        public Camera targetCamera;
        float nextUpdate;
        void Update()
        {
            if(Time.unscaledTime<nextUpdate)return;
            nextUpdate=Time.unscaledTime+updateInterval;
            if(!targetCamera)targetCamera=Camera.main;
            if(!targetCamera)return; // Future player camera is discovered, never reconfigured.
            Vector3 p=targetCamera.transform.position;
            foreach(var item in items)
            {
                var r=item.renderer;if(!r)continue;
                float limit=item.distance+(r.enabled?hysteresis:0);
                bool show=(r.bounds.ClosestPoint(p)-p).sqrMagnitude<limit*limit;
                if(r.enabled!=show)r.enabled=show;
            }
        }
        void OnDisable(){foreach(var item in items)if(item.renderer)item.renderer.enabled=true;}
    }
}
