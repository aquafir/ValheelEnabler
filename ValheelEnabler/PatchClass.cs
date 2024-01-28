using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using static ACE.Server.WorldObjects.Player;

namespace ValheelEnabler;

[HarmonyPatch]
public class PatchClass
{
    #region Settings
    const int RETRIES = 10;

    public static Settings Settings = new();
    static string settingsPath => Path.Combine(Mod.ModPath, "Settings.json");
    private FileInfo settingsInfo = new(settingsPath);

    private JsonSerializerOptions _serializeOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private void SaveSettings()
    {
        string jsonString = JsonSerializer.Serialize(Settings, _serializeOptions);

        if (!settingsInfo.RetryWrite(jsonString, RETRIES))
        {
            ModManager.Log($"Failed to save settings to {settingsPath}...", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
        }
    }

    private void LoadSettings()
    {
        if (!settingsInfo.Exists)
        {
            ModManager.Log($"Creating {settingsInfo}...");
            SaveSettings();
        }
        else
            ModManager.Log($"Loading settings from {settingsPath}...");

        if (!settingsInfo.RetryRead(out string jsonString, RETRIES))
        {
            Mod.State = ModState.Error;
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<Settings>(jsonString, _serializeOptions);
        }
        catch (Exception)
        {
            ModManager.Log($"Failed to deserialize Settings: {settingsPath}", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
            return;
        }
    }
    #endregion

    #region Start/Shutdown
    public void Start()
    {
        //Need to decide on async use
        Mod.State = ModState.Loading;
        LoadSettings();

        if (Mod.State == ModState.Error)
        {
            ModManager.DisableModByPath(Mod.ModPath);
            return;
        }

        Mod.State = ModState.Running;
    }

    public void Shutdown()
    {
        //if (Mod.State == ModState.Running)
        // Shut down enabled mod...

        //If the mod is making changes that need to be saved use this and only manually edit settings when the patch is not active.
        //SaveSettings();

        if (Mod.State == ModState.Error)
            ModManager.Log($"Improper shutdown: {Mod.ModPath}", ModManager.LogLevel.Error);
    }
    #endregion

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.Player_Tick), new Type[] { typeof(double) })]
    public static void PostPlayer_Tick(double currentUnixTime, ref Player __instance)
    {
        //Cooldowns
        __instance.ValHeelAbilityManager(__instance);
    }

    //Add to end of spellcasts
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.DoCastSpell_Inner), new Type[] { typeof(Spell), typeof(WorldObject), typeof(uint), typeof(WorldObject), typeof(CastingPreCheckStatus), typeof(bool) })]
    public static void PostDoCastSpell_Inner(Spell spell, WorldObject casterItem, uint manaUsed, WorldObject target, CastingPreCheckStatus castingPreCheckStatus, bool finishCast, ref Player __instance)
    {
        var lifeMagicSkill = __instance.GetCreatureSkill(Skill.LifeMagic);
        var warMagicSkill = __instance.GetCreatureSkill(Skill.WarMagic);
        var warChannelRoll = ThreadSafeRandom.Next((float)0.0, 1.0f);
        var warChannelChance = 0.25f;
        var currentUnixTime = (uint)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        if (__instance.IsHealer)
        {
            // Lifemagic HoT/Sot
            if (spell.School == MagicSchool.LifeMagic && spell.IsBeneficial && spell.VitalDamageType == DamageType.Health && !__instance.IsHoTTicking && lifeMagicSkill.AdvancementClass > SkillAdvancementClass.Trained)
                __instance.LifeMagicHot(__instance, (Player)target, Player.GetHoTSpell(spell.Name));
            else if (spell.School == MagicSchool.LifeMagic && spell.IsBeneficial && spell.VitalDamageType == DamageType.Stamina && !__instance.IsHoTTicking && lifeMagicSkill.AdvancementClass > SkillAdvancementClass.Trained)
                __instance.LifeMagicSot(__instance, (Player)target, Player.GetHoTSpell(spell.Name));
        }

        // War Channeling
        if (__instance.IsDps)
        {
            if (__instance.LastWarChannelTimestamp == 0)
                __instance.LastWarChannelTimestamp = currentUnixTime - __instance.WarChannelTimerDuration;

            if (spell.School == MagicSchool.WarMagic && spell.NumProjectiles > 0 && /*currentUnixTime - LastWarChannelTimestamp >= WarChannelTimerDuration &&*/ warChannelChance >= warChannelRoll && warMagicSkill.AdvancementClass > SkillAdvancementClass.Trained)
            {
                var procSpell = spell;
                int numCasts = __instance.NumOfChannelCasts;

                Player.WarMagicChannel(__instance, (Creature)target, procSpell, numCasts, false);
                __instance.LastWarChannelTimestamp = currentUnixTime;
            }
        }

        if (__instance.IsSneaking == true)
        {
            __instance.IsSneaking = false;
            __instance.UnSneak();
            __instance.SetProperty(PropertyInt.CloakStatus, (int)CloakStatus.Off);
            __instance.PlayParticleEffect(PlayScript.EnchantUpGreen, __instance.Guid);
        }
    }

    //Redirect to Val version of missile launch
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.LaunchMissile), new Type[] { typeof(WorldObject), typeof(int), typeof(MotionStance), typeof(bool) })]
    public static bool PreLaunchMissile(WorldObject target, int attackSequence, MotionStance stance, bool subsequent, ref Player __instance)
    {
        __instance.ValLaunchMissile(target, attackSequence, stance, subsequent);
        return false;
    }

    //Redirect to Val version of use
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.HandleActionUseItem), new Type[] { typeof(uint) })]
    public static bool PreHandleActionUseItem(uint itemGuid, ref Player __instance)
    {
        __instance.ValHandleActionUseItem(itemGuid);
        return false;
    }

    //Redirect to Val version of attack
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.Attack), new Type[] { typeof(WorldObject), typeof(int), typeof(bool) })]
    public static bool PreAttack(WorldObject target, int attackSequence, bool subsequent, ref Player __instance)
    {
        __instance.ValAttack(target, attackSequence, subsequent);

        return false;
    }

    //Redirect for targeting tactics
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.FindNextTarget))]
    public static bool PreFindNextTarget(ref Creature __instance, ref bool __result)
    {
        __result = __instance.ValFindNextTarget();
        return false;
    }

    //Add missing stuff to DamageHistoryInfo by an alternative constructor.  Not ideal
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DamageHistoryInfo), MethodType.Constructor, new Type[] { typeof(WorldObject), typeof(float) })]
    public static bool PreCtorDamageHistoryInfo(WorldObject attacker, float totalDamage, ref DamageHistoryInfo __instance)
    {
        __instance = new DamageHistoryInfo(attacker, true, totalDamage);
        return false;
    }
}

