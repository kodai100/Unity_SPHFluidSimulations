using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class SPH3D : MonoBehaviour {

    const int SIMULATION_BLOCK_SIZE = 32;
    public ComputeShader particleCS;

    ComputeBuffer particleBufferRead;
    ComputeBuffer particleBufferWrite;
    ComputeBuffer GridSorted;

    public int MaxParticleNum;

    // ---------- For SPH ------------
    public float SPH_RESTDENSITY = 600.0f;
    public float SPH_INTSTIFF = 3.0f; // 堅さ(?)
    public float SPH_PMASS = 0.00020543f; // 粒子質量
    public float SPH_SIMSCALE = 0.004f; // シミュレートするスケール
    public float H = 0.01f; // 有効半径(CELL_WIDTH)
    public static float PI = 3.141592653589793f;
    public float DT = 0.004f; // ΔT
    public float SPH_VISC = 0.2f; // 粘性
    public float SPH_LIMIT = 200.0f; // 速度制限
    public float SPH_RADIUS = 0.004f; // 半径
    public float SPH_EPSILON = 0.00001f; // 許容誤差
    public float SPH_EXTSTIFF = 10000.0f;   // 壁の反発力
    public float SPH_EXTDAMP = 256.0f;
    public float SPH_PDIST;
    public Vector3 MIN = new Vector3(0.0f, 0.0f, -10.0f);
    public Vector3 MAX = new Vector3(20.0f, 50.0f, 10.0f);
    public Vector3 INIT_MIN = new Vector3(0.0f, 0.0f, -10.0f);
    public Vector3 INIT_MAX = new Vector3(10.0f, 20.0f, 10.0f);
    public Vector3 GRAVITY = new Vector3(0, -10, 0);
    private float Poly6Kern; // Poly6Kernel関数の定数
    private float SpikyKern; // SpikyKernel関数の定数
    private float LapKern; // LaplacianKernel関数の定数(Viscosity)
    // ---------- For SPH ------------

    void OnDestroy() {
        ReleaseBuffer();
    }

    void Start() {
        InitializeVariables();
        InitializeComputeBuffer();
        InitializeGizmo();
    }

    public ComputeBuffer GetParticleBuffer(){
        return this.particleBufferRead;
    }

    public int GetMaxParticleNum(){
        return this.MaxParticleNum;
    }

    void Update() {
        int threadGroupSize = Mathf.CeilToInt(MaxParticleNum / SIMULATION_BLOCK_SIZE) +1;

        particleCS.SetInt("_MaxParticleNum", MaxParticleNum);
        particleCS.SetFloat("SPH_RESTDENSITY", SPH_RESTDENSITY);
        particleCS.SetFloat("SPH_INTSTIFF", SPH_INTSTIFF);
        particleCS.SetFloat("SPH_PMASS", SPH_PMASS);
        particleCS.SetFloat("SPH_SIMSCALE", SPH_SIMSCALE);
        particleCS.SetFloat("H", H);
        particleCS.SetFloat("PI", PI);
        particleCS.SetFloat("DT", DT);
        particleCS.SetFloat("SPH_VISC", SPH_VISC);
        particleCS.SetFloat("SPH_LIMIT", SPH_LIMIT);
        particleCS.SetFloat("SPH_RADIUS", SPH_RADIUS);
        particleCS.SetFloat("SPH_EPSILON", SPH_EPSILON);
        particleCS.SetFloat("SPH_EXTSTIFF", SPH_EXTSTIFF);
        particleCS.SetFloat("SPH_EXTDAMP", SPH_EXTDAMP);
        particleCS.SetFloat("SPH_PDIST", SPH_PDIST);
        particleCS.SetVector("MIN", MIN);
        particleCS.SetVector("MAX", MAX);
        particleCS.SetVector("INIT_MIN", INIT_MIN);
        particleCS.SetVector("GRAVITY", GRAVITY);
        particleCS.SetVector("INIT_MAX", INIT_MAX);
        particleCS.SetFloat("Poly6Kern", Poly6Kern);
        particleCS.SetFloat("SpikyKern", SpikyKern);
        particleCS.SetFloat("LapKern", LapKern);

        int kernel = particleCS.FindKernel("sph");
        particleCS.SetBuffer(kernel, "_ParticleBufferRead", particleBufferRead);
        particleCS.SetBuffer(kernel, "_ParticleBufferWrite", particleBufferWrite);
        //particleCS.SetBuffer(kernel, "_GridSorted", GridSorted);

        particleCS.Dispatch(kernel, threadGroupSize, 1, 1);

        SwapBuffer(ref particleBufferRead, ref particleBufferWrite);
    }

    void InitializeVariables(){
        SPH_PDIST = Mathf.Pow(SPH_PMASS / SPH_RESTDENSITY, 1.0f / 3.0f);
        Poly6Kern = 315.0f / (64.0f * PI * Mathf.Pow(H, 9.0f));
        SpikyKern = -45.0f / (PI * Mathf.Pow(H, 6.0f));
        LapKern = 45.0f / (PI * Mathf.Pow(H, 6.0f));
    }

    void InitializeComputeBuffer() {

        // 箱の大きさに従ってパーティクルを生成
        List<Particle> particles = new List<Particle>();
        float d = SPH_PDIST / SPH_SIMSCALE * 0.95f;
        for (float x = INIT_MIN.x + d; x <= INIT_MAX.x - d; x += d) {
            for (float y = INIT_MIN.y + d; y <= INIT_MAX.y - d; y += d) {
                for (float z = INIT_MIN.z + d; z <= INIT_MAX.z - d; z += d) {
                    particles.Add(new Particle(x, y, z));
                }
            }
        }
        MaxParticleNum = particles.Count;

        particleBufferRead = new ComputeBuffer(MaxParticleNum, Marshal.SizeOf(typeof(Particle)));    // パーティクル数を定義
        particleBufferWrite = new ComputeBuffer(MaxParticleNum, Marshal.SizeOf(typeof(Particle)));
        //GridSorted = new ComputeBuffer();
        
        Particle[] p = particles.ToArray();
        particleBufferRead.SetData(p);
        particleBufferWrite.SetData(p);

    }

    void SwapBuffer(ref ComputeBuffer src, ref ComputeBuffer dst) {
        ComputeBuffer tmp = src;
        src = dst;
        dst = tmp;
    }

    void ReleaseBuffer(){
        if (particleBufferRead != null){
            particleBufferRead.Release();
            particleBufferRead = null;
        }
        if (particleBufferWrite != null){
            particleBufferWrite.Release();
            particleBufferWrite = null;
        }
    }

    private Vector3 forGizmoMIN;
    private Vector3 forGizmoMAX;
    void InitializeGizmo() {
        forGizmoMIN = MIN;
        forGizmoMAX = MAX;
    }

    void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        Vector3 blank = ((forGizmoMAX - forGizmoMIN) - (MAX - MIN)) / 2;

        Gizmos.DrawWireCube(new Vector3(blank.x , (MAX.y - MIN.y) / 2, 0), MAX-MIN);
    }

}

[System.Serializable]
struct Particle{
    public Vector3 position;
    public Vector3 velocity;
    public Vector3 force;
    public float density;
    public float pressure;

    public Particle(float x, float y, float z){
        this.position = new Vector3(x, y, z);
        this.velocity = new Vector3(0f, 0f, 0f);
        this.force = new Vector3(0f, 0f, 0f);
        this.density = 0f;
        this.pressure = 0f;
    }
}