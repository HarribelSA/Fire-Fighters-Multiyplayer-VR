using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
namespace FirefighterVR
{
    // Exact baked geometry is reused only while the complete skin pose is unchanged.
    public sealed class AvatarSoleCache
    {
        static readonly ProfilerMarker Marker=new ProfilerMarker("Firefighter.SoleMeasure");
        static readonly ProfilerMarker BakeMarker=new ProfilerMarker("Firefighter.SoleBake");
        readonly SkinnedMeshRenderer renderer;
        readonly List<Vector3> vertices=new List<Vector3>();
        Transform[] bones;Matrix4x4[] pose;Mesh source;Matrix4x4 previousWorld;
        float previousMinimum;bool valid,cacheable;
        public int BakeCount{get;private set;}
        public int CacheHits{get;private set;}
        public AvatarSoleCache(SkinnedMeshRenderer renderer){this.renderer=renderer;Bind();}
        void Bind(){source=renderer.sharedMesh;bones=renderer.bones;pose=new Matrix4x4[bones.Length];valid=false;cacheable=source&&source.blendShapeCount==0&&!renderer.GetComponent<Cloth>();}
        static bool Same(Matrix4x4 a,Matrix4x4 b,float epsilon){for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>epsilon)return false;return true;}
        public void Invalidate()=>valid=false;
        public float Measure(Mesh scratch)
        {
            using var marker=Marker.Auto();if(source!=renderer.sharedMesh)Bind();
            var world=renderer.localToWorldMatrix;var inverse=renderer.worldToLocalMatrix;bool unchanged=valid&&cacheable;
            if(unchanged)for(int i=0;i<bones.Length;i++)if(!bones[i]||!Same(inverse*bones[i].localToWorldMatrix,pose[i],.0001f)){unchanged=false;break;}
            if(!unchanged){using(var bake=BakeMarker.Auto()){renderer.BakeMesh(scratch);scratch.GetVertices(vertices);}for(int i=0;i<bones.Length;i++)pose[i]=bones[i]?inverse*bones[i].localToWorldMatrix:Matrix4x4.zero;BakeCount++;valid=true;}
            else{CacheHits++;if(Same(world,previousWorld,.000001f))return previousMinimum;}
            float min=float.PositiveInfinity;for(int i=0;i<vertices.Count;i++)min=Mathf.Min(min,world.MultiplyPoint3x4(vertices[i]).y);
            previousWorld=world;previousMinimum=min;return min;
        }
    }
}
