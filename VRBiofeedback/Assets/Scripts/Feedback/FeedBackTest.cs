using UnityEngine;

public class FeedbackTest : MonoBehaviour
{
    public Material noise;
    public Material image;
    [Range(0f, 1f)] public float feedback;

    private MeshRenderer rend;
    private Material runtimeMat;

    void Start()
    {
        rend = GetComponent<MeshRenderer>();
        // Start with the noise material as base
        runtimeMat = new Material(noise);
        rend.material = runtimeMat;
    }

    void Update()
    {
        // Copy properties from noise material
        runtimeMat.CopyPropertiesFromMaterial(noise);
        
        // Then interpolate specific properties from image material
        // For color
        Color targetColor = Color.Lerp(noise.color, image.color, feedback);
        runtimeMat.color = targetColor;
        
        // For main texture - we need to handle this differently
        // We'll just switch based on threshold
        if (feedback > 0.5f)
        {
            runtimeMat.mainTexture = image.mainTexture;
        }
        else
        {
            runtimeMat.mainTexture = noise.mainTexture;
        }
        
        // If you have other properties, interpolate them here
        // For example:
        // float someValue = Mathf.Lerp(noise.GetFloat("_Property"), image.GetFloat("_Property"), feedback);
        // runtimeMat.SetFloat("_Property", someValue);
    }
}