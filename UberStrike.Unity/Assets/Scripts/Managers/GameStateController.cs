using System;
using Cmune.Util;
using UberStrike.Realtime.Common;
using UnityEngine;
using UberStrike.Core.Types;

public class GameStateController : Singleton<GameStateController>
{
    private ShopTryWeaponState _shopTryWeaponState;
    private DeathMatchState _deathMatchState;
    private TeamDeathMatchState _teamDeathMatchState;
    private TeamEliminationMatchState _teamEliminationMatchState;

    public StateMachine StateMachine { get; private set; }

    private GameStateController()
    {
        StateMachine = new StateMachine();
        _shopTryWeaponState = new ShopTryWeaponState();
        _deathMatchState = new DeathMatchState();
        _teamDeathMatchState = new TeamDeathMatchState();
        _teamEliminationMatchState = new TeamEliminationMatchState();

        StateMachine.RegisterState((int)GameStateId.DeathMatch, _deathMatchState);
        StateMachine.RegisterState((int)GameStateId.TeamDeathMatch, _teamDeathMatchState);
        StateMachine.RegisterState((int)GameStateId.TeamElimination, _teamEliminationMatchState);
        StateMachine.RegisterState((int)GameStateId.TryWeapon, _shopTryWeaponState);
        StateMachine.RegisterState((int)GameStateId.Training, new TrainingState());
        StateMachine.RegisterState((int)GameStateId.Tutorial, new TutorialState());

        CmuneEventHandler.AddListener<ShopTryEvent>(OnTryItem);
    }

    protected override void OnDispose()
    {
        CmuneEventHandler.RemoveListener<ShopTryEvent>(OnTryItem);
    }

    public void CreateGame(GameMetaData game)
    {
        // Dismiss the menu page we launched from (Play / Training / Create-Game).
        // Without this the launching PageScene stays active once the match loads,
        // so its OnGUI keeps running underneath gameplay -> console spam + a menu
        // overlay bleeding into the match. UnloadCurrentPage() is the canonical
        // page teardown (same call LoadPage uses on a page switch); it deactivates
        // the page GameObject, resets the current page to None, and drops the menu
        // MouseOrbit. No-op if no page is up. Pure UI teardown -- no map, shader,
        // material or lightmap involvement.
        MenuPageManager.Instance.UnloadCurrentPage();

        AvatarBuilder.Instance.UpdateLocalAvatar();

        LobbyConnectionManager.Stop();

        //check if the player is already connected to the current server
        if (!GameConnectionManager.Instance.IsConnectedToServer(game.ServerConnection))
        {
            GameConnectionManager.Stop();
        }
        if (IsMultiplayerGameMode(game.GameMode)) GameConnectionManager.Start(game);

        LoadLevel(game.MapID);
        LoadGameMode((GameMode)game.GameMode, game);
    }

    public void SpectateCurrentGame()
    {
        if (GameState.HasCurrentGame)
        {
            ModeratorGameMode.ModerateGameMode(GameState.CurrentGame);
        }
        else
        {
            Debug.LogError("SpectateCurrentGame: GameState doesn't has any game!");
        }
    }

    public void LoadLevel(int mapId)
    {
        var map = LevelManager.Instance.GetMapWithId(mapId);
        if (map != null)
        {
            GameState.SetCurrentSpace(map.Space);
            GameState.LocalPlayer.SetEnabled(true);
            GameState.LocalPlayer.SetPlayerControlState(LocalPlayer.PlayerState.None);
            SpawnPointManager.Instance.ConfigureSpawnPoints(GameState.CurrentSpace.SpawnPoints.GetComponentsInChildren<SpawnPoint>(true));
        }
        else
        {
            CmuneDebug.LogError("LoadLevel " + mapId + " failed because map can't be loaded");
        }
    }

    public void LoadTryWeaponMode(int itemId = 0)
    {
        BackgroundMusicPlayer.Instance.Stop();

        _shopTryWeaponState.ItemId = itemId;
        StateMachine.SetState((int)GameStateId.TryWeapon);
    }

    public static bool IsMultiplayerGameMode(int mode)
    {
        switch (mode)
        {
            case GameModeID.DeathMatch:
            case GameModeID.TeamDeathMatch:
            case GameModeID.EliminationMode:
                return true;
            default:
                return false;
        }
    }

    public void LoadGameMode(GameMode mode, GameMetaData data = null)
    {
        BackgroundMusicPlayer.Instance.Stop();

        switch (mode)
        {
            case GameMode.DeathMatch:
                _deathMatchState.GameMetaData = data;
                StateMachine.SetState((int)GameStateId.DeathMatch);
                break;

            case GameMode.TeamDeathMatch:
                _teamDeathMatchState.GameMetaData = data;
                StateMachine.SetState((int)GameStateId.TeamDeathMatch);
                break;

            case GameMode.TeamElimination:
                _teamEliminationMatchState.GameMetaData = data;
                StateMachine.SetState((int)GameStateId.TeamElimination);
                break;

            case GameMode.Tutorial:
                StateMachine.SetState((int)GameStateId.Tutorial);
                break;

            case GameMode.Training:
                StateMachine.SetState((int)GameStateId.Training);
                break;

            default:
                throw new NotImplementedException("The Game mode " + mode + " is not supported");
        }
    }

    public void UnloadGameMode()
    {
        StateMachine.PopAllStates();

        // Log game unload to Google Analytics
        //GoogleAnalytics.Instance.LogEvent("ui-loadgame-event", _gaGameMode.ToString(), Time.time - _gaStartTime, false);
    }

    public void UnloadLevelAndLoadPage(PageType page)
    {
        UnloadGameMode();

        BackgroundMusicPlayer.Instance.Play();
        MenuPageManager.Instance.LoadPage(page, false);
    }

    private void OnTryItem(ShopTryEvent ev)
    {
        if (ev.Item.ItemType == UberstrikeItemType.Weapon)
        {
            LoadTryWeaponMode(ev.Item.ItemId);
        }
        else if (ev.Item.ItemType == UberstrikeItemType.Gear)
        {
            TemporaryLoadoutManager.Instance.SetGearLoadout(InventoryManager.GetSlotTypeForGear(ev.Item as GearItem), ev.Item as GearItem);
            AvatarAnimationManager.Instance.SetAnimationState(PageType.Shop, ev.Item.ItemClass);

            switch (ev.Item.ItemType)
            {
                case UberstrikeItemType.Gear:
                    CmuneEventHandler.Route(new SelectLoadoutAreaEvent() { Area = LoadoutArea.Gear });
                    break;
                case UberstrikeItemType.Weapon:
                    CmuneEventHandler.Route(new SelectLoadoutAreaEvent() { Area = LoadoutArea.Weapons });
                    break;
                case UberstrikeItemType.QuickUse:
                    CmuneEventHandler.Route(new SelectLoadoutAreaEvent() { Area = LoadoutArea.QuickItems });
                    break;
            }
        }
    }
}