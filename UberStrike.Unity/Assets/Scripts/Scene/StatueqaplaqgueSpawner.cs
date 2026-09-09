using UnityEngine;
using UnityEngine.SceneManagement;

// Spawns the "Statueqaplaqgue" plaque GameObject in the Temple of the Raven
// secret room at runtime. The original GameObject was present in UB6's
// TempleOfTheRaven.unity but didn't survive the port into our 2022 scene.
//
// Rather than hand-edit the scene YAML (which is risky and easy to corrupt),
// we spawn at runtime from the known UB6 transform (Pos/Rot/Scale recorded
// below) with a built-in Cube mesh + the ported Statuesplaque material loaded
// from Resources. Scale is authored thin so a cube reads as a flat plaque.
//
// If the position needs fine-tuning in-Editor, move the spawned "Statueqaplaqgue"
// GameObject once in Play Mode, copy its transform, update the constants below.
public class StatueqaplaqgueSpawner : MonoBehaviour
{
    private const string TempleSceneName = "LevelTempleOfTheRaven";
    private const string MaterialResource = "Statueqaplaqgue/Statuesplaque";

    // UB6 scene values + user's Inspector resize on 2026-04-20 (original UB6 scale
    // was way too small — user bumped it up until the plaque was readable).
    private static readonly Vector3 LocalPosition = new Vector3(0.023327f, -53.215496f, 2.240325f);
    private static readonly Quaternion LocalRotation = new Quaternion(0.6711292f, 0f, 0f, 0.7413404f);
    private static readonly Vector3 LocalScale = new Vector3(2.27561f, 0.23550664f, 1.52441f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Cover the case where Temple is the initially loaded scene.
        TrySpawnIn(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TrySpawnIn(scene);
    }

    private static void TrySpawnIn(Scene scene)
    {
        if (!scene.IsValid() || scene.name != TempleSceneName) return;

        // Idempotent: skip if already spawned (second scene-load, repeat init, etc.).
        var existing = GameObject.Find("Statueqaplaqgue");
        if (existing != null) return;

        var mat = Resources.Load<Material>(MaterialResource);
        if (mat == null)
        {
            Debug.LogWarning("[Statueqaplaqgue] Material not found at Resources/" + MaterialResource);
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Statueqaplaqgue";
        go.transform.localPosition = LocalPosition;
        go.transform.localRotation = LocalRotation;
        go.transform.localScale = LocalScale;

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Clone the material so our brightness/emission tweaks don't persist
            // into the asset on disk or leak into other spawns.
            var instance = new Material(mat);
            instance.hideFlags = HideFlags.DontSave;
            // Lift the plaque enough that the engraved names read, but keep the
            // aged-bronze/copper tint of the reference (not the overbright sand
            // yellow the first attempt produced). Slight emission in warm copper
            // hues so the plaque reads in the Temple's dim ambient lighting.
            if (instance.HasProperty("_Color"))
                instance.SetColor("_Color", new Color(1.10f, 1.00f, 0.92f, 1f));
            if (instance.HasProperty("_EmissionColor"))
            {
                instance.EnableKeyword("_EMISSION");
                // Warm copper glow, lower magnitude than before.
                instance.SetColor("_EmissionColor", new Color(0.18f, 0.12f, 0.08f));
                instance.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            renderer.sharedMaterial = instance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        // CreatePrimitive attaches a BoxCollider; leave it for wall-blocking
        // parity with the UB6 MeshCollider (the cube and scaled-mesh volumes
        // are equivalent here).

        // Re-home the plaque onto the Temple map root instead of leaving it in
        // this raw additive scene. MapConfiguration.Awake -> LevelManager.
        // AddLoadedMap already reparented the Temple's content out of this scene
        // and into 'Levels' (under the persistent 'Latest' scene) BEFORE this
        // sceneLoaded callback ran, so this scene is now an empty abandoned
        // container. Leaving the plaque here made it float in world space with no
        // owning map, so it stayed visible under EVERY other map the player then
        // joined (the reported Spaceport / Monkey-Island bleed). Parenting it
        // under the map root ties its lifetime + visibility to the Temple: it is
        // torn down with the map and respawned on re-entry. Pure transform
        // reparenting -- no material / shader / lightmap change, so BeastLighting
        // is untouched (the plaque is RealtimeEmissive, never in the baked array).
        Transform mapRoot = FindTempleMapRoot();
        if (mapRoot != null)
        {
            // worldPositionStays=true: preserve the exact world placement the
            // constants above were tuned to; we only change ownership.
            go.transform.SetParent(mapRoot, true);
        }
        else
        {
            Debug.LogWarning("[Statueqaplaqgue] Temple map root '" + TempleSceneName +
                "' not found; leaving plaque in scene '" + scene.name + "'.");
            SceneManager.MoveGameObjectToScene(go, scene);
        }
    }

    // Locate the Temple's reparented map root. Matches the MapConfiguration whose
    // GameObject name is the Temple scene name (map roots keep the scene name --
    // see BeastLightmapLoader.RestoreByName(gameObject.name)). The include-inactive
    // search resolves the root whether it is already active or still cached-
    // inactive, and naturally ignores every other loaded map.
    private static Transform FindTempleMapRoot()
    {
        var maps = Object.FindObjectsOfType<MapConfiguration>(true);
        foreach (var m in maps)
        {
            if (m != null && m.gameObject.name == TempleSceneName)
                return m.transform;
        }
        var byName = GameObject.Find(TempleSceneName);
        return byName != null ? byName.transform : null;
    }
}
