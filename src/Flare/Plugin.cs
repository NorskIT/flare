using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using SignalSupport;
using UnityEngine;

namespace Flare;

[BepInPlugin(Id, "Flare", Version)]
[BepInDependency(Jotunn.Main.ModGuid)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "norskit_flare_plugin", Version = "1.3.2";
    internal static Plugin Instance = null!;
    internal ConfigEntry<float> Height = null!, Burn = null!, Descent = null!, Strength = null!, LightRadius = null!, LightIntensity = null!;
    internal ConfigEntry<bool> LightShadows = null!;
    internal ConfigEntry<float> SmokeLifetime = null!, SmokeStrength = null!;
    internal SignalNetwork Network = null!;
    private SignalVisuals visuals = null!;
    private Harmony harmony = null!;
    private readonly List<UnityEngine.Object> assets = new();

    private void Awake()
    {
        Instance = this;
        Height = Setting("Height", 150f, 20f, 500f, "Metres reached by a fully drawn vertical shot.");
        Burn = Setting("BurnSeconds", 60f, 5f, 180f, "Seconds of burning after the apex.");
        Descent = Setting("DescentSpeed", 1f, .2f, 10f, "Downward metres per second after the apex.");
        Strength = Setting("GlowStrength", 1f, 0f, 4f, "Brightness of the visible flare; no minimum screen size.");
        LightRadius = Setting("LightRadius", 250f, 0f, 1000f, "Light reach in metres from the airborne flare. Zero disables illumination.");
        LightIntensity = Setting("LightIntensity", 3f, 0f, 20f, "Brightness of illuminated terrain, buildings and characters.");
        LightShadows = Config.Bind("Local", "LightShadows", true, "Cast flare shadows on this client. Subject to game graphics settings; never synchronized.");
        SmokeLifetime = Setting("SmokeLifetime", 20f, 5f, 60f, "Seconds before emitted smoke dissolves.");
        SmokeStrength = Setting("SmokeStrength", 1f, 0f, 3f, "Opacity of the smoke trail. Zero disables smoke.");
        Network = new SignalNetwork(this);
        visuals = new SignalVisuals(Network);
        PrefabManager.OnVanillaPrefabsAvailable += RegisterItems;
        harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        CommandManager.Instance.AddConsoleCommand(new StatusCommand());
        Logger.LogInfo("Flare ready. Craft Bow & Flare with 1 Feather, 1 Surtling Core, 1 Wood and 1 Fine Wood.");
    }

    private ConfigEntry<float> Setting(string key, float value, float min, float max, string description) =>
        Config.Bind("Signal", key, value, new ConfigDescription(description + " The server controls each shot.",
            new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdminOnly = true }));

    private void RegisterItems()
    {
        var bow = new CustomItem(SignalIdentity.Bow, "Bow", new ItemConfig {
            Name = "Bow & Flare", Description = "Fire a harmless signal to help friends find you. Aim high. Consumed after one shot; no ammunition required.",
            Amount = 1, Requirements = new[] { new RequirementConfig("Feathers", 1), new RequirementConfig("SurtlingCore", 1),
                new RequirementConfig("Wood", 1), new RequirementConfig("FineWood", 1) } });
        Prepare(bow);
        bow.ItemPrefab.transform.Find("attach").gameObject.AddComponent<SignalDrawLight>();
        ItemManager.Instance.AddItem(bow);
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterItems;
    }

    private void Prepare(CustomItem item)
    {
        var shared = item.ItemDrop.m_itemData.m_shared;
        shared.m_ammoType = "";
        shared.m_maxQuality = 1;
        shared.m_maxStackSize = 1;
        shared.m_useDurability = false;
        shared.m_damages = default; shared.m_damagesPerLevel = default;
        shared.m_attackForce = 0;
        shared.m_skillType = Skills.SkillType.None;
        shared.m_attackStatusEffect = shared.m_equipStatusEffect = shared.m_setStatusEffect = null;
        shared.m_spawnOnHit = shared.m_spawnOnHitTerrain = null;
        // Retain draw/animation, remove every native projectile and damage path.
        shared.m_attack = shared.m_attack.Clone();
        shared.m_attack.m_attackProjectile = null;
        shared.m_attack.m_spawnOnTrigger = shared.m_attack.m_spawnOnHit = null;
        shared.m_attack.m_attackEitr = shared.m_attack.m_drawEitrDrain = shared.m_attack.m_reloadEitrDrain = 0;
        shared.m_attack.m_raiseSkillAmount = 0;
        shared.m_attack.m_consumeItem = false;
        shared.m_secondaryAttack = new Attack();
        shared.m_icons = new[] { MakeIcon() };
    }

    private Sprite MakeIcon()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Flare.item_icon.png")!;
        using var bytes = new System.IO.MemoryStream(); stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        // Avoid linking the installed Unity image module's newer netstandard facade.
        var conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true)!;
        var load = conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })!;
        if (!(bool)load.Invoke(null, new object[] { texture, bytes.ToArray(), true }))
            throw new InvalidOperationException("Unable to load signal item icon.");
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        assets.Add(sprite); assets.Add(texture);
        return sprite;
    }

    internal void Log(string message) => Logger.LogWarning(message);
    internal string SetServerSetting(string key, float value)
    {
        var entries = new[] { Height, Burn, Descent, Strength, LightRadius, LightIntensity, SmokeLifetime, SmokeStrength };
        foreach (var entry in entries)
        {
            if (!string.Equals(key, entry.Definition.Key, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Signal.Finite(value) || !entry.Description.AcceptableValues.IsValid(value)) return "Value is outside the setting's allowed range.";
            entry.Value = value;
            Config.Save();
            return entry.Definition.Key + " = " + value.ToString(CultureInfo.InvariantCulture);
        }
        return "Unknown server setting. Use Height, BurnSeconds, DescentSpeed, LightRadius, LightIntensity, GlowStrength, SmokeLifetime or SmokeStrength.";
    }
    private void Update() { Network.Update(); visuals.Update(); }
    private void OnDestroy()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterItems;
        harmony?.UnpatchSelf(); visuals?.Dispose(); Network?.Dispose();
        foreach (var asset in assets) if (asset) Destroy(asset);
    }
    internal static string Prefab(ItemDrop.ItemData? item) => item?.m_dropPrefab ? item.m_dropPrefab.name : "";
    internal static bool IsBow(ItemDrop.ItemData? item) => item != null && SignalIdentity.IsBow(Prefab(item), item.m_shared.m_itemType.ToString(), item.m_shared.m_ammoType);
    private sealed class StatusCommand : ConsoleCommand
    {
        public override string Name => "flare";
        public override string Help => "flare | flare reload | flare set <setting> <value> | flare shadows on/off (local)";
        public override void Run(string[] args, Terminal context)
        {
            if (args.Length == 1 && args[0] == "reload") context.AddString(Instance.Network.Reload());
            else if (args.Length == 3 && args[0] == "set" && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                context.AddString(Instance.Network.Set(args[1], value));
            else if (args.Length == 2 && args[0] == "shadows" && (args[1] == "on" || args[1] == "off"))
            {
                Instance.LightShadows.Value = args[1] == "on";
                Instance.Config.Save();
                context.AddString("Local flare shadows: " + args[1]);
            }
            else context.AddString(Instance.Network.Status);
        }
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class SignalAttackGate
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
    {
        if (__instance != Player.m_localPlayer || !Plugin.IsBow(__instance.GetCurrentWeapon())) return true;
        if (!secondaryAttack && Plugin.Instance.Network.CanFire) return true;
        __result = false;
        __instance.Message(MessageHud.MessageType.Center, "The signal needs a ready connection and uses primary attack only.");
        return false;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class SignalTrigger
{
    private sealed class Fired { }
    private static readonly ConditionalWeakTable<Attack, Fired> fired = new();
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Attack __instance, Humanoid ___m_character, ItemDrop.ItemData ___m_weapon, float ___m_attackDrawPercentage)
    {
        if (!Plugin.IsBow(___m_weapon)) return true;
        if (___m_character != Player.m_localPlayer || ___m_character.IsStaggering() || fired.TryGetValue(__instance, out _)) return false;
        fired.Add(__instance, new Fired());
        Plugin.Instance.Network.Fire(___m_character, ___m_weapon, ___m_attackDrawPercentage);
        return false;
    }
}
