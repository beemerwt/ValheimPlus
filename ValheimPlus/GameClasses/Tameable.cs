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

    public static class TameableHelpers
    {
        private static bool IgnoreHungry(Tameable tameable) => false;

        public static CodeMatcher IgnoreHungerTranspiler(CodeMatcher matcher, Func<Tameable, bool> pred = null)
        {
            var ignoreHungerMethod = AccessTools.Method(typeof(TameableHelpers), nameof(IgnoreHungry));
            var tameableIsHungry = AccessTools.Method(typeof(Tameable), nameof(Tameable.IsHungry));
            return matcher
                .MatchStartForward(new CodeMatch(inst => inst.Calls(tameableIsHungry)))
                .ThrowIfNotMatch("No match for IsHungry method call.")
                .Set(OpCodes.Call, pred?.Method ?? ignoreHungerMethod)
                .Start();
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
                ProcreationHelper.AddLoveInformation(__instance, procreation, ref __result);
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Awake))]
    public static class Tameable_Awake_Patch
    {
        public static void Postfix(Tameable __instance)
        {
            if (!Configuration.Current.Tameable.IsEnabled)
                return;

            __instance.m_tamingTime = Configuration.Current.Tameable.tameTime;
            __instance.m_tamingSpeedMultiplierRange = Configuration.Current.Tameable.tameBoostRange;
            __instance.m_tamingBoostMultiplier = Configuration.Current.Tameable.tameBoostMultiplier;

            if (__instance.m_nview.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, out float timeLeft))
                if (timeLeft > __instance.m_tamingTime)
                    __instance.m_nview.GetZDO().Set(ZDOVars.s_tameTimeLeft, __instance.m_tamingTime);
        }
    }

    [HarmonyPatch(typeof(Tameable))]
    public static class Tameable_IgnoreStatus_Patches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Tameable), nameof(Tameable.GetStatusString));
            yield return AccessTools.Method(typeof(Tameable), nameof(Tameable.TamingUpdate));
        }

        public static bool ShouldEnforceHunger(Tameable instance)
        {
            // if timeLeft > 0 we can ignore hunger to prevent random taming
            // The player MUST initiate taming with a piece of food
            float timeLeft = instance.m_nview.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft);
            return timeLeft == 0 && instance.IsHungry();
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            var config = Configuration.Current.Tameable;
            if (!config.IsEnabled || (!config.ignoreHunger && !config.ignoreAlerted))
                return instructions;

            try
            {
                var matcher = new CodeMatcher(instructions, ilGenerator);

                if (config.ignoreHunger)
                    TameableHelpers.IgnoreHungerTranspiler(matcher, ShouldEnforceHunger);

                if (config.ignoreAlerted)
                    BaseAIHelpers.IgnoreAlertedTranspiler(matcher);

                return matcher.InstructionEnumeration();
            } catch (Exception ex)
            {
                ValheimPlusPlugin.Logger.LogError(ex);
                return instructions;
            }
        }
    }
}
