using UnityEngine;
using System.Collections;

public class SPH2DRenderer : MonoBehaviour {

    public SPH2D GPUSPHCSScript;

    public Material ParticleRenderMat;

    void OnRenderObject() {
        DrawObject();
    }

    void DrawObject() {
        Material m = ParticleRenderMat;
        m.SetPass(0);
        m.SetBuffer("_Particles", GPUSPHCSScript.GetParticleBuffer());
        Graphics.DrawProcedural(MeshTopology.Points, GPUSPHCSScript.GetMaxParticleNum());
    }
}
