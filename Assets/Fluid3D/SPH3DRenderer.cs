using UnityEngine;
using System.Collections;

public class SPH3DRenderer : MonoBehaviour
{

    public SPH3D GPUSPHCSScript;

    public Material ParticleRenderMat;

    void OnRenderObject(){
        DrawObject();
    }

    void DrawObject(){
        Material m = ParticleRenderMat;
        m.SetPass(0);
        m.SetBuffer("_Particles", GPUSPHCSScript.GetParticleBuffer());
        Graphics.DrawProcedural(MeshTopology.Points, GPUSPHCSScript.GetMaxParticleNum());
    }
}
