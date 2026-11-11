using UnityEngine;

// Turns the lighthouse's beam round the tower in play mode: this sits on the beam's pivot in
// the lantern (lightHouseBigLight/Beam), which holds the cone of light (Orca/Lighthouse Beam)
// and the spot light that sweeps with it. LighthouseLights (Tools > Orca > Light Lighthouse
// Island) builds them. Game time, so it stops while the game is paused.
public sealed class LighthouseBeam : MonoBehaviour
{
    // Once round every twelve seconds.
    [SerializeField] private float degreesPerSecond = 30f;

    public float DegreesPerSecond => degreesPerSecond;

    private void Update()
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
