using UnityEngine;

/// <summary>
/// PhaseScrambleStimulus
/// 
/// VR-optimized phase-scrambled texture generation with:
/// - Single scramble parameter [0, 1]
/// - Continuous temporal evolution with frequency-dependent drift/shimmer
/// - Global translation / flow via Fourier phase ramps
/// - Stable Hermitian symmetry (same magnitude spectrum every frame)
/// 
/// Works with OpenXR (Meta Quest, Vive, SteamVR, etc.)
/// Stimulus runs at configurable FPS independent of headset refresh rate.
/// </summary>
public class PhaseScrambleStimulus : MonoBehaviour
{
    [Header("Source / Output")]
    public Texture2D sourceGrayscale;            // must be N x N, single-channel or readable
    public Material targetMaterial;              // material on your quad
    public string materialTextureProperty = "_BaseMap"; // URP Unlit uses _BaseMap

    [Header("Compute Shader")]
    public ComputeShader cs;

    [Header("Stimulus Parameters")]
    [Range(0f, 1f)] public float scramble = 1.0f; // 0 = original, 1 = fully scrambled
    [Tooltip("Horizontal motion in pixels/sec")]
    public float vxPixelsPerSec = 40f;
    [Tooltip("Vertical motion in pixels/sec")]
    public float vyPixelsPerSec = 0f;

    [Header("Phase Evolution Parameters")]
    [Tooltip("Stimulus update rate (independent of headset refresh)")]
    public float fpsStimulus = 60f;
    [Tooltip("Base time constant for phase evolution (seconds)")]
    public float tau0 = 1.5f;
    [Tooltip("Frequency scale parameter")]
    public float r0 = 0.02f;
    [Tooltip("Exponent for frequency-dependent evolution")]
    public float gamma = 1.0f;

    [Header("Advanced")]
    public uint seed = 1;
    [Tooltip("Auto-disable if preferred")]
    public bool autoDisableIfMissing = true;

    // Must match compute shader #define N and LOGN
    private const int N = 256;
    private const int LOGN = 8;

    private RenderTexture outTex;
    private ComputeBuffer magBuf;     // float A(k)
    private ComputeBuffer phi0Buf;    // float phi0(k)
    private ComputeBuffer pBuf;       // float2 unit phasors
    private ComputeBuffer specBuf;    // float2 complex spectrum
    private ComputeBuffer tempBuf;    // float2 scratch

    private int kInit, kUpdate, kIFFTH, kIFFTV, kOut;

    private float dx, dy;
    private uint frameIndex;
    private float accum;
    private float dtStim;
    private bool initialized;

    void Start()
    {
        if (!ValidateInputs()) return;
        InitializeShaders();
        initialized = true;
    }

    bool ValidateInputs()
    {
        if (sourceGrayscale == null)
        {
            Debug.LogError("[PhaseScrambleStimulus] Missing source image.");
            if (autoDisableIfMissing) enabled = false;
            return false;
        }

        if (sourceGrayscale.width != N || sourceGrayscale.height != N)
        {
            Debug.LogError($"[PhaseScrambleStimulus] Source must be {N}x{N} to match compute shader (currently {sourceGrayscale.width}x{sourceGrayscale.height}).");
            if (autoDisableIfMissing) enabled = false;
            return false;
        }

        if (cs == null)
        {
            Debug.LogError("[PhaseScrambleStimulus] Missing ComputeShader.");
            if (autoDisableIfMissing) enabled = false;
            return false;
        }

        return true;
    }

    void InitializeShaders()
    {
        // Create output RT (RFloat = single-channel float)
        outTex = new RenderTexture(N, N, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "PhaseScrambleOutput"
        };
        outTex.Create();

        if (targetMaterial != null)
            targetMaterial.SetTexture(materialTextureProperty, outTex);

        // Find compute kernels
        kInit = cs.FindKernel("InitRandomPhasor");
        kUpdate = cs.FindKernel("UpdateSpectrum");
        kIFFTH = cs.FindKernel("IFFT_Horizontal");
        kIFFTV = cs.FindKernel("IFFT_Vertical");
        kOut = cs.FindKernel("OutputReal");

        // Validate all kernels were found (if any are -1, the compute shader failed to compile)
        if (kInit < 0 || kUpdate < 0 || kIFFTH < 0 || kIFFTV < 0 || kOut < 0)
        {
            Debug.LogError($"[PhaseScrambleStimulus] Compute shader kernel(s) not found! " +
                           $"kInit={kInit} kUpdate={kUpdate} kIFFTH={kIFFTH} kIFFTV={kIFFTV} kOut={kOut}. " +
                           "Check the compute shader compiled without errors in the Console.");
            if (autoDisableIfMissing) enabled = false;
            return;
        }

        // Allocate buffers
        int n2 = N * N;
        magBuf  = new ComputeBuffer(n2, sizeof(float), ComputeBufferType.Default);
        phi0Buf = new ComputeBuffer(n2, sizeof(float), ComputeBufferType.Default);
        pBuf    = new ComputeBuffer(n2, sizeof(float) * 2, ComputeBufferType.Default);
        specBuf = new ComputeBuffer(n2, sizeof(float) * 2, ComputeBufferType.Default);
        tempBuf = new ComputeBuffer(n2, sizeof(float) * 2, ComputeBufferType.Default);

        // Precompute magnitude + original phase (one-time CPU cost)
        PrecomputeSpectrumCPU(sourceGrayscale, magBuf, phi0Buf);

        // Bind buffers to all kernels
        cs.SetBuffer(kInit, "P", pBuf);

        cs.SetBuffer(kUpdate, "P", pBuf);
        cs.SetBuffer(kUpdate, "Spectrum", specBuf);
        cs.SetBuffer(kUpdate, "Mag", magBuf);
        cs.SetBuffer(kUpdate, "Phi0", phi0Buf);

        cs.SetBuffer(kIFFTH, "Spectrum", specBuf);
        cs.SetBuffer(kIFFTH, "Temp", tempBuf);

        cs.SetBuffer(kIFFTV, "Temp", tempBuf);
        cs.SetBuffer(kIFFTV, "Spectrum", specBuf);

        cs.SetTexture(kOut, "OutTex", outTex);
        cs.SetBuffer(kOut, "Spectrum", specBuf);

        // Initialize random phasor field
        cs.SetInt("Seed", (int)seed);
        cs.Dispatch(kInit, N / 8, N / 8, 1);

        dx = 0f;
        dy = 0f;
        frameIndex = 0;
        accum = 0f;
        dtStim = 1.0f / Mathf.Max(fpsStimulus, 1e-3f);

        Debug.Log($"[PhaseScrambleStimulus] Initialized: {N}x{N}, stimulus @ {fpsStimulus} Hz");
    }

    void Update()
    {
        if (!initialized) return;

        // Update stimulus at independent rate (decoupled from headset refresh)
        accum += Time.deltaTime;
        while (accum >= dtStim)
        {
            accum -= dtStim;
            StepStimulus(dtStim);
        }
    }

    void StepStimulus(float dt)
    {
        // Accumulate translation (wraps implicitly when Dx/Dy are fed to phase ramp)
        dx += vxPixelsPerSec * dt;
        dy += vyPixelsPerSec * dt;

        // Set constant buffer parameters
        cs.SetFloat("Scramble", Mathf.Clamp01(scramble));
        cs.SetFloat("Dx", dx);
        cs.SetFloat("Dy", dy);
        cs.SetFloat("FPS", fpsStimulus);
        cs.SetFloat("Tau0", tau0);
        cs.SetFloat("R0", r0);
        cs.SetFloat("Gamma", gamma);
        cs.SetInt("Seed", (int)seed);
        cs.SetInt("FrameIndex", (int)frameIndex++);

        // 1) Update spectrum with phase scrambling and evolution
        cs.Dispatch(kUpdate, N / 8, N / 8, 1);

        // 2) iFFT rows
        cs.Dispatch(kIFFTH, 1, N, 1);

        // 3) iFFT columns
        cs.Dispatch(kIFFTV, 1, N, 1);

        // 4) Output real part to texture
        cs.Dispatch(kOut, N / 8, N / 8, 1);
    }

    void OnDestroy()
    {
        magBuf?.Release();
        phi0Buf?.Release();
        pBuf?.Release();
        specBuf?.Release();
        tempBuf?.Release();
        if (outTex != null) outTex.Release();
    }

    /// <summary>
    /// Precompute magnitude and phase spectrum from source image (one-time cost).
    /// For large images or production, consider pre-baking these assets offline in Python.
    /// </summary>
    void PrecomputeSpectrumCPU(Texture2D tex, ComputeBuffer mag, ComputeBuffer phi0)
    {
        // Extract pixels
        float[] img = new float[N * N];
        Color[] px = tex.GetPixels();
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                img[y * N + x] = px[y * N + x].r; // grayscale from R channel

        // Compute 2D FFT
        System.Numerics.Complex[] F = FFT2D.ForwardRealToComplex(img, N);

        // Extract magnitude and phase
        float[] A = new float[N * N];
        float[] P0 = new float[N * N];

        for (int i = 0; i < N * N; i++)
        {
            double re = F[i].Real;
            double im = F[i].Imaginary;
            A[i] = (float)System.Math.Sqrt(re * re + im * im);
            P0[i] = (float)System.Math.Atan2(im, re);
        }

        mag.SetData(A);
        phi0.SetData(P0);

        Debug.Log("[PhaseScrambleStimulus] Precomputed source spectrum");
    }

    // --- Inspector utilities ---
    public RenderTexture GetOutputTexture() => outTex;

    public void SetScramble(float value)
    {
        scramble = Mathf.Clamp01(value);
    }

    public void SetMotion(float vx, float vy)
    {
        vxPixelsPerSec = vx;
        vyPixelsPerSec = vy;
    }
}
