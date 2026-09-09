
using UberStrike.DataCenter.Common.Entities;
using UnityEngine;
using UberStrike.Core.Types;

public class ProjectileDetonator
{
    public float Radius { get; private set; }
    public float Damage { get; private set; }
    public int Force { get; private set; }
    public Vector3 Direction { get; set; }
    public int WeaponID { get; private set; }
    public UberstrikeItemClass WeaponClass { get; private set; }
    public int ProjectileID { get; private set; }
    public DamageEffectType DamageEffectFlag { get; private set; }
    public float DamageEffectValue { get; private set; }

    public ProjectileDetonator(float radius, float damage, int force, Vector3 direction, int projectileId, int weaponId,
        UberstrikeItemClass weaponClass, DamageEffectType damageEffectFlag, float damageEffectValue)
    {
        Radius = radius;
        Damage = damage;
        Force = force;
        Direction = direction;

        ProjectileID = projectileId;
        WeaponID = weaponId;
        WeaponClass = weaponClass;

        DamageEffectFlag = damageEffectFlag;
        DamageEffectValue = damageEffectValue;
    }

    public void Explode(Vector3 position)
    {
        ProjectileDetonator.Explode(position, ProjectileID, Damage, Direction, Radius, Force, WeaponID, WeaponClass, DamageEffectFlag, DamageEffectValue);
    }

    public static void Explode(Vector3 position, int projectileId, float damage, Vector3 dir, float radius, int force, int weaponId, UberstrikeItemClass weaponClass, DamageEffectType damageEffectFlag = 0, float damageEffectValue = 0)
    {
        Collider[] colliders = Physics.OverlapSphere(position, radius, UberstrikeLayerMasks.ExplosionMask);

        Vector3 testPoint = position;

        int i = 1;
        foreach (Collider hit in colliders)
        {
            BaseGameProp shootable = hit.transform.GetComponent<BaseGameProp>();
            if (shootable != null && shootable.RecieveProjectileDamage)
            {
                //ExplosionDebug.Hits.Add(hit.bounds.center);

                //nothing in between
                RaycastHit objectBetween;
                if (!Physics.Linecast(testPoint, hit.bounds.center, out objectBetween, UberstrikeLayerMasks.ProtectionMask) ||
                    objectBetween.transform == shootable.transform || objectBetween.transform.GetComponent<BaseGameProp>() != null)
                {
                    // Calculate distance from the explosion position to the closest point on the collider
                    Vector3 closestPoint = hit.ClosestPointOnBounds(position);

                    float falloff = 1;

                    Vector3 forceDirection = hit.transform.position - position;//closestPoint - position;
                    if (forceDirection.sqrMagnitude < 0.01f)
                    {
                        // Explosion is right at the target's origin (a straight-down cannon shot at your
                        // own feet — the vertical rocket-jump). Retail fell back to the projectile
                        // direction here, which is DOWNWARD for a straight-down shot and shoves you into
                        // the ground. Push UP instead so the vertical rocket-jump launches you.
                        forceDirection = Vector3.up;
                    }
                    else
                    {
                        if (radius > 1)
                        {
                            float distance = radius - Mathf.Clamp(forceDirection.magnitude, 0, radius);
                            falloff = Mathf.Clamp(distance / radius, 0, 0.6f) + 0.4f;
                        }

                        forceDirection = forceDirection.normalized;
                    }

                    // The hit points we apply fall decrease with distance from the explosion point
                    short d = (short)Mathf.CeilToInt(damage * falloff);

                    //local player 
                    if (shootable.IsLocal)
                    {
                        d /= 2;
                    }

                    if (d <= 0)
                    {
                        continue;
                    }

                    shootable.ApplyDamage(new DamageInfo(d)
                    {
                        Force = forceDirection * force,
                        Hitpoint = closestPoint,
                        ShotID = projectileId,
                        WeaponID = weaponId,
                        WeaponClass = weaponClass,
                        DamageEffectFlag = damageEffectFlag,
                        DamageEffectValue = damageEffectValue,
                    });
                }
            }

            i++;
        }
    }
}