using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AsciiHero.Editor
{
    // Headless CI has no graphics device. Rasterize the actual posed renderer meshes so
    // an intersecting body, underlayer or cape is visible in the build's fit diagnostic.
    static class KnightFitPreview
    {
        const int Width = 360, Height = 640;
        static Color Tint(Material m)
        {
            switch (m.name)
            {
                case "Cape": return new Color(.22f,.23f,.25f);
                case "Gauntlet": case "Shoulder": case "Cuirass": case "Bracer":
                case "Greave": case "Poleyn": case "Cuisse": case "Helmet": case "Sabaton":
                    return new Color(.80f,.84f,.88f);
                default: return m.color;
            }
        }
        public static void Write(string directory)
        {
            var body=Body.Defaults('m'); body[0]=178; body[Body.Ix("weight")]=150;
            body[Body.Ix("muscle")]=70; body[Body.Ix("fat")]=4; body[Body.Ix("neckLen")]=1.047f;
            var ch=Character.Build(new CharConfig { height=178, body=body, shadow=true },new Mats(true));
            try
            {
                var actor=new MotionActor(ch); actor.Animate(.25f,1/30f);
                var pixels=new Color32[Width*3*Height]; var depth=new float[pixels.Length];
                for(int i=0;i<pixels.Length;i++) { pixels[i]=new Color32(31,34,38,255); depth[i]=float.NegativeInfinity; }
                foreach(var renderer in ch.go.GetComponentsInChildren<Renderer>())
                {
                    Mesh mesh; bool baked=renderer is SkinnedMeshRenderer;
                    if(renderer is SkinnedMeshRenderer skin) { mesh=new Mesh(); skin.BakeMesh(mesh); }
                    else mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    var points=mesh.vertices; var triangles=mesh.triangles; var tint=Tint(renderer.sharedMaterial);
                    for(int i=0;i<points.Length;i++) points[i]=renderer.transform.TransformPoint(points[i]);
                    for(int view=0;view<3;view++)
                    {
                        var rotation=Quaternion.Euler(0,new float[]{-8,82,174}[view],0);
                        var projected=new Vector3[points.Length];
                        for(int k=0;k<points.Length;k++)
                        {
                            var p=rotation*points[k]; projected[k]=new Vector3(Width*.5f-p.x*288,28+p.y*288,p.z);
                        }
                        for(int k=0;k<triangles.Length;k+=3)
                        {
                            int ia=triangles[k],ib=triangles[k+1],ic=triangles[k+2];
                            var a=projected[ia]; var b=projected[ib]; var c=projected[ic];
                            float area=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
                            if(Mathf.Abs(area)<.001f) continue;
                            var norm=rotation*Vector3.Cross(points[ib]-points[ia],points[ic]-points[ia]).normalized;
                            float light=.37f+.63f*Mathf.Max(0,Vector3.Dot(norm,new Vector3(.34f,.56f,.75f)));
                            Color32 col=new Color(tint.r*light,tint.g*light,tint.b*light);
                            int xmin=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))));
                            int xmax=Mathf.Min(Width-1,Mathf.CeilToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))));
                            int ymin=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y))));
                            int ymax=Mathf.Min(Height-1,Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y))));
                            for(int y=ymin;y<=ymax;y++) for(int x=xmin;x<=xmax;x++)
                            {
                                float px=x+.5f-a.x,py=y+.5f-a.y;
                                float u=(px*(c.y-a.y)-py*(c.x-a.x))/area,v=((b.x-a.x)*py-(b.y-a.y)*px)/area;
                                if(u<0||v<0||u+v>1) continue;
                                int i=y*Width*3+view*Width+x; float z=a.z+u*(b.z-a.z)+v*(c.z-a.z);
                                if(z>depth[i]) {depth[i]=z; pixels[i]=col;}
                            }
                        }
                    }
                    if(baked) Object.DestroyImmediate(mesh);
                }
                var texture=new Texture2D(Width*3,Height,TextureFormat.RGB24,false); texture.SetPixels32(pixels); texture.Apply();
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,"Knight-fit.png"),texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally { Object.DestroyImmediate(ch.go); }
        }
    }
}
