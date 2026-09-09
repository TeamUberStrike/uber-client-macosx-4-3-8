using System.Collections;
using System.Collections.Generic;
using Cmune.Realtime.Common.Utils;
using Cmune.Util;
using UberStrike.DataCenter.Common.Entities;
using UberStrike.Realtime.Common;
using UnityEngine;
using UberStrike.Core.Types;

/// <summary>
/// Manages the weapons that player can use in game. And responds to user input.
/// </summary>
public class WeaponController : Singleton<WeaponController>, IWeaponController
{
    public void NextWeapon()
    {
        if (!HasAnyWeapon) return;

        if (_weapon != null && _weapon.InputHandler != null)
        {
            _weapon.InputHandler.Stop();
            _lastLoadoutType = _weapon.Slot;
            _weapon = null;
        }

        int i = _currentSlotID.Next;
        while (_weapons[i] == null) i = _currentSlotID.Next;

        ShowWeapon(_slotTypes[i]);
    }

    public void PrevWeapon()
    {
        if (!HasAnyWeapon) return;

        if (_weapon != null && _weapon.InputHandler != null)
        {
            _weapon.InputHandler.Stop();
            _lastLoadoutType = _weapon.Slot;
            _weapon = null;
        }

        int i = _currentSlotID.Prev;
        while (_weapons[i] == null) i = _currentSlotID.Prev;

        ShowWeapon(_slotTypes[i]);
    }

    public void ShowFirstWeapon()
    {
        _currentSlotID.Reset();

        NextWeapon();
    }

    public bool ShowWeapon(LoadoutSlotType slot)
    {
        return ShowWeapon(slot, false);
    }

    public bool ShowWeapon(LoadoutSlotType slot, bool force)
    {
        if (HudAssets.Exists) TemporaryWeaponHud.Instance.Enabled = false;

        if (force || _weapon == null || _weapon.Slot != slot)
        {
            WeaponSlot newWeapon = null;

            switch (slot)
            {
                case LoadoutSlotType.WeaponMelee:
                    newWeapon = _weapons[0];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 0;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponMelee);
                        if (GameState.HasCurrentPlayer) GameState.LocalCharacter.CurrentWeaponSlot = 0;
                    } break;
                case LoadoutSlotType.WeaponPrimary:
                    newWeapon = _weapons[1];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 1;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponPrimary);
                        if (GameState.HasCurrentPlayer) GameState.LocalCharacter.CurrentWeaponSlot = 1;
                    } break;
                case LoadoutSlotType.WeaponSecondary:
                    newWeapon = _weapons[2];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 2;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponSecondary);
                        if (GameState.HasCurrentPlayer) GameState.LocalCharacter.CurrentWeaponSlot = 2;
                    }
                    break;
                case LoadoutSlotType.WeaponTertiary:
                    newWeapon = _weapons[3];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 3;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponTertiary);
                        if (GameState.HasCurrentPlayer) GameState.LocalCharacter.CurrentWeaponSlot = 3;
                    } break;
                case LoadoutSlotType.WeaponPickup:
                    newWeapon = _weapons[4];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 4;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponPickup);
                        if (GameState.HasCurrentPlayer)
                            GameState.LocalCharacter.CurrentWeaponSlot = 4;

                        if (TimeLeftForPickUpWeapon > 0 && HudAssets.Exists)
                        {
                            TemporaryWeaponHud.Instance.Enabled = true;
                            TemporaryWeaponHud.Instance.StartCounting(30);
                            TemporaryWeaponHud.Instance.RemainingSeconds = TimeLeftForPickUpWeapon;
                        }
                    } break;

                // default value
                default:
                    newWeapon = _weapons[1];
                    if (newWeapon != null)
                    {
                        _currentSlotID.Current = 1;
                        WeaponsHud.Instance.SetActiveLoadout(LoadoutSlotType.WeaponPrimary);
                        if (GameState.HasCurrentPlayer) GameState.LocalCharacter.CurrentWeaponSlot = 1;
                    } break;
            }

            if (newWeapon != null)
            {
                _weaponSwitchTimeout = Time.time + 0.2f;
                _weapon = newWeapon;

                UpdateAmmoHUD();

                if (_weapon.Logic != null && _weapon.Decorator != null)
                {
                    WeaponFeedbackManager.Instance.PickUp(_weapon.Logic, _weapon.Decorator);
                    _weapon.Decorator.PlayEquipSound();
                }
                else
                {
                    Debug.LogError("Failed to show weapon: logic is null = " + (_weapon.Logic == null) + " decorator is null = " + (_weapon.Decorator == null));
                }

                return true;
            }

            else if (!HasAnyWeapon)
            {
                return false;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    public void UpdateAmmoHUD()
    {
        if (_weapon != null && HudAssets.Exists)
        {
            AmmoHud.Instance.Ammo = AmmoDepot.AmmoOfClass(_weapon.Item.ItemClass);
        }
    }

    public void PutdownCurrentWeapon()
    {
        WeaponFeedbackManager.Instance.PutDown();
    }

    public void PickupCurrentWeapon()
    {
        if (_weapon != null)
        {
            WeaponFeedbackManager.Instance.PickUp(_weapon.Logic, _weapon.Decorator);
        }
    }

    public bool Shoot()
    {
        bool succeed = false;

        if (IsWeaponReady && GameState.HasCurrentPlayer)
        {
            _weapon.NextShootTime = Time.time + WeaponConfigurationHelper.GetRateOfFire(_weapon.Logic.Config);

            //check munition here (melee has always enough ammo)
            if (AmmoDepot.HasAmmoOfClass(_weapon.Item.ItemClass))
            {
                CmunePairList<BaseGameProp, ShotPoint> hits;
                Ray ray = new Ray(GameState.LocalCharacter.ShootingPoint + LocalPlayer.EyePosition, GameState.LocalCharacter.ShootingDirection);

                _weapon.Logic.Shoot(ray, out hits);

                if (!_weapon.Decorator.HasShootAnimation)
                    WeaponFeedbackManager.Instance.Fire();

                AmmoDepot.UseAmmoOfClass(_weapon.Item.ItemClass);

                //update HUD
                UpdateAmmoHUD();

                if (HudAssets.Exists)
                    ReticleHud.Instance.TriggerReticle(_weapon.Item.ItemClass);

                succeed = true;
            }
            else
            {
                _weapon.Decorator.PlayOutOfAmmoSound();

                GameState.LocalCharacter.IsFiring = false;
            }
        }

        return succeed;
    }

    public WeaponSlot GetPrimaryWeapon()
    {
        return _weapons[1];
    }

    public void InitializeAllWeapons(Transform _weaponAttachPoint)
    {
        for (int i = 0; i < _weapons.Length; i++)
        {
            if (_weapons[i] != null && _weapons[i].Decorator != null)
                GameObject.Destroy(_weapons[i].Decorator.gameObject);

            _weapons[i] = null;
        }

        WeaponInfo.SlotType[] weaponInfoSlots = new WeaponInfo.SlotType[]{
            WeaponInfo.SlotType.Melee, WeaponInfo.SlotType.Primary, WeaponInfo.SlotType.Secondary, WeaponInfo.SlotType.Tertiary};

        for (int i = 0; i < LoadoutManager.WeaponSlots.Length; i++)
        {
            LoadoutSlotType type = LoadoutManager.WeaponSlots[i];

            InventoryItem item;
            if (LoadoutManager.Instance.TryGetItemInSlot(type, out item))
            {
                WeaponItem weapon = item.Item as WeaponItem;

                WeaponSlot slot = new WeaponSlot(type, weapon, _weaponAttachPoint, this);
                AddGameLogicToWeapon(slot);
                if (slot.Decorator)
                {
                    slot.Decorator.EnableShootAnimation = true;
                    slot.Decorator.IronSightPosition = weapon.Configuration.IronSightPosition;
                }

                _weapons[i] = slot;

                AmmoDepot.SetMaxAmmoForType(weapon, weapon.Configuration.MaxAmmo);
                AmmoDepot.SetStartAmmoForType(weapon, weapon.Configuration.StartAmmo);

                if (GameState.HasCurrentPlayer)
                    GameState.LocalCharacter.Weapons.SetWeaponSlot(weaponInfoSlots[i], weapon.ItemId, weapon.ItemClass);

                if (HudAssets.Exists) WeaponsHud.Instance.Weapons.SetSlotWeapon(type, item.Item as WeaponItem);
            }
            else if (GameState.HasCurrentPlayer)
            {
                GameState.LocalCharacter.Weapons.SetWeaponSlot(weaponInfoSlots[i], 0, 0);

                if (HudAssets.Exists)
                {
                    WeaponsHud.Instance.Weapons.SetSlotWeapon(type, null);
                }
            }
        }

        if (GameState.HasCurrentPlayer)
            GameState.LocalCharacter.Weapons.SetWeaponSlot(WeaponInfo.SlotType.Pickup, 0, 0);

        QuickItemController.Instance.Initialize();

        if (HudAssets.Exists)
        {
            WeaponsHud.Instance.Weapons.SetSlotWeapon(LoadoutSlotType.WeaponPickup, null);
        }

        Reset();
    }

    public void ResetPickupWeaponSlotInSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            _pickUpWeaponAutoRemovalTime = 0;
        }
        else
        {
            _pickUpWeaponAutoRemovalTime = Time.time + seconds;
        }
    }

    public void Reset()
    {
        AmmoDepot.Reset();

        _currentSlotID.SetRange(0, 3);

        _weapon = null;

        ShowFirstWeapon();

        ResetPickupWeaponSlotInSeconds(0);
    }

    public void SetPickupWeapon(int weaponID)
    {
        SetPickupWeapon(weaponID, true, false);
    }

    public void SetPickupWeapon(int weaponID, bool uniqueWeaponClass, bool forceAutoEquip)
    {
        //count up the pickup event
        _pickupWeaponEventCount++;

        WeaponItem item = ItemManager.Instance.GetWeaponItemInShop(weaponID);
        if (item != null)
        {
            if (GameState.HasCurrentPlayer)
            {
                if (!GameState.LocalCharacter.Weapons.ItemIDs.Contains(item.ItemId))
                {
                    bool canEquipWeapon = true;

                    for (int i = 0; i < 4; i++)
                    {
                        if (_weapons[i] != null && _weapons[i].Item.ItemClass == item.ItemClass)
                            canEquipWeapon = false;
                    }

                    //do we have already a weapon of the samwe class equipped
                    if (canEquipWeapon || !uniqueWeaponClass)
                    {
                        if (_weapons[4] != null && _weapons[4].Decorator != null)
                        {
                            _weapons[4].InputHandler.Stop();

                            GameObject.Destroy(_weapons[4].Decorator.gameObject);
                        }

                        WeaponSlot slot = new WeaponSlot(LoadoutSlotType.WeaponPickup, item, GameState.LocalPlayer.WeaponAttachPoint, this);
                        AddGameLogicToWeapon(slot);
                        if (slot.Decorator)
                        {
                            slot.Decorator.EnableShootAnimation = true;
                            slot.Decorator.IronSightPosition = item.Configuration.IronSightPosition;
                        }

                        int selectedWeapon = _currentSlotID.Current;
                        _currentSlotID.SetRange(0, 4);
                        _currentSlotID.Current = selectedWeapon;

                        if (HudAssets.Exists)
                        {
                            WeaponsHud.Instance.Weapons.SetSlotWeapon(LoadoutSlotType.WeaponPickup, slot.Item);
                            AmmoHud.Instance.Ammo = AmmoDepot.AmmoOfClass(item.ItemClass);
                        }

                        LoadoutManager.Instance.EquipWeapon(LoadoutSlotType.WeaponPickup, item);
                        GameState.LocalCharacter.Weapons.SetWeaponSlot(WeaponInfo.SlotType.Pickup, item.ItemId, item.ItemClass);

                        AmmoDepot.SetMaxAmmoForType(slot.Item, slot.Item.Configuration.MaxAmmo);
                        AmmoDepot.SetStartAmmoForType(slot.Item, slot.Item.Configuration.StartAmmo);

                        _weapons[4] = slot;

                        if (_weapon == null || forceAutoEquip || ApplicationDataManager.ApplicationOptions.GameplayAutoEquipEnabled || _currentSlotID.Current == 4)
                        {
                            ShowWeapon(LoadoutSlotType.WeaponPickup, true);
                        }
                    }
                    else
                    {
                        // Expected gameplay, not an error: you walked over a weapon
                        // pickup whose class you already hold. Kept as an informational
                        // log so it stops spamming the console as a red error.
                        Debug.Log("SetPickupWeapon skipped: item of the same class already equipped");
                    }
                }
            }
            else
            {
                Debug.LogError("SetPickupWeapon failed because no player defined yet");
            }

            AmmoDepot.AddAmmoOfClass(item.ItemClass);
            UpdateAmmoHUD();
        }
        else
        {
            ResetPickupSlot();
        }
    }

    /// <summary>
    /// removing item for PickUp slot
    /// </summary>
    public void ResetPickupSlot()
    {
        if (HudAssets.Exists) TemporaryWeaponHud.Instance.Enabled = false;

        // if we have PickUp weapon we need to remove it
        if (_weapons[4] != null && _weapons[4].Decorator != null)
        {
            _weapons[4].InputHandler.Stop();

            WeaponItem ownedWeapon = null;
            if (GetPlayerWeaponOfPickupClass(out ownedWeapon))
            {
                AmmoDepot.SetMaxAmmoForType(ownedWeapon, ownedWeapon.Configuration.MaxAmmo);
                AmmoDepot.RemoveExtraAmmoOfType(ownedWeapon.ItemClass);
                UpdateAmmoHUD();
            }

            MonoRoutine.Start(StartHidingWeapon(_weapons[4].Decorator.gameObject, true));

            if (_weapon != null && _weapon.Slot == LoadoutSlotType.WeaponPickup)
                WeaponFeedbackManager.Instance.PutDown();

            int selectedWeapon = _currentSlotID.Current;
            _currentSlotID.SetRange(0, 3);
            if (selectedWeapon != 4)
            {
                _currentSlotID.Current = selectedWeapon;
            }

            _weapons[4] = null;
            if (HudAssets.Exists) WeaponsHud.Instance.Weapons.SetSlotWeapon(LoadoutSlotType.WeaponPickup, null);

            if (selectedWeapon == 4)
            {
                if (HudAssets.Exists) WeaponsHud.Instance.ResetActiveWeapon();

                ShowWeapon(_lastLoadoutType);
            }
        }
    }

    /// <summary>
    /// Check if player have this class of weapon in his loadout
    /// </summary>
    /// <param name="itemClass">Weapon class to check</param>
    /// <returns>True if player have that class in his inventory</returns>
    public bool HasWeaponOfClass(UberstrikeItemClass itemClass)
    {
        for (int i = 0; i < 5; i++)
        {
            WeaponSlot slot = _weapons[i];

            if (slot != null && slot.HasWeapon && slot.Item.ItemClass == itemClass)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if player fave given weapon id in his pickup slot
    /// </summary>
    /// <param name="id">Weapon ID to check in pickup slot</param>
    /// <returns></returns>
    public bool CheckPlayerWeaponInPickupSlot(int id)
    {
        if (_weapons[4] != null && _weapons[4].HasWeapon && _weapons[4].Item.ItemId == id)
        {
            return true;
        }

        return false;
    }

    public void StopInputHandler()
    {
        if (_weapon != null)
        {
            _weapon.InputHandler.Stop();
        }
    }

    public int NextProjectileId()
    {
        return ProjectileManager.CreateGlobalProjectileID(PlayerNumber, ++_projectileId);
    }

    public byte PlayerNumber
    {
        get { return GameState.LocalCharacter.PlayerNumber; }
    }

    public bool IsLocal
    {
        get { return true; }
    }

    public Vector3 ShootingPoint
    {
        get { return GameState.LocalCharacter.ShootingPoint; }
    }

    public Vector3 ShootingDirection
    {
        get { return GameState.LocalCharacter.ShootingDirection; }
    }

    #region PROPERTIES

    public bool HasAnyWeapon
    {
        get
        {
            foreach (WeaponSlot slot in _weapons)
            {
                if (slot != null)
                    return true;
            }

            return false;
        }
    }

    public BaseWeaponDecorator CurrentDecorator
    {
        get { if (IsWeaponValid) return _weapon.Decorator; else return null; }
    }

    public bool IsWeaponValid
    {
        get { return _weapon != null && _weapon.Logic != null && _weapon.Decorator != null; }
    }

    public bool IsWeaponReady
    {
        get { return IsWeaponValid && _weapon.NextShootTime < Time.time && _weapon.Logic.IsWeaponActive; }
    }

    public bool IsSecondaryAction
    {
        get { return _weapon != null && !_weapon.InputHandler.CanChangeWeapon(); }
    }

    public bool IsEnabled
    {
        get { return _isWeaponControlEnabled && InputManager.Instance.IsInputEnabled; }
        set { _isWeaponControlEnabled = value; }
    }

    public int TimeLeftForPickUpWeapon
    {
        get
        {
            if (_pickUpWeaponAutoRemovalTime > Time.time)
            {
                return Mathf.RoundToInt(_pickUpWeaponAutoRemovalTime - Time.time);
            }
            else
            {
                return -1;
            }
        }
    }

    public LoadoutSlotType CurrentSlot { get { return _weapon != null ? _weapon.Slot : LoadoutSlotType.None; } }

    #endregion

    #region Private
    #region FUNCTIONS

    private WeaponController()
    {
        _weapons = new WeaponSlot[5];

        _currentSlotID = new CircularInteger(0, 3);

        InitInputEventHandlers();

        CmuneEventHandler.AddListener<InputChangeEvent>(OnInputChanged);
    }

    private void OnInputChanged(InputChangeEvent ev)
    {
        if (GameState.HasCurrentPlayer && IsEnabled && GameState.LocalCharacter.IsAlive)
        {
            InputEventHandler handler;

            if (_gameInputHandlers.TryGetValue(ev.Key, out handler))
            {
                handler.Callback(ev, handler.SlotType);
            }
        }
    }

    private void InitInputEventHandlers()
    {
        _gameInputHandlers.Add(GameInputKey.WeaponMelee, new InputEventHandler(LoadoutSlotType.WeaponMelee, SelectWeaponCallback));
        _gameInputHandlers.Add(GameInputKey.Weapon1, new InputEventHandler(LoadoutSlotType.WeaponPrimary, SelectWeaponCallback));
        _gameInputHandlers.Add(GameInputKey.Weapon2, new InputEventHandler(LoadoutSlotType.WeaponSecondary, SelectWeaponCallback));
        _gameInputHandlers.Add(GameInputKey.Weapon3, new InputEventHandler(LoadoutSlotType.WeaponTertiary, SelectWeaponCallback));
        _gameInputHandlers.Add(GameInputKey.Weapon4, new InputEventHandler(LoadoutSlotType.WeaponPickup, SelectWeaponCallback));

        //_gameInputHandlers.Add(GameInputKey.QuickItem1, new InputEventHandler(LoadoutSlotType.QuickUseItem1, SelectQuickItemCallback));
        //_gameInputHandlers.Add(GameInputKey.QuickItem2, new InputEventHandler(LoadoutSlotType.QuickUseItem2, SelectQuickItemCallback));
        //_gameInputHandlers.Add(GameInputKey.QuickItem3, new InputEventHandler(LoadoutSlotType.QuickUseItem3, SelectQuickItemCallback));

        _gameInputHandlers.Add(GameInputKey.PrevWeapon, new InputEventHandler(LoadoutSlotType.None, PrevWeaponCallback));
        _gameInputHandlers.Add(GameInputKey.NextWeapon, new InputEventHandler(LoadoutSlotType.None, NextWeaponCallback));

        _gameInputHandlers.Add(GameInputKey.PrimaryFire, new InputEventHandler(LoadoutSlotType.None, PrimaryFireCallback));
        _gameInputHandlers.Add(GameInputKey.SecondaryFire, new InputEventHandler(LoadoutSlotType.None, SecondaryFireCallback));
    }

    private void SelectWeaponCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    {
        if (ev.IsDown && !LevelCamera.Instance.IsZoomedIn)
        {
            ShowWeapon(slotType);
        }
    }

    //private void SelectQuickItemCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    //{
    //    if (ev.IsDown && !LevelCamera.Instance.IsZoomedIn)
    //    {
    //        QuickItemController.Instance.UseQuickItem(slotType);
    //    }
    //}

    private void PrevWeaponCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    {
        //if (slotType != LoadoutSlotType.None)
        {
            if ((_weapon == null || ev.IsDown && _weapon.InputHandler.CanChangeWeapon()) && GUITools.SaveClickIn(0.2f))
            {
                GUITools.Clicked();
                NextWeapon();
            }
            else
            {
                if (_weapon != null && ev.IsDown)
                    _weapon.InputHandler.OnPrevWeapon();
            }
        }
    }

    private void NextWeaponCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    {
        if ((_weapon == null || ev.IsDown && _weapon.InputHandler.CanChangeWeapon()) && GUITools.SaveClickIn(0.2f))
        {
            GUITools.Clicked();
            PrevWeapon();
        }
        else
        {
            if (_weapon != null && ev.IsDown)
                _weapon.InputHandler.OnNextWeapon();
        }
    }

    private void PrimaryFireCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    {
        if (_weapon == null || !_weapon.HasWeapon) return;

        if (ev.IsDown)
        {
            // QS stuck-trigger fix for public issue #45 (Part 1/2):
            // previously a press-edge arriving during the 200ms
            // _weaponSwitchTimeout lockout fell through to the else branch
            // and called OnPrimaryFire(false), clobbering the handler's
            // _isTriggerPulled to false. Since InputChangeEvents only fire
            // on state transitions, no further event arrived while the user
            // kept LMB held — full-auto stalled and semi-auto needed a
            // release-and-repress. Now a press during lockout is simply
            // dropped; release events still flow through normally. Part 2
            // (LateUpdate reconciliation below) synthesizes the press once
            // the lockout clears if LMB is still physically held.
            if (CanPlayerShoot)
                _weapon.InputHandler.OnPrimaryFire(true);
        }
        else
        {
            GameState.LocalCharacter.IsFiring = false;
            _weapon.InputHandler.OnPrimaryFire(false);
        }
    }

    private void SecondaryFireCallback(InputChangeEvent ev, LoadoutSlotType slotType)
    {
        if (GameState.HasCurrentPlayer && GameState.LocalCharacter.IsAlive &&
            IsEnabled && _weapon != null && _weapon.HasWeapon)
        {
            _weapon.InputHandler.OnSecondaryFire(ev.IsDown);
        }
    }

    private bool CanPlayerShoot
    {
        get
        {
            return GameState.HasCurrentPlayer && IsEnabled && GameState.LocalCharacter.IsAlive && _weaponSwitchTimeout < Time.time;
        }
    }

    public void LateUpdate()
    {
        if (CanPlayerShoot)
        {
            // QS stuck-trigger fix for public issue #45 (Part 2/2):
            // if LMB is physically held but the handler's trigger state is
            // false (because the press-edge arrived during the switch
            // lockout and was dropped by Part 1, OR because InputHandler.Stop
            // cleared _isTriggerPulled while the gate was blocked),
            // synthesize the missing press now that the gate is open.
            // Idempotent when the real press event already arrived in the
            // same frame — handlers gate further work on their own internal
            // trigger state. _reconciledFireHeld prevents re-synthesizing on
            // every frame of a continuous hold.
            if (_weapon != null && _weapon.HasWeapon)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_reconciledFireHeld)
                    {
                        _weapon.InputHandler.OnPrimaryFire(true);
                        _reconciledFireHeld = true;
                    }
                }
                else
                {
                    _reconciledFireHeld = false;
                }
            }

            //single fire shots
            if (_weapon != null && _weapon.HasWeapon && _weaponSwitchTimeout < Time.time)
            {
                _weapon.InputHandler.Update();
            }

            // check if need to remove pickup weapon
            if (_pickUpWeaponAutoRemovalTime > 0 && _pickUpWeaponAutoRemovalTime < Time.time)
            {
                _pickUpWeaponAutoRemovalTime = 0;

                if (GameState.HasCurrentPlayer)
                    GameState.LocalCharacter.Weapons.SetWeaponSlot(WeaponInfo.SlotType.Pickup, 0, 0);

                ResetPickupSlot();
            }

            if (HudAssets.Exists) TemporaryWeaponHud.Instance.RemainingSeconds = TimeLeftForPickUpWeapon;
        }
        else
        {
            // Lockout just (re-)engaged: drop the reconciliation latch so
            // that when it clears, a still-held LMB is replayed as a fresh
            // press edge.
            _reconciledFireHeld = false;

            if (GameState.HasCurrentPlayer)
                GameState.LocalCharacter.IsFiring = false;

            if (_weapon != null && _weapon.InputHandler != null)
                _weapon.InputHandler.Stop();
        }
    }

    private IEnumerator StartHidingWeapon(GameObject weapon, bool destroy)
    {
        float time = 0;

        while (time < 2)
        {
            yield return new WaitForEndOfFrame();

            time += Time.deltaTime;
        }

        if (destroy)
            GameObject.Destroy(weapon);
    }

    private IEnumerator StartApplyDamage(float delay, CmunePairList<BaseGameProp, ShotPoint> hits)
    {
        yield return new WaitForSeconds(delay);

        ApplyDamage(hits);
    }

    private void ApplyDamage(CmunePairList<BaseGameProp, ShotPoint> hits)
    {
        foreach (KeyValuePair<BaseGameProp, ShotPoint> p in hits)
        {
            DamageInfo shot = new DamageInfo((short)(_weapon.Logic.Config.DamagePerProjectile * p.Value.Count))
            {
                Force = GameState.LocalPlayer.WeaponCamera.transform.forward * _weapon.Logic.Config.DamageKnockback,
                Hitpoint = p.Value.MidPoint,
                ShotID = p.Value.ProjectileId,
                WeaponID = _weapon.Logic.Config.ID,
                WeaponClass = _weapon.Logic.Config.ItemClass,
                DamageEffectFlag = _weapon.Logic.Config.DamageEffectFlag,
                DamageEffectValue = _weapon.Logic.Config.DamageEffectValue,
                CriticalStrikeBonus = WeaponConfigurationHelper.GetCriticalStrikeBonus(_weapon.Logic.Config),
            };

            switch (shot.WeaponClass)
            {
                case UberstrikeItemClass.WeaponSniperRifle:
                case UberstrikeItemClass.WeaponHandgun:
                    if (shot.CriticalStrikeBonus == 0)
                        shot.CriticalStrikeBonus = 0.5f;
                    break;
            }

            if (p.Key != null) p.Key.ApplyDamage(shot);
        }
    }

    private void AddGameLogicToWeapon(WeaponSlot weapon)
    {
        float movement = WeaponConfigurationHelper.GetRecoilMovement(weapon.Item.Configuration);
        float kickback = WeaponConfigurationHelper.GetRecoilKickback(weapon.Item.Configuration);
        LoadoutSlotType slot = weapon.Slot;

        if (weapon.Logic is ProjectileWeapon)
        {
            ProjectileWeapon w = weapon.Logic as ProjectileWeapon;
            w.OnProjectileShoot += (p) =>
                {
                    var detonator = new ProjectileDetonator(WeaponConfigurationHelper.GetSplashRadius(w.Config), w.Config.DamagePerProjectile, w.Config.DamageKnockback, p.Direction, p.Id, w.Config.ID, w.Config.ItemClass, w.Config.DamageEffectFlag, w.Config.DamageEffectValue);
                    if (p.Projectile != null)
                    {
                        // 'arm' the projectile
                        p.Projectile.Detonator = detonator;

                        //don't sync projectiles of splatterguns
                        if (w.Config.ItemClass != UberstrikeItemClass.WeaponSplattergun)
                            GameState.CurrentGame.EmitProjectile(p.Position, p.Direction, slot, p.Id, false);
                    }
                    else
                    {
                        // directly feed the explosion position
                        detonator.Explode(p.Position);
                        GameState.CurrentGame.EmitProjectile(p.Position, p.Direction, slot, p.Id, true);
                    }

                    // only emit projectiles for non splatter guns
                    if (w.Config.ItemClass == UberstrikeItemClass.WeaponSplattergun)
                    {
                        GameState.LocalCharacter.IsFiring = true;
                    }
                    else
                    {
                        if (w.HasProjectileLimit)
                        {
                            ProjectileManager.Instance.AddLimitedProjectile(p.Projectile, p.Id, w.MaxConcurrentProjectiles);
                        }
                        else
                        {
                            ProjectileManager.Instance.AddProjectile(p.Projectile, p.Id);
                        }
                    }

                    LevelCamera.Instance.DoFeedback(LevelCamera.FeedbackType.ShootWeapon, Vector3.back, 0, movement / 8f, 0.1f, 0.3f, kickback / 3f, Vector3.left);
                };
        }
        else if (weapon.Logic is MeleeWeapon)
        {
            float delay = weapon.Logic.HitDelay;
            weapon.Logic.OnTargetHit += (h) =>
                {
                    //set mode to firing
                    if (GameState.LocalCharacter != null)
                    {
                        if (weapon.Item.Configuration.HasAutomaticFire)
                            GameState.LocalCharacter.IsFiring = true;
                        else
                            GameState.CurrentGame.SingleBulletFire();
                    }

                    if (h != null)
                    {
                        MonoRoutine.Start(StartApplyDamage(delay, h));
                    }

                    LevelCamera.Instance.DoFeedback(LevelCamera.FeedbackType.ShootWeapon, Vector3.back, 0, movement / 8f, 0.1f, 0.3f, kickback / 3f, Vector3.left);
                };
        }
        else
        {
            weapon.Logic.OnTargetHit += (h) =>
                {
                    //set mode to firing
                    if (GameState.LocalCharacter != null)
                    {
                        if (weapon.Item.Configuration.HasAutomaticFire)
                            GameState.LocalCharacter.IsFiring = true;
                        else
                            GameState.CurrentGame.SingleBulletFire();
                    }

                    if (h != null)
                    {
                        ApplyDamage(h);
                    }

                    LevelCamera.Instance.DoFeedback(LevelCamera.FeedbackType.ShootWeapon, Vector3.back, 0, movement / 8f, 0.1f, 0.3f, kickback / 3f, Vector3.left);
                };
        }
    }

    /// <summary>
    /// Returns true if player already have equipped weapons with same class he have picked up weapon
    /// </summary>
    /// <param name="sameClassWeapon">Returned player equipped WeaponItem</param>
    /// <returns></returns>
    private bool GetPlayerWeaponOfPickupClass(out WeaponItem sameClassWeapon)
    {
        sameClassWeapon = null;

        if (_weapons[4] != null && _weapons[4].HasWeapon)
        {
            for (int i = 0; i < 4; i++)
            {
                WeaponSlot slot = _weapons[i];

                if (slot != null && slot.HasWeapon && slot.Item.ItemClass == _weapons[4].Item.ItemClass)
                {
                    sameClassWeapon = slot.Item;

                    return true;
                }
            }
        }

        return false;
    }

    private class InputEventHandler
    {
        public LoadoutSlotType SlotType { get; private set; }
        public System.Action<InputChangeEvent, LoadoutSlotType> Callback { get; private set; }

        public InputEventHandler(LoadoutSlotType slotType, System.Action<InputChangeEvent, LoadoutSlotType> callback)
        {
            SlotType = slotType;
            Callback = callback;
        }
    }
    #endregion

    #region FIELDS
    private WeaponSlot[] _weapons;
    private WeaponSlot _weapon;
    private CircularInteger _currentSlotID;

    private bool _isWeaponControlEnabled = true;

    private float _weaponSwitchTimeout = 0;
    private int _pickupWeaponEventCount = 0;
    private float _pickUpWeaponAutoRemovalTime = 0;
    private int _projectileId;

    // Latch for the QS press-during-lockout reconciliation in LateUpdate.
    // True while LMB is held and the handler has already been told about
    // it via the reconciled press; reset on release or when the gate
    // re-engages. Prevents re-synthesizing a press every frame.
    private bool _reconciledFireHeld = false;

    private LoadoutSlotType _lastLoadoutType = LoadoutSlotType.WeaponPrimary;

    private readonly LoadoutSlotType[] _slotTypes = { LoadoutSlotType.WeaponMelee, LoadoutSlotType.WeaponPrimary, LoadoutSlotType.WeaponSecondary, LoadoutSlotType.WeaponTertiary, LoadoutSlotType.WeaponPickup };

    private Dictionary<GameInputKey, InputEventHandler> _gameInputHandlers = new Dictionary<GameInputKey, InputEventHandler>();

    #endregion

    #endregion
}