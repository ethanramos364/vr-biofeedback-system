using UnityEngine;

public class AudioFeedbackTest : MonoBehaviour
{
    public AudioSource noise;
    public AudioSource nature;
    [Range(0f, 1f)] public float feedback;
    
    void Start()
    {
        // Ensure both audio sources are playing
        if (!noise.isPlaying) noise.Play();
        if (!nature.isPlaying) nature.Play();
    }

    void Update()
    {
        // Equal power crossfade for smoother transition
        float angle = feedback * Mathf.PI * 0.5f;
        noise.volume = Mathf.Cos(angle);
        nature.volume = Mathf.Sin(angle);
    }
}