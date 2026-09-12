namespace SignalSupport;
public static class SignalIdentity
{
    public const string Bow = "norskit_flare_flarebow";
    public static bool IsBow(string prefab, string type, string ammo) => prefab == Bow && type == "Bow" && ammo == "";
}
