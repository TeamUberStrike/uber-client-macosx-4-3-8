using System.Collections.Generic;

// Runtime zoom override for sniper-class weapons.
//
// The per-weapon _zoomInformation authored on the weapon prefabs does NOT survive to runtime — the
// weapon configuration is rebuilt/clobbered when the shop/loadout item is assembled (the same config-
// copy pattern documented for skins: the bootstrapper copies a base config over the prefab's authored
// scope/zoom/tracer fields). So editing the prefab data has no effect in-game; the fix is a runtime
// override applied at the point WeaponSlot builds the input handler.
//
// Values are the 4.7.1 catalog multipliers (min / default / max), matched to our 4.3.8 weapon IDs by
// PrefabName. Scoped FOV = 60 / CurrentMultiplier; the mouse wheel steps CurrentMultiplier within
// [Min, Max]. ZoomInfo's constructor order is (default, min, max).
public static class SniperZoomOverride
{
    private static readonly Dictionary<int, ZoomInfo> _byId = new Dictionary<int, ZoomInfo>
    {
        { 1004, new ZoomInfo(2f, 2f, 4f) },   // Sniper Rifle (PaintSniper)
        { 1016, new ZoomInfo(2f, 2f, 4f) },   // Ordinator Rifle
        { 1017, new ZoomInfo(2f, 2f, 4f) },   // Deliverator
        { 1018, new ZoomInfo(2f, 2f, 8f) },   // Vanquisher
        { 1331, new ZoomInfo(2f, 2f, 8f) },   // Vanquisher Kongregate Edition (no 4.7.1 match; uses Vanquisher values)
        { 1147, new ZoomInfo(4f, 4f, 4f) },   // Particle Lance
        { 1239, new ZoomInfo(2f, 2f, 8f) },   // Nefarious Needler
        { 1244, new ZoomInfo(2f, 2f, 16f) },  // Dark Vanquisher
        { 1246, new ZoomInfo(2f, 2f, 8f) },   // Fusion Lance
        { 1300, new ZoomInfo(2f, 2f, 4f) },   // Snap Shot
        { 1301, new ZoomInfo(2f, 2f, 8f) },   // Vanquisher [Dragon]
        { 1316, new ZoomInfo(4f, 4f, 4f) },   // Particle Lance [Dragon]
        { 1355, new ZoomInfo(2f, 2f, 16f) },  // AWP
        { 1356, new ZoomInfo(2f, 2f, 16f) },  // AWP [Black]
        { 1357, new ZoomInfo(2f, 2f, 16f) },  // AWP [Camo]
        { 1358, new ZoomInfo(2f, 2f, 16f) },  // AWP [Tiger]
        { 9012, new ZoomInfo(2f, 2f, 4f) },   // Void Amethyst (sniper skin)
    };

    // Returns a FRESH ZoomInfo for the weapon (so CurrentMultiplier state doesn't bleed between
    // equips), or the config's own ZoomInformation when there is no override entry.
    public static ZoomInfo Resolve(WeaponItemConfiguration config)
    {
        if (config != null && _byId.TryGetValue(config.ID, out var template))
            return new ZoomInfo(template.DefaultMultiplier, template.MinMultiplier, template.MaxMultiplier);
        return config != null ? config.ZoomInformation : null;
    }
}
