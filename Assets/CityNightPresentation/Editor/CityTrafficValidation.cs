using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityNightPresentation.Editor
{
    public static class CityTrafficValidation
    {
        static bool Overlap(CityTrafficManager.Vehicle a,CityTrafficManager.Vehicle b)
        {
            a.path.Sample(a.distance,out var ap,out var af);b.path.Sample(b.distance,out var bp,out var bf);
            af.y=0;bf.y=0;af.Normalize();bf.Normalize();var ar=Vector3.Cross(Vector3.up,af);var br=Vector3.Cross(Vector3.up,bf);var d=bp-ap;d.y=0;
            foreach(var axis in new[]{af,ar,bf,br})
            {
                float ra=Mathf.Abs(Vector3.Dot(axis,af))*a.length*.5f+Mathf.Abs(Vector3.Dot(axis,ar))*1.25f;
                float rb=Mathf.Abs(Vector3.Dot(axis,bf))*b.length*.5f+Mathf.Abs(Vector3.Dot(axis,br))*1.25f;
                if(Mathf.Abs(Vector3.Dot(d,axis))>ra+rb)return false;
            }
            return true;
        }
        public static object Run()
        {
            var m=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityTrafficManager>()).Single();
            var agents=m.vehicles;var starts=agents.Select(v=>v.distance).ToArray();var speeds=agents.Select(v=>v.speed).ToArray();int active=m.activeVehicles;int mask=m.obstacleMask.value;
            float clock=m.Clock;var paths=agents.Select(v=>v.path).Distinct().ToArray();
            var stopChecks=new List<object>();int overlaps=0;var progress=new float[agents.Length];int stoppedTicks=0;
            try
            {
                // First isolate each stop gate and force all-red. No realtime game loop is entered.
                foreach(var path in paths)for(int g=0;g<path.gates.Length;g++)
                {
                    path.Initialize();float target=path.GateDistance(g);
                    var v=new CityTrafficManager.Vehicle{body=agents[0].body,path=path,distance=target-8,length=5,cruiseSpeed=7,speed=7};
                    m.vehicles=new[]{v};m.activeVehicles=1;m.obstacleMask=0;
                    foreach(var s in m.signals)s.externalAllRed=true;
                    for(int step=0;step<300;step++)m.SimulateStep(.05f);
                    bool held=v.distance<=target+.01f && v.speed<.05f;
                    foreach(var s in m.signals)s.externalAllRed=false;
                    for(int step=0;step<1800&&v.distance<target+35;step++)m.SimulateStep(.05f);
                    stopChecks.Add(new{path=path.name,gate=g,heldAtRed=held,resumedAfterGreen=v.distance>target+25});
                }
                m.vehicles=agents;m.activeVehicles=active;m.obstacleMask=mask;m.ResetClock();
                for(int i=0;i<agents.Length;i++){agents[i].distance=starts[i];agents[i].speed=0;agents[i].reserved=null;}
                m.Initialize();
                for(int step=0;step<3600;step++)
                {
                    foreach(var v in agents){v.path.Sample(v.distance,out var p,out var f);v.body.SetPositionAndRotation(p,Quaternion.LookRotation(f));}
                    Physics.SyncTransforms();m.SimulateStep(.05f);
                    if(agents.Any(v=>v.speed<.1f))stoppedTicks++;
                    if(step%10==0)for(int i=0;i<agents.Length;i++)for(int j=i+1;j<agents.Length;j++)if(Overlap(agents[i],agents[j]))overlaps++;
                }
                for(int i=0;i<agents.Length;i++)progress[i]=agents[i].distance-starts[i];
            }
            finally
            {
                m.vehicles=agents;m.activeVehicles=active;m.obstacleMask=mask;
                foreach(var s in m.signals)s.externalAllRed=false;
                for(int i=0;i<agents.Length;i++){agents[i].distance=starts[i];agents[i].speed=speeds[i];agents[i].reserved=null;}
                m.ResetClock(clock);m.Initialize();Physics.SyncTransforms();EditorUtility.SetDirty(m);
            }
            return new{validation="Deterministic Edit Mode simulation; not Play Mode or Quest profiling",simulatedSeconds=180,stopChecks=stopChecks.ToArray(),progressMeters=progress,allVehiclesProgressed=progress.All(p=>p>20),sampledVehicleOverlaps=overlaps,stepsWithStoppedVehicles=stoppedTicks};
        }
    }
}
