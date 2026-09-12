using System;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Flare;

// Attached only to this item's equipped visual. Equipment and bow_aim are
// already synchronized by the game, so observers need no additional RPC.
internal sealed class SignalDrawLight : MonoBehaviour
{
    private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftItem =
        AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
    private Player? player;
    private Animator? animator;
    private VisEquipment? equipment;
    private Transform? leftHand, rightHand;
    private GameObject? effect;
    private Light? light;
    private bool failed;

    private void LateUpdate()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || failed) return;
        if (!player)
        {
            player = GetComponentInParent<Player>();
            if (!player) return;
            animator = player.GetComponentInChildren<Animator>();
            equipment = player.GetComponentInChildren<VisEquipment>();
            var bones = player.GetComponentsInChildren<Transform>(true);
            leftHand = bones.FirstOrDefault(t => t.name == "LeftHand_Attach");
            rightHand = bones.FirstOrDefault(t => t.name == "RightHand_Attach");
        }
        bool drawing = animator && equipment && LeftItem(equipment) == gameObject &&
            !player.IsDead() && !player.IsStaggering() && animator.GetBool("bow_aim");
        if (!drawing || !leftHand || !rightHand) { if (effect) effect.SetActive(false); return; }
        try
        {
            if (!effect) CreateEffect();
            if (!effect) return;
            var direction = leftHand.position - rightHand.position;
            if (direction.sqrMagnitude < .001f) { effect.SetActive(false); return; }
            effect.transform.SetPositionAndRotation(rightHand.position, Quaternion.LookRotation(direction, player.transform.up));
            effect.SetActive(true);
            light!.shadows = Plugin.Instance.LightShadows.Value ? LightShadows.Soft : LightShadows.None;
        }
        catch (Exception e)
        {
            failed = true;
            if (effect) Destroy(effect);
            Plugin.Instance.Log("Drawn signal lighting unavailable: " + e.Message);
        }
    }

    private void CreateEffect()
    {
        var arrow = PrefabManager.Instance.GetPrefab("ArrowFire");
        var model = arrow.transform.Find("model");
        var mesh = model.GetComponent<MeshFilter>().sharedMesh;
        var projectile = arrow.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_attackProjectile;
        var visual = projectile.transform.Find("visual");
        var torchLight = PrefabManager.Instance.GetPrefab("Torch").transform.Find("attach/equiped/Point light");
        effect = new GameObject("norskit_flare_drawn_arrow");
        effect.SetActive(false); effect.transform.SetParent(transform, false);
        var arrowModel = new GameObject("model"); arrowModel.transform.SetParent(effect.transform, false);
        arrowModel.AddComponent<MeshFilter>().sharedMesh = mesh;
        arrowModel.AddComponent<MeshRenderer>().sharedMaterials = model.GetComponent<MeshRenderer>().sharedMaterials;
        // The arrow mesh runs along +Z; put its nock at the drawing hand.
        arrowModel.transform.localPosition = Vector3.forward * -mesh.bounds.min.z;
        float length = mesh.bounds.size.z;
        foreach (var name in new[] { "flames_local", "flames" })
        {
            var flames = Instantiate(visual.Find(name).gameObject, effect.transform);
            flames.transform.localPosition = Vector3.forward * length;
            flames.transform.localRotation = Quaternion.identity;
            flames.SetActive(true);
        }
        // Clone only the light and LightFlicker: no warmth, damage, audio or networking.
        var lamp = Instantiate(torchLight.gameObject, effect.transform);
        lamp.transform.localPosition = Vector3.forward * length;
        light = lamp.GetComponent<Light>();
        lamp.SetActive(true);
    }

    private void OnDisable() { if (effect) effect.SetActive(false); }
    private void OnDestroy() { if (effect) Destroy(effect); }
}
