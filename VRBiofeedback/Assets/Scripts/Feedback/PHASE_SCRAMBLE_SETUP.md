# Phase-Scrambled Stimulus System for VR
## OpenXR-Compatible Implementation (Meta Quest, Vive, SteamVR, PCVR)

### Overview

This system generates phase-scrambled visual stimuli in real-time on Quest, Vive, or SteamVR headsets using Unity + OpenXR + Compute Shaders. It provides:

- **Single scramble parameter** $s \in [0,1]$ (0 = original image, 1 = fully phase-scrambled)
- **Continuous temporal evolution** with frequency-dependent "drift vs shimmer" (low freqs slow, high freqs fast)
- **Global translation/flow** via Fourier phase-ramp motion
- **Stable energy** — same magnitude spectrum every frame, preventing flicker

### Architecture

```
Source Image (256×256 grayscale)
        ↓
   CPU: FFT → Magnitude A(k) + Phase φ₀(k)
        ↓
   GPU Compute Shader each frame:
   ├─ UpdateSpectrum: 
   │  ├─ Evolve random phasors P(k,t) with AR(1) update
   │  ├─ Mix: φ_mix = arg((1−s)e^(iφ₀) + se^(iφ_rand))
   │  └─ Ramp: φ_ramp = −2π(f_x·Δx + f_y·Δy)
   │  └─ Output: S(k) = A(k) exp(i(φ_mix + φ_ramp))
   ├─ iFFT (rows, then columns in shared memory)
   └─ Output real part → RFloat texture
        ↓
   Display Shader (URP Unlit)
   ├─ Sample RFloat texture
   ├─ Remap to [0,1] (Brightness/Contrast/Offset props)
   └─ Render on head-locked quad
```

### Files Provided

| File | Role |
|------|------|
| `PhaseScramble.compute` | GPU compute shader (phase scrambling, iFFT, evolution) |
| `PhaseScrambleStimulus.cs` | Main controller (buffers, dispatch, params) |
| `FFT2D.cs` | CPU FFT helper (one-time precomputation) |
| `PhaseScrambleDisplay.shader` | URP Unlit shader for display (sampling + remap) |

### Quick Start

#### 1. **Create a 256×256 Grayscale Source Image**

- Export from Python (e.g., numpy) as PNG or use an existing test image
- **Important**: Save as 256×256, grayscale or RGB (we'll extract R channel)
- Place in `Assets/Audios/` or another readable folder

Example (Python):
```python
import numpy as np
from PIL import Image

# Create a simple 1/f noise or natural texture
img = np.random.randn(256, 256)
img = (img - img.min()) / (img.max() - img.min()) * 255
Image.fromarray(img.astype('uint8')).save('source.png')
```

#### 2. **Scene Setup**

**Hierarchy example:**
```
XR Origin (or your VR rig)
└─ Camera (Main Camera)
   └─ StimulusPlane (Quad)
      └ (MeshFilter: Quad, MeshRenderer, Collider)
```

**Create the Stimulus Plane:**
1. Create a new Quad: `Right-click → 3D Object → Quad`
2. Name it `StimulusPlane`
3. **Parent to camera** (so it moves/rotates with head):
   - Drag into Camera hierarchy in the Inspector
4. **Configure transform:**
   - Position: `(0, 0, 1.5)` (1.5m in front of eyes)
   - Rotation: `(0, 0, 0)` (face camera)
   - Scale: `(4, 4, 1)` (adjust for desired visual angle; ~60° at 1.5m)

#### 3. **Create Display Material**

1. Create a new Material: `Right-click in Project → Material`
2. Name it `Mat_PhaseScramble`
3. In Inspector, set Shader to `VR/PhaseScrambleDisplay`
4. Assign to the Quad's `MeshRenderer` → Material

#### 4. **Set Up PhaseScrambleStimulus Component**

1. **Add component to StimulusPlane:**
   - Inspector → `Add Component` → `PhaseScrambleStimulus`

2. **Assign references:**
   - **Source Grayscale**: Drag your 256×256 PNG into this field
   - **Target Material**: Drag `Mat_PhaseScramble`
   - **Compute Shader**: Drag `PhaseScramble.compute`

3. **Tune parameters** (Inspector UI):
   - **Scramble** [0, 1]: Start at 0 (original) or 1 (fully scrambled)
   - **Vx/Vy Pixels Per Sec**: Motion speed (e.g., 40, 0 for rightward drift)
   - **FPS Stimulus**: 60 (independent of headset refresh; can be less)
   - **Tau0**: 1.5s (phase evolution time constant)
   - **R0**: 0.02 (frequency scale; smaller = more low-freq emphasis)
   - **Gamma**: 1.0 (frequency exponent; >1 = flicker more at high freq)

#### 5. **Compile and Deploy**

**For Meta Quest 3:**
- Follow [Meta's OpenXR + Unity setup](https://developer.meta.com/docs/guides/unity/getting-started-with-vr)
- Ensure:
  - Player Settings → XR Plug-in Management → OpenXR enabled
  - Graphics API on Android set to Vulkan (Quest now recommends Vulkan)
  - Minimum API level 29+
- Build & deploy

**For Steam VR / Vive (PCVR):**
- OpenXR runtime enabled (Steam VR or native Vive runtime)
- Build as Windows Standalone with OpenXR
- Deploy

### Key Parameters Explained

#### **Scramble** [0, 1]
- **0**: Original phase preserved → stimulus is identical to source every frame
- **1**: Pure random phase (drifting) → no recognizable structure
- **0.5**: Blend between source + random → intermediate phase coherence

This is implemented as circular mixture:
$$\phi_{\text{mix}} = \arg\left((1-s) e^{i\phi_0} + s e^{i\phi_{\text{rand}}}\right)$$

#### **Tau0** (seconds)
- Base time constant for phase phasor evolution
- Larger τ → phase changes slowly (drift-like, slow shimmer)
- Smaller τ → phase changes rapidly (flickery)

#### **R0** (cycles/pixel)
- Frequency at which τ = Tau0
- Below R0: phase evolves slowly (low-freq drift)
- Above R0: phase evolves faster (high-freq shimmer)

#### **Gamma** (exponent)
- Controls frequency-dependence: $\tau(r) = \tau_0 \left( \frac{R_0}{r + R_0} \right)^{\gamma}$
- Gamma = 1.0: standard OU-like behavior
- Gamma > 1: emphasize high-frequency shimmer
- Gamma < 1: emphasize low-frequency drift

#### **Motion (Vx, Vy)**
- **Vx, Vy** in pixels/sec
- Applied as Fourier phase ramp: $\phi_{\text{ramp}} = -2\pi(f_x \Delta x + f_y \Delta y)$
- Creates illusion of global translation while keeping magnitude fixed

#### **FPS Stimulus**
- Stimulus update rate (Hz), independent of headset refresh
- On Quest @ 90 Hz, can set FPS = 60 or 30 for efficiency
- Compute shader only runs `ceil(HMD_Hz / Stimulus_FPS)` times per HMD frame

---

### Rendering: Display Shader Properties

Material `Mat_PhaseScramble` exposes:

| Property | Default | Purpose |
|----------|---------|---------|
| **Texture** | (none) | Auto-set by PhaseScrambleStimulus |
| **Brightness** | 1.0 | Global multiply factor |
| **Contrast** | 1.0 | Steepness: `(val - 0.5) × Contrast` |
| **Offset** | 0.5 | Mean value (centering) |
| **Clamp to [0,1]** | ✓ | Prevent out-of-range display |

**Typical remapping:**
```glsl
out = offset + brightness × contrast × (raw - 0.5)
// Default (offset=0.5, b=1, c=1):
out = 0.5 + 1 × 1 × (raw - 0.5) = raw  // passthrough
```

---

### Performance Considerations

#### Quest 3 Performance
- **Recommended**: 256×256 @ 60 Hz stimulus on Android Vulkan
- **Compute dispatch**:
  - `UpdateSpectrum`: 32×32 groups (N/8 = 256/8)
  - `IFFT_Horizontal`: 1×256 groups
  - `IFFT_Vertical`: 1×256 groups
  - `OutputReal`: 32×32 groups
  - Total: ~dominated by iFFT memory access (groupshared shared memory is fast)

#### Optimization Tips
1. **Decouple stimulus FPS from HMD**: Set `fpsStimulus=30` for half compute cost
2. **Pre-bake spectrum offline**: Offline Python FFT → serialize Mag + Phase0 as assets (avoids CPU FFT at startup)
3. **Larger quad**: Use a larger virtual quad (scale) rather than increasing texture resolution
4. **Reduce shader complexity**: Remove Contrast/Offset transforms if not needed; simplify in shader

#### PC (SteamVR/Desktop)
- Can handle 512×512 or even 1024×1024 if needed
- Compute shader is the same; just update `#define N` in shader + C# const

---

### Extending: Common Modifications

#### **A) Add Luminance-Only Scrambling (Color Images)**
To preserve color while scrambling brightness:

1. In CPU precompute: Convert RGB → YCbCr, FFT only Y channel
2. In compute shader: Keep Cr/Cb fixed, only update Y phase
3. In display shader: Convert YCbCr → RGB for output

#### **B) Motion Trails or Filtered Motion**
To smooth translations or add acceleration:

```csharp
// In PhaseScrambleStimulus.cs
float vx_smooth = Mathf.Lerp(vx_prev, vxPixelsPerSec, 0.2f); // low-pass
dx += vx_smooth * dt;
```

#### **C) Spatial Mask (e.g., Gaussian)**
To fade stimulus at edges (for cleaner appearance):

1. Pre-multiply magnitude spectrum: `A(k) *= GaussianEnvelope[k]`
2. or in display shader post-process

#### **D) Replace iFFT with Faster GPU FFT**
For 1024×1024+ sizes, use a dedicated GPU FFT library (e.g., VkFFT, cuFFT):

- Reimplement `UpdateSpectrum → Spectrum` using external lib
- Rest of pipeline unchanged

---

### Debugging

#### **Issue: Texture is black or all 0s**

1. **Verify source image**: Load in preview, confirm 256×256 grayscale
2. **Verify compute shader compiled**: Check console for compile errors
3. **Verify buffers created**: Add Debug.Log in `InitializeShaders()`
4. **Check Dispatch counts**: Ensure N=256 matches `#define N 256` in compute shader

#### **Issue: Texture is gray (all same value)**

1. **Check if DC (0,0) frequency is dominating**: If you see uniform gray, DC is too large
   - In `UpdateSpectrum`, verify DC special-case: `if (x==0 && y==0) Spectrum[i] = float2(Mag[i], 0);`
2. **Verify Phi0 precompute**: Print first few values of `P0[]` array in `PrecomputeSpectrumCPU`

#### **Issue: Gibbs ringing / artifacts at edges**

- Apply Hann or other window to source in CPU precompute
- Reduce contrast (lower _Contrast shader property)

#### **Issue: Phase not evolving (image frozen to source)**

1. Check `Scramble` slider value:
   - If 0, you'll see source + no random phase → frozen
   - Increase to >0.1 to see animation
2. Check `fpsStimulus` > 0 and AccumTime logic in `Update()`

#### **Issue: Poor performance on Quest**

1. Reduce resolution: Set `N=128` in compute + C#
2. Reduce stimulus FPS: Set `fpsStimulus=30` (even 20)
3. Enable single-pass stereo rendering (Meta's doc)
4. Profile with Meta XR Diagnostics tool

---

### Next Steps

#### **1. Parametric Psychophysics**
Sweep scramble [0,1], measure detection/discrimination threshold

#### **2. Adaptive Stimulus**
- Feed back reaction time / detection state
- Adjust scramble dynamically (e.g., 1-up-2-down staircircuit)

#### **3. Multi-Condition Experiments**
- Multiple quads, different stimuli in parallel
- Mosaic of small windows

#### **4. Offline Pre-Rendering**
- Python script to generate Mag + Phase0, save as Unity assets (.asset)
- Load directly, skip CPU FFT at startup

#### **5. Advanced Motion Clouds**
- If you want spatiotemporal spectral control (orientations, local motion):
  - Full Motion Clouds framework (Jose-Manuel Alonso et al.)
  - Spectral envelope in Fourier space controls local velocity

---

### Full Integration Checklist

- [ ] Create 256×256 grayscale PNG source image
- [ ] Create Quad in hierarchy, parent to camera
- [ ] Create `Mat_PhaseScramble` with `PhaseScrambleDisplay` shader
- [ ] Add `PhaseScrambleStimulus` component to Quad
- [ ] Assign references (source, material, compute shader)
- [ ] Test in editor (Play mode → adjust sliders)
- [ ] Build for Quest / SteamVR per platform docs
- [ ] Deploy and test on headset
- [ ] Collect data / tune parameters based on perception thresholds

---

### References

- **Fourier Phase Scrambling**: Dakin et al. (1999), Baker & Meese (2011)
- **Motion Clouds**: Sanz-Leon et al. (2012) — for spatiotemporal spectral control
- **Meta Quest + OpenXR**: https://developer.meta.com/docs/guides/unity/getting-started-with-vr
- **Unity URP**: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal/manual/index.html
- **Radix-2 FFT**: https://en.wikipedia.org/wiki/Cooley%E2%80%93Tukey_FFT_algorithm

