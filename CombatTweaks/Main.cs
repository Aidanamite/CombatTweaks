using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using UnityEngine;

namespace CombatTweaks
{
    [BepInPlugin("Aidanamite.CombatTweaks", "CombatTweaks", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{Environment.CurrentDirectory}\\BepInEx\\{modName}";
        public static ConfigEntry<bool> DisableInvencibleMod;
        public static ConfigEntry<bool> DisableContinuousAttackMod;

        void Awake()
        {
            DisableInvencibleMod = Config.Bind(new ConfigDefinition("settings", "DisableInvencibleMod"), false);
            DisableContinuousAttackMod = Config.Bind(new ConfigDefinition("settings", "DisableContinuousAttackMod"), false);
            new Harmony($"com.Aidanamite.{modName}").PatchAll(modAssembly);
            Logger.LogInfo($"{modName} has loaded");
        }
    }

    [HarmonyPatch(typeof(HeroMerchantController), "SecondaryAttackUseStart")]
    static class Patch_SecondaryAttackUseStart
    {
        static void Prefix(HeroMerchantController __instance)
        {
            if (__instance.currentEquippedWeapon.weaponMaster.weaponType == WeaponEquipmentMaster.WeaponType.ShortSword)
                __instance.currentEquippedWeapon.GetComponent<ShortSword>().ResetNumberOfShieldHits();
        }
    }

    [HarmonyPatch(typeof(HeroMerchantController), "AddSpeedEffectModifier", new Type[] { typeof(float), typeof(float) })]
    static class Patch_UseDashAbility
    {
        static void Prefix(ref float multiplier)
        {
            if (Environment.StackTrace.Contains("at Dash"))
                multiplier *= 1.2f;
        }
    }

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class Patch_DealDamageToEnemy
    {
        public static bool overrideInvincible = false;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var f = code[code.FindIndex(code.FindIndex(x => x.opcode == OpCodes.Ldarg_1), x => x.opcode == OpCodes.Stfld)].operand;
            var ind = code.FindIndex(x => x.operand is MethodInfo method && method.Name == "set_" + nameof(EnemyStats.CurrentHealth));
            code.Insert(ind + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(Patch_DealDamageToEnemy), nameof(AfterApplyDamage))
                ));
            code.InsertRange(
                ind,
                new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloca_S,1),
                    new CodeInstruction(OpCodes.Ldloca_S,2),
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldflda,f),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(Patch_DealDamageToEnemy),nameof(BeforeApplyDamage))
                    )
                }
            );
            return code;
        }

        static void BeforeApplyDamage(Enemy instance, ref bool wasInvulnerable, ref bool wasInvencible, ref float damageDealt)
        {
            var vars = instance.enemyStats.GetOrAddComponent<EnemyVars>();
            damageDealt = instance.totalDamage;
            if (!wasInvulnerable)
            {
                vars.maxDamage = instance.totalDamage;
                return;
            }
            if (Main.DisableInvencibleMod.Value || instance.totalDamage <= vars.maxDamage)
                return;
            var before = vars.maxDamage;
            vars.maxDamage = instance.totalDamage;
            instance.totalDamage -= before;
            damageDealt = instance.totalDamage;
            wasInvulnerable = false;
            wasInvencible = false;
            overrideInvincible = true;
        }

        static void AfterApplyDamage()
        {
            overrideInvincible = false;
        }
    }

    public class EnemyVars : UnityEngine.MonoBehaviour
    {
        public float maxDamage = 0;
    }

    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.CurrentHealth), MethodType.Setter)]
    static class Patch_SetEnemyHealth
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.Insert(
                code.FindIndex(x =>
                    x.opcode == OpCodes.Ldfld
                    && x.operand is FieldInfo f
                    && f.Name == "_invincible"
                ) + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(Patch_SetEnemyHealth),nameof(OverrideInvencibleCheck))
                )
            );
            return code;
        }

        static bool OverrideInvencibleCheck(bool original) => Patch_DealDamageToEnemy.overrideInvincible ? false : original;
    }

    [HarmonyPatch(typeof(WeaponCollisionDetection),"Start")]
    static class Patch_WeaponCollisionDetection
    {
        static void Postfix(WeaponCollisionDetection __instance)
        {
            if (!__instance.GetComponent<WeaponCollisionStay>())
                __instance.gameObject.AddComponent<WeaponCollisionStay>();
        }
    }

    public class WeaponCollisionStay : MonoBehaviour
    {
        WeaponCollisionDetection _t;
        WeaponCollisionDetection target => _t ? _t : _t = GetComponent<WeaponCollisionDetection>();
        void OnTriggerStay2D(Collider2D other) => target.OnTriggerEnter2D(other);
        void OnCollisionStay2D(Collision2D other) => target.OnCollisionEnter2D(other);
    }
}