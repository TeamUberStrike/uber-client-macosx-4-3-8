using UberStrike.Realtime.Common;
using Cmune.Util;
using UnityEngine;

class TrainingState : IState
{
    public TrainingState()
    {
        _stateMachine = new StateMachine();
        _stateMachine.RegisterState((int)GameStateId.PregameLoadout, new InGamePregameLoadoutState());
        _stateMachine.RegisterState((int)GameStateId.Playing, new InGamePlayingState(_stateMachine, _hudDrawFlag));
        _stateMachine.RegisterState((int)GameStateId.Killed, new InGamePlayerKilledState(_stateMachine, _hudDrawFlag, true));
        _stateMachine.RegisterState((int)GameStateId.Paused, new InGamePlayerPausedState(_stateMachine));
    }

    public void OnEnter()
    {
        _trainingGameMode = new TrainingFpsMode(GameConnectionManager.Rmi);
        GameState.CurrentGame = _trainingGameMode;

        MenuPageManager.Instance.UnloadCurrentPage();
        MenuPageManager.Instance.enabled = false;

        LevelCamera.Instance.SetLevelCamera(GameState.CurrentSpace.Camera,
            GameState.CurrentSpace.DefaultViewPoint.position,
            GameState.CurrentSpace.DefaultViewPoint.rotation);
        GameState.LocalPlayer.SetPlayerControlState(LocalPlayer.PlayerState.None);
        GameState.LocalPlayer.SetEnabled(true);

        CmuneEventHandler.AddListener<OnModeInitializedEvent>(OnModeStart);

        HudUtil.Instance.SetPlayerTeam(TeamID.NONE);
        QuickItemController.Instance.IsConsumptionEnabled = false;
        QuickItemController.Instance.Restriction.IsEnabled = false;

        _stateMachine.SetState((int)GameStateId.PregameLoadout);
    }

    public void OnExit()
    {
        _stateMachine.PopAllStates();
        CmuneEventHandler.RemoveListener<OnModeInitializedEvent>(OnModeStart);
        GameModeUtil.OnExitGameMode();
        _trainingGameMode = null;
    }

    public void OnUpdate()
    {
        if (_trainingGameMode.IsMatchRunning)
        {
            QuickItemController.Instance.Update();
            if (_trainingGameMode.IsWaitingForSpawn)
            {
                GameModeUtil.UpdateWaitingForSpawnMsg(_trainingGameMode, false);
            }

            _stateMachine.Update();
        }
    }

    public void OnGUI()
    {
        if (_trainingGameMode.IsMatchRunning)
        {
            _stateMachine.OnGUI();
        }
    }

    #region Private

    private void OnModeStart(OnModeInitializedEvent ev)
    {
        _stateMachine.SetState((int)GameStateId.Playing);

        GameState.LocalPlayer.SetWeaponControlState(PlayerHudState.Playing);

        HudUtil.Instance.ClearAllFeedbackHud();
        FrameRateHud.Instance.Enable = true;
        ShowTrainingGameMessages();
        HudUtil.Instance.AddInGameEvent(GameState.LocalCharacter.PlayerName, LocalizedStrings.EnteredTrainingMode);
    }

    private void ShowTrainingGameMessages()
    {
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage, string.Empty);
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage, LocalizedStrings.TrainingTutorialMsg01);
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage, LocalizedStrings.MessageQuickItemsTry);
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage, LocalizedStrings.TrainingTutorialMsg03);
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage, LocalizedStrings.TrainingTutorialMsg04);
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg05,
            InputManager.Instance.InputChannelForSlot(GameInputKey.Forward),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Left),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Backward),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Right)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg06,
            InputManager.Instance.InputChannelForSlot(GameInputKey.PrimaryFire)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg07,
            InputManager.Instance.InputChannelForSlot(GameInputKey.NextWeapon),
            InputManager.Instance.InputChannelForSlot(GameInputKey.PrevWeapon)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg08,
            InputManager.Instance.InputChannelForSlot(GameInputKey.WeaponMelee),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Weapon1),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Weapon2),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Weapon3),
            InputManager.Instance.InputChannelForSlot(GameInputKey.Weapon4)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg09,
            InputManager.Instance.InputChannelForSlot(GameInputKey.Crouch)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            string.Format(LocalizedStrings.TrainingTutorialMsg10,
            InputManager.Instance.InputChannelForSlot(GameInputKey.Fullscreen)));
        EventFeedbackHud.Instance.EnqueueFeedback(InGameEventFeedbackType.CustomMessage,
            LocalizedStrings.TrainingTutorialMsg11);
    }

    private TrainingFpsMode _trainingGameMode;
    private StateMachine _stateMachine;
    private const HudDrawFlags _hudDrawFlag = HudDrawFlags.HealthArmor |
                    HudDrawFlags.Ammo | HudDrawFlags.Weapons | HudDrawFlags.EventStream |
                    HudDrawFlags.Reticle | HudDrawFlags.XpPoints | HudDrawFlags.StateMsg;

    #endregion
}
