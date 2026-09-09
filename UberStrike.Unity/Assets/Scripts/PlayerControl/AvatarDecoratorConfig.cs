
using System.Collections.Generic;
using Cmune.Util;
using UberStrike.DataCenter.Common.Entities;
using UnityEngine;
using UberStrike.Core.Types;

public class AvatarDecoratorConfig : MonoBehaviour
{
    [SerializeField]
    private AvatarBone[] _avatarBones;

    [SerializeField]
    private AvatarType _avatarType;

    private Color _skinColor;
    private List<Material> _materials;

    // SKIN-TONE LIFT. The skin albedo (e.g. LutzSkin.psd flesh ~ (0.85,0.68,0.52)) is a natural
    // light-tan meant to be shown near-white; the game multiplies the account SkinColor onto it. A dark
    // account value (a saved tan such as #C69C6D = (0.78,0.61,0.43)) multiplies the albedo DOWN into
    // orange-brown. This lifts the applied skin colour toward white so skin reads natural regardless of
    // the server value. Range [0,1]: 0 = raw account value (original behaviour, customisation honoured),
    // 1 = pure white (shows the raw albedo). TUNABLE: lower it to re-introduce the account skin tint.
    public static float SkinToneLift = 0.85f;

    private void Awake()
    {
        _materials = new List<Material>();

        foreach (AvatarBone b in _avatarBones)
        {
            b.Collider = b.Transform.GetComponent<Collider>();
            b.Rigidbody = b.Transform.GetComponent<Rigidbody>();
            b.OriginalPosition = b.Transform.localPosition;
            b.OriginalRotation = b.Transform.localRotation;
        }
    }

    public List<Material> MaterialGroup { get { return _materials; } }

    public Transform GetBone(BoneIndex bone)
    {
        foreach (AvatarBone b in _avatarBones)
        {
            if (b.Bone == bone)
                return b.Transform;
        }
        return transform;
    }

    public Color SkinColor
    {
        get { return _skinColor; }

        set
        {
            _skinColor = value;
            UpdateMaterials();
            var applied = Color.Lerp(_skinColor, Color.white, Mathf.Clamp01(SkinToneLift));
            foreach (var m in _materials)
            {
                if (m.name.Contains("Skin"))
                {
                    m.color = applied;
                }
            }
        }
    }

    public AvatarType AvatarType
    {
        get { return _avatarType; }
        set { _avatarType = value; }
    }

    public IEnumerable<AvatarBone> Bones
    {
        get { return _avatarBones; }
    }

    public void SetBones(List<AvatarBone> bones)
    {
        _avatarBones = bones.ToArray();
    }

    public void UpdateMaterials()
    {
        SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
        if (smr)
        {
            _materials.Clear();
            foreach (Material m in smr.materials)
            {
                _materials.Add(m);
            }
        }
        else
        {
            CmuneDebug.LogError("UpdateSkinMaterials failed because no SkinnedMeshRenderer attached on {0}!", gameObject.name);
        }
    }

    public static void CopyBones(AvatarDecoratorConfig srcAvatar, AvatarDecoratorConfig dstAvatar)
    {
        foreach (AvatarBone bone in srcAvatar._avatarBones)
        {
            Transform dst = dstAvatar.GetBone(bone.Bone);
            if (dst)
            {
                dst.position = bone.Transform.position;
                dst.rotation = bone.Transform.rotation;
            }
        }
    }
}