using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using System.Linq;
#endif

/// <summary>
/// Automatically restores orphaned Beast lightmaps when map scenes load.
///
/// Unity 2022 clears renderer lightmapIndex values when m_LightingDataAsset is null
/// (which it is for all migrated scenes). This script:
/// 1. Loads the original Beast LightmapFar-*.exr textures directly into LightmapSettings
///    (no pixel manipulation — the imported RGBM texture is decoded correctly by the shader)
/// 2. Re-assigns lightmapIndex and lightmapScaleOffset to each renderer
/// 3. Disables directional lights to prevent double-lighting of static objects
///    (original Unity 3.5.5 used "Single Lightmaps" where baked lights were excluded
///    from runtime rendering — Unity 2022 has no equivalent, so lights must be disabled)
/// 4. Restores original ambient intensity per map
///
/// All renderer data recovered from git commit d11b013b (original Unity 3.5.5 project).
/// 796 name-based + 114 position-based entries across 13 maps (2 skipped).
/// </summary>
public static class BeastLightmapLoader
{
    // =====================================================================
    // TUNING PARAMETERS
    // =====================================================================

    const float PositionMatchThreshold = 0.5f;
    const int AssignDelayFrames = 15;

    // =====================================================================
    // STRUCTS
    // =====================================================================

    struct RendererLightmapInfo
    {
        public int lightmapIndex;
        public Vector4 scaleOffset;

        public RendererLightmapInfo(int index, float sx, float sy, float ox, float oy)
        {
            lightmapIndex = index;
            scaleOffset = new Vector4(sx, sy, ox, oy);
        }
    }

    struct PositionLightmapInfo
    {
        public Vector3 position;
        public int lightmapIndex;
        public Vector4 scaleOffset;

        public PositionLightmapInfo(Vector3 pos, int index, Vector4 so)
        {
            position = pos;
            lightmapIndex = index;
            scaleOffset = so;
        }
    }

    // =====================================================================
    // NAME-BASED LIGHTMAP DATA (recovered from git d11b013b)
    // Unique renderer names matched by GameObject.name at runtime.
    // Duplicate-name entries with identical data are deduplicated.
    // =====================================================================

    // GideonsTower: 144 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> GideonsTowerLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "AirconModels0", new RendererLightmapInfo(5, 0.554687977f, 0.554687977f, 0.939307988f, -0.255854994f) },
        { "Glass3", new RendererLightmapInfo(5, 0.786132991f, 0.786132991f, 0.975566983f, -0.758723021f) },
        { "Glass2", new RendererLightmapInfo(5, 0.786132991f, 0.786132991f, 0.937494993f, -0.636897027f) },
        { "Glass1", new RendererLightmapInfo(5, 0.786132991f, 0.786132991f, 0.93284899f, -0.682115972f) },
        { "Windows_Frame_Red3", new RendererLightmapInfo(5, 0.457031012f, 0.457031012f, 0.980913997f, -0.381365001f) },
        { "Windows_Frame_Red2", new RendererLightmapInfo(5, 0.457031012f, 0.457031012f, 0.82485503f, 0.306742996f) },
        { "Windows_Frame_Red1", new RendererLightmapInfo(5, 0.457031012f, 0.457031012f, 0.878144026f, -0.0497566015f) },
        { "StoneDark3", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.193985999f, -0.345429003f) },
        { "StoneDark2 1", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.00601811009f, -0.242656007f) },
        { "StoneDark1 1", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, 0.317481011f, 0.0115860999f) },
        { "StoneBright3", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.436672002f, -0.270610005f) },
        { "StoneBright2", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.800603986f, 0.00677412981f) },
        { "StoneBright1", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.599729002f, -0.626212001f) },
        { "Exit", new RendererLightmapInfo(5, 0.352539003f, 0.352539003f, 0.971068978f, 0.536154985f) },
        { "Brick", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.0218602009f, -0.391624004f) },
        { "Wall", new RendererLightmapInfo(4, 0.612304986f, 0.612304986f, 0.666402996f, 0.397026002f) },
        { "Flooring", new RendererLightmapInfo(3, 0.99902302f, 0.99902302f, -0.0159296002f, 0.0128624002f) },
        { "Net", new RendererLightmapInfo(3, 0.366210997f, 0.366210997f, 0.213170007f, 0.187094003f) },
        { "NetFrame", new RendererLightmapInfo(3, 0.143555f, 0.143555f, 0.325946003f, 0.407023013f) },
        { "Ladders", new RendererLightmapInfo(4, 0.12207f, 0.12207f, 0.978034019f, -0.0784742981f) },
        { "Ropes", new RendererLightmapInfo(4, 0.0390625f, 0.0390625f, 0.824384987f, -0.0311884992f) },
        { "WallRoof", new RendererLightmapInfo(4, 0.724609017f, 0.724609017f, 0.605019987f, -0.117395997f) },
        { "Roof", new RendererLightmapInfo(3, 0.565429986f, 0.565429986f, 0.623659015f, 0.445845008f) },
        { "Glass", new RendererLightmapInfo(1, 0.410156012f, 0.410156012f, 0.260212004f, -0.103325002f) },
        { "DoorFrames", new RendererLightmapInfo(4, 0.177734002f, 0.177734002f, 0.91634202f, 0.822345018f) },
        { "WindowFrames3", new RendererLightmapInfo(6, 0.361328006f, 0.361328006f, -0.00754070003f, 0.642237008f) },
        { "WindowFrames1", new RendererLightmapInfo(1, 0.294921994f, 0.294921994f, -0.0031983701f, -0.146004006f) },
        { "StoneDark", new RendererLightmapInfo(6, 0.833007991f, 0.833007991f, 0.474160999f, -0.346902013f) },
        { "StoneDark2", new RendererLightmapInfo(7, 1.32813001f, 1.32813001f, -0.0251451991f, -0.760697007f) },
        { "StoneDark1", new RendererLightmapInfo(3, 0.950195014f, 0.950195014f, 0.470726997f, -0.457195014f) },
        { "StoneBright", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0115604997f, 0.00918900967f) },
        { "Frame2", new RendererLightmapInfo(5, 0.154296994f, 0.154296994f, 0.941779017f, 0.0366671011f) },
        { "Frame1", new RendererLightmapInfo(5, 0.125f, 0.125f, 0.941443026f, 0.102348f) },
        { "Joints2", new RendererLightmapInfo(5, 0.0292968992f, 0.0292968992f, 0.991599977f, -0.0231015999f) },
        { "Joints1", new RendererLightmapInfo(5, 0.0351562984f, 0.0351562984f, 0.980660021f, 0.0228289999f) },
        { "Borardwalk2", new RendererLightmapInfo(5, 0.275391012f, 0.275391012f, 0.193969995f, 0.476220995f) },
        { "Boardwalk1", new RendererLightmapInfo(5, 0.238280997f, 0.238280997f, 0.345551014f, 0.486853004f) },
        { "Props1", new RendererLightmapInfo(3, 0.208008006f, 0.208008006f, 0.546652019f, 0.384043992f) },
        { "Props49", new RendererLightmapInfo(4, 0.251953006f, 0.251953006f, 0.539898992f, 0.501378f) },
        { "Props48", new RendererLightmapInfo(5, 0.446289003f, 0.446289003f, 0.618146002f, 0.557613015f) },
        { "Props47", new RendererLightmapInfo(4, 0.015625f, 0.015625f, 0.993498027f, 0.983648002f) },
        { "Props46", new RendererLightmapInfo(1, 0.0683593974f, 0.0683593974f, 0.138500005f, -0.0563690998f) },
        { "Props45", new RendererLightmapInfo(4, 0.0107421996f, 0.0107421996f, 0.997458994f, 0.988619983f) },
        { "Props44", new RendererLightmapInfo(1, 0.00878905971f, 0.00878905971f, 0.163497999f, -0.00713443989f) },
        { "Props43", new RendererLightmapInfo(4, 0.168945f, 0.168945f, 0.915588975f, 0.768409014f) },
        { "Props42", new RendererLightmapInfo(1, 0.00878905971f, 0.00878905971f, 0.160566002f, -0.00711067999f) },
        { "Props41", new RendererLightmapInfo(4, 0.00683593983f, 0.00683593983f, 0.824644983f, 0.00318978005f) },
        { "Props40", new RendererLightmapInfo(6, 0.370117009f, 0.370117009f, 0.413403004f, 0.630539f) },
        { "Props39", new RendererLightmapInfo(4, 0.128905997f, 0.128905997f, 0.916194022f, 0.677210987f) },
        { "Props38", new RendererLightmapInfo(3, 0.125976995f, 0.125976995f, 0.172091007f, 0.42464599f) },
        { "Props37", new RendererLightmapInfo(4, 0.125976995f, 0.125976995f, 0.916130006f, 0.742762983f) },
        { "Props36", new RendererLightmapInfo(1, 0.0429687984f, 0.0429687984f, 0.150426f, -0.0331153981f) },
        { "Props35", new RendererLightmapInfo(4, 0.125976995f, 0.125976995f, 0.853690028f, 0.665251017f) },
        { "Props34", new RendererLightmapInfo(4, 0.0673827976f, 0.0673827976f, 0.634805977f, 0.685042977f) },
        { "Props33", new RendererLightmapInfo(4, 0.0498046987f, 0.0498046987f, 0.979525983f, 0.00950128958f) },
        { "Props32", new RendererLightmapInfo(3, 0.078125f, 0.078125f, 0.955069005f, 0.523590982f) },
        { "Props31", new RendererLightmapInfo(5, 0.466796994f, 0.466796994f, 0.342272997f, 0.536508977f) },
        { "Props30", new RendererLightmapInfo(3, 0.0791015998f, 0.0791015998f, 0.95668602f, 0.561882973f) },
        { "Props29", new RendererLightmapInfo(4, 0.12207f, 0.12207f, 0.916271985f, 0.625325024f) },
        { "Glass3 1", new RendererLightmapInfo(5, 0.786132991f, 0.786132991f, 0.975566983f, -0.734309018f) },
        { "Glass2 1", new RendererLightmapInfo(5, 0.786132991f, 0.786132991f, 0.958002985f, -0.637872994f) },
        { "Glass1 1", new RendererLightmapInfo(3, 0.786132991f, 0.786132991f, 0.500231981f, -0.0717640966f) },
        { "Windows_Frame_Red3 1", new RendererLightmapInfo(5, 0.457031012f, 0.457031012f, 0.980913997f, -0.365740001f) },
        { "Windows_Frame_Red2 1", new RendererLightmapInfo(5, 0.457031012f, 0.457031012f, 0.955713987f, 0.291117996f) },
        { "Windows_Frame_Red1 1", new RendererLightmapInfo(3, 0.457031012f, 0.457031012f, 0.543183029f, 0.252977997f) },
        { "StoneDark3 1", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.85511899f, -0.304412991f) },
        { "StoneDark2 3", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, 0.323083013f, -0.242656007f) },
        { "StoneDark1 3", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.0116210002f, 0.0115860999f) },
        { "StoneBright3 1", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.518703997f, -0.270610005f) },
        { "StoneBright2 1", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, -0.0070136399f, -0.209046006f) },
        { "StoneBright1 1", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.0135524999f, 0.0134364003f) },
        { "Exit 1", new RendererLightmapInfo(5, 0.352539003f, 0.352539003f, 0.971068978f, 0.519553006f) },
        { "Brick 1", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.0218602009f, -0.391624004f) },
        { "Wall 1", new RendererLightmapInfo(2, 0.612304986f, 0.612304986f, 0.624410987f, 0.396948993f) },
        { "Flooring 1", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, -0.0159296002f, 0.0128624002f) },
        { "Net 1", new RendererLightmapInfo(3, 0.366210997f, 0.366210997f, 0.342076004f, 0.168539003f) },
        { "NetFrame 1", new RendererLightmapInfo(3, 0.143555f, 0.143555f, 0.293718994f, 0.407023013f) },
        { "Ladders 1", new RendererLightmapInfo(2, 0.12207f, 0.12207f, 0.467292011f, -0.0784742981f) },
        { "Ropes 1", new RendererLightmapInfo(3, 0.0390625f, 0.0390625f, 0.032393001f, -0.0311884992f) },
        { "WallRoof 1", new RendererLightmapInfo(2, 0.724609017f, 0.724609017f, 0.501504004f, -0.117395997f) },
        { "Roof 1", new RendererLightmapInfo(2, 0.565429986f, 0.565429986f, 0.863892972f, -0.138139993f) },
        { "Glass 1", new RendererLightmapInfo(0, 0.410156012f, 0.410156012f, 0.260212004f, -0.103325002f) },
        { "DoorFrames 1", new RendererLightmapInfo(3, 0.177734002f, 0.177734002f, 0.422201008f, 0.373126f) },
        { "WindowFrames3 1", new RendererLightmapInfo(5, 0.361328006f, 0.361328006f, 0.615505993f, 0.404931992f) },
        { "WindowFrames1 1", new RendererLightmapInfo(0, 0.294921994f, 0.294921994f, -0.0031983701f, -0.146004006f) },
        { "StoneDark 1", new RendererLightmapInfo(2, 0.833007991f, 0.833007991f, -0.010214f, -0.346902013f) },
        { "StoneDark2 2", new RendererLightmapInfo(6, 0.664062977f, 0.664062977f, 0.712037027f, 0.0561746992f) },
        { "StoneDark1 2", new RendererLightmapInfo(3, 0.950195014f, 0.950195014f, -0.0185311008f, -0.457195014f) },
        { "StoneBright 1", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0115604997f, 0.00918900967f) },
        { "Frame2 1", new RendererLightmapInfo(3, 0.154296994f, 0.154296994f, 0.956426978f, 0.570846975f) },
        { "Frame1 1", new RendererLightmapInfo(3, 0.125f, 0.125f, -0.00191652996f, 0.362112999f) },
        { "Joints2 1", new RendererLightmapInfo(3, 0.0292968992f, 0.0292968992f, 0.993552983f, 0.970062017f) },
        { "Joints1 1", new RendererLightmapInfo(3, 0.0351562984f, 0.0351562984f, 0.979683995f, -0.0103740999f) },
        { "Borardwalk2 1", new RendererLightmapInfo(3, 0.275391012f, 0.275391012f, 0.0260010995f, 0.275049001f) },
        { "Boardwalk1 1", new RendererLightmapInfo(3, 0.238280997f, 0.238280997f, 0.507659972f, 0.557165027f) },
        { "Props1 1", new RendererLightmapInfo(2, 0.208008006f, 0.208008006f, 0.937277019f, 0.709240019f) },
        { "Props49 1", new RendererLightmapInfo(2, 0.251953006f, 0.251953006f, 0.871930003f, 0.749424994f) },
        { "Props48 1", new RendererLightmapInfo(3, 0.446289003f, 0.446289003f, 0.712872028f, 0.282222986f) },
        { "Props47 1", new RendererLightmapInfo(2, 0.015625f, 0.015625f, 0.489591002f, -0.0124458997f) },
        { "Props46 1", new RendererLightmapInfo(0, 0.0683593974f, 0.0683593974f, 0.138500005f, -0.0563690998f) },
        { "Props45 1", new RendererLightmapInfo(3, 0.0107421996f, 0.0107421996f, 0.995505989f, -0.00845069997f) },
        { "Props44 1", new RendererLightmapInfo(0, 0.00878905971f, 0.00878905971f, 0.163497999f, -0.00713443989f) },
        { "Props43 1", new RendererLightmapInfo(3, 0.168945f, 0.168945f, 0.607972026f, 0.346534014f) },
        { "Props42 1", new RendererLightmapInfo(0, 0.00878905971f, 0.00878905971f, 0.160566002f, -0.00711067999f) },
        { "Props41 1", new RendererLightmapInfo(3, 0.00683593983f, 0.00683593983f, 0.171324f, 0.992447972f) },
        { "Props40 1", new RendererLightmapInfo(6, 0.370117009f, 0.370117009f, 0.200512007f, 0.630539f) },
        { "Props39 1", new RendererLightmapInfo(2, 0.128905997f, 0.128905997f, 0.874202013f, 0.786585987f) },
        { "Props38 1", new RendererLightmapInfo(3, 0.125976995f, 0.125976995f, 0.655489028f, 0.460779011f) },
        { "Props37 1", new RendererLightmapInfo(3, 0.125976995f, 0.125976995f, 0.533317983f, 0.427334011f) },
        { "Props36 1", new RendererLightmapInfo(0, 0.0429687984f, 0.0429687984f, 0.150426f, -0.0331153981f) },
        { "Props35 1", new RendererLightmapInfo(2, 0.125976995f, 0.125976995f, 0.874197006f, 0.730681002f) },
        { "Props34 1", new RendererLightmapInfo(2, 0.0673827976f, 0.0673827976f, 0.95707202f, -0.0385900997f) },
        { "Props33 1", new RendererLightmapInfo(3, 0.0498046987f, 0.0498046987f, 0.978550017f, -0.0344440006f) },
        { "Props32 1", new RendererLightmapInfo(3, 0.078125f, 0.078125f, 0.169912994f, 0.398591012f) },
        { "Props31 1", new RendererLightmapInfo(3, 0.466796994f, 0.466796994f, 0.712390006f, 0.536508977f) },
        { "Props30 1", new RendererLightmapInfo(3, 0.0791015998f, 0.0791015998f, 0.95668602f, 0.600946009f) },
        { "Props29 1", new RendererLightmapInfo(3, 0.12207f, 0.12207f, 0.547131002f, 0.523761988f) },
        { "AirconModels3", new RendererLightmapInfo(6, 0.84375f, 0.84375f, 0.72052002f, -0.0285468996f) },
        { "AirconModels2", new RendererLightmapInfo(6, 0.703125f, 0.703125f, 0.361514002f, -0.0204978008f) },
        { "AirconModels1", new RendererLightmapInfo(6, 0.581054986f, 0.581054986f, 0.949868023f, -0.510075986f) },
        { "Ground", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, -0.00733420998f, -0.505079985f) },
        { "GardenBed7", new RendererLightmapInfo(5, 0.78222698f, 0.78222698f, 0.971436024f, 0.219516993f) },
        { "GardenBed6", new RendererLightmapInfo(5, 0.78222698f, 0.78222698f, 0.971096992f, 0.163497999f) },
        { "GardenBed5", new RendererLightmapInfo(5, 0.958984017f, 0.958984017f, 0.806581974f, -0.551343977f) },
        { "GardenBed4", new RendererLightmapInfo(5, 0.984375f, 0.984375f, 0.967859983f, -0.121634997f) },
        { "GardenBed3", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.305730999f, -0.317977011f) },
        { "GardenBed2", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.343818009f, -0.35996899f) },
        { "GardenBed1", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.858509004f, -0.210675001f) },
        { "StreetLamp5", new RendererLightmapInfo(5, 0.350585997f, 0.350585997f, 0.942354977f, -0.0889697f) },
        { "StreetLamp4", new RendererLightmapInfo(6, 0.393554986f, 0.393554986f, 0.95095998f, -0.355363995f) },
        { "StreetLamp3", new RendererLightmapInfo(6, 0.34375f, 0.34375f, 0.951171994f, -0.240995005f) },
        { "StreetLamp2", new RendererLightmapInfo(6, 0.251953006f, 0.251953006f, 0.977671981f, 0.748026013f) },
        { "StreetLamp1", new RendererLightmapInfo(5, 0.323242009f, 0.323242009f, 0.284081012f, 0.327188998f) },
        { "SmallHouse2", new RendererLightmapInfo(5, 0.599609017f, 0.599609017f, 0.857237995f, 0.0321161002f) },
        { "SmallHouse1", new RendererLightmapInfo(5, 0.369141012f, 0.369141012f, 0.95648998f, 0.412268013f) },
        { "Building_6", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.558187008f, -0.188008994f) },
        { "Building_2", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.622771025f, 0.00737142982f) },
        { "Building_1", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.799791992f, -0.179094002f) },
        { "Building_7", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.190108001f, -0.205662996f) },
        { "Building_8", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.854059994f, -0.105639003f) },
        { "Building_9", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.437234014f, -0.205620006f) },
        { "Building_5", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.854627013f, 0.0079395799f) },
        { "Building_4", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.626605988f, -0.443098009f) },
        { "Building_3", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, 0.310227007f, -0.206187993f) },
    };

    // TheWarehouse: 44 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> TheWarehouseLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Ramp2", new RendererLightmapInfo(0, 0.0947265998f, 0.0947265998f, 0.987766981f, -0.0059936f) },
        { "Ramp2Net", new RendererLightmapInfo(0, 0.115234002f, 0.115234002f, 0.988026023f, 0.827614009f) },
        { "Ramp1", new RendererLightmapInfo(0, 0.0947265998f, 0.0947265998f, 0.987766981f, 0.824084997f) },
        { "Ramp1Net", new RendererLightmapInfo(0, 0.115234002f, 0.115234002f, 0.988026023f, 0.851050973f) },
        { "Shelf2", new RendererLightmapInfo(0, 0.262695014f, 0.262695014f, 0.848986983f, 0.464589f) },
        { "Shelf1", new RendererLightmapInfo(0, 0.262695014f, 0.262695014f, 0.848986983f, 0.565174997f) },
        { "ElectricBox6", new RendererLightmapInfo(0, 0.0742188022f, 0.0742188022f, 0.956233978f, 0.805790007f) },
        { "ElectricBox5", new RendererLightmapInfo(0, 0.0820313022f, 0.0820313022f, 0.986078978f, 0.0467736982f) },
        { "ElectricBox4", new RendererLightmapInfo(0, 0.0791015998f, 0.0791015998f, 0.982168972f, 0.803606987f) },
        { "ElectricBox3", new RendererLightmapInfo(0, 0.0742188022f, 0.0742188022f, 0.96013999f, 0.0596967004f) },
        { "ElectricBox2", new RendererLightmapInfo(0, 0.0820313022f, 0.0820313022f, 0.986078978f, 0.0291955993f) },
        { "ElectricBox1", new RendererLightmapInfo(0, 0.0791015998f, 0.0791015998f, 0.982168972f, 0.819231987f) },
        { "VentDuct2Net", new RendererLightmapInfo(0, 0.310546994f, 0.310546994f, 0.849372029f, 0.317979008f) },
        { "VentDuct2", new RendererLightmapInfo(0, 0.504882991f, 0.504882991f, 0.476101011f, -0.000515214982f) },
        { "VentDuct1Net", new RendererLightmapInfo(0, 0.310546994f, 0.310546994f, 0.305427015f, 0.298447996f) },
        { "VentDuct1", new RendererLightmapInfo(0, 0.504882991f, 0.504882991f, -0.000461494987f, -0.000515214982f) },
        { "SmallCrate6", new RendererLightmapInfo(0, 0.0810547024f, 0.0810547024f, 0.955515027f, 0.850607991f) },
        { "SmallCrate5", new RendererLightmapInfo(0, 0.0859375f, 0.0859375f, 0.959661007f, -0.0176021997f) },
        { "SmallCrate4", new RendererLightmapInfo(0, 0.0683593974f, 0.0683593974f, 0.959807992f, 0.0507407002f) },
        { "SmallCrate3", new RendererLightmapInfo(0, 0.0810547024f, 0.0810547024f, 0.959420979f, 0.0166232008f) },
        { "SmallCrate2", new RendererLightmapInfo(0, 0.0859375f, 0.0859375f, 0.955754995f, 0.875952005f) },
        { "SmallCrate1", new RendererLightmapInfo(0, 0.0683593974f, 0.0683593974f, 0.955901027f, 0.833944023f) },
        { "MediumCrate6", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, 0.95539701f, 0.873265028f) },
        { "MediumCrate5", new RendererLightmapInfo(0, 0.154296994f, 0.154296994f, 0.624768019f, 0.300224006f) },
        { "MediumCrate4", new RendererLightmapInfo(0, 0.177734002f, 0.177734002f, 0.126984999f, 0.300045013f) },
        { "MediumCrate3", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, 0.952467024f, -0.0886491016f) },
        { "MediumCrate2", new RendererLightmapInfo(0, 0.154296994f, 0.154296994f, 0.39527601f, 0.409599006f) },
        { "MediumCrate1", new RendererLightmapInfo(0, 0.177734002f, 0.177734002f, 0.148469001f, 0.422116011f) },
        { "LargeCrate6", new RendererLightmapInfo(0, 0.32714799f, 0.32714799f, 0.620693982f, 0.674732029f) },
        { "LargeCrate5", new RendererLightmapInfo(0, 0.174805f, 0.174805f, 0.683948994f, 0.389075011f) },
        { "LargeCrate4", new RendererLightmapInfo(0, 0.174805f, 0.174805f, 0.239088997f, 0.400976986f) },
        { "LargeCrate3", new RendererLightmapInfo(0, 0.32714799f, 0.32714799f, 0.393155009f, 0.674732029f) },
        { "LargeCrate2", new RendererLightmapInfo(0, 0.174805f, 0.174805f, 0.62242502f, 0.389075011f) },
        { "LargeCrate1", new RendererLightmapInfo(0, 0.174805f, 0.174805f, 0.239088997f, 0.46152401f) },
        { "YellowContainers", new RendererLightmapInfo(0, 0.347656012f, 0.347656012f, -0.00270103989f, 0.253969014f) },
        { "RedContainers2", new RendererLightmapInfo(1, 0.566406012f, 0.566406012f, -0.0112405f, 0.436967999f) },
        { "RedContainers1", new RendererLightmapInfo(0, 0.347656012f, 0.347656012f, 0.619764984f, 0.429650009f) },
        { "GreyContainers2", new RendererLightmapInfo(1, 0.748046994f, 0.748046994f, 0.268189996f, 0.258852988f) },
        { "GreyContainers1", new RendererLightmapInfo(1, 0.748046994f, 0.748046994f, 0.408814996f, -0.288022012f) },
        { "BlueContainers2", new RendererLightmapInfo(0, 0.283203006f, 0.283203006f, 0.448484004f, 0.281962991f) },
        { "BlueContainers1", new RendererLightmapInfo(0, 0.347656012f, 0.347656012f, 0.392226011f, 0.429650009f) },
        { "Ground", new RendererLightmapInfo(0, 0.505859017f, 0.505859017f, -0.00734240981f, 0.498815f) },
        { "Walls", new RendererLightmapInfo(1, 0.876953006f, 0.876953006f, -0.0122710997f, -0.322492987f) },
        { "Roof", new RendererLightmapInfo(0, 0.448242009f, 0.448242009f, 0.846985996f, 0.55588901f) },
    };

    // FortWinter: 67 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> FortWinterLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Tree_Trunk10", new RendererLightmapInfo(2, 0.191405997f, 0.191405997f, 0.973572016f, 0.808910012f) },
        { "Tree_Trunk9", new RendererLightmapInfo(2, 0.167969003f, 0.167969003f, 0.922019005f, 0.832436025f) },
        { "Tree_Trunk8", new RendererLightmapInfo(2, 0.213866994f, 0.213866994f, 0.975368977f, 0.741609991f) },
        { "Tree_Trunk7", new RendererLightmapInfo(2, 0.229491994f, 0.229491994f, 0.933251977f, 0.714326978f) },
        { "Tree_Trunk6", new RendererLightmapInfo(2, 0.229491994f, 0.229491994f, 0.664696991f, -0.150904998f) },
        { "Tree_Trunk5", new RendererLightmapInfo(2, 0.229491994f, 0.229491994f, 0.954738975f, 0.719210029f) },
        { "Tree_Trunk4", new RendererLightmapInfo(2, 0.276367009f, 0.276367009f, 0.96706301f, -0.222992003f) },
        { "Tree_Trunk3", new RendererLightmapInfo(2, 0.330078006f, 0.330078006f, 0.934414029f, 0.570488989f) },
        { "Tree_Trunk2", new RendererLightmapInfo(2, 0.213866994f, 0.213866994f, 0.937282979f, 0.770906985f) },
        { "Tree_Trunk1", new RendererLightmapInfo(2, 0.213866994f, 0.213866994f, 0.954860985f, 0.775789976f) },
        { "Tree_Trunk", new RendererLightmapInfo(2, 0.213866994f, 0.213866994f, 0.967532992f, -0.110642999f) },
        { "Tree_Leaves10", new RendererLightmapInfo(2, 0.569335997f, 0.569335997f, 0.845211029f, -0.340380013f) },
        { "Tree_Leaves9", new RendererLightmapInfo(2, 0.498046994f, 0.498046994f, 0.694728971f, -0.243068993f) },
        { "Tree_Leaves8", new RendererLightmapInfo(2, 0.633789003f, 0.633789003f, 0.542261004f, -0.331340998f) },
        { "Tree_Leaves7", new RendererLightmapInfo(2, 0.681640983f, 0.681640983f, 0.692267001f, -0.502847016f) },
        { "Tree_Leaves6", new RendererLightmapInfo(2, 0.681640983f, 0.681640983f, 0.796774983f, 0.322338015f) },
        { "Tree_Leaves5", new RendererLightmapInfo(2, 0.681640983f, 0.681640983f, 0.836798012f, -0.550698996f) },
        { "Tree_Leaves4", new RendererLightmapInfo(2, 0.820312977f, 0.820312977f, 0.300419003f, -0.628037989f) },
        { "Tree_Leaves3", new RendererLightmapInfo(2, 0.980468988f, 0.980468988f, 0.480883986f, -0.792352021f) },
        { "Tree_Leaves2", new RendererLightmapInfo(2, 0.633789003f, 0.633789003f, 0.37331599f, -0.327434987f) },
        { "Tree_Leaves1", new RendererLightmapInfo(2, 0.633789003f, 0.633789003f, 0.788354993f, 0.248737007f) },
        { "Tree_Leaves", new RendererLightmapInfo(2, 0.633789003f, 0.633789003f, 0.617456973f, 0.25850299f) },
        { "WireFencePoles", new RendererLightmapInfo(1, 0.18457f, 0.18457f, 0.878419995f, -0.130570993f) },
        { "WireFence", new RendererLightmapInfo(1, 0.429688007f, 0.429688007f, 0.501058996f, -0.218044996f) },
        { "WallSnow", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, 0.565020025f, 0.00744212f) },
        { "WallSmallDoor", new RendererLightmapInfo(0, 0.758789003f, 0.758789003f, -0.0127483997f, -0.388924003f) },
        { "WallRock", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, 0.575168014f, -0.272181988f) },
        { "WallDoorsSnow", new RendererLightmapInfo(1, 0.491210997f, 0.491210997f, -0.00576381013f, -0.151694998f) },
        { "WallDoors", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, 0.565753996f, 0.0074154702f) },
        { "GroundSnow", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.0175256003f, 0.0135324998f) },
        { "Ground", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, 0.715430021f, -0.704723001f) },
        { "TelephoneWire", new RendererLightmapInfo(0, 0.427733988f, 0.427733988f, 0.859148979f, 0.405211985f) },
        { "TelephonePoleLightBulb1", new RendererLightmapInfo(2, 0.0341796987f, 0.0341796987f, 0.995427012f, -0.0295030996f) },
        { "TelephonePoleLightBulb0", new RendererLightmapInfo(2, 0.0341796987f, 0.0341796987f, 0.997372985f, 0.962661982f) },
        { "TelephonePoleLight1", new RendererLightmapInfo(2, 0.123047002f, 0.123047002f, 0.952098012f, 0.876811028f) },
        { "TelephonePoleLight0", new RendererLightmapInfo(2, 0.123047002f, 0.123047002f, 0.93918401f, 0.877016008f) },
        { "Pole2", new RendererLightmapInfo(2, 0.35839799f, 0.35839799f, -0.00428216998f, -0.0742335021f) },
        { "Pole1", new RendererLightmapInfo(2, 0.35839799f, 0.35839799f, 0.568048f, 0.646070004f) },
        { "SmallRock6", new RendererLightmapInfo(2, 0.0205077995f, 0.0205077995f, 0.996376991f, -0.0180040002f) },
        { "SmallRock5", new RendererLightmapInfo(0, 0.0341796987f, 0.0341796987f, 0.978783011f, -0.0303470008f) },
        { "SmallRock4", new RendererLightmapInfo(1, 0.0986327976f, 0.0986327976f, 0.929506004f, -0.0888163f) },
        { "SmallRock3", new RendererLightmapInfo(2, 0.107422002f, 0.107422002f, 0.96459198f, 0.892787993f) },
        { "SmallRock2", new RendererLightmapInfo(2, 0.0791015998f, 0.0791015998f, 0.689229012f, -0.0341359004f) },
        { "SmallRock1", new RendererLightmapInfo(0, 0.0244140998f, 0.0244140998f, 0.983684003f, -0.0216722004f) },
        { "SmallRock", new RendererLightmapInfo(0, 0.106445f, 0.106445f, 0.967486024f, -0.0958316997f) },
        { "SandBag1", new RendererLightmapInfo(1, 0.29589799f, 0.29589799f, 0.0612825006f, 0.0499145985f) },
        { "SandBag0", new RendererLightmapInfo(2, 0.29589799f, 0.29589799f, 0.618900001f, 0.705187976f) },
        { "BarrierWire", new RendererLightmapInfo(0, 0.226563007f, 0.226563007f, 0.826357007f, 0.572589993f) },
        { "Barrier", new RendererLightmapInfo(0, 0.121094003f, 0.121094003f, 0.933261991f, -0.0854474008f) },
        { "MountainSnow", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, 0.655498981f, -0.941290021f) },
        { "MountainRock", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, 0.0886949971f, -0.689306021f) },
        { "HouseSnow 3", new RendererLightmapInfo(1, 0.616210997f, 0.616210997f, 0.680997014f, -0.0909871012f) },
        { "HouseMain 3", new RendererLightmapInfo(3, 0.830078006f, 0.830078006f, -0.00938786007f, 0.187580004f) },
        { "HouseBase 3", new RendererLightmapInfo(1, 0.643554986f, 0.643554986f, 0.498064011f, -0.252920002f) },
        { "HouseSnow 2", new RendererLightmapInfo(1, 0.616210997f, 0.616210997f, 0.686809003f, -0.258747011f) },
        { "HouseMain 2", new RendererLightmapInfo(2, 0.830078006f, 0.830078006f, -0.00938786007f, 0.187580004f) },
        { "HouseBase 2", new RendererLightmapInfo(1, 0.643554986f, 0.643554986f, 0.692399979f, -0.457020998f) },
        { "HouseSnow 1", new RendererLightmapInfo(0, 0.616210997f, 0.616210997f, 0.773769975f, 0.0994426012f) },
        { "HouseMain 1", new RendererLightmapInfo(1, 0.830078006f, 0.830078006f, -0.00938786007f, 0.187580004f) },
        { "HouseBase 1", new RendererLightmapInfo(0, 0.643554986f, 0.643554986f, 0.535174012f, -0.151356995f) },
        { "HouseSnow", new RendererLightmapInfo(0, 0.616210997f, 0.616210997f, 0.810832977f, 0.385784f) },
        { "HouseMain", new RendererLightmapInfo(0, 0.830078006f, 0.830078006f, -0.00938786007f, 0.187580004f) },
        { "HouseBase", new RendererLightmapInfo(0, 0.643554986f, 0.643554986f, 0.716813982f, -0.169911996f) },
        { "Grass4", new RendererLightmapInfo(0, 0.815429986f, 0.815429986f, 0.408358991f, -0.500913024f) },
        { "Grass3", new RendererLightmapInfo(1, 0.632812977f, 0.632812977f, 0.0213421993f, -0.387255013f) },
        { "Grass2", new RendererLightmapInfo(2, 0.694335997f, 0.694335997f, 0.0383533984f, -0.430132002f) },
        { "Grass1", new RendererLightmapInfo(1, 0.632812977f, 0.632812977f, 0.257952988f, -0.385645986f) },
    };

    // LostParadise2: 51 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> LostParadise2LightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Tile", new RendererLightmapInfo(8, 2.0f, -2.0f, -0.5f, 1.5f) },
        { "CabanaHut3", new RendererLightmapInfo(1, 0.382813007f, 0.382813007f, 0.821604013f, 0.283172995f) },
        { "CabanaHut1", new RendererLightmapInfo(1, 0.382813007f, 0.382813007f, 0.292306989f, 0.617156982f) },
        { "CabanaHut2", new RendererLightmapInfo(1, 0.382813007f, 0.382813007f, 0.593088984f, 0.265594989f) },
        { "HelicopterBody", new RendererLightmapInfo(0, 0.208984002f, 0.208984002f, 0.896634996f, 0.181139007f) },
        { "HelicopterWindow", new RendererLightmapInfo(0, 0.0527344011f, 0.0527344011f, 0.36690101f, -0.0302009005f) },
        { "Ivy_Ruins", new RendererLightmapInfo(0, 0.0839843974f, 0.0839843974f, 0.592144012f, -0.0684484988f) },
        { "Ivy_TwinPeaks", new RendererLightmapInfo(1, 0.352539003f, 0.352539003f, 0.73231101f, 0.297856987f) },
        { "PlaneBroken", new RendererLightmapInfo(0, 0.248046994f, 0.248046994f, 0.882175982f, 0.0514106005f) },
        { "RuinsPillars", new RendererLightmapInfo(0, 0.110352002f, 0.110352002f, 0.396183014f, -0.0712233037f) },
        { "RuinsTemple", new RendererLightmapInfo(0, 0.341796994f, 0.341796994f, -0.00266410992f, -0.138533995f) },
        { "SniperTower1", new RendererLightmapInfo(2, 0.638671994f, 0.638671994f, 0.521762013f, -0.278216988f) },
        { "SniperTower2", new RendererLightmapInfo(1, 0.638671994f, 0.638671994f, 0.457309008f, 0.370220989f) },
        { "TwinPeaksBridge1", new RendererLightmapInfo(2, 0.234375f, 0.234375f, 0.892817974f, 0.541974008f) },
        { "TwinPeaksBridge2", new RendererLightmapInfo(2, 0.235351995f, 0.235351995f, 0.895978987f, 0.610844016f) },
        { "RockLarge1", new RendererLightmapInfo(2, 0.211914003f, 0.211914003f, 0.863336027f, 0.0639567971f) },
        { "RockLarge2", new RendererLightmapInfo(2, 0.265625f, 0.265625f, 0.503817976f, 0.552057981f) },
        { "RockLarge3", new RendererLightmapInfo(0, 0.306641012f, 0.306641012f, 0.717055976f, -0.186386004f) },
        { "RockLarge4", new RendererLightmapInfo(2, 0.211914003f, 0.211914003f, 0.863335013f, 0.403784007f) },
        { "RockLarge5", new RendererLightmapInfo(2, 0.290039003f, 0.290039003f, 0.741717994f, 0.292093992f) },
        { "RockLarge6", new RendererLightmapInfo(2, 0.253906012f, 0.253906012f, 0.566734016f, 0.229929999f) },
        { "RockLarge7", new RendererLightmapInfo(3, 0.385742009f, 0.771484017f, 0.0751399025f, -0.111695997f) },
        { "RockLarge8", new RendererLightmapInfo(3, 0.317382991f, 0.634765983f, 0.567830026f, -0.311004013f) },
        { "RockLarge9", new RendererLightmapInfo(2, 0.345703006f, 0.345703006f, -0.0119655998f, 0.662536979f) },
        { "RockLarge10", new RendererLightmapInfo(2, 0.223633006f, 0.223633006f, 0.581323981f, 0.335525006f) },
        { "Terrain_Main", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0159907006f, 0.0128945997f) },
        { "Terrain_Outer", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0105199004f, -0.311971992f) },
        { "TwinPeaks", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0170699991f, -0.00536536984f) },
        { "CerimanLeaves1", new RendererLightmapInfo(0, 0.0810547024f, 0.0810547024f, 0.559946001f, -0.049407199f) },
        { "CerimanLeaves2", new RendererLightmapInfo(1, 0.102539003f, 0.102539003f, 0.956140995f, -0.0626365989f) },
        { "CerimanStalks1", new RendererLightmapInfo(0, 0.0439453013f, 0.0439453013f, 0.289020985f, -0.00830496009f) },
        { "CerimanStalks2", new RendererLightmapInfo(1, 0.0556640998f, 0.0556640998f, 0.0388792008f, 0.936551988f) },
        { "CocoTreesLeaves1", new RendererLightmapInfo(1, 0.784179986f, 0.784179986f, 0.446868986f, -0.257265002f) },
        { "CocoTreesLeaves2", new RendererLightmapInfo(1, 0.430664003f, 0.430664003f, 0.806786001f, 0.393263012f) },
        { "CocoTreesLeaves3", new RendererLightmapInfo(2, 0.793945014f, 0.793945014f, -0.0120470999f, -0.242614999f) },
        { "CocoTreesTrunk1", new RendererLightmapInfo(0, 0.386718988f, 0.386718988f, -0.00108869001f, 0.0129592f) },
        { "CocoTreesTrunk2", new RendererLightmapInfo(1, 0.216796994f, 0.216796994f, 0.733603001f, 0.352063f) },
        { "CocoTreesTrunk3", new RendererLightmapInfo(0, 0.388671994f, 0.388671994f, 0.841731012f, -0.189045995f) },
        { "GrassGroup", new RendererLightmapInfo(1, 0.716796994f, 0.716796994f, 0.814209998f, 0.288740009f) },
        { "GrassGroup1", new RendererLightmapInfo(0, 0.293945014f, 0.293945014f, 0.899954975f, 0.170396f) },
        { "GrassGroup2", new RendererLightmapInfo(2, 0.355468988f, 0.355468988f, 0.189599007f, 0.459773988f) },
        { "GrassGroup3", new RendererLightmapInfo(2, 0.363281012f, 0.363281012f, 0.880945981f, -0.167109996f) },
        { "LeafyPlants1", new RendererLightmapInfo(0, 0.163085997f, 0.163085997f, 0.233140007f, -0.0298638009f) },
        { "LeafyPlants2", new RendererLightmapInfo(1, 0.0947265998f, 0.0947265998f, 0.149578005f, 0.90526402f) },
        { "LeafyPlants3", new RendererLightmapInfo(2, 0.125976995f, 0.125976995f, 0.915504992f, 0.859737992f) },
        { "PalmTreesLeaves1", new RendererLightmapInfo(2, 0.328125f, 0.328125f, 0.879585981f, -0.224649996f) },
        { "PalmTreesLeaves2", new RendererLightmapInfo(2, 0.29785201f, 0.29785201f, 0.896530986f, 0.638894975f) },
        { "PalmTreesTrunk1", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.324586987f, -0.0876464993f) },
        { "PalmTreesTrunk2", new RendererLightmapInfo(1, 0.118164003f, 0.118164003f, 0.955632985f, -0.0461389013f) },
        { "Accelerator_Light", new RendererLightmapInfo(10, 0.0380859002f, 0.0380859002f, 0.283542991f, 0.964206994f) },
        { "Accelerator", new RendererLightmapInfo(10, 0.119140998f, 0.119140998f, 0.82131201f, 0.469749004f) },
    };

    // SkyGarden: 26 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> SkyGardenLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Rock 2", new RendererLightmapInfo(2, 0.914062977f, 0.914062977f, -0.0141321002f, -0.0144985002f) },
        { "Ground 1", new RendererLightmapInfo(4, 0.182616994f, 0.182616994f, 0.513171971f, -0.123630002f) },
        { "Edge 2", new RendererLightmapInfo(4, 0.205078006f, 0.205078006f, 0.382678986f, -0.0854770988f) },
        { "Edge", new RendererLightmapInfo(4, 0.25f, 0.25f, 0.602585018f, -0.00933155976f) },
        { "UnderPartRock", new RendererLightmapInfo(4, 0.395507991f, 0.395507991f, 0.668138981f, 0.610301018f) },
        { "BaseGround 1", new RendererLightmapInfo(6, 0.644531012f, 0.644531012f, -0.0080554802f, 0.362870991f) },
        { "UnderPart", new RendererLightmapInfo(4, 0.189453006f, 0.189453006f, 0.572596014f, -0.141918004f) },
        { "Inside 2", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0216473006f, 0.0712651014f) },
        { "Inside", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0216473006f, 0.0712651014f) },
        { "FloatingRockInside", new RendererLightmapInfo(4, 0.301757991f, 0.301757991f, 0.776356995f, -0.200142995f) },
        { "Edge 1", new RendererLightmapInfo(4, 0.212890998f, 0.212890998f, 0.662992001f, 0.789029002f) },
        { "Edge 4", new RendererLightmapInfo(4, 0.205078006f, 0.205078006f, 0.447131991f, -0.0854770988f) },
        { "Box1", new RendererLightmapInfo(4, 0.199219003f, 0.199219003f, -0.00335195009f, -0.00951553974f) },
        { "Inside 1", new RendererLightmapInfo(7, 0.866210997f, 0.866210997f, -0.0248819999f, 0.149805993f) },
        { "Rock 3", new RendererLightmapInfo(4, 0.160155997f, 0.160155997f, 0.685949028f, -0.108499996f) },
        { "FloatingRock", new RendererLightmapInfo(4, 0.287108988f, 0.287108988f, 0.865423024f, 0.717898011f) },
        { "Edge 3", new RendererLightmapInfo(2, 0.212890998f, 0.212890998f, -0.00302338996f, 0.789029002f) },
        { "Rock 1", new RendererLightmapInfo(4, 0.523437977f, 0.523437977f, -0.0050842301f, 0.484450996f) },
        { "Box2 1", new RendererLightmapInfo(5, 0.244140998f, 0.244140998f, -0.00389603991f, -0.00219926005f) },
        { "UnderPart 1", new RendererLightmapInfo(2, 0.189453006f, 0.189453006f, 0.128260002f, 0.812183976f) },
        { "Box2", new RendererLightmapInfo(5, 0.244140998f, 0.244140998f, 0.612314999f, 0.758543015f) },
        { "BaseGround", new RendererLightmapInfo(5, 0.644531012f, 0.644531012f, -0.0080554802f, 0.362870991f) },
        { "Rock", new RendererLightmapInfo(3, 0.88964802f, 0.88964802f, -0.0087305503f, -0.0089724604f) },
        { "Box1 1", new RendererLightmapInfo(4, 0.199219003f, 0.199219003f, 0.190007001f, -0.00951553974f) },
        { "Ground 2", new RendererLightmapInfo(2, 0.182616994f, 0.182616994f, 0.0688356012f, 0.817776024f) },
        { "Rock 4", new RendererLightmapInfo(4, 0.160155997f, 0.160155997f, 0.732824028f, -0.108499996f) },
    };

    // TheBunker: 98 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> TheBunkerLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Container4", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.629922986f, 0.526955009f) },
        { "CommonLights28", new RendererLightmapInfo(6, 0.0185547005f, 0.0185547005f, 0.326148003f, 0.981419981f) },
        { "Net4 1", new RendererLightmapInfo(5, 0.352539003f, 0.352539003f, 0.184618995f, 0.654752016f) },
        { "Container6", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.819375992f, 0.0630877018f) },
        { "Frame_Steps", new RendererLightmapInfo(2, 0.164063007f, 0.164063007f, 0.972323f, 0.83910799f) },
        { "CommonLights23 1", new RendererLightmapInfo(1, 0.0185547005f, 0.0185547005f, 0.0517336018f, 0.981419981f) },
        { "Container3 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.819375992f, -0.115622997f) },
        { "CommonLights20 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.424780011f, 0.981419981f) },
        { "FanFrame", new RendererLightmapInfo(0, 0.114257999f, 0.114257999f, -0.00180114002f, 0.886398971f) },
        { "CommonLights20", new RendererLightmapInfo(4, 0.0185547005f, 0.0185547005f, 0.0107178995f, 0.981419981f) },
        { "CommonLights19 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.414038002f, 0.981419981f) },
        { "CommonLights27 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.467749f, 0.981419981f) },
        { "CommonLights16 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.381812006f, 0.981419981f) },
        { "FrameCeling", new RendererLightmapInfo(2, 0.163085997f, 0.163085997f, 0.0804053992f, 0.840699971f) },
        { "Corridoor 1", new RendererLightmapInfo(8, 0.584960997f, 0.584960997f, 0.45115599f, -0.0190379005f) },
        { "CommonLights25 1", new RendererLightmapInfo(1, 0.0185547005f, 0.0185547005f, 0.0732178986f, 0.981419981f) },
        { "Fan3", new RendererLightmapInfo(0, 0.0576171987f, 0.0576171987f, 0.0990123972f, 0.942870975f) },
        { "Fan2", new RendererLightmapInfo(0, 0.0576171987f, 0.0576171987f, 0.0833396018f, 0.942804992f) },
        { "CeilingFrame 1", new RendererLightmapInfo(1, 0.410156012f, 0.410156012f, 0.741122007f, -0.0190423001f) },
        { "FanFrame 1", new RendererLightmapInfo(1, 0.114257999f, 0.114257999f, -0.00180114002f, 0.886398971f) },
        { "Container3", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.819375992f, -0.115622997f) },
        { "MainFrame 1", new RendererLightmapInfo(3, 0.38964799f, 0.38964799f, 0.731576025f, -0.0104475003f) },
        { "Ground 1", new RendererLightmapInfo(1, 0.953125f, 0.953125f, -0.0333927982f, -0.0367549993f) },
        { "CommonLights16", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.911108971f, 0.981419981f) },
        { "Net3 1", new RendererLightmapInfo(3, 0.362304986f, 0.362304986f, 0.0907713994f, 0.654515982f) },
        { "MachineWindowFrame 1", new RendererLightmapInfo(5, 0.0751952976f, 0.0751952976f, 0.364154994f, 0.925118983f) },
        { "Fan2 1", new RendererLightmapInfo(1, 0.0576171987f, 0.0576171987f, 0.0833396018f, 0.942804992f) },
        { "Container1", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.629922986f, 0.705666006f) },
        { "Container4 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.629922986f, 0.526955009f) },
        { "Lights", new RendererLightmapInfo(0, 0.0712890998f, 0.0712890998f, 0.0317276008f, 0.929248989f) },
        { "Container1 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.629922986f, 0.705666006f) },
        { "CommonLights21", new RendererLightmapInfo(4, 0.0185547005f, 0.0185547005f, 0.0214600991f, 0.981419981f) },
        { "Net1 1", new RendererLightmapInfo(5, 0.167969003f, 0.167969003f, 0.279127002f, 0.835115016f) },
        { "Lights 1", new RendererLightmapInfo(1, 0.0712890998f, 0.0712890998f, 0.0317276008f, 0.929248989f) },
        { "CeilingFrame", new RendererLightmapInfo(0, 0.410156012f, 0.410156012f, 0.741122007f, -0.0190423001f) },
        { "Fan3 1", new RendererLightmapInfo(1, 0.0576171987f, 0.0576171987f, 0.0990123972f, 0.942870975f) },
        { "MiddleBord", new RendererLightmapInfo(2, 0.323242009f, 0.323242009f, 0.484030992f, 0.684809983f) },
        { "CommonLights26", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.964819014f, 0.981419981f) },
        { "MiddleBordFrame 1", new RendererLightmapInfo(6, 0.103515998f, 0.103515998f, 0.264052004f, 0.899764001f) },
        { "CommonLights25", new RendererLightmapInfo(0, 0.0185547005f, 0.0185547005f, 0.0732178986f, 0.981419981f) },
        { "CommonLights29", new RendererLightmapInfo(6, 0.0185547005f, 0.0185547005f, 0.336890012f, 0.981419981f) },
        { "RoomWall", new RendererLightmapInfo(7, 0.497070014f, 0.497070014f, 0.527267992f, -0.0118580004f) },
        { "Fan4 1", new RendererLightmapInfo(3, 0.0576171987f, 0.0576171987f, -0.00107760006f, 0.942771018f) },
        { "CommonLights19", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.943334997f, 0.981419981f) },
        { "RoomInside 1", new RendererLightmapInfo(6, 0.607421994f, 0.607421994f, -0.0130439997f, -0.0128693003f) },
        { "RoomInside", new RendererLightmapInfo(7, 0.607421994f, 0.607421994f, -0.0130439997f, -0.0128693003f) },
        { "CommonLights27", new RendererLightmapInfo(4, 0.0185547005f, 0.0185547005f, 0.0322022997f, 0.981419981f) },
        { "CommonLights28 1", new RendererLightmapInfo(6, 0.0185547005f, 0.0185547005f, 0.347631991f, 0.981419981f) },
        { "MiddleBordFrame", new RendererLightmapInfo(2, 0.103515998f, 0.103515998f, 0.76600498f, 0.899764001f) },
        { "Container5 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.793008029f, 0.526955009f) },
        { "CommonLights29 1", new RendererLightmapInfo(6, 0.0185547005f, 0.0185547005f, 0.358374f, 0.981419981f) },
        { "MiddleBordFrame1 1", new RendererLightmapInfo(2, 0.207030997f, 0.207030997f, 0.90753299f, 0.778970003f) },
        { "Frame_Steps 1", new RendererLightmapInfo(5, 0.164063007f, 0.164063007f, 0.475252986f, 0.83910799f) },
        { "WindowFrame 1", new RendererLightmapInfo(3, 0.102539003f, 0.102539003f, 0.0679306984f, 0.897153974f) },
        { "CommonLights23", new RendererLightmapInfo(0, 0.0185547005f, 0.0185547005f, 0.0517336018f, 0.981419981f) },
        { "Container5", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.793008029f, 0.526955009f) },
        { "CommonLights18", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.932592988f, 0.981419981f) },
        { "CommonLights26 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.457006991f, 0.981419981f) },
        { "CommonLights15 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.371069014f, 0.981419981f) },
        { "ceiling_Round", new RendererLightmapInfo(4, 0.637695014f, 0.637695014f, 0.553970993f, 0.37402299f) },
        { "Container6 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.819375992f, 0.0630877018f) },
        { "ceiling_Flat", new RendererLightmapInfo(4, 0.800781012f, 0.800781012f, -0.0233424995f, -0.0239617992f) },
        { "CommonLights17 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.392553985f, 0.981419981f) },
        { "MainWall", new RendererLightmapInfo(2, 0.861328006f, 0.861328006f, -0.0300539006f, -0.0307157002f) },
        { "Net1", new RendererLightmapInfo(2, 0.167969003f, 0.167969003f, 0.825025976f, 0.835115016f) },
        { "Net0", new RendererLightmapInfo(2, 0.375f, 0.375f, 0.288807988f, 0.635662973f) },
        { "CommonLights21 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.435523003f, 0.981419981f) },
        { "MiddleBord 1", new RendererLightmapInfo(6, 0.323242009f, 0.323242009f, -0.00815659016f, 0.684809983f) },
        { "ceiling_Flat 1", new RendererLightmapInfo(5, 0.800781012f, 0.800781012f, -0.0233424995f, -0.0239617992f) },
        { "MiddleBordFrame1", new RendererLightmapInfo(2, 0.207030997f, 0.207030997f, 0.90753299f, -0.00520937005f) },
        { "CommonLights24", new RendererLightmapInfo(0, 0.0185547005f, 0.0185547005f, 0.0624756999f, 0.981419981f) },
        { "Net0 1", new RendererLightmapInfo(5, 0.375f, 0.375f, -0.0129498001f, 0.635662973f) },
        { "Machine 1", new RendererLightmapInfo(7, 0.462891012f, 0.462891012f, 0.412344992f, 0.547585011f) },
        { "FrameCeling 1", new RendererLightmapInfo(5, 0.163085997f, 0.163085997f, 0.682944f, -0.0040268302f) },
        { "Net4", new RendererLightmapInfo(2, 0.352539003f, 0.352539003f, 0.671923995f, 0.654752016f) },
        { "Ground", new RendererLightmapInfo(0, 0.953125f, 0.953125f, -0.0333927982f, -0.0367549993f) },
        { "Fan4", new RendererLightmapInfo(0, 0.0576171987f, 0.0576171987f, 0.115133002f, 0.942771018f) },
        { "CommonLights17", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.921850979f, 0.981419981f) },
        { "CommonLights18 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.403295994f, 0.981419981f) },
        { "CommonLights22", new RendererLightmapInfo(2, 0.0185547005f, 0.0185547005f, 0.954077005f, 0.981419981f) },
        { "CommonLights15", new RendererLightmapInfo(4, 0.0185547005f, 0.0185547005f, -2.42565002e-05f, 0.981419981f) },
        { "MachineWindowFrame", new RendererLightmapInfo(2, 0.0751952976f, 0.0751952976f, 0.82020998f, 0.903634012f) },
        { "Container2", new RendererLightmapInfo(0, 0.298828006f, 0.298828006f, 0.793008029f, 0.705666006f) },
        { "Container2 1", new RendererLightmapInfo(1, 0.298828006f, 0.298828006f, 0.793008029f, 0.705666006f) },
        { "Fan1 1", new RendererLightmapInfo(3, 0.0576171987f, 0.0576171987f, 0.0152505999f, 0.942140996f) },
        { "MainWall 1", new RendererLightmapInfo(3, 0.861328006f, 0.861328006f, -0.0300539006f, -0.0307157002f) },
        { "MainFrame", new RendererLightmapInfo(2, 0.38964799f, 0.38964799f, 0.731576025f, -0.0104475003f) },
        { "Corridoor", new RendererLightmapInfo(8, 0.584960997f, 0.584960997f, -0.0175944008f, -0.0190379005f) },
        { "ceiling_Round 1", new RendererLightmapInfo(5, 0.637695014f, 0.637695014f, 0.553970993f, 0.37402299f) },
        { "Fan1", new RendererLightmapInfo(0, 0.0576171987f, 0.0576171987f, 0.131461993f, 0.942140996f) },
        { "CommonLights22 1", new RendererLightmapInfo(5, 0.0185547005f, 0.0185547005f, 0.446265012f, 0.981419981f) },
        { "Net2 1", new RendererLightmapInfo(6, 0.219726995f, 0.219726995f, 0.181440994f, 0.783502996f) },
        { "CommonLights24 1", new RendererLightmapInfo(1, 0.0185547005f, 0.0185547005f, 0.0624756999f, 0.981419981f) },
        { "Net2", new RendererLightmapInfo(2, 0.219726995f, 0.219726995f, -0.00410600007f, 0.783502996f) },
        { "Machine", new RendererLightmapInfo(6, 0.462891012f, 0.462891012f, 0.412344992f, 0.547585011f) },
        { "WindowFrame", new RendererLightmapInfo(2, 0.102539003f, 0.102539003f, 0.287656993f, 0.797545016f) },
        { "Net3", new RendererLightmapInfo(2, 0.362304986f, 0.362304986f, 0.0907713994f, 0.654515982f) },
        { "RoomWall 1", new RendererLightmapInfo(6, 0.497070014f, 0.497070014f, 0.527267992f, -0.0118580004f) },
    };

    // Spaceship: 89 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> SpaceshipLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "DoorFrame", new RendererLightmapInfo(1, 0.549804986f, 0.549804986f, 0.582520008f, 0.455565989f) },
        { "SpaceShipBody", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, 0.000488280988f, 0.0122069996f) },
        { "OutSide2", new RendererLightmapInfo(1, 0.478516012f, 0.478516012f, 0.0467096008f, -0.0185273997f) },
        { "Board", new RendererLightmapInfo(1, 0.262695014f, 0.262695014f, 0.940191984f, 0.686209023f) },
        { "Boder", new RendererLightmapInfo(1, 0.244140998f, 0.244140998f, 0.388188988f, -0.0735211968f) },
        { "WindowsFrame 1", new RendererLightmapInfo(4, 0.29589799f, 0.29589799f, 0.868487f, 0.0141139003f) },
        { "Wall 3", new RendererLightmapInfo(1, 0.0527344011f, 0.0527344011f, 0.507885993f, 0.0675012991f) },
        { "WallBase 1", new RendererLightmapInfo(1, 0.0566405989f, 0.0566405989f, 0.0263484009f, 0.0318920016f) },
        { "Ground5 1", new RendererLightmapInfo(1, 0.046875f, 0.046875f, 0.0265024006f, 0.0881327018f) },
        { "'Gate '", new RendererLightmapInfo(1, 0.0136719001f, 0.0136719001f, 0.0463658012f, -0.0126646003f) },
        { "GateLcok 1", new RendererLightmapInfo(1, 0.00976562966f, 0.00976562966f, 0.0444079004f, -0.00692288019f) },
        { "GateBoard 1", new RendererLightmapInfo(1, 0.0566405989f, 0.0566405989f, 0.0264961999f, 0.0667639002f) },
        { "axis 1", new RendererLightmapInfo(1, 0.0146484002f, 0.0146484002f, 0.0424520001f, -0.0129028f) },
        { "GateBase 1", new RendererLightmapInfo(1, 0.0488280989f, 0.0488280989f, 0.0312958993f, -0.00779276993f) },
        { "Wall 2", new RendererLightmapInfo(1, 0.0527344011f, 0.0527344011f, 0.507885993f, 0.0235559996f) },
        { "WallBase", new RendererLightmapInfo(1, 0.0566405989f, 0.0566405989f, 0.0263484009f, 0.0201731995f) },
        { "Ground5", new RendererLightmapInfo(1, 0.046875f, 0.046875f, 0.509900987f, 0.0559060983f) },
        { "GateLcok", new RendererLightmapInfo(1, 0.00976562966f, 0.00976562966f, 0.0444079004f, -0.0088759996f) },
        { "GateBoard", new RendererLightmapInfo(1, 0.0566405989f, 0.0566405989f, 0.0264961999f, 0.0491858013f) },
        { "axis", new RendererLightmapInfo(1, 0.0146484002f, 0.0146484002f, 0.0404989012f, -0.0129028f) },
        { "GateBase", new RendererLightmapInfo(1, 0.0488280989f, 0.0488280989f, 0.0312958993f, -0.0283006001f) },
        { "Sign", new RendererLightmapInfo(1, 0.00878905971f, 0.00878905971f, 0.988770008f, 0.990723014f) },
        { "TunnelWall", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, 0.523302019f, -0.505872011f) },
        { "TunnelCeiling", new RendererLightmapInfo(4, 0.67285198f, 0.67285198f, 0.736016989f, 0.390551001f) },
        { "Frame", new RendererLightmapInfo(2, 0.398438007f, 0.398438007f, 0.784493983f, -0.101056002f) },
        { "DoorBirail", new RendererLightmapInfo(2, 0.137695f, 0.137695f, 0.925391972f, 0.862267971f) },
        { "Door", new RendererLightmapInfo(3, 0.259766012f, 0.259766012f, 0.0813513994f, 0.74287802f) },
        { "Pillar", new RendererLightmapInfo(4, 0.489257991f, 0.489257991f, -0.00482229982f, 0.550486982f) },
        { "Net 1", new RendererLightmapInfo(3, 0.99902302f, 0.99902302f, 0.299872011f, 0.00810801983f) },
        { "ShootingAreaBox", new RendererLightmapInfo(2, 0.294921994f, 0.294921994f, 0.881507993f, -0.121333003f) },
        { "Platform", new RendererLightmapInfo(2, 0.372070014f, 0.372070014f, 0.316249996f, 0.140624002f) },
        { "SprtLights", new RendererLightmapInfo(2, 0.154296994f, 0.154296994f, 0.52126497f, 0.658156991f) },
        { "Wall 1", new RendererLightmapInfo(3, 0.99902302f, 0.99902302f, 0.462565988f, -0.64576f) },
        { "Fence", new RendererLightmapInfo(2, 0.149414003f, 0.149414003f, 0.977227986f, -0.0749901012f) },
        { "LightBase 2", new RendererLightmapInfo(4, 0.319335997f, 0.319335997f, 0.933937013f, 0.682188988f) },
        { "Ceiling 1", new RendererLightmapInfo(2, 0.976562977f, 0.976562977f, 0.310626f, 0.0369657986f) },
        { "Railing", new RendererLightmapInfo(1, 0.119140998f, 0.119140998f, 0.923349023f, -0.0265691001f) },
        { "LightBase", new RendererLightmapInfo(5, 0.46875f, 0.46875f, 0.767426014f, 0.508602023f) },
        { "Connecter", new RendererLightmapInfo(1, 0.379882991f, 0.379882991f, 0.387492001f, -0.0643550977f) },
        { "LED", new RendererLightmapInfo(1, 0.279296994f, 0.279296994f, 0.0138899004f, -0.143468007f) },
        { "LEDBase", new RendererLightmapInfo(1, 0.118164003f, 0.118164003f, 0.0259236991f, -0.0599753f) },
        { "Inside2", new RendererLightmapInfo(4, 0.926757991f, 0.926757991f, -0.00691694021f, -0.328103006f) },
        { "Inside3", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, 0.0655160025f, -0.608439028f) },
        { "Ground1", new RendererLightmapInfo(5, 0.727539003f, 0.727539003f, 0.589946985f, -0.367511004f) },
        { "Ground2", new RendererLightmapInfo(1, 0.576171994f, 0.576171994f, 0.328873008f, -0.291772991f) },
        { "Outside1", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.00998871028f, -0.0814526975f) },
        { "Outside2", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.269919991f, 0.0119701996f) },
        { "TunnelShelf", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.0104510998f, 0.0074881902f) },
        { "LightsAnimated", new RendererLightmapInfo(1, 0.149414003f, 0.149414003f, 0.509091973f, -0.0573229007f) },
        { "DoorRight1", new RendererLightmapInfo(1, 0.275391012f, 0.275391012f, 0.814638019f, 0.0907566026f) },
        { "DoorLeft1", new RendererLightmapInfo(1, 0.275391012f, 0.275391012f, 0.874208987f, 0.492119998f) },
        { "DoorLeft2", new RendererLightmapInfo(1, 0.275391012f, 0.275391012f, 0.333193004f, 0.119073004f) },
        { "DoorRight2", new RendererLightmapInfo(1, 0.275391012f, 0.275391012f, 0.586121976f, 0.415951997f) },
        { "Pipes", new RendererLightmapInfo(1, 0.283203006f, 0.283203006f, 0.703312993f, 0.406866997f) },
        { "Wall", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.00797611009f, -0.405537993f) },
        { "Net", new RendererLightmapInfo(5, 0.403320014f, 0.403320014f, 0.544294f, 0.599794984f) },
        { "WallRidge1", new RendererLightmapInfo(4, 0.15625f, 0.15625f, 0.952336013f, -0.0354404002f) },
        { "DoorPlate", new RendererLightmapInfo(1, 0.0839843974f, 0.0839843974f, 0.507333994f, -0.0251045991f) },
        { "DoorLocks", new RendererLightmapInfo(1, 0.0146484002f, 0.0146484002f, 0.00890885014f, 0.984104991f) },
        { "Cameras", new RendererLightmapInfo(1, 0.0390625f, 0.0390625f, 0.0283272993f, 0.0261646006f) },
        { "Frames", new RendererLightmapInfo(1, 0.387695014f, 0.387695014f, 0.811953008f, 0.0830022022f) },
        { "Glass", new RendererLightmapInfo(5, 0.214844003f, 0.214844003f, 0.735908985f, 0.786230981f) },
        { "Desk", new RendererLightmapInfo(2, 0.267578006f, 0.267578006f, 0.867770016f, 0.733570993f) },
        { "ShopTable", new RendererLightmapInfo(4, 0.186523005f, 0.186523005f, 0.974861026f, -0.0979833007f) },
        { "ShopWall", new RendererLightmapInfo(4, 0.320313007f, 0.320313007f, 0.321577996f, 0.50760603f) },
        { "DoorLocks2", new RendererLightmapInfo(1, 0.0253906008f, 0.0253906008f, 0.0315339006f, 0.0235029999f) },
        { "DoorPlate2", new RendererLightmapInfo(1, 0.0839843974f, 0.0839843974f, 0.507332981f, -0.0453647003f) },
        { "ArmoryDoor", new RendererLightmapInfo(1, 0.209960997f, 0.209960997f, 0.818149984f, 0.0541313998f) },
        { "Connecter1", new RendererLightmapInfo(1, 0.227539003f, 0.227539003f, 0.925630987f, 0.0043875901f) },
        { "Glass 1", new RendererLightmapInfo(1, 0.233398005f, 0.233398005f, 0.471516013f, -0.112809002f) },
        { "SignLight", new RendererLightmapInfo(2, 0.0117188003f, 0.0117188003f, 0.136138007f, 0.987664998f) },
        { "Room", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, 0.0320395008f, -0.278173f) },
        { "Room 1", new RendererLightmapInfo(4, 0.569335997f, 0.569335997f, 0.654147029f, 0.129025996f) },
        { "WindowsFrame", new RendererLightmapInfo(5, 0.31152299f, 0.31152299f, 0.656461f, 0.690672994f) },
        { "ShootingAreaWall", new RendererLightmapInfo(2, 0.173828006f, 0.173828006f, -0.00103846996f, 0.826861978f) },
        { "WallRidge2", new RendererLightmapInfo(0, 0.32910201f, 0.32910201f, 0.958631992f, 0.670911014f) },
        { "GroundLayer1", new RendererLightmapInfo(2, 0.976562977f, 0.976562977f, -0.00989468955f, -0.0183304008f) },
        { "DoorRight6", new RendererLightmapInfo(2, 0.275391012f, 0.275391012f, 0.419129997f, 0.538999021f) },
        { "DoorLeft6", new RendererLightmapInfo(2, 0.275391012f, 0.275391012f, 0.317568004f, 0.538995028f) },
        { "DoorPlate1", new RendererLightmapInfo(0, 0.0839843974f, 0.0839843974f, 0.266205013f, -0.0548033006f) },
        { "DoorFrames1", new RendererLightmapInfo(0, 0.209960997f, 0.209960997f, -0.00118606002f, -0.103095002f) },
        { "Inside", new RendererLightmapInfo(4, 0.338867009f, 0.338867009f, 0.401854992f, 0.660645008f) },
        { "Ground 1", new RendererLightmapInfo(4, 0.674804986f, 0.674804986f, 0.609441996f, -0.295114994f) },
        { "Ceiling 2", new RendererLightmapInfo(6, 0.865234017f, 0.865234017f, -0.0110360999f, -0.278645992f) },
        { "LightBase 3", new RendererLightmapInfo(2, 0.0634765998f, 0.0634765998f, 0.127646998f, 0.935892999f) },
        { "Lights 2", new RendererLightmapInfo(4, 0.149414003f, 0.149414003f, 0.339457989f, 0.759666979f) },
        { "Outside 1", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0187640991f, 0.0761044994f) },
        { "Pipes 2", new RendererLightmapInfo(4, 0.399414003f, 0.399414003f, 0.737564981f, 0.395471007f) },
        { "DoorFrames 1", new RendererLightmapInfo(1, 0.209960997f, 0.209960997f, 0.874790013f, 0.658623993f) },
    };

    // SpaceportAlpha: 1 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> SpaceportAlphaLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "JumpPark", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0174984001f, 0.0337136984f) },
    };

    // TempleOfTheRaven: 91 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> TempleOfTheRavenLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Pad", new RendererLightmapInfo(0, 0.0654297024f, 0.0654297024f, 0.391833991f, 0.615467012f) },
        { "Border1", new RendererLightmapInfo(1, 0.54589802f, 0.54589802f, 0.445686996f, 0.460606992f) },
        { "Border2", new RendererLightmapInfo(1, 0.502929986f, 0.502929986f, 0.500925004f, -0.00354140997f) },
        { "CocoTree_Leaves", new RendererLightmapInfo(1, 0.268554986f, 0.268554986f, 0.636271f, 0.463645995f) },
        { "CocoTree_Trunk", new RendererLightmapInfo(0, 0.142578006f, 0.142578006f, 0.233309999f, 0.778173029f) },
        { "Flower1", new RendererLightmapInfo(0, 0.120117001f, 0.120117001f, 0.77363199f, 0.566492021f) },
        { "Flower2", new RendererLightmapInfo(0, 0.193359002f, 0.193359002f, 0.931768f, 0.585781991f) },
        { "Flower3", new RendererLightmapInfo(0, 0.28222701f, 0.28222701f, -0.00754824979f, 0.721715987f) },
        { "GrassGroup1", new RendererLightmapInfo(0, 0.174805f, 0.174805f, 0.407655001f, 0.542246997f) },
        { "GrassGroup2", new RendererLightmapInfo(0, 0.181640998f, 0.181640998f, 0.536728978f, 0.511633992f) },
        { "GrassGroup3", new RendererLightmapInfo(0, 0.12793f, 0.12793f, 0.493328989f, 0.587962985f) },
        { "GrassGroup4", new RendererLightmapInfo(0, 0.133789003f, 0.133789003f, 0.904407978f, 0.617650986f) },
        { "InnerPillar1", new RendererLightmapInfo(0, 0.527343988f, 0.527343988f, 0.932439029f, 0.475015014f) },
        { "InnerPillar2", new RendererLightmapInfo(0, 0.346679986f, 0.346679986f, 0.127939001f, 0.537750006f) },
        { "Ivy1", new RendererLightmapInfo(0, 0.21875f, 0.21875f, 0.935510993f, 0.688637018f) },
        { "Ivy2", new RendererLightmapInfo(1, 0.362304986f, 0.362304986f, 0.839998007f, 0.056237001f) },
        { "Ivy3", new RendererLightmapInfo(0, 0.335938007f, 0.335938007f, 0.590107977f, 0.362213999f) },
        { "Ivy4", new RendererLightmapInfo(0, 0.37597701f, 0.37597701f, 0.130034f, 0.62570399f) },
        { "Ivy5", new RendererLightmapInfo(0, 0.31347701f, 0.31347701f, 0.0207857005f, 0.556851029f) },
        { "Ivy6", new RendererLightmapInfo(0, 0.404296994f, 0.404296994f, 0.814234018f, 0.595485985f) },
        { "JumpPad1", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.775132f, 0.609834015f) },
        { "JumpPad2", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.775132f, 0.582490027f) },
        { "JumpPad3", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.905013978f, 0.589326024f) },
        { "JumpPad4", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.493880987f, 0.555146992f) },
        { "JumpPad5", new RendererLightmapInfo(0, 0.129883006f, 0.129883006f, 0.457749009f, 0.539521992f) },
        { "LevelFloor1", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.00891902018f, -0.225602999f) },
        { "LevelFloor2", new RendererLightmapInfo(1, 0.837890983f, 0.837890983f, 0.426250994f, -0.588118017f) },
        { "LevelFloor3", new RendererLightmapInfo(0, 0.914062977f, 0.914062977f, 0.240187004f, 0.0920628011f) },
        { "MainPillar1", new RendererLightmapInfo(1, 0.338867009f, 0.338867009f, 0.433750987f, -0.0267852992f) },
        { "MainPillar2", new RendererLightmapInfo(1, 0.326171994f, 0.326171994f, 0.749638975f, -0.00420780014f) },
        { "MainPillar3", new RendererLightmapInfo(1, 0.296875f, 0.296875f, 0.432752013f, 0.0633910969f) },
        { "MainPillar4", new RendererLightmapInfo(1, 0.285156012f, 0.285156012f, 0.913676023f, 0.468708992f) },
        { "MainPillarTop1", new RendererLightmapInfo(1, 0.525390983f, 0.525390983f, 0.448466986f, 0.209839001f) },
        { "MainPillarTop2", new RendererLightmapInfo(1, 0.581054986f, 0.581054986f, 0.716957986f, 0.176092997f) },
        { "MainPillarTop3", new RendererLightmapInfo(1, 0.543945014f, 0.543945014f, 0.716893017f, 0.0858339965f) },
        { "MainPillarTop4", new RendererLightmapInfo(1, 0.534179986f, 0.534179986f, 0.473793f, 0.0858156979f) },
        { "MainPillarTop5", new RendererLightmapInfo(1, 0.489257991f, 0.489257991f, 0.839640975f, -0.172181994f) },
        { "MainPillarTop6", new RendererLightmapInfo(1, 0.476563007f, 0.476563007f, 0.595202029f, 0.188770995f) },
        { "MainWall1", new RendererLightmapInfo(0, 0.12793f, 0.12793f, 0.813426018f, 0.623593986f) },
        { "MainWall2", new RendererLightmapInfo(1, 0.453125f, 0.453125f, -0.00791305955f, -0.0474550016f) },
        { "MainWall3", new RendererLightmapInfo(0, 0.541992009f, 0.541992009f, 0.476328999f, -0.00477692997f) },
        { "Mountain1", new RendererLightmapInfo(1, 0.393554986f, 0.393554986f, -0.00296997f, 0.0443435013f) },
        { "Mountain2", new RendererLightmapInfo(1, 0.393554986f, 0.393554986f, 0.947710991f, -0.350944996f) },
        { "Mountain3", new RendererLightmapInfo(1, 0.241210997f, 0.241210997f, 0.915566027f, 0.461860001f) },
        { "PalmTree1", new RendererLightmapInfo(0, 0.0498046987f, 0.0498046987f, 0.238306999f, 0.850390017f) },
        { "PalmTree2", new RendererLightmapInfo(0, 0.0498046987f, 0.0498046987f, 0.237331003f, 0.845507026f) },
        { "PalmTree3", new RendererLightmapInfo(0, 0.0498046987f, 0.0498046987f, 0.124049f, 0.836718023f) },
        { "PalmTree4", new RendererLightmapInfo(0, 0.0498046987f, 0.0498046987f, 0.124049f, 0.831834972f) },
        { "PalmTreeLeaves1", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, -0.000746443984f, 0.675706983f) },
        { "PalmTreeLeaves2", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, 0.233629003f, 0.694262028f) },
        { "PalmTreeLeaves3", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, 0.233629003f, 0.675706983f) },
        { "PalmTreeLeaves4", new RendererLightmapInfo(0, 0.125976995f, 0.125976995f, 0.258042991f, 0.590745986f) },
        { "StoneDoors15", new RendererLightmapInfo(0, 0.32910201f, 0.32910201f, 0.932331979f, 0.519370973f) },
        { "StoneDoors16", new RendererLightmapInfo(0, 0.32910201f, 0.32910201f, 0.930410981f, 0.486481011f) },
        { "RavenLights1", new RendererLightmapInfo(0, 0.0625f, 0.0625f, -0.000240848996f, 0.774295986f) },
        { "RavenLights2", new RendererLightmapInfo(0, 0.0595703013f, 0.0595703013f, -0.000282663008f, 0.764585972f) },
        { "RavenLights3", new RendererLightmapInfo(0, 0.0644531026f, 0.0644531026f, 0.234130993f, 0.825563014f) },
        { "RavenLights4", new RendererLightmapInfo(0, 0.0644531026f, 0.0644531026f, 0.234130993f, 0.813844025f) },
        { "RavenLights5", new RendererLightmapInfo(0, 0.102539003f, 0.102539003f, -0.000675709976f, 0.765432f) },
        { "Lights1", new RendererLightmapInfo(1, 0.124022998f, 0.124022998f, 0.960822999f, -0.0287372991f) },
        { "Lights2", new RendererLightmapInfo(1, 0.0839843974f, 0.0839843974f, 0.982672989f, 0.915454984f) },
        { "MainWall", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.00787451025f, 0.00858819019f) },
        { "Pillars", new RendererLightmapInfo(1, 0.626953006f, 0.626953006f, 0.839353979f, -0.418177009f) },
        { "Rocks1", new RendererLightmapInfo(1, 0.142578006f, 0.142578006f, 0.957832992f, -0.0116029f) },
        { "Rocks2", new RendererLightmapInfo(1, 0.0947265998f, 0.0947265998f, 0.982353985f, 0.874009013f) },
        { "Rocks3", new RendererLightmapInfo(1, 0.0283202995f, 0.0283202995f, 0.991658986f, -0.0256402995f) },
        { "Rocks4", new RendererLightmapInfo(1, 0.125976995f, 0.125976995f, 0.981872976f, 0.856606007f) },
        { "Spiderweb1", new RendererLightmapInfo(1, 0.0351562984f, 0.0351562984f, 0.996169984f, -0.0315603986f) },
        { "Spiderweb2", new RendererLightmapInfo(1, 0.0546875f, 0.0546875f, 0.991137981f, -0.0461524017f) },
        { "StoneDoors1", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.932573974f, 0.432114005f) },
        { "StoneDoors2", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.577679992f, 0.443486005f) },
        { "StoneDoors3", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.93245399f, 0.45945099f) },
        { "StoneDoors4", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.333745003f, 0.407658011f) },
        { "StoneDoors6", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.534503996f, 0.38658601f) },
        { "StoneDoors5", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.705793977f, 0.415980995f) },
        { "StoneDoors10", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.286318004f, 0.407860011f) },
        { "StoneDoors9", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.705709994f, 0.443417996f) },
        { "StoneDoors8", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.33276999f, 0.380185992f) },
        { "StoneDoors7", new RendererLightmapInfo(0, 0.26660201f, 0.26660201f, 0.285679996f, 0.380109996f) },
        { "StoneDoors11", new RendererLightmapInfo(0, 0.370117009f, 0.370117009f, 0.283748001f, 0.341706991f) },
        { "StoneDoors12", new RendererLightmapInfo(0, 0.370117009f, 0.370117009f, 0.438282996f, 0.341262996f) },
        { "StoneDoors13", new RendererLightmapInfo(0, 0.123047002f, 0.123047002f, -0.00157279999f, 0.693171024f) },
        { "StoneDoors14", new RendererLightmapInfo(0, 0.123047002f, 0.123047002f, -0.00120187004f, 0.728164017f) },
        { "TopGarden", new RendererLightmapInfo(1, 0.550781012f, 0.550781012f, 0.712599993f, 0.455179006f) },
        { "Goldpile", new RendererLightmapInfo(0, 0.0507812984f, 0.0507812984f, 0.234294996f, 0.84887898f) },
        { "TreasureChest", new RendererLightmapInfo(0, 0.432617009f, 0.432617009f, 0.811396003f, 0.443354011f) },
        { "UnderwaterRoomWalls", new RendererLightmapInfo(0, 0.607421994f, 0.607421994f, 0.805368006f, 0.0441154987f) },
        { "Vine1", new RendererLightmapInfo(0, 0.208984002f, 0.208984002f, 0.534344018f, 0.528468013f) },
        { "Vine2", new RendererLightmapInfo(1, 0.251953006f, 0.251953006f, 0.346500009f, 0.448477f) },
        { "Vine3", new RendererLightmapInfo(0, 0.26464799f, 0.26464799f, 0.333929002f, 0.455516011f) },
        { "Vine4", new RendererLightmapInfo(1, 0.189453006f, 0.189453006f, 0.947085977f, -0.106351003f) },
    };

    // MonkeyIsland: 91 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> MonkeyIslandLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "BrokenPillar10", new RendererLightmapInfo(0, 0.09375f, 0.09375f, 0.224977002f, 0.806457996f) },
        { "BrokenPillar9", new RendererLightmapInfo(0, 0.0898438022f, 0.0898438022f, 0.0580304004f, 0.469327003f) },
        { "BrokenPillar8", new RendererLightmapInfo(0, 0.0898438022f, 0.0898438022f, 0.431077003f, 0.878507018f) },
        { "BrokenPillar7", new RendererLightmapInfo(0, 0.0869140998f, 0.0869140998f, 0.971150994f, 0.734054029f) },
        { "BrokenPillar6", new RendererLightmapInfo(0, 0.09375f, 0.09375f, 0.192750007f, 0.774231017f) },
        { "BrokenPillar5", new RendererLightmapInfo(0, 0.0869140998f, 0.0869140998f, 0.278768003f, 0.842453003f) },
        { "BrokenPillar4", new RendererLightmapInfo(0, 0.0820313022f, 0.0820313022f, 0.958512008f, 0.762835979f) },
        { "BrokenPillar3", new RendererLightmapInfo(0, 0.0820313022f, 0.0820313022f, 0.158708006f, 0.739399016f) },
        { "BrokenPillar2", new RendererLightmapInfo(1, 0.302733988f, 0.302733988f, 0.573202014f, 0.578251004f) },
        { "BrokenPillar1", new RendererLightmapInfo(0, 0.116210997f, 0.116210997f, 0.666144013f, 0.884594977f) },
        { "FullPillar7", new RendererLightmapInfo(0, 0.107422002f, 0.107422002f, 0.0246447995f, 0.450089991f) },
        { "FullPillar6", new RendererLightmapInfo(0, 0.110352002f, 0.110352002f, 0.126176998f, 0.698045015f) },
        { "FullPillar5", new RendererLightmapInfo(0, 0.110352002f, 0.110352002f, 0.192582995f, 0.786912024f) },
        { "FullPillar4", new RendererLightmapInfo(0, 0.161133006f, 0.161133006f, 0.533801973f, 0.839434981f) },
        { "FullPillar3", new RendererLightmapInfo(0, 0.110352002f, 0.110352002f, 0.851761997f, 0.853318989f) },
        { "FullPillar2", new RendererLightmapInfo(0, 0.0732422024f, 0.0732422024f, 0.385389f, 0.903584003f) },
        { "FullPillar1", new RendererLightmapInfo(0, 0.0732422024f, 0.0732422024f, 0.753552973f, 0.928974986f) },
        { "Bridge3", new RendererLightmapInfo(1, 0.457031012f, 0.457031012f, 0.846225977f, 0.549450994f) },
        { "Bridge2", new RendererLightmapInfo(1, 0.40527299f, 0.40527299f, 0.621095002f, 0.477187991f) },
        { "Bridge1", new RendererLightmapInfo(1, 0.675781012f, 0.675781012f, 0.652961016f, 0.108249001f) },
        { "SmallStoneWalls", new RendererLightmapInfo(1, 0.209960997f, 0.209960997f, 0.554772019f, 0.580354989f) },
        { "Shipwreck", new RendererLightmapInfo(1, 0.949218988f, 0.949218988f, -0.0155367004f, 0.0625637993f) },
        { "MonkeyTower", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.00503657013f, -0.556317985f) },
        { "StandardTreeLeaves5", new RendererLightmapInfo(1, 0.0644531026f, 0.0644531026f, 0.967485011f, -0.0117031997f) },
        { "StandardTreeLeaves4", new RendererLightmapInfo(1, 0.322266012f, 0.322266012f, 0.404401988f, 0.679364979f) },
        { "StandardTreeLeaves3", new RendererLightmapInfo(1, 0.210938007f, 0.210938007f, 0.482315987f, 0.625693977f) },
        { "StandardTreeLeaves2", new RendererLightmapInfo(0, 0.0810547024f, 0.0810547024f, 0.408937007f, 0.882641971f) },
        { "StandardTreeLeaves1", new RendererLightmapInfo(0, 0.186523005f, 0.186523005f, 0.278908014f, 0.813718021f) },
        { "StandardTreeTrunk5", new RendererLightmapInfo(1, 0.0292968992f, 0.0292968992f, 0.000395706011f, 0.0793794021f) },
        { "StandardTreeTrunk4", new RendererLightmapInfo(0, 0.145508006f, 0.145508006f, 0.356496006f, 0.854869008f) },
        { "StandardTreeTrunk3", new RendererLightmapInfo(0, 0.0927734002f, 0.0927734002f, 0.777323008f, 0.907042027f) },
        { "StandardTreeTrunk2", new RendererLightmapInfo(0, 0.0351562984f, 0.0351562984f, 0.459127992f, 0.964025974f) },
        { "StandardTreeTrunk1", new RendererLightmapInfo(0, 0.0869140998f, 0.0869140998f, 0.540103972f, 0.864091992f) },
        { "PalTreeLeaves9", new RendererLightmapInfo(1, 0.100585997f, 0.100585997f, 0.955139995f, 0.229665995f) },
        { "PalTreeLeaves8", new RendererLightmapInfo(1, 0.120117001f, 0.120117001f, 0.910771012f, 0.635545015f) },
        { "PalTreeLeaves7", new RendererLightmapInfo(1, 0.0800781026f, 0.0800781026f, 0.961286008f, 0.139373004f) },
        { "PalTreeLeaves6", new RendererLightmapInfo(0, 0.150390998f, 0.150390998f, 0.466141999f, 0.850777984f) },
        { "PalTreeLeaves5", new RendererLightmapInfo(0, 0.078125f, 0.078125f, 0.72113198f, 0.922317028f) },
        { "PalTreeLeaves4", new RendererLightmapInfo(0, 0.101562999f, 0.101562999f, 0.57902199f, 0.898200989f) },
        { "PalTreeLeaves3", new RendererLightmapInfo(0, 0.0761718974f, 0.0761718974f, 0.688741982f, 0.923780024f) },
        { "PalTreeLeaves2", new RendererLightmapInfo(1, 0.078125f, 0.078125f, 0.962148011f, 0.115799002f) },
        { "PalTreeLeaves1", new RendererLightmapInfo(1, 0.0957031026f, 0.0957031026f, 0.424122989f, 0.575376987f) },
        { "PalmTreeTrunk9", new RendererLightmapInfo(0, 0.0976563022f, 0.0976563022f, 0.125736997f, 0.676968992f) },
        { "PalmTreeTrunk8", new RendererLightmapInfo(1, 0.126953006f, 0.126953006f, 0.961085975f, 0.0428458005f) },
        { "PalmTreeTrunk7", new RendererLightmapInfo(1, 0.150390998f, 0.150390998f, 0.351628989f, 0.151290998f) },
        { "PalmTreeTrunk6", new RendererLightmapInfo(1, 0.101562999f, 0.101562999f, 0.965735018f, 0.0250285994f) },
        { "PalmTreeTrunk5", new RendererLightmapInfo(1, 0.18457f, 0.18457f, 0.815798998f, 0.625355005f) },
        { "PalmTreeTrunk4", new RendererLightmapInfo(1, 0.131835997f, 0.131835997f, 0.859731019f, 0.61164403f) },
        { "PalmTreeTrunk3", new RendererLightmapInfo(0, 0.101562999f, 0.101562999f, 0.357131004f, 0.873193026f) },
        { "PalmTreeTrunk2", new RendererLightmapInfo(1, 0.105469003f, 0.105469003f, 0.966387987f, -0.0667179972f) },
        { "PalmTreeTrunk1", new RendererLightmapInfo(1, 0.121094003f, 0.121094003f, 0.954477012f, 0.178359002f) },
        { "BridgeIvy3", new RendererLightmapInfo(1, 0.0820313022f, 0.0820313022f, 0.983015001f, -0.0420880988f) },
        { "BridgeIvy2", new RendererLightmapInfo(1, 0.147460997f, 0.147460997f, 0.809701025f, 0.714137018f) },
        { "BridgeIvy1", new RendererLightmapInfo(0, 0.0742188022f, 0.0742188022f, 0.385607988f, 0.879903972f) },
        { "Ivy6", new RendererLightmapInfo(1, 0.15332f, 0.15332f, 0.911086023f, 0.653012991f) },
        { "Ivy5", new RendererLightmapInfo(0, 0.103515998f, 0.103515998f, 0.625715017f, 0.897396982f) },
        { "Ivy4", new RendererLightmapInfo(0, 0.112305f, 0.112305f, 0.408039004f, 0.887768984f) },
        { "Ivy3", new RendererLightmapInfo(1, 0.189453006f, 0.189453006f, 0.403659999f, 0.564024985f) },
        { "Ivy2", new RendererLightmapInfo(1, 0.165039003f, 0.165039003f, 0.850220978f, 0.122860998f) },
        { "Ivy1", new RendererLightmapInfo(1, 0.251953006f, 0.251953006f, 0.574643016f, 0.751230001f) },
        { "UnderwaterPlants2", new RendererLightmapInfo(1, 0.0800781026f, 0.0800781026f, 0.977708995f, 0.858201027f) },
        { "UnderwaterPlants1", new RendererLightmapInfo(1, 0.370117009f, 0.370117009f, 0.736133993f, -0.152311996f) },
        { "SmallPlant6", new RendererLightmapInfo(1, 0.0458984002f, 0.0458984002f, 0.977403998f, 0.930579007f) },
        { "SmallPlant5", new RendererLightmapInfo(0, 0.104492001f, 0.104492001f, 0.867232025f, 0.896354973f) },
        { "SmallPlant4", new RendererLightmapInfo(1, 0.0595703013f, 0.0595703013f, 0.977217972f, 0.900197983f) },
        { "SmallPlant3", new RendererLightmapInfo(0, 0.0517578013f, 0.0517578013f, 0.884549975f, 0.912118018f) },
        { "SmallPlant2", new RendererLightmapInfo(0, 0.0595703013f, 0.0595703013f, 0.830733001f, 0.903127015f) },
        { "SmallPlant1", new RendererLightmapInfo(0, 0.0693359002f, 0.0693359002f, -0.000454077992f, 0.498335987f) },
        { "Grass8", new RendererLightmapInfo(1, 0.488281012f, 0.488281012f, 0.418381006f, -0.147434995f) },
        { "Grass7", new RendererLightmapInfo(1, 0.197265998f, 0.197265998f, 0.783381999f, 0.0731825978f) },
        { "Grass6", new RendererLightmapInfo(1, 0.234375f, 0.234375f, 0.848191023f, 0.634343982f) },
        { "Grass5", new RendererLightmapInfo(1, 0.216796994f, 0.216796994f, 0.404561996f, 0.620163023f) },
        { "Grass4", new RendererLightmapInfo(1, 0.18457f, 0.18457f, 0.967616022f, -0.0968798026f) },
        { "Grass3", new RendererLightmapInfo(1, 0.15918f, 0.15918f, 0.977505982f, 0.840278983f) },
        { "Grass2", new RendererLightmapInfo(1, 0.351563007f, 0.351563007f, 0.719662011f, 0.647787988f) },
        { "Grass1", new RendererLightmapInfo(0, 0.333007991f, 0.333007991f, -0.00196087989f, 0.478233993f) },
        { "Tunnel2", new RendererLightmapInfo(1, 0.341796994f, 0.341796994f, 0.73368901f, 0.521785975f) },
        { "Tunnel1", new RendererLightmapInfo(0, 0.588867009f, 0.588867009f, 0.915872991f, 0.414251f) },
        { "TerrainOuter", new RendererLightmapInfo(0, 0.123047002f, 0.123047002f, 0.000487980986f, 0.55517602f) },
        { "TerrainMain", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.00322800991f, -0.0181543995f) },
        { "StoneRamp", new RendererLightmapInfo(0, 0.37402299f, 0.37402299f, 0.182512f, 0.628699005f) },
        { "Cave", new RendererLightmapInfo(1, 0.716796994f, 0.716796994f, 0.420758992f, -0.0880080983f) },
        { "WaterRocks", new RendererLightmapInfo(2, 2.28125f, 2.28125f, -0.0208218005f, -1.53261995f) },
        { "Rocks5", new RendererLightmapInfo(1, 0.25097701f, 0.25097701f, 0.930692971f, 0.619894981f) },
        { "Rocks4", new RendererLightmapInfo(0, 0.119140998f, 0.119140998f, 0.904779971f, 0.880881011f) },
        { "Rocks3", new RendererLightmapInfo(1, 0.245116994f, 0.245116994f, 0.480818987f, 0.517952025f) },
        { "Rocks2", new RendererLightmapInfo(0, 0.108397998f, 0.108397998f, 0.88341099f, 0.837399006f) },
        { "Rocks1", new RendererLightmapInfo(1, 0.578125f, 0.578125f, 0.771197975f, -0.129760996f) },
        { "JumpPad", new RendererLightmapInfo(0, 0.191405997f, 0.191405997f, -0.000928346999f, 0.808089972f) },
        { "FirePot", new RendererLightmapInfo(0, 0.0380859002f, 0.0380859002f, 0.792331994f, 0.963230014f) },
        { "fire_pot", new RendererLightmapInfo(0, 0.0361328013f, 0.0361328013f, 0.831402004f, 0.963238001f) },
    };

    // CuberStrike: 16 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> CuberStrikeLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "polySurface1485", new RendererLightmapInfo(14, 0.99902302f, 0.99902302f, -0.0159115996f, 0.0574066006f) },
        { "polySurface1486", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0159029998f, 0.0163141005f) },
        { "polySurface1487", new RendererLightmapInfo(13, 0.99902302f, 0.99902302f, -0.0158462003f, 0.0163141005f) },
        { "polySurface1488", new RendererLightmapInfo(9, 0.99902302f, 0.99902302f, -0.0158083998f, 0.0163141005f) },
        { "polySurface1489", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.0159124006f, 0.0622977018f) },
        { "polySurface1490", new RendererLightmapInfo(10, 0.99902302f, 0.99902302f, -0.0158556998f, -0.0159124006f) },
        { "polySurface1491", new RendererLightmapInfo(11, 0.99902302f, 0.99902302f, -0.0158556998f, 0.0163141005f) },
        { "polySurface1492", new RendererLightmapInfo(15, 0.99902302f, 0.99902302f, -0.0157989003f, -0.0159124006f) },
        { "polySurface1493", new RendererLightmapInfo(7, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158840995f) },
        { "polySurface1494", new RendererLightmapInfo(12, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158462003f) },
        { "polySurface1495", new RendererLightmapInfo(8, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158840995f) },
        { "polySurface1496", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158178993f) },
        { "polySurface1497", new RendererLightmapInfo(3, 0.99902302f, 0.99902302f, -0.0158540998f, -0.0159131009f) },
        { "polySurface1498", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, -0.0159021001f, -0.0159210991f) },
        { "polySurface1499", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, -0.0159082003f, -0.0159082003f) },
        { "polySurface1500", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0120106004f, 0.0123321004f) },
    };

    // CuberSpace: 16 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> CuberSpaceLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "polySurface1485", new RendererLightmapInfo(14, 0.99902302f, 0.99902302f, -0.0159115996f, 0.0574066006f) },
        { "polySurface1486", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0159029998f, 0.0163141005f) },
        { "polySurface1487", new RendererLightmapInfo(13, 0.99902302f, 0.99902302f, -0.0158462003f, 0.0163141005f) },
        { "polySurface1488", new RendererLightmapInfo(9, 0.99902302f, 0.99902302f, -0.0158083998f, 0.0163141005f) },
        { "polySurface1489", new RendererLightmapInfo(4, 0.99902302f, 0.99902302f, -0.0159124006f, 0.0622977018f) },
        { "polySurface1490", new RendererLightmapInfo(10, 0.99902302f, 0.99902302f, -0.0158556998f, -0.0159124006f) },
        { "polySurface1491", new RendererLightmapInfo(11, 0.99902302f, 0.99902302f, -0.0158556998f, 0.0163141005f) },
        { "polySurface1492", new RendererLightmapInfo(15, 0.99902302f, 0.99902302f, -0.0157989003f, -0.0159124006f) },
        { "polySurface1493", new RendererLightmapInfo(7, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158840995f) },
        { "polySurface1494", new RendererLightmapInfo(12, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158462003f) },
        { "polySurface1495", new RendererLightmapInfo(8, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158840995f) },
        { "polySurface1496", new RendererLightmapInfo(5, 0.99902302f, 0.99902302f, -0.0159124006f, -0.0158178993f) },
        { "polySurface1497", new RendererLightmapInfo(3, 0.99902302f, 0.99902302f, -0.0158540998f, -0.0159131009f) },
        { "polySurface1498", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, -0.0159021001f, -0.0159210991f) },
        { "polySurface1499", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, -0.0159082003f, -0.0159082003f) },
        { "polySurface1500", new RendererLightmapInfo(0, 0.99902302f, 0.99902302f, -0.0120106004f, 0.0123321004f) },
    };

    // AqualabResearchHub: 62 renderers (name-based)
    static readonly Dictionary<string, RendererLightmapInfo> AqualabResearchHubLightmapData =
        new Dictionary<string, RendererLightmapInfo>
    {
        { "Dome", new RendererLightmapInfo(6, 0.99902302f, 0.99902302f, -0.00131690002f, 0.00240442995f) },
        { "domeFraming", new RendererLightmapInfo(1, 0.99902302f, 0.99902302f, -0.0146907996f, -0.0158914998f) },
        { "glassDome", new RendererLightmapInfo(2, 0.99902302f, 0.99902302f, -0.516695023f, 0.540690005f) },
        { "detail2", new RendererLightmapInfo(4, 0.166991994f, 0.166991994f, 0.86649102f, -0.0139698004f) },
        { "Room4", new RendererLightmapInfo(5, 0.46875f, 0.46875f, -0.00580521021f, -0.00584597979f) },
        { "Lights3", new RendererLightmapInfo(3, 0.03125f, 0.03125f, 0.97288698f, 0.967949986f) },
        { "GroundFrame2", new RendererLightmapInfo(4, 0.118164003f, 0.118164003f, 0.162885994f, 0.842455029f) },
        { "pipes3", new RendererLightmapInfo(3, 0.197265998f, 0.197265998f, 0.650328994f, -0.0021883999f) },
        { "GroundFrame4", new RendererLightmapInfo(4, 0.199219003f, 0.199219003f, 0.546387017f, 0.000488280988f) },
        { "Lights2", new RendererLightmapInfo(4, 0.0400390998f, 0.0400390998f, 0.155431002f, 0.919705987f) },
        { "Machine", new RendererLightmapInfo(3, 0.556640983f, 0.556640983f, -0.00699908007f, 0.449997991f) },
        { "panel4", new RendererLightmapInfo(3, 0.0839843974f, 0.0839843974f, 0.823558986f, 0.114311002f) },
        { "rocks1", new RendererLightmapInfo(5, 0.0078125f, 0.0078125f, 0.0707404986f, 0.991569996f) },
        { "GroundPiece4", new RendererLightmapInfo(4, 0.253906012f, 0.253906012f, 0.152684003f, 0.749010026f) },
        { "detail1", new RendererLightmapInfo(4, 0.166991994f, 0.166991994f, 0.81863898f, -0.0139698004f) },
        { "PoleLight1", new RendererLightmapInfo(3, 0.0595703013f, 0.0595703013f, 0.910013974f, 0.941605985f) },
        { "LightFrames1", new RendererLightmapInfo(4, 0.0771484002f, 0.0771484002f, 0.0338753983f, 0.882598996f) },
        { "CeilingVents", new RendererLightmapInfo(2, 0.0742188022f, 0.0742188022f, 0.131669998f, 0.925840974f) },
        { "Room5", new RendererLightmapInfo(4, 0.46875f, 0.46875f, -0.00580342999f, -0.00584597979f) },
        { "PoleLightBulb2", new RendererLightmapInfo(3, 0.0166015998f, 0.0166015998f, 0.983820975f, 0.982932985f) },
        { "Lights4", new RendererLightmapInfo(3, 0.03125f, 0.03125f, 0.977769017f, -0.0252142996f) },
        { "Room2", new RendererLightmapInfo(3, 0.39160201f, 0.39160201f, -0.00801896956f, -0.00818851963f) },
        { "GroundPiece6", new RendererLightmapInfo(4, 0.15332f, 0.15332f, 0.390257001f, 0.848025978f) },
        { "GroundFrame3", new RendererLightmapInfo(4, 0.199219003f, 0.199219003f, 0.435059011f, 0.000488280988f) },
        { "panel1", new RendererLightmapInfo(3, 0.0839843974f, 0.0839843974f, 0.926097989f, 0.899466991f) },
        { "Lights1", new RendererLightmapInfo(4, 0.0400390998f, 0.0400390998f, 0.792150021f, 0.95974499f) },
        { "Room1", new RendererLightmapInfo(3, 0.39160201f, 0.39160201f, 0.519653022f, 0.614184976f) },
        { "pipes4", new RendererLightmapInfo(3, 0.12207f, 0.12207f, 0.88013798f, -0.000809877005f) },
        { "door2", new RendererLightmapInfo(5, 0.0390625f, 0.0390625f, 0.0643204972f, 0.960061014f) },
        { "walls2", new RendererLightmapInfo(4, 0.182616994f, 0.182616994f, 0.738274992f, -0.0153225996f) },
        { "rocks3", new RendererLightmapInfo(3, 0.0078125f, 0.0078125f, 0.97992003f, 0.991569996f) },
        { "Chimney", new RendererLightmapInfo(0, 0.350585997f, 0.350585997f, -0.00424946006f, 0.666675985f) },
        { "pipes1", new RendererLightmapInfo(3, 0.197265998f, 0.197265998f, 0.517606974f, -0.00106369006f) },
        { "door1", new RendererLightmapInfo(5, 0.0390625f, 0.0390625f, 0.0672501028f, 0.960061014f) },
        { "LightFrames2", new RendererLightmapInfo(4, 0.0771484002f, 0.0771484002f, 0.766296983f, 0.922639012f) },
        { "Props1", new RendererLightmapInfo(4, 0.144530997f, 0.144530997f, 0.470681995f, 0.856621027f) },
        { "pipes2", new RendererLightmapInfo(3, 0.12207f, 0.12207f, 0.823498011f, -0.000809815014f) },
        { "GroundFrame1", new RendererLightmapInfo(4, 0.118164003f, 0.118164003f, 0.798210025f, 0.88341099f) },
        { "GroundPiece2", new RendererLightmapInfo(4, 0.253906012f, 0.253906012f, -0.00258991006f, 0.749010026f) },
        { "panel3", new RendererLightmapInfo(3, 0.0839843974f, 0.0839843974f, 0.898754001f, 0.839896977f) },
        { "Room3", new RendererLightmapInfo(2, 0.559570014f, 0.559570014f, -0.00707371021f, -0.00701845996f) },
        { "fence2", new RendererLightmapInfo(3, 0.0527344011f, 0.0527344011f, 0.926491976f, 0.947313011f) },
        { "fence1 1", new RendererLightmapInfo(3, 0.146484002f, 0.146484002f, 0.777979016f, -0.0121942004f) },
        { "Props3", new RendererLightmapInfo(3, 0.0361328013f, 0.0361328013f, 0.967778027f, -0.0273784995f) },
        { "PoleLightBulb1", new RendererLightmapInfo(3, 0.0166015998f, 0.0166015998f, 0.981868029f, 0.982932985f) },
        { "MinddlePath", new RendererLightmapInfo(3, 0.233398005f, 0.233398005f, 0.363757014f, -0.0067002601f) },
        { "GroundPiece5", new RendererLightmapInfo(4, 0.15332f, 0.15332f, 0.309201986f, 0.848025978f) },
        { "rocks2", new RendererLightmapInfo(5, 0.0078125f, 0.0078125f, 0.0726936013f, 0.991569996f) },
        { "Props2", new RendererLightmapInfo(4, 0.144530997f, 0.144530997f, 0.600565016f, 0.856621027f) },
        { "fence1", new RendererLightmapInfo(3, 0.0527344011f, 0.0527344011f, 0.936513007f, -0.0359911993f) },
        { "LightFrames3", new RendererLightmapInfo(3, 0.0439453013f, 0.0439453013f, 0.944251001f, 0.955915987f) },
        { "Door1", new RendererLightmapInfo(4, 0.0976563022f, 0.0976563022f, -0.000791725994f, 0.862389982f) },
        { "Door2", new RendererLightmapInfo(4, 0.0976563022f, 0.0976563022f, 0.731630027f, 0.902428985f) },
        { "LightFrames4", new RendererLightmapInfo(3, 0.0439453013f, 0.0439453013f, 0.954020977f, -0.0354406983f) },
        { "RoomWindows", new RendererLightmapInfo(2, 0.0566405989f, 0.0566405989f, 0.0937547013f, 0.961914003f) },
        { "PoleLight2", new RendererLightmapInfo(3, 0.0595703013f, 0.0595703013f, 0.894156992f, 0.939508021f) },
        { "walls1", new RendererLightmapInfo(4, 0.182616994f, 0.182616994f, 0.658195972f, -0.0153225996f) },
        { "panel2", new RendererLightmapInfo(4, 0.0839843974f, 0.0839843974f, 0.91535598f, -0.00678303977f) },
        { "fence2 1", new RendererLightmapInfo(3, 0.146484002f, 0.146484002f, 0.800440013f, -0.0121942004f) },
        { "GroundPiece3", new RendererLightmapInfo(5, 0.132813007f, 0.132813007f, 0.0312041007f, 0.867833972f) },
        { "GroundPiece1", new RendererLightmapInfo(5, 0.132813007f, 0.132813007f, -0.00102241f, 0.867833972f) },
        { "Props4", new RendererLightmapInfo(3, 0.0556640998f, 0.0556640998f, 0.958724022f, 0.943688989f) },
    };

    // =====================================================================
    // POSITION-BASED LIGHTMAP DATA (duplicate names, different positions)
    // Matched by Vector3.Distance(renderer.transform.position, entry.position)
    // =====================================================================

    // LostParadise2: 18 renderers (position-based)
    static readonly PositionLightmapInfo[] LostParadise2PositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(88.866836f, 37.106135f, -78.663369f), 1, new Vector4(0.0693359002f, 0.0693359002f, 8.57166015e-07f, 0.930168986f)),
        new PositionLightmapInfo(new Vector3(91.300224f, 18.065615f, -18.285066f), 1, new Vector4(0.0693359002f, 0.0693359002f, 8.57166015e-07f, 0.926262975f)),
        new PositionLightmapInfo(new Vector3(-16.178271f, 27.035425f, 32.305172f), 0, new Vector4(0.0693359002f, 0.0693359002f, 0.326173007f, -0.0659246966f)),
        new PositionLightmapInfo(new Vector3(88.866837f, 37.576927f, -78.663368f), 2, new Vector4(0.188476995f, 0.188476995f, -0.0147793004f, 0.652926981f)),
        new PositionLightmapInfo(new Vector3(91.300224f, 18.536407f, -18.285065f), 2, new Vector4(0.188476995f, 0.188476995f, 0.27135399f, 0.654879987f)),
        new PositionLightmapInfo(new Vector3(-16.17827f, 27.506218f, 32.305172f), 0, new Vector4(0.188476995f, 0.188476995f, -0.0479823984f, 0.365817994f)),
        new PositionLightmapInfo(new Vector3(88.866837f, 37.576927f, -78.663368f), 1, new Vector4(0.125976995f, 0.125976995f, 0.789533973f, 0.509434998f)),
        new PositionLightmapInfo(new Vector3(91.300224f, 18.536407f, -18.285065f), 2, new Vector4(0.125976995f, 0.125976995f, 0.00144837005f, 0.54459101f)),
        new PositionLightmapInfo(new Vector3(-16.17827f, 27.506218f, 32.305172f), 0, new Vector4(0.125976995f, 0.125976995f, 0.200666994f, -0.000330635987f)),
        new PositionLightmapInfo(new Vector3(88.866837f, 37.576927f, -78.663368f), 2, new Vector4(0.104492001f, 0.104492001f, 0.886229992f, 0.598145008f)),
        new PositionLightmapInfo(new Vector3(91.300224f, 18.536407f, -18.285065f), 2, new Vector4(0.104492001f, 0.104492001f, 0.781737983f, 0.329589993f)),
        new PositionLightmapInfo(new Vector3(-16.17827f, 27.506218f, 32.305172f), 2, new Vector4(0.104492001f, 0.104492001f, 0.172362998f, 0.61767602f)),
        new PositionLightmapInfo(new Vector3(88.866837f, 37.576927f, -78.663368f), 2, new Vector4(0.105469003f, 0.105469003f, 0.886781991f, 0.899120986f)),
        new PositionLightmapInfo(new Vector3(91.300224f, 18.536407f, -18.285065f), 2, new Vector4(0.105469003f, 0.105469003f, 0.170962006f, 0.51630801f)),
        new PositionLightmapInfo(new Vector3(-16.17827f, 27.506218f, 32.305172f), 2, new Vector4(0.105469003f, 0.105469003f, 0.531313002f, 0.470409989f)),
        new PositionLightmapInfo(new Vector3(-68.832573f, 0.948788f, 9.259949f), 2, new Vector4(0.191405997f, 0.191405997f, 0.320360988f, 0.808089972f)),
        new PositionLightmapInfo(new Vector3(-27.933964f, 2.10279f, -3.828539f), 2, new Vector4(0.191405997f, 0.191405997f, 0.508836985f, 0.808089972f)),
        new PositionLightmapInfo(new Vector3(-16.586519f, 0.936104f, 59.815216f), 2, new Vector4(0.191405997f, 0.191405997f, 0.697314024f, 0.808089972f)),
    };

    // Spaceship: 28 renderers (position-based)
    static readonly PositionLightmapInfo[] SpaceshipPositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(57.178997f, -4.741281f, 91.290733f), 1, new Vector4(0.0683593974f, 0.0683593974f, 0.92149502f, 0.932461023f)),
        new PositionLightmapInfo(new Vector3(0.0f, 0.0f, 0.0f), 1, new Vector4(0.126953006f, 0.126953006f, 0.0159146003f, 0.0196109004f)),
        new PositionLightmapInfo(new Vector3(61.382988f, -6.868941f, 60.67981f), 1, new Vector4(0.0761718974f, 0.0761718974f, 0.924695015f, 0.0411325991f)),
        new PositionLightmapInfo(new Vector3(60.978165f, -6.868941f, 53.427632f), 1, new Vector4(0.0761718974f, 0.0761718974f, -0.000109945002f, 0.129999995f)),
        new PositionLightmapInfo(new Vector3(53.56073f, -6.868941f, 55.032727f), 1, new Vector4(0.0761718974f, 0.0761718974f, -0.000109945002f, 0.155389994f)),
        new PositionLightmapInfo(new Vector3(60.978165f, -6.868941f, 48.455647f), 1, new Vector4(0.0761718974f, 0.0761718974f, 0.93641299f, 0.121211f)),
        new PositionLightmapInfo(new Vector3(53.786392f, -6.868941f, 47.688836f), 1, new Vector4(0.0761718974f, 0.0761718974f, -0.000109945002f, 0.180781007f)),
        new PositionLightmapInfo(new Vector3(53.786392f, -6.868941f, 46.294167f), 1, new Vector4(0.0761718974f, 0.0761718974f, 0.93641299f, 0.146601006f)),
        new PositionLightmapInfo(new Vector3(25.732712f, -6.868941f, 3.827897f), 1, new Vector4(0.0761718974f, 0.0761718974f, -0.000109945002f, 0.206172004f)),
        new PositionLightmapInfo(new Vector3(-20.623917f, -1.787819f, -12.104759f), 0, new Vector4(0.0761718974f, 0.0761718974f, 0.105359003f, -0.0516408011f)),
        new PositionLightmapInfo(new Vector3(-21.993286f, -1.787819f, -11.956359f), 0, new Vector4(0.0761718974f, 0.0761718974f, 0.137584999f, -0.0516408011f)),
        new PositionLightmapInfo(new Vector3(-25.309494f, -1.787819f, -12.300285f), 2, new Vector4(0.0761718974f, 0.0761718974f, 0.0311401002f, 0.922968984f)),
        new PositionLightmapInfo(new Vector3(-25.514786f, -0.662695f, -12.204322f), 2, new Vector4(0.0761718974f, 0.0761718974f, 0.0633665994f, 0.922968984f)),
        new PositionLightmapInfo(new Vector3(-16.02034f, -1.787819f, -12.301879f), 0, new Vector4(0.0761718974f, 0.0761718974f, 0.169811994f, -0.0516408011f)),
        new PositionLightmapInfo(new Vector3(-16.029831f, -1.787819f, -11.102699f), 2, new Vector4(0.0761718974f, 0.0761718974f, 0.0955931991f, 0.922968984f)),
        new PositionLightmapInfo(new Vector3(-16.02034f, -0.656793f, -12.301879f), 0, new Vector4(0.0761718974f, 0.0761718974f, 0.202038005f, -0.0516408011f)),
        new PositionLightmapInfo(new Vector3(-16.02034f, 0.476676f, -12.301879f), 0, new Vector4(0.0761718974f, 0.0761718974f, 0.234265f, -0.0516408011f)),
        new PositionLightmapInfo(new Vector3(-25.508591f, -1.787819f, 7.376648f), 5, new Vector4(0.0761718974f, 0.0761718974f, 0.801648021f, 0.922968984f)),
        new PositionLightmapInfo(new Vector3(-25.508591f, -1.787819f, 8.611359f), 4, new Vector4(0.0761718974f, 0.0761718974f, 0.960828006f, 0.0382029004f)),
        new PositionLightmapInfo(new Vector3(-25.508591f, -0.672215f, 8.611359f), 5, new Vector4(0.0761718974f, 0.0761718974f, 0.769420981f, 0.922968984f)),
        new PositionLightmapInfo(new Vector3(-24.322449f, -1.787819f, 8.629029f), 4, new Vector4(0.0761718974f, 0.0761718974f, 0.339733988f, 0.81457001f)),
        new PositionLightmapInfo(new Vector3(-24.322449f, -0.654821f, 8.629029f), 4, new Vector4(0.0761718974f, 0.0761718974f, 0.339733988f, 0.789179981f)),
        new PositionLightmapInfo(new Vector3(57.007321f, -7.802154f, 76.362527f), 1, new Vector4(0.458983988f, 0.458983988f, 0.817166984f, -0.300680012f)),
        new PositionLightmapInfo(new Vector3(0.0f, 0.0f, 0.0f), 2, new Vector4(0.788085997f, 0.788085997f, 0.50998199f, -0.0927345976f)),
        new PositionLightmapInfo(new Vector3(0.0f, 0.259595f, 19.306839f), 1, new Vector4(0.265625f, 0.265625f, -0.00123634003f, -0.0833135992f)),
        new PositionLightmapInfo(new Vector3(0.0f, 0.0f, 0.0f), 4, new Vector4(0.287108988f, 0.287108988f, 0.866991997f, -0.115427002f)),
        new PositionLightmapInfo(new Vector3(54.93698f, -7.653847f, 86.223868f), 1, new Vector4(0.176758006f, 0.176758006f, -0.000737995026f, 0.822762012f)),
        new PositionLightmapInfo(new Vector3(-6.503623f, 2.157625f, -12.639692f), 2, new Vector4(0.0322265998f, 0.0322265998f, 0.133168995f, 0.967199981f)),
    };

    // SpaceportAlpha: 17 renderers (position-based)
    static readonly PositionLightmapInfo[] SpaceportAlphaPositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(20.945843f, 9.761684f, -3.090372f), 1, new Vector4(0.191405997f, 0.382813007f, 0.187547997f, 0.616180003f)),
        new PositionLightmapInfo(new Vector3(0.0f, 27.348572f, -32.388073f), 1, new Vector4(0.191405997f, 0.382813007f, -0.000928346999f, 0.616180003f)),
        new PositionLightmapInfo(new Vector3(3.853116f, -1.448989f, -0.573702f), 1, new Vector4(0.191405997f, 0.382813007f, -0.000928346999f, -0.00100755994f)),
        new PositionLightmapInfo(new Vector3(-3.865691f, -1.448989f, -0.573702f), 1, new Vector4(0.191405997f, 0.382813007f, 0.376024991f, 0.616180003f)),
        new PositionLightmapInfo(new Vector3(0.0f, -1.448989f, -17.935602f), 1, new Vector4(0.191405997f, 0.382813007f, 0.187547997f, -0.00100755994f)),
        new PositionLightmapInfo(new Vector3(-20.92189f, 9.761684f, -3.090372f), 1, new Vector4(0.191405997f, 0.382813007f, 0.564500988f, 0.616180003f)),
        new PositionLightmapInfo(new Vector3(0.0f, -1.448989f, 1.866917f), 1, new Vector4(0.191405997f, 0.382813007f, 0.376024991f, -0.00100755994f)),
        new PositionLightmapInfo(new Vector3(14.804558f, 23.781188f, -22.333244f), 0, new Vector4(0.0380859002f, 0.0380859002f, 0.381199002f, 0.000339507998f)),
        new PositionLightmapInfo(new Vector3(-8.879122f, 6.239264f, 66.817611f), 0, new Vector4(0.0380859002f, 0.0380859002f, 0.420260996f, 0.000339507998f)),
        new PositionLightmapInfo(new Vector3(9.031218f, 6.239264f, 66.817611f), 0, new Vector4(0.0380859002f, 0.0380859002f, 0.459324002f, 0.000339507998f)),
        new PositionLightmapInfo(new Vector3(-0.182397f, -0.846139f, 25.067606f), 0, new Vector4(0.0380859002f, 0.0380859002f, 0.498385996f, 0.000339507998f)),
        new PositionLightmapInfo(new Vector3(-14.921025f, 23.647332f, -22.259157f), 0, new Vector4(0.0380859002f, 0.0380859002f, 0.537449002f, 0.000339507998f)),
        new PositionLightmapInfo(new Vector3(13.648873f, 23.686649f, -22.333244f), 1, new Vector4(0.119140998f, 0.238280997f, 0.565452993f, 4.57763999e-05f)),
        new PositionLightmapInfo(new Vector3(-8.879122f, 5.579549f, 65.864029f), 1, new Vector4(0.119140998f, 0.238280997f, 0.753929019f, 0.759810984f)),
        new PositionLightmapInfo(new Vector3(9.031218f, 5.579549f, 65.864029f), 1, new Vector4(0.119140998f, 0.238280997f, 0.684593022f, 4.57763999e-05f)),
        new PositionLightmapInfo(new Vector3(-0.182397f, -1.505854f, 26.021189f), 1, new Vector4(0.119140998f, 0.238280997f, 0.873070002f, 0.759810984f)),
        new PositionLightmapInfo(new Vector3(-13.765341f, 23.552793f, -22.259157f), 1, new Vector4(0.119140998f, 0.238280997f, 0.803734004f, 4.57763999e-05f)),
    };

    // MonkeyIsland: 35 renderers (position-based)
    static readonly PositionLightmapInfo[] MonkeyIslandPositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(-36.082367f, 8.785101f, 10.276195f), 1, new Vector4(0.119140998f, 0.119140998f, 0.257835001f, 0.485374004f)),
        new PositionLightmapInfo(new Vector3(142.948776f, -0.354208f, 8.604453f), 1, new Vector4(0.119140998f, 0.119140998f, 0.0898666009f, 0.41506201f)),
        new PositionLightmapInfo(new Vector3(-36.045127f, 9.444816f, 11.22905f), 1, new Vector4(0.0380859002f, 0.0380859002f, 0.740574002f, 0.215183005f)),
        new PositionLightmapInfo(new Vector3(143.763239f, 0.471085f, 8.595173f), 1, new Vector4(0.0380859002f, 0.0380859002f, 0.956394017f, 0.220065996f)),
        new PositionLightmapInfo(new Vector3(-24.956217f, 1.758575f, 9.021568f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.483509988f, 0.643435001f)),
        new PositionLightmapInfo(new Vector3(-38.308395f, -13.42461f, -76.5802f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.91417402f, 0.65515399f)),
        new PositionLightmapInfo(new Vector3(-24.882601f, -12.688545f, -55.922039f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.533315003f, 0.642458975f)),
        new PositionLightmapInfo(new Vector3(-39.451065f, -13.405998f, -76.086182f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.352651f, 0.324099004f)),
        new PositionLightmapInfo(new Vector3(-23.139177f, -12.480976f, -57.022022f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.250111997f, 0.333864987f)),
        new PositionLightmapInfo(new Vector3(-5.529715f, 1.078171f, 2.670235f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.465932012f, 0.624880016f)),
        new PositionLightmapInfo(new Vector3(-29.696382f, -12.543484f, -50.19392f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.861440003f, 0.646364987f)),
        new PositionLightmapInfo(new Vector3(25.3396f, 1.015347f, -43.109585f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.350697994f, 0.359254986f)),
        new PositionLightmapInfo(new Vector3(5.574624f, 1.049301f, -40.304485f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.598744988f, 0.63952899f)),
        new PositionLightmapInfo(new Vector3(17.094364f, 1.569424f, -39.326755f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.582143009f, 0.616091013f)),
        new PositionLightmapInfo(new Vector3(-40.991486f, -13.364517f, -77.571831f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.91612798f, 0.619997978f)),
        new PositionLightmapInfo(new Vector3(-40.991486f, -13.160687f, -75.996269f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.863393009f, 0.611208975f)),
        new PositionLightmapInfo(new Vector3(-27.105875f, -12.905758f, -49.302925f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.248159006f, 0.369020998f)),
        new PositionLightmapInfo(new Vector3(-28.646299f, -12.535313f, -48.064472f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.717885017f, 0.312379986f)),
        new PositionLightmapInfo(new Vector3(-40.991486f, -13.536823f, -74.377342f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.104603998f, 0.542849004f)),
        new PositionLightmapInfo(new Vector3(-25.553202f, -12.783874f, -57.9202f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.205190003f, 0.546755016f)),
        new PositionLightmapInfo(new Vector3(8.652336f, 1.218273f, -45.364906f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.0440572016f, 0.546755016f)),
        new PositionLightmapInfo(new Vector3(-39.451065f, -13.364258f, -77.289497f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.348744988f, 0.394412011f)),
        new PositionLightmapInfo(new Vector3(4.881876f, 0.897514f, -43.528229f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.865346014f, 0.57605201f)),
        new PositionLightmapInfo(new Vector3(-30.825724f, -12.174568f, -48.752998f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.918080986f, 0.584841013f)),
        new PositionLightmapInfo(new Vector3(-39.451065f, -13.399231f, -74.738121f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.246206f, 0.40417701f)),
        new PositionLightmapInfo(new Vector3(-24.956217f, 1.924416f, 10.319595f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.717885017f, 0.366091013f)),
        new PositionLightmapInfo(new Vector3(8.652336f, 1.218273f, -44.11557f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.0274557006f, 0.533083975f)),
        new PositionLightmapInfo(new Vector3(-25.239901f, 1.290672f, 7.587448f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.188587993f, 0.533083975f)),
        new PositionLightmapInfo(new Vector3(23.702459f, 1.407356f, -43.588734f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.94737798f, 0.35827899f)),
        new PositionLightmapInfo(new Vector3(-5.529715f, 1.085335f, 4.066643f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.843861997f, 0.401248008f)),
        new PositionLightmapInfo(new Vector3(24.070442f, 1.108036f, -42.206909f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.590932012f, 0.704958975f)),
        new PositionLightmapInfo(new Vector3(-22.821741f, -12.381378f, -54.612465f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.569447994f, 0.68542701f)),
        new PositionLightmapInfo(new Vector3(3.013351f, 0.95467f, -42.354942f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.354604006f, 0.288942993f)),
        new PositionLightmapInfo(new Vector3(25.561336f, 1.200714f, -41.113056f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.252065003f, 0.298709005f)),
        new PositionLightmapInfo(new Vector3(18.179869f, 1.569424f, -39.945274f), 1, new Vector4(0.0537109002f, 0.0537109002f, 0.549916983f, 0.661989987f)),
    };

    // CuberStrike: 8 renderers (position-based)
    static readonly PositionLightmapInfo[] CuberStrikePositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(91.259644f, 21.446409f, 9.83092f), 16, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-67.388931f, 15.211954f, 10.641872f), 17, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-13.235493f, 0.060831f, 38.329983f), 16, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(58.639736f, 0.060831f, -58.691719f), 16, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(45.223698f, 0.060831f, 43.568848f), 16, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(-15.41856f, 0.060831f, -92.322014f), 17, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-42.998177f, 0.060831f, 10.629448f), 17, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(85.663429f, 24.480377f, -85.907394f), 17, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, -0.00199692999f)),
    };

    // CuberSpace: 8 renderers (position-based)
    static readonly PositionLightmapInfo[] CuberSpacePositionData = new PositionLightmapInfo[]
    {
        new PositionLightmapInfo(new Vector3(91.259644f, 21.446409f, 9.83092f), 16, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-67.388931f, 15.211954f, 10.641872f), 17, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-13.235493f, 0.060831f, 38.329983f), 16, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(58.639736f, 0.060831f, -58.691719f), 16, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(45.223698f, 0.060831f, 43.568848f), 16, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(-15.41856f, 0.060831f, -92.322014f), 17, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, 0.521440983f)),
        new PositionLightmapInfo(new Vector3(-42.998177f, 0.060831f, 10.629448f), 17, new Vector4(0.479492009f, 0.479492009f, -0.00306051993f, -0.00199692999f)),
        new PositionLightmapInfo(new Vector3(85.663429f, 24.480377f, -85.907394f), 17, new Vector4(0.479492009f, 0.479492009f, 0.466666013f, -0.00199692999f)),
    };

    // =====================================================================
    // SCENE LOOKUP TABLES
    // =====================================================================

    public static readonly Dictionary<string, string> SceneLightmapFolders = new Dictionary<string, string>
    {
        { "LevelCuberStrike", "Assets/Scenes/CuberStrike/LevelCuberStrike/" },
        { "LevelCuberSpace", "Assets/Scenes/CuberSpace/LevelCuberSpace/" },
        { "LevelGideonsTower", "Assets/Scenes/GideonsTower/LevelGideonsTower/" },
        { "LevelFortWinter", "Assets/Scenes/FortWinter/LevelFortWinter/" },
        { "LevelTheWarehouse", "Assets/Scenes/TheWarehouse/LevelTheWarehouse/" },
        { "LevelLostParadise2", "Assets/Scenes/LostParadise2/LevelLostParadise2/" },
        { "LevelMonkeyIsland", "Assets/Scenes/MonkeyIsland/LevelMonkeyIsland/" },
        { "LevelSkyGarden", "Assets/Scenes/SkyGarden/LevelSkyGarden/" },
        { "LevelTempleOfTheRaven", "Assets/Scenes/TempleOfTheRaven/LevelTempleOfTheRaven/" },
        { "LevelSpaceportAlpha", "Assets/Scenes/SpaceportAlpha/LevelSpaceportAlpha/" },
        { "LevelTheBunker", "Assets/Scenes/TheBunker/LevelTheBunker/" },
        { "LevelAqualabResearchHub", "Assets/Scenes/AqualabResearchHub/LevelAqualabResearchHub/" },
        { "LevelSpaceship", "Assets/Scenes/Spaceship/LevelSpaceship/" },
    };

    // Name-based matching
    static readonly Dictionary<string, Dictionary<string, RendererLightmapInfo>> SceneRendererData =
        new Dictionary<string, Dictionary<string, RendererLightmapInfo>>
    {
        { "LevelGideonsTower", GideonsTowerLightmapData },
        { "LevelTheWarehouse", TheWarehouseLightmapData },
        { "LevelFortWinter", FortWinterLightmapData },
        { "LevelLostParadise2", LostParadise2LightmapData },
        { "LevelSkyGarden", SkyGardenLightmapData },
        { "LevelTheBunker", TheBunkerLightmapData },
        { "LevelSpaceship", SpaceshipLightmapData },
        { "LevelSpaceportAlpha", SpaceportAlphaLightmapData },
        { "LevelTempleOfTheRaven", TempleOfTheRavenLightmapData },
        { "LevelMonkeyIsland", MonkeyIslandLightmapData },
        { "LevelCuberStrike", CuberStrikeLightmapData },
        { "LevelCuberSpace", CuberSpaceLightmapData },
        { "LevelAqualabResearchHub", AqualabResearchHubLightmapData },
    };

    // Position-based matching
    static readonly Dictionary<string, PositionLightmapInfo[]> ScenePositionData =
        new Dictionary<string, PositionLightmapInfo[]>
    {
        { "LevelLostParadise2", LostParadise2PositionData },
        { "LevelSpaceship", SpaceshipPositionData },
        { "LevelSpaceportAlpha", SpaceportAlphaPositionData },
        { "LevelMonkeyIsland", MonkeyIslandPositionData },
        { "LevelCuberStrike", CuberStrikePositionData },
        { "LevelCuberSpace", CuberSpacePositionData },
    };

    // Original ambient intensity values from git commit d11b013b (before "Fix lighting" commit).
    // Only maps where the "Fix lighting" commit (16abe999) significantly changed ambient.
    static readonly Dictionary<string, float> OriginalAmbientIntensity = new Dictionary<string, float>
    {
        { "LevelCuberStrike", 0.283f },       // was doubled to 0.567 → causes blueish tint
        { "LevelSpaceportAlpha", 0.470f },     // was slashed to 0.1 → way too dark
        { "LevelTempleOfTheRaven", 0.261f },   // was reduced to 0.147 → too dark
        // Ported from origin/main. Aqualab's over-brightness is fixed by disabling its realtime
        // point lights (see AdjustSceneLights), so it keeps its natural ambient. SuperPRISM's lights
        // are baked, so a mild ambient trim stays as a fallback. Tunable after a visual check.
        { "LevelSuperPRISMReactor", 0.55f },
    };

    // Per-map target intensity for the scene's primary directional light, overriding
    // the default "zero it out" behaviour in AdjustSceneLights. Needed for maps whose
    // baked lightmaps expect a live directional complement. Unity 3.5 reference values.
    // Dict miss → fall through to the default zeroing behaviour for that map.
    static readonly Dictionary<string, float> DirectionalLightTargetIntensity = new Dictionary<string, float>
    {
        { "LevelTempleOfTheRaven", 0.5f },     // 3.5 reference had warm directional at 0.5
    };


    // =====================================================================
    // RUNTIME
    // =====================================================================

    // Saved lobby lightmaps — LightmapSettings.lightmaps is global, so when a map
    // loads and overwrites it, we need to restore the lobby's lightmaps on return.
    internal static LightmapData[] lobbyLightmaps;

    // Saved directional light states — when entering a map we disable directional lights
    // to prevent double-lighting. On lobby return we restore them.
    internal static readonly Dictionary<int, float> savedLightIntensity = new Dictionary<int, float>();
    internal static readonly Dictionary<int, LightShadows> savedLightShadows = new Dictionary<int, LightShadows>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Handle lobby ("Latest" scene contains the lobby/training zone geometry)
        if (scene.name == "Latest")
        {
            FixLobbyLighting();
            return;
        }

        if (!scene.name.StartsWith("Level"))
            return;

        Restore(scene);
    }

    /// <summary>
    /// Re-entry hook for <see cref="MapConfiguration.SetEnabled"/> (true).
    /// Unity 2022 keeps additively-loaded scenes alive across
    /// SetCurrentSpace transitions — `SceneManager.sceneLoaded` fires only
    /// on the first load, so <see cref="OnSceneLoaded"/> goes dark on map
    /// re-entry and `LightmapSettings.lightmaps` keeps whatever the lobby
    /// or previous map left behind. Temple renderers' lightmapIndex=0/1
    /// then resolve to the wrong textures → flat-lit re-entry.
    /// MapConfiguration.SetEnabled(true) fires on every activation
    /// (proven via logs 2026-04-24), so we hook there. Idempotent with
    /// OnSceneLoaded on first entry; lobby and non-Level scenes bypass.
    /// </summary>
    public static void RestoreByName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        // Non-Level* scenes (e.g. SuperPRISMReactor) use Unity's native
        // lightmap auto-discovery and handle re-entry themselves.
        if (!sceneName.StartsWith("Level")) return;
        // Lobby re-entry is already handled by BeastLobbyLightmapGuard —
        // don't race it from here.
        if (sceneName == "LevelSpaceship") return;

        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning($"[BeastLightmapLoader] RestoreByName: scene '{sceneName}' " +
                             "not found or not loaded; skipping re-entry restore.");
            return;
        }
        Restore(scene);
    }

    static void Restore(Scene scene)
    {
        string sceneName = scene.name;

        // LevelSpaceship (lobby/training zone) has no BeastLightmap config — handle it first.
        // Fix brightness: migration changed the main directional light from realtime to baked,
        // and there are no Beast lightmaps for the lobby. Boost ambient to compensate.
        if (sceneName == "LevelSpaceship")
        {
            FixLobbyLighting();
            return;
        }

        // Load per-map lightmap textures from Resources (works in both Editor and builds).
        // The BeastLightmapMapData ScriptableObjects are auto-generated by
        // BeastLightmapConfigBuilder (Tools > Rebuild Beast Lightmap Config).
        var mapData = Resources.Load<BeastLightmapMapData>("BeastLightmaps/" + sceneName);
        if (mapData == null || mapData.lightmaps == null || mapData.lightmaps.Length == 0)
        {
            Debug.LogWarning($"[BeastLightmapLoader] No lightmap data found for {sceneName} " +
                             "(run Tools > Rebuild Beast Lightmap Config in Editor)");
            return;
        }

        // Load lightmap textures DIRECTLY — no pixel manipulation.
        // The imported RGBM texture is decoded correctly by the shader in one pass.
        LightmapData[] lightmapData = new LightmapData[mapData.lightmaps.Length];
        int loaded = 0;
        for (int i = 0; i < mapData.lightmaps.Length; i++)
        {
            lightmapData[i] = new LightmapData();
            if (mapData.lightmaps[i] != null)
            {
                lightmapData[i].lightmapColor = mapData.lightmaps[i];
                loaded++;

                // DIAGNOSTIC: log texture details to debug black lightmaps in builds
                var tex = mapData.lightmaps[i];
                Debug.Log($"[BeastLightmapLoader] DIAG {scene.name} lightmap[{i}]: " +
                          $"{tex.width}x{tex.height} format={tex.format} readable={tex.isReadable}");
                try
                {
                    if (tex.isReadable && tex.width > 0 && tex.height > 0)
                    {
                        Color c = tex.GetPixel(tex.width / 2, tex.height / 2);
                        Debug.Log($"[BeastLightmapLoader] DIAG {scene.name} lightmap[{i}] center pixel: R={c.r:F4} G={c.g:F4} B={c.b:F4} A={c.a:F4}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.Log($"[BeastLightmapLoader] DIAG {scene.name} lightmap[{i}] GetPixel failed: {e.Message}");
                }
            }
            else
            {
                Debug.Log($"[BeastLightmapLoader] DIAG {scene.name} lightmap[{i}]: NULL texture");
            }
        }

        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = lightmapData;

        bool hasData = SceneRendererData.ContainsKey(scene.name) || ScenePositionData.ContainsKey(scene.name);
        if (!hasData)
        {
            Debug.Log($"[BeastLightmapLoader] Loaded {loaded} lightmaps for {scene.name} (no renderer data)");
            return;
        }

        Debug.Log($"[BeastLightmapLoader] Loaded {loaded} lightmaps for {scene.name}, assigning renderers in {AssignDelayFrames} frames...");

        // Defer renderer assignment — geometry spawns AFTER sceneLoaded fires.
        // Uses a coroutine via BeastLightmapCoroutineHost (works in both Editor and builds).
        Scene capturedScene = scene;
        LightmapData[] cachedLightmaps = lightmapData;

        BeastLightmapCoroutineHost.Run(DelayedAssign(sceneName, capturedScene, cachedLightmaps));
    }

    static IEnumerator DelayedAssign(string sceneName, Scene capturedScene, LightmapData[] cachedLightmaps)
    {
        LogLightingSnapshot(sceneName, "PRE-DELAY");

        for (int i = 0; i < AssignDelayFrames; i++)
            yield return null;

        // Re-set lightmaps in case another scene load overwrote them
        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = cachedLightmaps;

        // DIAGNOSTIC: verify lightmaps are still set after delay
        Debug.Log($"[BeastLightmapLoader] DIAG {sceneName} after delay: " +
                  $"lightmapsMode={LightmapSettings.lightmapsMode} " +
                  $"lightmaps.Length={LightmapSettings.lightmaps?.Length ?? -1}");
        if (LightmapSettings.lightmaps != null)
        {
            for (int d = 0; d < LightmapSettings.lightmaps.Length; d++)
            {
                var lm = LightmapSettings.lightmaps[d];
                var tex = lm?.lightmapColor;
                Debug.Log($"[BeastLightmapLoader] DIAG {sceneName} LightmapSettings[{d}]: " +
                          $"color={tex?.width}x{tex?.height} fmt={tex?.format} isNull={tex == null}");
            }
        }
        try
        {
            Vector4 hdr = Shader.GetGlobalVector("unity_Lightmap_HDR");
            Debug.Log($"[BeastLightmapLoader] DIAG {sceneName} unity_Lightmap_HDR = ({hdr.x:F4}, {hdr.y:F4}, {hdr.z:F4}, {hdr.w:F4})");
        }
        catch { Debug.Log($"[BeastLightmapLoader] DIAG {sceneName} unity_Lightmap_HDR not available"); }

        AssignRenderers(sceneName);

        // LevelSpaceship is handled in FixLobbyLighting() at the top of Restore().

        // RenderSettings is per-active-scene. Level scenes are loaded additively,
        // so the lobby scene is typically active. We must set the Level scene as
        // active before modifying RenderSettings (ambient intensity, etc.).
        Scene previousActive = SceneManager.GetActiveScene();
        if (capturedScene.IsValid() && capturedScene.isLoaded)
            SceneManager.SetActiveScene(capturedScene);

        AdjustSceneLights(sceneName);

        // Snapshot after the full AdjustSceneLights pass — this is the steady
        // state the player sees for the rest of the map session.
        LogLightingSnapshot(sceneName, "POST-ADJUST");

        // Restore previous active scene to avoid side effects
        if (previousActive.IsValid() && previousActive.isLoaded && previousActive != capturedScene)
            SceneManager.SetActiveScene(previousActive);

        // Spawn a per-frame guard that keeps LightmapSettings.lightmaps pinned
        // to the array we just loaded from Resources.
        //
        // Diagnosed 2026-04-21: on map re-entry, Unity's additive scene load
        // auto-unions the Level*.unity scene's YAML-baked lightmap references
        // into LightmapSettings.lightmaps, growing the array (Temple: 2 → 7
        // entries). The new entries share asset names with ours (LightmapFar-0
        // through LightmapFar-6) but are DIFFERENT texture assets — the
        // scene's baked data was stripped/blanked during the Unity 2022 port,
        // so those scene-side references resolve to stripped textures.
        // Renderers' lightmapIndex=0/1 stays in bounds, but the textures at
        // those slots flip from our valid Resources-loaded ones to the
        // scene's stripped ones. Rendering goes flat-lit even though every
        // other piece of state (ambient, skybox, HDR decode, directional
        // lights, renderer indices/scaleOffsets) is correct.
        //
        // Re-assigning LightmapSettings.lightmaps once after DelayedAssign
        // isn't enough — the auto-union happens AFTER our assignment. The
        // guard re-asserts every frame; the check is a cheap array-length
        // compare. Guard self-destructs when the player leaves the map
        // (detected via GameState.CurrentSpace.name change).
        if (cachedLightmaps != null && cachedLightmaps.Length > 0)
        {
            var guardGO = new GameObject("BeastMapLightmapGuard_" + sceneName);
            guardGO.hideFlags = HideFlags.HideAndDontSave;
            var guard = guardGO.AddComponent<BeastMapLightmapGuard>();
            guard.mapSceneName = sceneName;
            guard.expectedLightmaps = cachedLightmaps;
        }
    }

    // Condensed lighting state dump used by the re-entry-flash diagnostic.
    // Prints ambient, all directional lights with intensity + owning scene,
    // and count of non-directional lights. Tag distinguishes call sites.
    static void LogLightingSnapshot(string sceneName, string tag)
    {
        int dirCount = 0, otherCount = 0;
        float maxDirIntensity = 0f;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("[BeastLightmapLoader] FLASH-DIAG ").Append(sceneName).Append(' ').Append(tag)
          .Append(" ambient=").Append(RenderSettings.ambientLight).Append(" intensity=")
          .Append(RenderSettings.ambientIntensity.ToString("F3")).Append(" mode=")
          .Append(RenderSettings.ambientMode);
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light == null) continue;
            if (light.type == LightType.Directional)
            {
                dirCount++;
                if (light.intensity > maxDirIntensity) maxDirIntensity = light.intensity;
                sb.Append(" | dir '").Append(light.gameObject.name)
                  .Append("' scene=").Append(light.gameObject.scene.name)
                  .Append(" I=").Append(light.intensity.ToString("F3"))
                  .Append(" enabled=").Append(light.enabled);
            }
            else otherCount++;
        }
        sb.Append(" | dirCount=").Append(dirCount).Append(" maxDirI=")
          .Append(maxDirIntensity.ToString("F3")).Append(" otherLights=").Append(otherCount);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Fix lobby/training zone brightness. Loads Beast lightmaps for LevelSpaceship,
    /// fixes the main directional light, assigns renderers, and restores ambient.
    /// Called when the "Latest" scene loads (lobby geometry lives in Latest).
    /// </summary>
    static void FixLobbyLighting()
    {
        // Load LevelSpaceship Beast lightmaps — the lobby scene (Latest) has no
        // LightingDataAsset, so lightmaps must be loaded via the Beast system.
        var mapData = Resources.Load<BeastLightmapMapData>("BeastLightmaps/LevelSpaceship");
        if (mapData != null && mapData.lightmaps != null && mapData.lightmaps.Length > 0)
        {
            LightmapData[] lightmapData = new LightmapData[mapData.lightmaps.Length];
            int loaded = 0;
            for (int i = 0; i < mapData.lightmaps.Length; i++)
            {
                lightmapData[i] = new LightmapData();
                if (mapData.lightmaps[i] != null)
                {
                    lightmapData[i].lightmapColor = mapData.lightmaps[i];
                    loaded++;
                }
            }
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            LightmapSettings.lightmaps = lightmapData;
            lobbyLightmaps = lightmapData;
            Debug.Log($"[BeastLightmapLoader] Lobby: Loaded {loaded} Beast lightmaps for LevelSpaceship");

            // Assign renderers (deferred — geometry may not be ready yet)
            BeastLightmapCoroutineHost.Run(DelayedLobbyAssign(lightmapData));
        }
        else
        {
            lobbyLightmaps = LightmapSettings.lightmaps;
            Debug.LogWarning("[BeastLightmapLoader] Lobby: No Beast lightmap data for LevelSpaceship");
        }

        // Fix ALL directional lights — migration changed them from realtime/baked-only to Mixed.
        // Original 3.5.5: Light1032561182 was realtime (m_ActuallyLightmapped=0) at 0.5 intensity.
        // The other two (130, 135) were baked fill lights at 0.05 and 0.10.
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light == null || light.type != LightType.Directional) continue;
            string lScene = light.gameObject.scene.name;
            if (lScene != "Latest" && lScene != "LevelSpaceship") continue;
            if (light.shadows != LightShadows.None)
            {
#if UNITY_EDITOR
                light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                light.intensity = 0.8f;    // was 0.5 - lobby read too dim
                light.color = Color.white; // was a warm tint that browned the (dynamic) avatar; neutralize it
            }
            Debug.Log($"[BeastLightmapLoader] Lobby: Directional '{light.gameObject.name}' -> realtime, intensity={light.intensity}, color={light.color}");
        }

        // Lobby ambient. KNOB 1 of 2. Original 3.5.5 ambient was 0.246 neutral grey; it only read "too
        // dark" in the migrated project because the migration ALSO killed the realtime directional that
        // used to light the room (see the key light below). With that directional restored, ambient can
        // sit back near the original instead of being bandaged up to 0.42 (which over-flattened the
        // avatar). Kept neutral grey (no colour cast, so no "brown" tint from the fill). Nudge up toward
        // 0.35 if the room still reads dark after the directional is in.
        const float LobbyAmbient = 0.28f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(LobbyAmbient, LobbyAmbient, LobbyAmbient, 1f);
        RenderSettings.ambientIntensity = 1.0f;
        Debug.Log($"[BeastLightmapLoader] Lobby: Ambient -> {LobbyAmbient} flat (near original 0.246; directional now carries the room)");

        // LOBBY KEY LIGHT -- restores the realtime directional the migration deactivated. KNOB 2 of 2.
        // Root cause of BOTH "room darker" and "avatar skin is brown": original LevelSpaceship had ONE
        // realtime directional (Light1032561182 @ 0.5, m_ActuallyLightmapped=0) that lit the room AND the
        // dynamic avatar, plus two BAKED fill lights (0.05 + 0.10) that are already inside the Beast
        // lightmaps. The migration left all three on INACTIVE GameObjects, so the room lost its only
        // realtime directional and the avatar -- a SkinnedMeshRenderer with no lightmap/probes, on layer
        // Default(0), skin-colour white -- was left on flat ambient alone: legacy-Diffuse tan texture x
        // dim grey = muddy "brown". An earlier attempt culled this light to the PLAYER layers (18/20/19),
        // but the lobby avatar built by AvatarBuilder.CreateLocalAvatar stays on layer Default(0) (SetLayers
        // is only called for REMOTE avatars) -- so it hit nothing. Fix = re-create that one realtime
        // directional with cullingMask = Everything, exactly as the original had it. This is NOT double-
        // lighting: the directional was realtime (never baked), so adding it back does not double-count the
        // baked fills that live in the lightmaps. It restores the original ambient + 1-realtime-directional
        // model. Lobby-scoped: this method runs only for the lobby (scene "Latest"/"LevelSpaceship"), and
        // the map-load path (AdjustSceneLights) destroys any BeastLobbyAvatarLight before a map renders, so
        // it can never touch another map's lighting. Drop any stale copy first (idempotent on re-entry).
        foreach (var stale in Object.FindObjectsOfType<Light>())
            if (stale != null && (stale.gameObject.name == "BeastLobbyAvatarLight" || stale.gameObject.name == "BeastWeaponsLight"))
                Object.Destroy(stale.gameObject);

        var avatarLightGO = new GameObject("BeastLobbyAvatarLight");
        var avatarKey = avatarLightGO.AddComponent<Light>();
        avatarKey.type = LightType.Directional;
        avatarKey.color = Color.white;
        avatarKey.intensity = 0.65f;                        // ~= original 0.5 + a touch for the now-ambient-light fills; nudge 0.5..0.8
        avatarKey.shadows = LightShadows.None;               // original realtime lobby directional cast no shadows
        avatarLightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);  // angled key (upper front-right); tune angle to taste
        avatarKey.cullingMask = -1;                          // Everything -- lights the avatar (Default) AND the room, faithful to the original realtime directional
        Debug.Log("[BeastLightmapLoader] Lobby: Spawned BeastLobbyAvatarLight (white directional, intensity=0.65, cullingMask=Everything; restores the migration-deactivated realtime rig)");
    }

    static IEnumerator DelayedLobbyAssign(LightmapData[] lightmapData)
    {
        for (int i = 0; i < AssignDelayFrames; i++)
            yield return null;

        // Re-set lightmaps after delay
        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = lightmapData;

        // Assign renderers for lobby
        AssignRenderers("LevelSpaceship");
        Debug.Log("[BeastLightmapLoader] Lobby: Renderer assignment complete");
    }

    static void AdjustSceneLights(string sceneName)
    {
        // Reset lobby ambient override — FixLobbyLighting sets bright ambient for the lobby,
        // which would bleed into maps if not reset. Restore the scene's own ambient color.
        RenderSettings.ambientLight = new Color(0.246f, 0.246f, 0.246f, 1f);
        RenderSettings.ambientIntensity = 1.0f;

        // Clean up BeastWeaponsLight + the lobby avatar key light from previous loads. Destroying
        // BeastLobbyAvatarLight here keeps the lobby-only avatar light from bleeding into a map.
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light != null && (light.gameObject.name == "BeastWeaponsLight" || light.gameObject.name == "BeastLobbyAvatarLight"))
            {
                Object.Destroy(light.gameObject);
                Debug.Log("[BeastLightmapLoader] Destroyed old " + light.gameObject.name);
            }
        }

        // Aqualab carries many REALTIME point/spot lights (68 at intensity 6-16) whose contribution is
        // ALSO baked into the lightmaps -> they double-light every static surface -> the map renders far
        // too bright (the user reported "Spotlights, Point lights & Lights are too bright"). The
        // directional pass below already disables baked directional lights for this reason; do the same
        // for the non-directional realtime lights here.
        // NOTE: scoped to Aqualab ONLY. SuperPRISM was judged acceptable by the user with its point/spot
        // lights left ON (the earlier filter disabled 0), so we must NOT touch its lights or we regress it.
        if (sceneName == "LevelAqualabResearchHub")
        {
            int killed = 0;
            foreach (var light in Object.FindObjectsOfType<Light>())
            {
                if (light == null || light.type == LightType.Directional) continue;
                // The map's point/spot lights live in the persistent "Latest" runtime scene, not the
                // Level* scene (same as the directional lights above), so match on NOT-the-lobby rather
                // than the map name. BeastWeaponsLight is directional, already excluded above.
                if (light.gameObject.scene.name == "LevelSpaceship") continue;   // never touch lobby lights
                if (!light.enabled) continue;
                light.enabled = false;
                killed++;
            }
            Debug.Log($"[BeastLightmapLoader] {sceneName}: disabled {killed} realtime non-directional lights (baked into lightmaps; were double-lighting)");
        }

        // Original Unity 3.5.5 used "Single Lightmaps" (m_ActuallyLightmapped: 1) where
        // baked directional lights were EXCLUDED from runtime rendering of static objects.
        // Unity 2022 has no equivalent — all realtime directional lights add on top of
        // lightmapped surfaces, causing double-lighting.
        //
        // Fix: disable directional scene lights (they're baked into lightmaps already).
        // Then spawn a dedicated "weapons light" clone restricted to dynamic layers only
        // (LocalPlayer/Weapons/RemotePlayer). This restores Blinn-Phong specular highlights
        // on weapons without double-lighting any static geometry.
        //
        // NOTE: Map directional lights live in the persistent "Latest" scene, NOT in their
        // Level scenes. The old scene-name filter blocked everything. We now accept lights
        // from both the Level scene and "Latest", skipping only the lobby scene.
        var allLights = Object.FindObjectsOfType<Light>();
        int adjusted = 0;
        float sunIntensity = 0f;
        Color sunColor = Color.white;
        Quaternion sunRotation = Quaternion.identity;
        bool hasSun = false;

        foreach (var light in allLights)
        {
            if (light == null) continue;
            if (light.type != LightType.Directional) continue;
            // Include lobby lights — they must be saved and disabled to prevent
            // double-lighting on maps. RestoreLobbyLightmaps() restores them on return.
            string lightScene = light.gameObject.scene.name;

            int id = light.GetInstanceID();

            // Save original state on first encounter so we can:
            // 1. Use original intensity for weapons light (not 0 from previous disable)
            // 2. Restore lights when returning to lobby
            if (!savedLightIntensity.ContainsKey(id))
            {
                savedLightIntensity[id] = light.intensity;
                savedLightShadows[id] = light.shadows;
            }

            // Skip already-disabled lights but still consider saved state for weapons light
            float effectiveIntensity = savedLightIntensity[id];
            if (!light.enabled || light.intensity == 0f)
            {
                // Use brightest light for weapons clone (e.g. Monkey Island has
                // BackLight=0.40 + Sunlight=1.20 — we want Sunlight)
                if (effectiveIntensity > sunIntensity)
                {
                    sunIntensity = effectiveIntensity;
                    sunColor = light.color;
                    sunRotation = light.transform.rotation;
                    hasSun = true;
                }
                continue;
            }

            // Use brightest light for weapons clone
            if (light.intensity > sunIntensity)
            {
                sunIntensity = light.intensity;
                sunColor = light.color;
                sunRotation = light.transform.rotation;
                hasSun = true;
            }

            float oldIntensity = light.intensity;
            float targetIntensity;
            bool hasOverride = DirectionalLightTargetIntensity.TryGetValue(sceneName, out targetIntensity);
            light.shadows = LightShadows.None;
            light.intensity = hasOverride ? targetIntensity : 0f;

            if (hasOverride)
            {
                Debug.Log($"[BeastLightmapLoader] {sceneName}: Preserved directional light '{light.gameObject.name}' " +
                          $"in scene '{lightScene}' at intensity={targetIntensity:F2} (was {oldIntensity:F2})");
            }
            else
            {
                Debug.Log($"[BeastLightmapLoader] {sceneName}: Disabled directional light '{light.gameObject.name}' " +
                          $"in scene '{lightScene}' (was intensity={oldIntensity:F2})");
            }
            adjusted++;
        }
        if (adjusted > 0)
            Debug.Log($"[BeastLightmapLoader] {sceneName}: Disabled {adjusted} directional light(s)");

        // Spawn a weapons-only directional light cloned from the scene's sun.
        // This restores Blinn-Phong specular on weapons/players without affecting
        // lightmapped static geometry.
        if (hasSun)
        {
            var go = new GameObject("BeastWeaponsLight");
            var weaponsLight = go.AddComponent<Light>();
            weaponsLight.type = LightType.Directional;
            weaponsLight.color = sunColor;
            weaponsLight.intensity = sunIntensity;
            go.transform.rotation = sunRotation;
            weaponsLight.shadows = LightShadows.None;
            weaponsLight.cullingMask = LayerUtil.CreateLayerMask(
                UberstrikeLayer.LocalPlayer,
                UberstrikeLayer.Weapons,
                UberstrikeLayer.RemotePlayer
            );
            Debug.Log($"[BeastLightmapLoader] {sceneName}: Spawned BeastWeaponsLight " +
                      $"(intensity={sunIntensity:F2}, color={sunColor}, " +
                      $"layers=LocalPlayer+Weapons+RemotePlayer)");
        }

        // Restore original ambient intensity for maps where "Fix lighting" commit changed it.
        if (OriginalAmbientIntensity.ContainsKey(sceneName))
        {
            float originalAmbient = OriginalAmbientIntensity[sceneName];
            float currentAmbient = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = originalAmbient;
            Debug.Log($"[BeastLightmapLoader] {sceneName}: Restored ambient intensity {currentAmbient:F3} → {originalAmbient:F3}");
        }

        // Spawn a guard that restores lobby lightmaps when the player leaves this map.
        // LightmapSettings.lightmaps is global — this map's lightmaps overwrite the lobby's.
        // Without restoration, returning to lobby shows map lightmap data on lobby geometry.
        if (lobbyLightmaps != null)
        {
            var guardGO = new GameObject("BeastLobbyLightmapGuard");
            guardGO.hideFlags = HideFlags.HideAndDontSave;
            var guard = guardGO.AddComponent<BeastLobbyLightmapGuard>();
            guard.mapSceneName = sceneName;
        }
    }

    static void AssignRenderers(string sceneName)
    {
        var allRenderers = Object.FindObjectsOfType<MeshRenderer>();
        int nameAssigned = 0;
        int posAssigned = 0;

        // Strategy 1: Name-based matching
        if (SceneRendererData.ContainsKey(sceneName))
        {
            var rendererData = SceneRendererData[sceneName];
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (rendererData.TryGetValue(r.gameObject.name, out RendererLightmapInfo info))
                {
                    r.lightmapIndex = info.lightmapIndex;
                    r.lightmapScaleOffset = info.scaleOffset;
                    nameAssigned++;
                }
            }
        }

        // Strategy 2: Position-based matching
        if (ScenePositionData.ContainsKey(sceneName))
        {
            var posEntries = ScenePositionData[sceneName];
            bool[] matched = new bool[posEntries.Length];
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                Vector3 pos = r.transform.position;
                for (int i = 0; i < posEntries.Length; i++)
                {
                    if (matched[i]) continue;
                    if (Vector3.Distance(pos, posEntries[i].position) < PositionMatchThreshold)
                    {
                        r.lightmapIndex = posEntries[i].lightmapIndex;
                        r.lightmapScaleOffset = posEntries[i].scaleOffset;
                        posAssigned++;
                        matched[i] = true;
                        break;
                    }
                }
            }
        }

        int totalExpected = (SceneRendererData.ContainsKey(sceneName) ? SceneRendererData[sceneName].Count : 0)
                          + (ScenePositionData.ContainsKey(sceneName) ? ScenePositionData[sceneName].Length : 0);

        Debug.Log($"[BeastLightmapLoader] {sceneName}: {nameAssigned + posAssigned}/{totalExpected} " +
                  $"(name={nameAssigned}, pos={posAssigned}) from {allRenderers.Length} total renderers");

        // DIAGNOSTIC: log sample renderers to debug black lightmaps in builds
        int diagCount = 0;
        foreach (var r in allRenderers)
        {
            if (r != null && r.lightmapIndex >= 0 && diagCount < 5)
            {
                string shaderName = r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null";
                Debug.Log($"[BeastLightmapLoader] DIAG renderer '{r.gameObject.name}' " +
                          $"lightmapIndex={r.lightmapIndex} scaleOffset={r.lightmapScaleOffset} " +
                          $"shader={shaderName}");
                diagCount++;
            }
        }
    }

#if UNITY_EDITOR
    static string FindLightmapFolder(string sceneName)
    {
        if (SceneLightmapFolders.ContainsKey(sceneName))
        {
            string folder = SceneLightmapFolders[sceneName];
            if (Directory.Exists(folder) && Directory.GetFiles(folder, "LightmapFar-*.exr").Length > 0)
                return folder;
        }

        string stripped = sceneName.Replace("Level", "");
        string[] candidates = new[]
        {
            $"Assets/Scenes/{stripped}/{sceneName}/",
            $"Assets/Scenes/{sceneName}/",
            $"Assets/Scenes/{stripped}/",
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && Directory.GetFiles(c, "LightmapFar-*.exr").Length > 0)
                return c;
        }

        return null;
    }
#endif
}

/// <summary>
/// Singleton MonoBehaviour for running coroutines from static context.
/// Used by BeastLightmapLoader to defer renderer assignment by N frames
/// (geometry spawns after sceneLoaded fires). Works in both Editor and builds.
/// </summary>
public class BeastLightmapCoroutineHost : MonoBehaviour
{
    static BeastLightmapCoroutineHost instance;

    public static void Run(IEnumerator routine)
    {
        if (instance == null)
        {
            var go = new GameObject("BeastLightmapCoroutineHost");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            instance = go.AddComponent<BeastLightmapCoroutineHost>();
        }
        instance.StartCoroutine(routine);
    }
}

/// <summary>
/// Restores lobby (LevelSpaceship) lightmaps when the player leaves a map.
/// LightmapSettings.lightmaps is global — map loading overwrites it, so the
/// lobby floor shows garbage lightmap data without restoration.
/// Uses GameState.CurrentSpace to detect map→lobby transitions.
/// </summary>
public class BeastLobbyLightmapGuard : MonoBehaviour
{
    public string mapSceneName;
    float gracePeriod = 1f;

    void Update()
    {
        // Brief grace period for GameState to initialize on map load
        if (gracePeriod > 0f)
        {
            gracePeriod -= Time.deltaTime;
            return;
        }

        // Check every frame — GameState.CurrentSpace is a trivial property getter
        if (!GameState.Exists) return;

        bool stillOnMap = false;
        if (GameState.HasCurrentSpace)
        {
            var space = GameState.CurrentSpace;
            // Use space.name (GameObject name, e.g. "LevelFortWinter") instead of
            // space.gameObject.scene.name because MapConfiguration objects live in
            // the persistent "Latest" scene, not in their Level scenes.
            if (space != null && space.name == mapSceneName)
            {
                stillOnMap = true;
            }
        }

        if (!stillOnMap)
        {
            RestoreLobbyLightmaps();
            Destroy(gameObject);
        }
    }

    static void RestoreLobbyLightmaps()
    {
        if (BeastLightmapLoader.lobbyLightmaps == null) return;

        // Kill any lingering BeastMapLightmapGuard BEFORE restoring the lobby
        // array. Map guards watch for Length drift and slam LightmapSettings
        // back to the map's N-entry array; without this clear they overwrite
        // the lobby's 7-entry restoration and render garbage on GroundLayer1.
        //
        // Must use the static `current` pointer (or Resources.FindObjectsOfTypeAll)
        // — plain FindObjectsOfType skips objects with HideFlags.HideAndDontSave,
        // which our guard GameObject has, so it silently returned 0 hits in the
        // 2026-04-21 trace.
        var straggler = BeastMapLightmapGuard.current;
        if (straggler != null)
        {
            Debug.Log($"[BeastLobbyLightmapGuard] Destroying lingering map guard for '{straggler.mapSceneName}' before lobby restore.");
            Destroy(straggler.gameObject);
            BeastMapLightmapGuard.current = null;
        }

        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = BeastLightmapLoader.lobbyLightmaps;

        // Restore directional lights that were disabled for map rendering.
        // The lobby needs these active for proper character lighting.
        int restored = 0;
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light == null) continue;
            int id = light.GetInstanceID();
            if (BeastLightmapLoader.savedLightIntensity.ContainsKey(id))
            {
                light.intensity = BeastLightmapLoader.savedLightIntensity[id];
                light.shadows = BeastLightmapLoader.savedLightShadows[id];
                restored++;
            }
        }

        // Restore lobby ambient and directional lights
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.246f, 0.246f, 0.246f, 1f);
        RenderSettings.ambientIntensity = 1.0f;

        // Re-fix directional lights (they were disabled by AdjustSceneLights)
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light == null || light.type != LightType.Directional) continue;
            string lScene = light.gameObject.scene.name;
            if (lScene != "Latest" && lScene != "LevelSpaceship") continue;
            if (light.shadows != LightShadows.None)
            {
#if UNITY_EDITOR
                light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                light.intensity = 0.5f;
            }
        }

        Debug.Log($"[BeastLightmapLoader] Restored lobby lightmaps ({BeastLightmapLoader.lobbyLightmaps.Length} textures), " +
                  $"{restored} directional light(s), ambient=0.246 flat");
    }
}

/// <summary>
/// Per-frame watchdog that pins LightmapSettings.lightmaps to the
/// Resources-loaded array for a specific map. Addresses the re-entry
/// regression diagnosed 2026-04-21 via TempleRuntimeCapture diff:
/// Unity's additive-scene-load auto-unions the scene's YAML-baked lightmap
/// references into LightmapSettings.lightmaps on map re-entry (observed:
/// Temple grew from 2 → 7 entries), replacing our valid Resources textures
/// with the scene's stripped/blank textures that share the same asset
/// names. Rendering goes flat-lit even though every other lighting state
/// field is correct.
///
/// The guard checks LightmapSettings.lightmaps.Length once per frame. If it
/// doesn't match the expected length, it reassigns. Self-destructs when
/// GameState reports the player is no longer on this map.
/// </summary>
public class BeastMapLightmapGuard : MonoBehaviour
{
    // At-most-one-alive registry. SceneManager.sceneUnloaded proved unreliable
    // in UberStrike's additive-scene model — Temple stays loaded when the user
    // returns to lobby, so the Temple guard never dies and later fights the
    // next map's guard (Temple 2 ↔ LostParadise2 4 ping-pong, 900+ reasserts
    // observed 2026-04-21). When a new guard spawns it evicts the previous one.
    internal static BeastMapLightmapGuard current;

    public string mapSceneName;
    public LightmapData[] expectedLightmaps;
    int reassertCount = 0;

    void Awake()
    {
        if (current != null && current != this)
        {
            Debug.Log($"[BeastMapLightmapGuard] Evicting previous guard for '{current.mapSceneName}' " +
                      $"(new guard for '{mapSceneName}' spawning).");
            Destroy(current.gameObject);
        }
        current = this;
    }

    void Start()
    {
        Debug.Log($"[BeastMapLightmapGuard] Spawned for {mapSceneName}, " +
                  $"expected lightmap count={expectedLightmaps.Length}, " +
                  $"current={LightmapSettings.lightmaps?.Length ?? 0}");
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }

    void LateUpdate()
    {
        // Length-only check. Unity's `LightmapSettings.lightmaps` getter
        // always returns a fresh copy of the internal array, so reference
        // equality would always fail and we'd reassert every frame. Array
        // length is the signal we actually care about — the re-entry
        // regression grows the array (e.g. Temple 2 → 7) and that's the
        // symptom we want to squash.
        //
        // No lobby bail-out here. Earlier version had a reference-equality
        // check against BeastLightmapLoader.lobbyLightmaps that self-destructed
        // the guard if `lightmaps[0].lightmapColor == lobby[0].lightmapColor`.
        // Unity's auto-union on Temple re-entry pulls in scene-side texture
        // references that sometimes share instances with the lobby array
        // (same `LightmapFar-0` asset loaded once and referenced twice), so
        // the check fired FALSE POSITIVE and killed the guard mid-map — flat-
        // lit re-entry regression returned. The primary destruction path
        // (BeastLobbyLightmapGuard.RestoreLobbyLightmaps destroying
        // BeastMapLightmapGuard.current explicitly) is sufficient on its own.
        var currentArr = LightmapSettings.lightmaps;
        int currentLength = currentArr != null ? currentArr.Length : 0;

        // Primary signal: array length drift. Catches the observed "2 → 7"
        // auto-union on Temple re-entry.
        if (currentLength != expectedLightmaps.Length)
        {
            LightmapSettings.lightmaps = expectedLightmaps;
            reassertCount++;
            if (reassertCount == 1 || reassertCount % 60 == 0)
                Debug.Log($"[BeastMapLightmapGuard] {mapSceneName}: re-asserted lightmaps " +
                          $"(LENGTH drift: was {currentLength}, now {expectedLightmaps.Length}, " +
                          $"total reasserts={reassertCount})");
            return;
        }

        // β 2026-04-24: secondary content check. If Unity shuffles the array
        // to the expected length but replaces slot textures with different
        // Texture2D references (e.g., another map's LightmapFar-0), the
        // length check passes silently and re-entry is flat-lit. Diagnose
        // whether this is the 2026-04-22 regression signature. Reference
        // equality on `lightmapColor` works here — each map's
        // LightmapFar-*.exr is a distinct asset at its own GUID, so Unity
        // loads distinct Texture2D instances and divergent slots are
        // observable via `!=`. (This differs from the old lobby bail-out
        // false-positive: that checked against LOBBY lightmaps for
        // SELF-DESTRUCT; this checks against OWN expected for REASSERT.)
        int firstMismatchSlot = -1;
        for (int i = 0; i < currentLength; i++)
        {
            var cur = currentArr[i]?.lightmapColor;
            var exp = expectedLightmaps[i]?.lightmapColor;
            if (cur != exp)
            {
                firstMismatchSlot = i;
                break;
            }
        }

        if (firstMismatchSlot >= 0)
        {
            var badTex = currentArr[firstMismatchSlot]?.lightmapColor;
            var goodTex = expectedLightmaps[firstMismatchSlot]?.lightmapColor;
            LightmapSettings.lightmaps = expectedLightmaps;
            reassertCount++;
            if (reassertCount == 1 || reassertCount % 60 == 0)
                Debug.Log($"[BeastMapLightmapGuard] {mapSceneName}: re-asserted lightmaps " +
                          $"(CONTENT drift at slot {firstMismatchSlot}: " +
                          $"was '{(badTex != null ? badTex.name : "null")}' " +
                          $"(id={(badTex != null ? badTex.GetInstanceID() : 0)}), " +
                          $"expected '{(goodTex != null ? goodTex.name : "null")}' " +
                          $"(id={(goodTex != null ? goodTex.GetInstanceID() : 0)}), " +
                          $"total reasserts={reassertCount})");
        }
    }
}
