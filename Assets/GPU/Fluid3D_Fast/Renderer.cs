using UnityEngine;
using System.Collections;


public class Renderer : MonoBehaviour {

    public SPH3D_Fast GPUFluidCSScript;

    public Material RenderParticleMat;
    public Camera RenderCamera;

    public bool Enable = true;

    void OnRenderObject() {

        if (Enable)
            DrawParticle();
    }

    void DrawParticle() {
        Material m = RenderParticleMat;

        var inverseViewMatrix = RenderCamera.worldToCameraMatrix.inverse;

        m.SetPass(0);
        m.SetMatrix("_InverseMatrix", inverseViewMatrix);
        m.SetBuffer("_ParticlesBuffer", GPUFluidCSScript.GetParticlesBuffer());
        m.SetBuffer("_ParticlesDensityBuffer", GPUFluidCSScript.GetParticlesDensityBuffer());
        Graphics.DrawProcedural(MeshTopology.Points, GPUFluidCSScript.GetNumParticles());
    }
}
