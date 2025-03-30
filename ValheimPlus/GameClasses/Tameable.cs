﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    public enum TameableMortalityTypes
    {
        Normal,
        Essential,
        Immortal
    }

    [Flags]
    public enum AnimalType
    {
        None = 0,
        Boar = 1 << 0,
        Wolf = 1 << 1,
        Lox = 1 << 2,
        Hen = 1 << 3,
        Asksvin = 1 << 4,
        All = (1 << 5) - 1
    }

    public static class TameableHelpers
    {
        public readonly static Dictionary<string, AnimalType> NamedTypes = new() {
            { "$enemy_asksvin", AnimalType.Asksvin },
            { "$enemy_asksvin_hatchling", AnimalType.Asksvin },
            { "$enemy_boar", AnimalType.Boar },
            { "$enemy_boarpiggy", AnimalType.Boar },
            { "$enemy_wolf", AnimalType.Wolf },
            { "$enemy_wolfcub", AnimalType.Wolf },
            { "$enemy_lox", AnimalType.Lox },
            { "$enemy_loxcalf", AnimalType.Lox },
            { "$enemy_hen", AnimalType.Hen },
            { "$enemy_chicken", AnimalType.Hen }
        };

        public static bool ShouldIgnoreHunger(Tameable instance)
        {
            var config = Configuration.Current.Tameable;
            if (!config.IsEnabled || !config.ignoreHunger || !IsValidAnimalType(instance.m_character.m_name))
                return false;

            if (!instance.m_nview)
                return false;

            // if timeLeft > 0 we can ignore hunger to prevent random taming
            // The player MUST initiate taming with a piece of food
            float timeLeft = instance.m_nview.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft);
            return timeLeft == 0 && instance.IsHungry();
        }

        public static bool IsValidAnimalType(string name)
        {
            if (!NamedTypes.TryGetValue(name, out AnimalType type))
                return false;

            var config = Configuration.Current.Tameable;
            return config.animalTypes.HasFlag(type);
        }
    }

    /// <summary>
    /// Adds a text indicator so player's know when an animal they've tamed has been stunned.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    public static class Tameable_GetHoverText_Patch
    {
        public static void Postfix(Tameable __instance, ref string __result)
        {
            if (Configuration.Current.Tameable.IsEnabled && Configuration.Current.Tameable.stunInformation)
            {
                // If tamed creature is recovering from a stun, then add Stunned to hover text.
                if (__instance.m_character.m_nview.GetZDO().GetBool("isRecoveringFromStun"))
                    __result = __result.Insert(__result.IndexOf(" )"), ", Stunned");
            }

            var procreation = __instance.GetComponent<Procreation>();
            if (procreation != null)
                ProcreationHelpers.AddLoveInformation(__instance, procreation, ref __result);
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.IsHungry))]
    public static class Tameable_IsHungry_Patch
    {
        private static void Postfix(Tameable __instance, ref bool __result)
        {
            if (!__instance.m_character)
            {
                ValheimPlusPlugin.Logger.LogWarning("Tameable_IsHungry_Patch: m_character is null");
                return;
            }

            var isTamed = __instance.m_character.IsTamed();
            __result = __result && !(isTamed ?
                ProcreationHelpers.ShouldIgnoreHunger(__instance)
                : TameableHelpers.ShouldIgnoreHunger(__instance));
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Awake))]
    public static class Tameable_Awake_Patch
    {
        public static void Postfix(Tameable __instance)
        {
            var config = Configuration.Current.Tameable;
            if (!config.IsEnabled)
                return;

            if (!TameableHelpers.IsValidAnimalType(__instance.m_character.m_name))
                return;

            Helper.applyModifierValueTo(ref __instance.m_tamingTime, config.tameTimeMultiplier);
            Helper.applyModifierValueTo(ref __instance.m_tamingSpeedMultiplierRange, config.tameBoostRangeMultiplier);
            Helper.applyModifierValueTo(ref __instance.m_tamingBoostMultiplier, config.tameBoostMultiplier);

            if (__instance.m_nview.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, out float timeLeft))
                if (timeLeft > __instance.m_tamingTime)
                    __instance.m_nview.GetZDO().Set(ZDOVars.s_tameTimeLeft, __instance.m_tamingTime);
        }
    }

    [HarmonyPatch(typeof(Tameable))]
    public static class Tameable_Alerted_Patches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Tameable), nameof(Tameable.TamingUpdate));
            yield return AccessTools.Method(typeof(Tameable), nameof(Tameable.GetStatusString));
        }

        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            var config = Configuration.Current.Tameable;
            if (!config.IsEnabled || !config.ignoreAlerted)
                return instructions;

            var matcher = new CodeMatcher(instructions, ilGenerator);
            BaseAIHelpers.IsAlertedTranspiler(matcher, TameableHelpers.IsValidAnimalType);
            return matcher.InstructionEnumeration();
        }
    }
}
