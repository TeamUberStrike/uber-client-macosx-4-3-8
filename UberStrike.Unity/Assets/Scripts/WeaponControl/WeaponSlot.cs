using UberStrike.DataCenter.Common.Entities;
using UnityEngine;
using UberStrike.Core.Types;

public class WeaponSlot
{
    public LoadoutSlotType Slot { get; private set; }
    public BaseWeaponLogic Logic { get; private set; }
    public BaseWeaponDecorator Decorator { get; private set; }
    public WeaponItem Item { get; private set; }
    public WeaponInputHandler InputHandler { get; private set; }

    public float NextShootTime { get; set; }
    public bool HasWeapon { get { return Item != null; } }

    public WeaponSlot(LoadoutSlotType slot, WeaponItem item, Transform attachPoint, IWeaponController controller)
    {
        Slot = slot;
        Item = item;

        CreateWeaponLogic(item, controller);
        CreateWeaponInputHandler(Logic, item, Decorator, controller.IsLocal);

        ConfigureWeaponDecorator(attachPoint);
    }

    private void CreateWeaponLogic(WeaponItem weapon, IWeaponController controller)
    {
        switch (weapon.ItemClass)
        {
            case UberstrikeItemClass.WeaponCannon:
            case UberstrikeItemClass.WeaponLauncher:
            case UberstrikeItemClass.WeaponSplattergun:
                {
                    ProjectileWeaponDecorator decorator = CreateProjectileWeaponDecorator(weapon);
                    Logic = new ProjectileWeapon(weapon, decorator, controller);
                    Decorator = decorator;
                }
                break;
            case UberstrikeItemClass.WeaponMachinegun:
                {
                    Decorator = InstantiateWeaponDecorator(weapon);
                    if (weapon.Configuration.ProjectilesPerShot > 1)
                        Logic = new InstantMultiHitWeapon(weapon, Decorator, weapon.Configuration.ProjectilesPerShot, controller);
                    else
                        Logic = new InstantHitWeapon(weapon, Decorator, controller);
                }
                break;

            case UberstrikeItemClass.WeaponHandgun:
            case UberstrikeItemClass.WeaponSniperRifle:
                {
                    Decorator = InstantiateWeaponDecorator(weapon);
                    Logic = new InstantHitWeapon(weapon, Decorator, controller);
                }
                break;

            case UberstrikeItemClass.WeaponMelee:
                {
                    Decorator = InstantiateWeaponDecorator(weapon);
                    Logic = new MeleeWeapon(weapon, Decorator as MeleeWeaponDecorator, controller);
                    break;
                }
            case UberstrikeItemClass.WeaponShotgun:
                {
                    Decorator = InstantiateWeaponDecorator(weapon);
                    Logic = new InstantMultiHitWeapon(weapon, Decorator, weapon.Configuration.ProjectilesPerShot, controller);
                    break;
                }

            default:
                {
                    throw new System.Exception("Failed to create weapon logic!");
                }
        }
    }

    private ProjectileWeaponDecorator CreateProjectileWeaponDecorator(WeaponItem item)
    {
        ProjectileWeaponDecorator weapon = ItemManager.Instance.Instantiate(item.ItemId).GetComponent<ProjectileWeaponDecorator>();

        if (weapon)
        {
            //weapon.SetMaxProjectile(item.MaxConcurrentProjectiles);
            weapon.SetMissileTimeOut(item.Configuration.MissileTimeToDetonate / 1000f);
        }

        return weapon;
    }

    private BaseWeaponDecorator InstantiateWeaponDecorator(WeaponItem item)
    {
        return ItemManager.Instance.Instantiate(item.ItemId).GetComponent<BaseWeaponDecorator>();
    }

    private void ConfigureWeaponDecorator(Transform parent)
    {
        if (Decorator)
        {
            Decorator.IsEnabled = false;
            Decorator.transform.parent = parent;
            Decorator.DefaultPosition = Item.Configuration.Position;
            Decorator.DefaultAngles = Item.Configuration.Rotation;
            Decorator.CurrentPosition = Item.Configuration.Position;
            Decorator.gameObject.name = Slot + " " + Item.ItemClass;

            Decorator.SetSurfaceEffect(Item.Configuration.ParticleEffect);

            LayerUtil.SetLayerRecursively(Decorator.transform, UberstrikeLayer.Weapons);
        }
        else
        {
            Debug.LogError("Failed to configure WeaponDecorator!");
        }
    }

    private void CreateWeaponInputHandler(IWeaponLogic logic, WeaponItem item, BaseWeaponDecorator decorator, bool isLocal)
    {
        switch (item.Configuration.InputHandlerType)
        {
            case WeaponInputHandlerType.SniperRifle:
                // Prefab _zoomInformation is clobbered when the loadout config is rebuilt, so force the
                // intended per-weapon zoom here (see SniperZoomOverride).
                InputHandler = new SniperRifleInputHandler(logic, isLocal, SniperZoomOverride.Resolve(item.Configuration));
                break;

            case WeaponInputHandlerType.Minigun:
                InputHandler = new MinigunInputHandler(logic, isLocal, decorator as MinigunWeaponDecorator);
                break;

            default:
                if (item.Configuration.SecondaryAction == WeaponSecondaryAction.ExplosionTrigger)
                    InputHandler = new ExplosionInputHandler(logic, isLocal);
                else if (item.Configuration.SecondaryAction == WeaponSecondaryAction.IronSight)
                    InputHandler = new IronsightInputHandler(logic, isLocal, SniperZoomOverride.Resolve(item.Configuration));
                else if (item.Configuration.HasAutomaticFire)
                    InputHandler = new FullAutoWeaponInputHandler(logic, isLocal);
                else
                    InputHandler = new SemiAutoWeaponInputHandler(logic, isLocal);
                break;
        }
    }
}