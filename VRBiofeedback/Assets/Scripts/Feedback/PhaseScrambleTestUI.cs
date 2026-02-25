using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PhaseScrambleTestUI
/// 
/// Optional UI controller for real-time parameter tuning in play mode.
/// Attach to a Canvas to enable interactive control of stimulus parameters.
/// Useful for psychophysics experiments and parameter exploration.
/// </summary>
public class PhaseScrambleTestUI : MonoBehaviour
{
    public PhaseScrambleStimulus stimulus;
    public Canvas canvas;

    [Header("UI Sliders (auto-created if canvas provided)")]
    public Slider sliderScramble;
    public Slider sliderVx;
    public Slider sliderVy;
    public Slider sliderTau0;
    public Slider sliderR0;
    public Slider sliderGamma;
    public Slider sliderFPS;

    [Header("Display")]
    public Text textStatus;

    void Start()
    {
        if (stimulus == null)
            stimulus = FindObjectOfType<PhaseScrambleStimulus>();

        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();

        if (stimulus == null || canvas == null)
        {
            Debug.LogWarning("[PhaseScrambleTestUI] Missing stimulus or canvas.");
            enabled = false;
            return;
        }

        // Optional: auto-create sliders if not assigned
        if (sliderScramble == null)
            CreateUISliders();

        // Bind listeners
        sliderScramble.onValueChanged.AddListener(v => stimulus.SetScramble(v));
        sliderVx.onValueChanged.AddListener(v => stimulus.vxPixelsPerSec = v);
        sliderVy.onValueChanged.AddListener(v => stimulus.vyPixelsPerSec = v);
        sliderTau0.onValueChanged.AddListener(v => stimulus.tau0 = v);
        sliderR0.onValueChanged.AddListener(v => stimulus.r0 = v);
        sliderGamma.onValueChanged.AddListener(v => stimulus.gamma = v);
        sliderFPS.onValueChanged.AddListener(v => stimulus.fpsStimulus = v);

        // Set initial values
        sliderScramble.value = stimulus.scramble;
        sliderVx.value = stimulus.vxPixelsPerSec;
        sliderVy.value = stimulus.vyPixelsPerSec;
        sliderTau0.value = stimulus.tau0;
        sliderR0.value = stimulus.r0;
        sliderGamma.value = stimulus.gamma;
        sliderFPS.value = stimulus.fpsStimulus;
    }

    void Update()
    {
        if (textStatus != null)
        {
            textStatus.text = $"Scramble: {stimulus.scramble:F2} | " +
                              $"Motion: ({stimulus.vxPixelsPerSec:F1}, {stimulus.vyPixelsPerSec:F1}) px/s | " +
                              $"Tau0: {stimulus.tau0:F2}s | " +
                              $"R0: {stimulus.r0:F4} | " +
                              $"Gamma: {stimulus.gamma:F2} | " +
                              $"FPS: {stimulus.fpsStimulus:F0}";
        }
    }

    void CreateUISliders()
    {
        Debug.LogWarning("[PhaseScrambleTestUI] Auto-creating UI (not implemented; assign sliders manually).");
    }
}
