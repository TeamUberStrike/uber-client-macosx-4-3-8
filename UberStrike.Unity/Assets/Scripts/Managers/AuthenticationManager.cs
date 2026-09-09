using System.Collections;
using Cmune.DataCenter.Common.Entities;
using Cmune.Util;
using UberStrike.Core.Types;
using UberStrike.Core.ViewModel;
using UberStrike.WebService.Unity;
using UnityEngine;

public class AuthenticationManager : Singleton<AuthenticationManager>
{
#if UNITY_EDITOR
    // Used to streamline WebPlayer testing
    // We load the test account from the PlayerPrefs - if it's invalid, we just show the login panel
    private string testWebPlayerEmail;
    private string testWebPlayerPassword;
#endif

    private bool _forceTutorialStart = false;
    private ProgressPopupDialog _progress;

    private AuthenticationManager()
    {
        _progress = new ProgressPopupDialog(LocalizedStrings.SettingUp, LocalizedStrings.ProcessingLogin);

#if UNITY_EDITOR
        testWebPlayerPassword = CmunePrefs.ReadKey<string>(CmunePrefs.Key.Player_Password);
        testWebPlayerEmail = CmunePrefs.ReadKey<string>(CmunePrefs.Key.Player_Email);
#endif
    }

    public void LoginByChannel()
    {
        switch (ApplicationDataManager.Channel)
        {
            case ChannelType.WebPortal:
            case ChannelType.WebFacebook:
            case ChannelType.Kongregate:
                {
#if UNITY_EDITOR
                    Debug.LogWarning(string.Format("Using test account to simulate {2} WebPlayer login. Email={0} Password={1}", testWebPlayerEmail, testWebPlayerPassword, ApplicationDataManager.Channel.ToString()));
                    AuthenticateMember(testWebPlayerEmail, testWebPlayerPassword);
#else
                    AuthenticateMember(ApplicationDataManager.WebPlayerSrcValues);
#endif
                    break;
                }
            case ChannelType.OSXStandalone:
            case ChannelType.WindowsStandalone:
            case ChannelType.MacAppStore:
                {
                    // Show the login panel
                    MenuPageManager.Instance.LoadPage(PageType.Login, true);
                    break;
                }
            default:
                {
                    CmuneDebug.LogError("No login mode defined for unsupported channel: " + ApplicationDataManager.Channel);
                    break;
                }
        }
    }

    public void AuthenticateMember(string emailAddress, string password)
    {
        MonoRoutine.Start(StartAuthenticateMember(emailAddress, password, null));
    }

    public void AuthenticateMember(WebPlayerSrcValues webArguments)
    {
        MonoRoutine.Start(StartAuthenticateMember(string.Empty, string.Empty, webArguments));
    }

    private IEnumerator StartAuthenticateMember(string emailAddress, string password, WebPlayerSrcValues webArguments)
    {
        // Choose the right authentication method
        _progress.Text = "Authenticating Account";
        _progress.ManualProgress = 0.1f;
        PopupSystem.Show(_progress);
        MemberAuthenticationResultView loginResult = null;

        if (webArguments != null && webArguments.IsValid)
        {
            yield return UberStrike.WebService.Unity.AuthenticationWebServiceClient.LoginMemberCookie(
                webArguments.Cmid,
                webArguments.Expiration,
                webArguments.Content,
                webArguments.Hash,
                ApplicationDataManager.Channel,
                SystemInfo.deviceUniqueIdentifier,
                (ev) => loginResult = ev,
                (ex) =>
                {
                    DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
                });
        }
        else if (!string.IsNullOrEmpty(emailAddress) && !string.IsNullOrEmpty(password))
        {
            yield return AuthenticationWebServiceClient.LoginMemberEmail(emailAddress, password, ApplicationDataManager.Channel, SystemInfo.deviceUniqueIdentifier,
                (ev) => loginResult = ev,
                (ex) =>
                {
                    DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
                });
        }
        else
        {
            ShowLoginErrorPopup(LocalizedStrings.Error, "The email address or password you are trying to use is empty.");
            yield break;
        }

        // Check authorization
        if (loginResult == null)
        {
            //ShowLoginErrorPopup(LocalizedStrings.Error, "Login was unsuccessful. There was an error communicating with the server.");
            ShowLoginErrorPopup(LocalizedStrings.Error, "There was a problem loading UberStrike. Please check your internet connection and try again.");
            yield break;
        }
        else if (loginResult.MemberAuthenticationResult != MemberAuthenticationResult.Ok)
        {
            PopupSystem.HideMessage(_progress);
            LoginPanelGUI.ErrorMessage = LocalizedStrings.LoginFailed + ": " + loginResult.MemberAuthenticationResult.ToString();
            LoginPanelGUI.IsBanned = loginResult.MemberAuthenticationResult == MemberAuthenticationResult.IsBanned;
            if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // Editor/WebGL lock the app on a login failure. Only show the "banned" message when the
                // account is actually banned; otherwise surface the real reason (set just above on
                // LoginPanelGUI.ErrorMessage, e.g. "Login Failed: InvalidMember") so a missing account or
                // version mismatch isn't misdiagnosed as a ban — which previously cost a debug session.
                ApplicationDataManager.Instance.LockApplication(
                    LoginPanelGUI.IsBanned ? LocalizedStrings.YourAccountHasBeenBanned : LoginPanelGUI.ErrorMessage);
            }
            else
            {
                PanelManager.Instance.OpenPanel(PanelType.Login);
            }
            yield break;
        }

        PlayerDataManager.Instance.ServerLocalPlayerMemberView = loginResult.MemberView;
        ApplicationDataManager.ServerDateTime = loginResult.ServerTime;
        CmuneEventHandler.Route(new LoginEvent(loginResult.MemberView.PublicProfile.AccessLevel));

        _progress.Text = "Loading Friends List";
        _progress.ManualProgress = 0.15f;
        yield return MonoRoutine.Start(CommsManager.Instance.GetContactsByGroups());

        // Get the level caps
        _progress.Text = "Loading Character Data";
        _progress.ManualProgress = 0.2f;

        yield return UserWebServiceClient.GetLevelCapsView(
             ApplicationDataManager.Instance.SetLevelCapsView,
             (ex) =>
             {
                 DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
                 ApplicationDataManager.Instance.LockApplication("There was an error loading player level data.");
             });

        //set the player statistics view after we got the level caps
        PlayerDataManager.Instance.SetPlayerStatisticsView(loginResult.PlayerStatisticsView);

        // Get the live feeds (only if we are not on MAS or OSX Standalones, as the top area of the menu is blocked by OS X - it's a Unity bug... grrr)
        if (ApplicationDataManager.Channel != ChannelType.MacAppStore && ApplicationDataManager.Channel != ChannelType.OSXStandalone)
        {
            _progress.Text = "Loading News Feeds";
            _progress.ManualProgress = 0.3f;

            yield return ApplicationWebServiceClient.GetLiveFeed(
                 GlobalUIRibbon.Instance.SetLiveFeeds,
                 (ex) =>
                 {
                     DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
                     ApplicationDataManager.Instance.LockApplication("There was an error getting the latest news.");
                 });
        }

        // Get all maps dynamically
        _progress.Text = "Loading Map Data";
        _progress.ManualProgress = 0.4f;

        bool mapsLoadedSuccessfully = true;
        MapType mapType = MapType.StandardDefinition;
        switch (ApplicationDataManager.Channel)
        {
            case ChannelType.MacAppStore:
            case ChannelType.WindowsStandalone:
                mapType = MapType.HighDefinition;
                break;
        }
        yield return ApplicationWebServiceClient.GetMaps(ApplicationDataManager.VersionShort, UberStrike.Core.Types.LocaleType.en_US, mapType,
            (callback) =>
            {
                mapsLoadedSuccessfully = LevelManager.Instance.InitializeMapsToLoad(callback);
            },
            (ex) =>
            {
                DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
                ApplicationDataManager.Instance.LockApplication("There was an error loading the maps.");
            });

        if (!mapsLoadedSuccessfully)
        {
            ShowLoginErrorPopup(LocalizedStrings.Error, "There was an error getting the Maps.\nPlease contact support@cmune.com");
            PopupSystem.HideMessage(_progress);
            yield break;
        }

        // Get the Item Mall
        _progress.ManualProgress = 0.5f;
        _progress.Text = "Loading Weapons and Gear";

        yield return MonoRoutine.Start(ItemManager.Instance.StartGetShop());

        // If the Item Mall failed
        if (!ItemManager.Instance.ValidateItemMall())
        {
            ShowLoginErrorPopup("Error Getting Shop Data", "There was an error getting the Shop Data.\nPlease contact support@cmune.com");
            PopupSystem.HideMessage(_progress);
            yield break;
        }

        // Get the players inventory
        _progress.ManualProgress = 0.6f;
        _progress.Text = "Loading Player Inventory";

        yield return MonoRoutine.Start(ItemManager.Instance.StartGetInventory(false));

        if (!InventoryManager.Instance.HasPrivateersLicense())
        {
            ShowLoginErrorPopup(LocalizedStrings.Error, "It looks like you're trying to login with an old account.\nPlease contact support@cmune.com to upgrade.");
            yield break;
        }

        // Get the players loadout
        _progress.ManualProgress = 0.7f;
        _progress.Text = "Getting Player Loadout";

        yield return MonoRoutine.Start(PlayerDataManager.Instance.StartGetLoadout());
        // If the loadout failed
        if (!LoadoutManager.Instance.ValidateLoadout())
        {
            ShowLoginErrorPopup("Error Getting Player Loadout", "There was an error getting the Player Loadout.\nPlease contact support@cmune.com");
            yield break;
        }

        // Get the players statistics
        _progress.ManualProgress = 0.85f;
        _progress.Text = "Loading Player Statistics";
        yield return MonoRoutine.Start(PlayerDataManager.Instance.StartGetMember());

        // If players statistics failed
        if (!PlayerDataManager.Instance.ValidateMemberData())
        {
            ShowLoginErrorPopup("Error Getting Player Statistics", "There was an error getting the Player Statistics.\nPlease contact support@cmune.com");
            yield break;
        }

        // Checking in clan
        _progress.ManualProgress = 0.9f;
        _progress.Text = "Loading Clan Data";

        yield return ClanWebServiceClient.GetMyClanId(PlayerDataManager.CmidSecure, CommonConfig.ApplicationIdUberstrike,
            (id) =>
            {
                PlayerDataManager.ClanID = id;
                ClanDataManager.Instance.RefreshClanData(true);
            },
            (ex) =>
            {
                DebugConsoleManager.SendExceptionReport(ex.Message, ex.StackTrace);
            });

        // Show the menu bar in the browser
        ApplicationDataManager.Instance.ShowMenuTabsInBrowser();

        // Save the last login credentials
        CmunePrefs.WriteKey(CmunePrefs.Key.Player_Email, emailAddress);
        CmunePrefs.WriteKey(CmunePrefs.Key.Player_Password, password);

        // Update the email address stored in PlayerDataManager (mostly useful for standalone/mobile builds where we pass the email in some Urls)
        if (!string.IsNullOrEmpty(emailAddress)) PlayerDataManager.EmailSecure = emailAddress;

        GameState.LocalDecorator = AvatarBuilder.Instance.CreateLocalAvatar();

        // If everything went ok, we can load the home scene and show the main menu.
        PopupSystem.HideMessage(_progress);

        yield return new WaitForEndOfFrame();

        LotteryManager.Instance.RefreshLotteryItems();
        yield return new WaitForEndOfFrame();
        InboxManager.Instance.Initialize();
        yield return new WaitForEndOfFrame();
        MasBundleManager.Instance.Initialize();
        yield return new WaitForEndOfFrame();

        if (loginResult.WeeklySpecial != null)
        {
            ItemPromotionManager.Instance.WeeklySpecial = new ItemPromotionView(loginResult.WeeklySpecial);
        }

        // Open the tutorial if the player is new
        if (!loginResult.IsTutorialComplete || _forceTutorialStart)
        {
            GameStateController.Instance.LoadLevel(0);
            GameStateController.Instance.LoadGameMode(GameMode.Tutorial);
        }
        else if (!loginResult.IsAccountComplete)
        {
            // Get the player to choose an in-game name
            PanelManager.Instance.OpenPanel(PanelType.CompleteAccount);
        }
        else
        {
            // Authentication completed successfully, allow the player to access the home screen
            MenuPageManager.Instance.LoadPage(PageType.Home);
            GlobalUIRibbon.IsVisible = true;
            GlobalUIRibbon.Instance.Show();

            //lucky draw is only available 1x per day
            if (loginResult.LuckyDraw != null)
            {
                var popup = LotteryManager.Instance.RunLuckyDraw(loginResult.LuckyDraw.ToUnityItem());
                popup.ShowNavigationArrows = false;
                popup.HelpText = "CLAIM YOUR DAILY LUCK";

                //show facebook popup right after (1x day)
                if (ApplicationDataManager.Channel == ChannelType.WebFacebook)
                {
                    EventPopupManager.Instance.AddEventPopup(new FacebookInvitePopupDialog());
                }
            }
        }
    }

    private void ShowLoginErrorPopup(string title, string message)
    {
        PopupSystem.HideMessage(_progress);
        PopupSystem.ShowMessage(title, message, PopupSystem.AlertType.OK,
            () =>
            {
                LoginPanelGUI.ErrorMessage = string.Empty;
                LoginByChannel();
            });
    }
}