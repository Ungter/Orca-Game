using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Puts a LampGlow light in the head (pCube1) of every lamp, lamp1, lamp2... in the island
// map when it imports, so the lights live in the model and follow it into any scene.
public class IslandLampLights : AssetPostprocessor
{
    private const string MapPath = "Assets/IslanddemoV3.fbx";
    private static readonly Regex LampName = new Regex(@"^lamp\d*$");

    public override uint GetVersion()
    {
        // Bump when the lamp setup or LampGlow defaults change, so the map reimports.
        return 2;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!string.Equals(assetPath, MapPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int added = 0;
        foreach (Transform lamp in root.GetComponentsInChildren<Transform>(true))
        {
            if (!LampName.IsMatch(lamp.name))
            {
                continue;
            }
            foreach (Transform head in lamp)
            {
                // Some lamps keep their Maya namespace, e.g. "maplamp:pCube1".
                if (!head.name.EndsWith("pCube1", StringComparison.Ordinal))
                {
                    continue;
                }
                var glow = new GameObject("Lamp Light");
                glow.transform.SetParent(head, false);
                MeshFilter mesh = head.GetComponent<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null)
                {
                    glow.transform.localPosition = mesh.sharedMesh.bounds.center;
                }
                glow.AddComponent<Light>();
                glow.AddComponent<LampGlow>();
                added++;
            }
        }
        if (added == 0)
        {
            Debug.LogWarning("[IslandLampLights] No lampN/pCube1 found in " + MapPath);
        }
    }
}
