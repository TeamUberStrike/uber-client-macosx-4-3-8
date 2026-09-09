using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider))]
public class ForceField : MonoBehaviour
{
    #region Fields

    [SerializeField]
    private Vector3 _direction;

    [SerializeField]
    private int _force = 1000;

    private float gizmofactor = 0.0055f;

    // Set true to re-enable verbose [JumpPad] particle-decoration logging (fires on every jump-pad
    // map load); kept off in normal/shipped builds to avoid per-load log spam.
    private const bool VerboseJumpPad = false;

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        DecorateOrphanedJumpPads();
        SceneManager.sceneLoaded += OnSceneLoadedForParticles;
    }

    private static void OnSceneLoadedForParticles(Scene scene, LoadSceneMode mode)
    {
        // Only decorate JumpPads when a Level map scene loads (not lobby, not UI)
        if (!scene.name.StartsWith("Level") || scene.name == "LevelSpaceship")
            return;
        DecorateOrphanedJumpPads();
    }

    private void FitTriggerToChildMeshes(Collider collider)
    {
        var box = collider as BoxCollider;
        if (box == null) return;

        var renderers = GetComponentsInChildren<MeshRenderer>();
        if (renderers == null || renderers.Length == 0) return;

        // Aggregate world bounds of all child meshes.
        Bounds world = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

        // Retail behaviour: the pad launches you when you STAND ON it — the authored
        // trigger is a modest box at the pad's TOP (launch) surface, not a tall column.
        // The old port fitted the trigger to the full mesh + 2.5u upward + 1.25x wide,
        // so the player entered it on approach / while jumping past and got launched
        // prematurely ("immediate push on contact"). Anchor a thin box at the pad top
        // instead: reachBelow catches a grounded capsule's feet sitting on the surface,
        // reachAbove gives a little fast-approach tolerance without launching jumpers-over.
        const float reachAbove = 0.6f;
        const float reachBelow = 0.35f;
        float padTop = world.max.y;
        Vector3 worldMin = new Vector3(world.min.x, padTop - reachBelow, world.min.z);
        Vector3 worldMax = new Vector3(world.max.x, padTop + reachAbove, world.max.z);
        Vector3 fittedCenter = (worldMin + worldMax) * 0.5f;
        Vector3 fittedSize = worldMax - worldMin;

        // Convert to the BoxCollider's local space.
        Vector3 localCenter = transform.InverseTransformPoint(fittedCenter);
        Vector3 localSize = transform.InverseTransformVector(fittedSize);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

        // Footprint only (no over-widening) so the edges match the visible pad.
        if (localSize.y < (reachAbove + reachBelow)) localSize.y = reachAbove + reachBelow;

        // Only apply the fit when the existing collider is clearly TOO SMALL for
        // the visible pad — e.g., SPR pads ship with a default 1×1×1 BoxCollider.
        // Temple of the Raven (and other maps) author pad colliders deliberately
        // with specific center offsets, and overwriting those broke player-jump
        // triggering. The guard below runs the fit only when the existing XZ
        // footprint is noticeably smaller than the visible mesh.
        Vector3 current = box.size;
        bool tooSmall = current.x < localSize.x * 0.75f || current.z < localSize.z * 0.75f;
        if (!tooSmall) return;

        box.size = new Vector3(
            Mathf.Max(current.x, localSize.x),
            localSize.y,   // force the thin surface height; never keep the oversized authored Y (a tall column launches you on approach / at ground level near the pad)
            Mathf.Max(current.z, localSize.z));
        box.center = localCenter;
    }

    private static void DecorateOrphanedJumpPads()
    {
        // Find disabled ForceField instances and spawn particles at their positions.
        // On TempleOfTheRaven, the ForceField prefab instances start disabled but get
        // enabled by MapConfiguration.SetEnabled(). We parent the particles to the
        // ForceField so Awake() detects them via GetComponentInChildren<ParticleSystem>()
        // and skips creating duplicates.
        foreach (var ff in Resources.FindObjectsOfTypeAll<ForceField>())
        {
            if (ff == null) continue;
            if (ff.gameObject == null) continue;
            if (ff.gameObject.activeSelf)
                continue;
            if (!ff.gameObject.scene.IsValid())
                continue;
            if (ff.gameObject.name.IndexOf("accel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            // Skip if already decorated
            if (ff.GetComponentInChildren<ParticleSystem>() != null)
                continue;

            var anchor = new GameObject("JumpPadParticles");
            // `new GameObject` places the anchor in the ACTIVE scene, which may
            // not be the ForceField's own scene (additive scene loads / lobby
            // staying loaded while a level loads). Move it so per-scene logic
            // downstream sees the right scene name.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(anchor, ff.gameObject.scene);
            anchor.transform.SetParent(ff.transform, false);
            anchor.transform.position = ff.transform.position;
            SpawnJumpPadParticlesOn(anchor.transform);
        }
    }

    private void Awake()
    {
        var collider = GetComponent<Collider>();
        collider.isTrigger = true;
        gameObject.layer = (int)UberstrikeLayer.IgnoreRaycast;

        // Port artifact: some ForceField BoxColliders shipped at 1×1×1 local while
        // the pad's visible mesh (a child) spans much wider → players can walk onto
        // the pad edge without entering the trigger, and only jump when they reach
        // the dead center. Fit the collider to the combined child-mesh bounds so the
        // trigger covers the full pad surface. Non-Box colliders (MeshCollider etc.)
        // are left alone.
        FitTriggerToChildMeshes(collider);

        // Only spawn on JumpPads, not Accelerator pads (accel, AcceleratorPad, etc.)
        if (gameObject.name.IndexOf("accel", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (VerboseJumpPad) Debug.Log("[JumpPad/" + gameObject.name + "] Skipped: name contains 'accel'. Scene=" + gameObject.scene.name);
            return;
        }

        // Skip if this object already has working particle systems
        var existing = GetComponentInChildren<ParticleSystem>();
        if (existing != null)
        {
            if (VerboseJumpPad) Debug.Log("[JumpPad/" + gameObject.name + "] Skipped: already has ParticleSystem '" + existing.name + "'. Scene=" + gameObject.scene.name);
            return;
        }

        if (VerboseJumpPad) Debug.Log("[JumpPad/" + gameObject.name + "] Spawning particles. Scene=" + gameObject.scene.name);
        // Spawn particles directly on the ForceField's transform. On flattened
        // (ForgeRipper-migrated) scenes the mesh child may sit at world origin
        // while the ForceField parent is at the real pad position, so using
        // the child would put particles at (0,0,0). On non-flattened scenes
        // (LP2/CS) child and parent share the same world pos anyway.
        SpawnJumpPadParticlesOn(transform);
    }

    // Walks up the transform chain until we find a GameObject whose Scene is
    // valid + has a non-empty name. Handles DecorateOrphanedJumpPads' case
    // where the direct parent is a freshly-created anchor in the active scene,
    // not the ForceField's scene.
    private static string ResolveOwningSceneName(Transform parent, out string sceneNameForLog)
    {
        sceneNameForLog = "(none)";
        if (parent == null) return string.Empty;
        var t = parent;
        while (t != null)
        {
            var s = t.gameObject.scene;
            if (s.IsValid() && !string.IsNullOrEmpty(s.name))
            {
                sceneNameForLog = s.name;
                if (s.name.StartsWith("Level")) return s.name;
            }
            t = t.parent;
        }
        // Fallback to top of chain if no "Level*" scene found upward.
        return sceneNameForLog == "(none)" ? string.Empty : sceneNameForLog;
    }

    // Walks up the transform chain to find the nearest MapConfiguration
    // ancestor, returning its GameObject.name (e.g. "SuperPRISMReactor" in the
    // SPR scene, "LevelTempleOfTheRaven" in the Temple scene — the naming is
    // inconsistent across maps, hence the substring-based matching at the
    // call site). This is the authoritative map-identity signal: each pad is
    // authored as a descendant of exactly one MapConfiguration, and that
    // coupling is stable across scene-load ordering, additive loads, and
    // runtime scene-reparenting. Immune to the cross-map scan leak that
    // happens when a previous map isn't fully unloaded before the next loads.
    private static string FindOwningMapName(Transform t)
    {
        for (var cur = t; cur != null; cur = cur.parent)
        {
            if (cur.gameObject.GetComponent<MapConfiguration>() != null)
                return cur.gameObject.name;
        }
        return null;
    }

    public static void SpawnJumpPadParticlesOn(Transform parent, float scale = 1f)
    {
        // Team material: JumpPad_beta → yellow variant; anything else → default (blue).
        // Color comes from the material's baked _TintColor, so no startColor tint
        // is needed (startColor would multiply with _TintColor and mute the hue).
        // `scale` shrinks orb/sparkle sizes + emission radius for small carriers
        // like SpringGrenade; velocities + rates stay the same so motion reads right.
        // Reactor variant: SPR swaps the generic ring sprite for a reactor-core
        // silhouette (same texture as the armor_b/armor_y pickup decals). Same
        // shader + tint, so motion/diffusion stay identical — only the sprite changes.
        bool yellow = parent != null && parent.name != null
            && parent.name.IndexOf("beta", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // Variant selection keys off the pad's owning MapConfiguration, walked
        // up the transform chain. Previous attempts used padScene or a scan
        // of loaded scenes — both leaked across maps because (a) pads can be
        // authored in either a Level* scene OR Latest.unity (runtime host),
        // and (b) the game doesn't reliably unload the previous map scene
        // when the next one loads, so `scanSawReactor && scanSawTemple` can
        // both be true.
        //
        // MapConfiguration is authoritative: each pad is a descendant of
        // exactly one MapConfiguration (the map it belongs to). Map identity
        // via ancestor walk is immune to scene-load ordering, additive loads,
        // and runtime scene-reparenting.
        //
        // Naming quirk: the MapConfiguration GameObject isn't named
        // consistently — SPR's is "SuperPRISMReactor", Temple's is
        // "LevelTempleOfTheRaven". Use substring match on the discriminating
        // substring for robustness, not equality on the full name.
        string ownerMap = FindOwningMapName(parent);
        bool reactor     = ownerMap != null && ownerMap.IndexOf("SuperPRISMReactor", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool templeGreen = ownerMap != null && ownerMap.IndexOf("TempleOfTheRaven", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // Diagnostic fields kept for the Debug.Log below.
        string padScene = "";
        if (parent != null)
        {
            var padOwnScene = parent.gameObject.scene;
            if (padOwnScene.IsValid() && !string.IsNullOrEmpty(padOwnScene.name))
                padScene = padOwnScene.name;
        }
        string sceneNameForLog = padScene;
        if (string.IsNullOrEmpty(padScene))
            padScene = ResolveOwningSceneName(parent, out sceneNameForLog);

        string matName;
        if (reactor)          matName = yellow ? "JumpPadParticlesReactorYellow" : "JumpPadParticlesReactorBlue";
        else if (templeGreen) matName = "JumpPadParticlesGreen";
        else                  matName = yellow ? "JumpPadParticlesYellow" : "JumpPadParticles";
        Material jumpPadMat = Resources.Load<Material>(matName);
        if (VerboseJumpPad) Debug.Log("[JumpPad] parent=" + (parent != null ? parent.name : "null") + " padScene=" + padScene + " ownerMap=" + (ownerMap ?? "(none)") + " yellow=" + yellow + " reactor=" + reactor + " templeGreen=" + templeGreen + " matName=" + matName + " loaded=" + (jumpPadMat != null));
        if (jumpPadMat == null)
            Debug.LogWarning("[JumpPad] Resources.Load failed for '" + matName + "' — falling back to default.");
        Color tint = Color.white;

        // ---- Particle System 1: Big glowing orbs (original "Particle System" child) ----
        var glowGO = new GameObject("Particle System");
        glowGO.transform.SetParent(parent, false);
        glowGO.transform.localPosition = new Vector3(0f, 0.121f, 0f);

        var glowPS = glowGO.AddComponent<ParticleSystem>();
        var glowMain = glowPS.main;
        glowMain.loop = true;
        glowMain.prewarm = true;
        glowMain.playOnAwake = true;
        glowMain.startLifetime = 1f;
        glowMain.startSpeed = 0f;
        glowMain.startSize = 7f * scale;
        glowMain.startColor = tint;
        glowMain.maxParticles = 20;
        glowMain.simulationSpace = ParticleSystemSimulationSpace.World;
        glowMain.gravityModifier = 0f;

        var glowEmission = glowPS.emission;
        glowEmission.enabled = true;
        glowEmission.rateOverTime = 4f;

        var glowShape = glowPS.shape;
        glowShape.enabled = false;

        // Temple pads: drop a soft point light at the pad base so the green cast
        // bleeds onto surrounding walls (matches Steam/UB6 reference look). Skip
        // for other variants so we don't regress their established appearance.
        if (templeGreen)
        {
            var lightGO = new GameObject("JumpPadGlow");
            lightGO.transform.SetParent(parent, false);
            lightGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var glowLight = lightGO.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(0.25f, 0.9f, 0.35f);
            glowLight.range = 6f;
            glowLight.intensity = 1.6f;
            glowLight.shadows = LightShadows.None;
            glowLight.renderMode = LightRenderMode.Auto;
        }

        // Original 3.5.5 worldVelocity = (0, 2.5, 0) — always straight up in world space
        var glowVelocity = glowPS.velocityOverLifetime;
        glowVelocity.enabled = true;
        glowVelocity.space = ParticleSystemSimulationSpace.World;
        glowVelocity.x = 0f;
        glowVelocity.y = 2.5f;
        glowVelocity.z = 0f;

        // Color animation: alpha fades in then out
        var glowColor = glowPS.colorOverLifetime;
        glowColor.enabled = true;
        var glowGradient = new Gradient();
        glowGradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(tint, 0f),
                new GradientColorKey(tint, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.04f, 0f),
                new GradientAlphaKey(0.71f, 0.25f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0.71f, 0.75f),
                new GradientAlphaKey(0.04f, 1f)
            }
        );
        glowColor.color = glowGradient;

        // Renderer: Horizontal Billboard (original StretchParticles=4) — flat, facing ceiling
        var glowRenderer = glowGO.GetComponent<ParticleSystemRenderer>();
        glowRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        glowRenderer.maxParticleSize = 1f;
        if (jumpPadMat != null)
            glowRenderer.material = jumpPadMat;

        // ---- Particle System 2: Small sparkle dots (original "F_JP_particle" child) ----
        var sparkleGO = new GameObject("F_JP_particle");
        sparkleGO.transform.SetParent(parent, false);
        sparkleGO.transform.localPosition = Vector3.zero;

        var sparklePS = sparkleGO.AddComponent<ParticleSystem>();
        var sparkleMain = sparklePS.main;
        sparkleMain.loop = true;
        sparkleMain.prewarm = true;
        sparkleMain.playOnAwake = true;
        sparkleMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        sparkleMain.startSpeed = 0f;
        sparkleMain.startSize = 0.1f * scale;
        sparkleMain.startColor = tint;
        sparkleMain.maxParticles = 50;
        sparkleMain.simulationSpace = ParticleSystemSimulationSpace.World;
        sparkleMain.gravityModifier = 0f;

        var sparkleEmission = sparklePS.emission;
        sparkleEmission.enabled = true;
        sparkleEmission.rateOverTime = 20f;

        var sparkleShape = sparklePS.shape;
        sparkleShape.enabled = true;
        sparkleShape.shapeType = ParticleSystemShapeType.Sphere;
        sparkleShape.radius = 1f * scale;
        sparkleShape.randomDirectionAmount = 0f;

        // Original 3.5.5 worldVelocity = (0, 4, 0) — always straight up in world space
        var sparkleVelocity = sparklePS.velocityOverLifetime;
        sparkleVelocity.enabled = true;
        sparkleVelocity.space = ParticleSystemSimulationSpace.World;
        sparkleVelocity.x = 0f;
        sparkleVelocity.y = 4f;
        sparkleVelocity.z = 0f;

        var sparkleColor = sparklePS.colorOverLifetime;
        sparkleColor.enabled = true;
        sparkleColor.color = glowGradient;

        var sparkleRenderer = sparkleGO.GetComponent<ParticleSystemRenderer>();
        sparkleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        sparkleRenderer.maxParticleSize = 0.25f;
        if (jumpPadMat != null)
            sparkleRenderer.material = jumpPadMat;
    }

    private void OnTriggerEnter(Collider collider)
    {
        if (collider.tag == "Player")
        {
            GameState.LocalPlayer.MoveController.ApplyForce(_direction.normalized * _force, CharacterMoveController.ForceType.Exclusive);

            SfxManager.Play2dAudioClip(SoundEffectType.PropsJumpPad2D);
        }
        else if (collider.gameObject.layer == (int)UberstrikeLayer.RemotePlayer)
        {
            SfxManager.Play3dAudioClip(SoundEffectType.PropsJumpPad, 1.0f, 0.1f, 10.0f, AudioRolloffMode.Linear, transform.position);
        }

        //else if (collider.tag == "Prop")
        //{
        //    BaseGameProp prop = collider.GetComponent<BaseGameProp>();
        //    if (prop != null)
        //    {
        //        prop.ApplyForce(Vector3.zero, _direction.normalized * _force * 2);
        //    }
        //}
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawSphere(transform.localPosition, 0.2f);
        Vector3 v = _direction.normalized;
        v.y *= 0.6f;
        Gizmos.DrawLine(transform.localPosition, transform.localPosition + v * Mathf.Log(_force) * _force * gizmofactor);//);
    }
}
