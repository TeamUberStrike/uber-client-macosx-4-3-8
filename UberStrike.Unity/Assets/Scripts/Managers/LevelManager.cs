using System.Collections;
using System.Collections.Generic;
using Cmune.Util;
using UberStrike.Core.Models.Views;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : Singleton<LevelManager>
{
    private LightmapData[] _originalLightmaps;
    private Dictionary<int, UberstrikeMap> mapsById = new Dictionary<int, UberstrikeMap>();
    private MapLoader _loader;
    private UberstrikeMap _initialMap;

    public IEnumerable<UberstrikeMap> AllMaps { get { return mapsById.Values; } }
    public int CurrentLoadingLevelId { get { return _loader.MapToLoad != null ? _loader.MapToLoad.Id : -1; } }
    public float CurrentProgress { get { return _loader.Progress; } }
    public bool IsLoading { get { return _loader.Progress != 1; } }
    public int Count { get { return mapsById.Count; } }

    public bool IsSimulateWebplayer { get; private set; }
    public string SimulatedWebPlayerPath { get; private set; }
    public void SimulateWebplayer(string path)
    {
        IsSimulateWebplayer = true;
        SimulatedWebPlayerPath = path;
    }

    private LevelManager()
    {
        Clear();

        _loader = new MapLoader();
    }

    private MapView CreateMapView(string name, int id, bool isBluebox = false)
    {
        return new MapView
        {
            Description = name,
            DisplayName = name,
            MapId = id,
            SceneName = (name.StartsWith("Level") ? "" : "Level") + name,
            IsBlueBox = isBluebox,
            FileName = string.Format("Map-{0:00}.unity3d", id)
        };
    }

    public string GetMapDescription(int mapId)
    {
        UberstrikeMap map;
        if (mapsById.TryGetValue(mapId, out map) && map != null)
        {
            return map.Description;
        }
        else
        {
            return LocalizedStrings.None;
        }
    }

    public string GetMapName(int mapId)
    {
        UberstrikeMap map;
        if (mapsById.TryGetValue(mapId, out map) && map != null)
        {
            return map.Name;
        }
        else
        {
            return LocalizedStrings.None;
        }
    }

    public UberstrikeMap GetMapWithId(int mapId)
    {
        UberstrikeMap map = null;
        mapsById.TryGetValue(mapId, out map);
        return map;
    }

    public bool IsBlueBox(int mapId)
    {
        UberstrikeMap mapContainer;
        if (mapsById.TryGetValue(mapId, out mapContainer))
        {
            return mapContainer.IsBluebox;
        }
        else
        {
            return false;
        }
    }

    public bool HasMapWithId(int mapId)
    {
        return mapsById.ContainsKey(mapId);
    }

    public void AddLoadedMap(MapConfiguration map)
    {
        UberstrikeMap mapContainer;
        if (mapsById.TryGetValue(map.MapId, out mapContainer))
        {
            mapContainer.Space = map;
        }
        else
        {
            //TODO: why would that ever happen?
            mapContainer = new UberstrikeMap(new MapView()
                {
                    MapId = map.MapId,
                });
            mapContainer.Space = map;
            mapsById.Add(map.MapId, mapContainer);
        }

        //TODO: Scene system refactoring - TF
        // Right now we always load all maps 'additively' to our existing main scene.
        // Along with all assets unity is also loading the lightmaps and is automatically adding them to the LightmapSettings.lightmaps array.
        // Because we never unload scenes but only delete the gameobject hierarchy from our main scene
        // unity keeps filling up the lightmap array every time we load a new level!
        // The real fix would be to define every scene as self contained and stop using Application.LoadLevelAdditive
        // but just use Application.LoadLevel. We will be forced to keep a clean MVC archicture and get rid of all the 
        // mono configuration scripts we are using currently. But because 4.3.8 is on the way out we go for a HACK.
        if (map.MapId == 0)
            _originalLightmaps = LightmapSettings.lightmaps;

        // Capture the map's own additive scene BEFORE we reparent its root into
        // 'Levels' (which lives in the persistent 'Latest' scene) — after the
        // reparent, map.gameObject.scene reports 'Latest', not the map scene.
        string currentSceneName = map.gameObject.scene.name;

        map.transform.parent = GetLevelsParent();

        //clear all old levels
        foreach (KeyValuePair<int, UberstrikeMap> kvp in mapsById)
        {
            int id = kvp.Key;
            UberstrikeMap m = kvp.Value;
            // if not initial level and not this level
            if (id != 0 && mapContainer.Space != m.Space)
            {
                if (m.Space != null)
                {
                    GameObject.DestroyImmediate(m.Space.gameObject);
                    m.Space = null;
                }
            }
        }
        // Unity 2022 additive-scene leak fix. Application.LoadLevelAdditiveAsync
        // creates a PERSISTENT empty Scene container per map in 2022 (3.5.5 merged
        // additive loads into the active scene, so the DestroyImmediate above was
        // full cleanup). Unload every stale Level* container now — keeping the lobby
        // (LevelSpaceship / mapId 0), the map we just loaded, and the active scene.
        // 'Latest' and 'DontDestroyOnLoad' don't start with 'Level' -> never touched.
        // Load MODE is unchanged (stays additive); this touches neither
        // BeastLightmapLoader nor any shader/material. Removing the stale scenes also
        // stops Unity's stale-lightmap auto-union that the BeastMapLightmapGuard fights.
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.IsValid() || !s.isLoaded) continue;
            if (!s.name.StartsWith("Level")) continue;
            if (s.name == "LevelSpaceship") continue;         // lobby (mapId 0)
            if (s.name == currentSceneName) continue;         // the map we just loaded
            if (s == SceneManager.GetActiveScene()) continue; // never unload the active scene
            SceneManager.UnloadSceneAsync(s);
        }
        Resources.UnloadUnusedAssets();
        //Debug.Log("LightMaps: " + LightmapSettings.lightmaps.Length);
    }

    private Transform GetLevelsParent()
    {
        GameObject levels = GameObject.Find("Levels");
        if (levels == null)
        {
            levels = new GameObject("Levels");
        }
        return levels.transform;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="mapId">The ID of the map, as defined in instrumentation</param>
    /// <param name="name">The name of the scene to load, without the [Level] prefix</param>
    public void AddMapView(int mapId, string name)
    {
        mapsById.Add(mapId, new UberstrikeMap(CreateMapView(name, mapId)));
    }

    public void AddMapView(MapView mapView)
    {
        mapsById.Add(mapView.MapId, new UberstrikeMap(mapView));
    }

    // restore to initial settings
    private void Clear()
    {
        if (_initialMap == null)
        {
            _initialMap = new UberstrikeMap(CreateMapView("LevelSpaceShip", 0)) { IsEnabled = false };
        }
        mapsById.Clear();
        mapsById.Add(0, _initialMap);
    }

    public bool InitializeMapsToLoad(List<MapView> maps)
    {
        Clear();

        foreach (var m in maps)
        {
            if (!mapsById.ContainsKey(m.MapId))
            {
                mapsById.Add(m.MapId, new UberstrikeMap(m));
            }
        }

        return (mapsById.Count > 0);
    }

    public void CancelLoadMap(int mapId)
    {
        if (_loader.MapToLoad != null && _loader.MapToLoad.Id == mapId)
        {
            CoroutineManager.StopCoroutine(_loader.StartLoadingMap);
        }
    }

    public void LoadMap(int mapId)
    {
        UberstrikeMap map;
        if (mapsById.TryGetValue(mapId, out map) && !map.IsLoaded)
        {
            _loader.MapToLoad = map;
            CoroutineManager.StartCoroutine(_loader.StartLoadingMap, true);
        }
    }

    private class MapLoader
    {
        public UberstrikeMap MapToLoad { get; set; }
        public float Progress { get; private set; }

        public IEnumerator StartLoadingMap()
        {
            int id = CoroutineManager.Begin(StartLoadingMap);

            if (Application.isEditor && !ApplicationDataManager.Instance.LoadMapsUsingWebService)//Instance.IsSimulateWebplayer)
            {
                yield return Application.LoadLevelAdditiveAsync(MapToLoad.SceneName);
            }
            else
            {
                WWW loader;
                string fileToLoad = string.Empty;

#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
                if (System.IO.File.Exists(ApplicationDataManager.BaseStandaloneMapsURL + MapToLoad.FileName))
                {
                    Debug.LogError("Map found:" + ApplicationDataManager.BaseStandaloneMapsURL + MapToLoad.FileName);
                    fileToLoad = ApplicationDataManager.BaseStandaloneMapsURL + MapToLoad.FileName;
                    loader = new WWW("file://" + fileToLoad);
                }
                else
                {
                    Debug.LogWarning("Map NOT found:" + ApplicationDataManager.BaseStandaloneMapsURL + MapToLoad.FileName);
                    fileToLoad = ApplicationDataManager.BaseMapsURL + MapToLoad.FileName;
                    loader = WWW.LoadFromCacheOrDownload(fileToLoad, 1);
                }
#elif UNITY_EDITOR
                if (ApplicationDataManager.Instance.LoadMapsUsingWebService)
                {
                    fileToLoad = ApplicationDataManager.BaseMapsURL + MapToLoad.FileName;
                    loader = WWW.LoadFromCacheOrDownload(fileToLoad, 1);
                }
                //if (Instance.IsSimulateWebplayer)
                //{
                //    fileToLoad = Instance.SimulatedWebPlayerPath + MapToLoad.FileName;
                //    loader = new WWW(fileToLoad);
                //}
                else
                {
                    fileToLoad = ApplicationDataManager.BaseStandaloneMapsURL + MapToLoad.SceneName + ".unity3d";
                    loader = WWW.LoadFromCacheOrDownload(fileToLoad, 1);
                }
#else
                fileToLoad = ApplicationDataManager.BaseMapsURL + MapToLoad.FileName;
                loader = WWW.LoadFromCacheOrDownload(fileToLoad, 1);
#endif

                Progress = 0;
                while (!loader.isDone && CoroutineManager.IsCurrent(StartLoadingMap, id))
                {
                    yield return new WaitForEndOfFrame();
                    Progress = loader.progress;
                }

                Progress = 1;

                if (CoroutineManager.IsCurrent(StartLoadingMap, id))
                {
                    if (string.IsNullOrEmpty(loader.error))
                    {
                        AssetBundle assetBundle = loader.assetBundle;
                        if (assetBundle != null)
                        {
                            LightmapSettings.lightmaps = LevelManager.Instance._originalLightmaps;

                            //wait one more grace frame
                            yield return new WaitForEndOfFrame();
                            yield return Application.LoadLevelAdditiveAsync(MapToLoad.SceneName);

                            // Log the time it took to load a map
                            //GoogleAnalytics.Instance.LogEvent("app-map-load", MapToLoad.SceneName, Time.time - gaStartTime, true);

                            assetBundle.Unload(false);
                        }
                        else
                        {
                            CmuneDebug.LogError("Failed to load " + fileToLoad + ", probably outdated asset");
                        }
                    }
                    else
                    {
                        Debug.LogError("Loading Streamed Level at '" + fileToLoad + "' failed with error: " + loader.error);
                    }
                }

                loader.Dispose();
            }

            CoroutineManager.End(StartLoadingMap, id);
        }
    }
}