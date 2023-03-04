﻿using HarmonyLib;
using Framework.Managers;
using Framework.EditorScripts.EnemiesBalance;
using Framework.EditorScripts.BossesBalance;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Widgets;
using Framework.Dialog;
using System.Collections.Generic;
using UnityEngine.UI;

namespace BlasphemousRandomizer.Patches
{
    // Change functionality of the Ossuary
    [HarmonyPatch(typeof(OssuaryManager), "CheckGroupCompletion")]
    public class OssuaryManager_Patch
    {
        public static bool Prefix(OssuaryManager __instance)
        {
            __instance.pendingRewards = 0;
            int collected = OssuaryManager.CountAlreadyRetrievedCollectibles();
            int alreadyClaimed = 0;

            for (int i = 0; i < 11; i++)
            {
                string id = "OSSUARY_REWARD_" + (i + 1);
                if (Core.Events.GetFlag(id))
                {
                    alreadyClaimed++;
                }
                else if (collected >= (i + 1) * 4)
                {
                    Core.Events.SetFlag(id, true, false);
                    __instance.pendingRewards++;
                }
            }
            __instance.alreadyClaimedRewards = alreadyClaimed;
            Core.Events.LaunchEvent(__instance.CheckRewardsEvent, string.Empty);
            return false;
        }
    }

    // Make enemies stay as ng
    [HarmonyPatch(typeof(GameModeManager), "GetCurrentEnemiesBalanceChart")]
    public class GameModeEnemies_Patch
    {
        public static bool Prefix(ref EnemiesBalanceChart __result, EnemiesBalanceChart ___newGameEnemiesBalanceChart)
        {
            __result = ___newGameEnemiesBalanceChart;
            return false;
        }
    }

    // Make bosses stay as ng
    [HarmonyPatch(typeof(GameModeManager), "GetCurrentBossesBalanceChart")]
    public class GameModeBosses_Patch
    {
        public static bool Prefix(ref BossesBalanceChart __result, BossesBalanceChart ___newGameBossesBalanceChart)
        {
            __result = ___newGameBossesBalanceChart;
            return false;
        }
    }

    // Call load data even if loading vanilla game
    [HarmonyPatch(typeof(PersistentManager), "LoadSnapShot")]
    public class PersistentManagerLoad_Patch
    {
        public static void Postfix(PersistentManager __instance, PersistentManager.SnapShot snapShot)
        {
            if (!snapShot.commonElements.ContainsKey(Main.Randomizer.PersistentID))
            {
                Main.Randomizer.LoadGame(null);
            }
        }
    }

    // Update dialogs
    [HarmonyPatch(typeof(DialogManager), "StartConversation")]
    public class DialogManager_Patch
    {
        public static void Prefix(string conversiationId, Dictionary<string, DialogObject> ___allDialogs)
        {
            Main.Randomizer.Log("Starting dialog: " + conversiationId);

            if (conversiationId == "DLG_QT_0904" && Main.Randomizer.gameConfig.items.disableNPCDeath)
            {
                // Change socorro options for cleofas quest
                DialogObject current = ___allDialogs[conversiationId];
                if (current.answersLines.Count > 1)
                    current.answersLines.RemoveAt(1);
            }
            else if (Main.Randomizer.gameConfig.general.allowHints && conversiationId.Length == 8 && int.TryParse(conversiationId.Substring(4), out int id) && id > 2000 && id < 2035)
            {
                // Change corpse hints
                string hint = Main.Randomizer.hintShuffler.getHint(conversiationId);
                DialogObject current = ___allDialogs[conversiationId];
                current.dialogLines.Clear();
                current.dialogLines.Add(hint);
            }
        }
    }

    // Log what flags are being set & track certain ones
    [HarmonyPatch(typeof(EventManager), "SetFlag")]
    public class EventManagerSet_Patch
    {
        public static void Prefix(EventManager __instance, string id, bool b)
        {
            string formatted = __instance.GetFormattedId(id);
            if (formatted == "" || formatted == "REVEAL_FAITH_PLATFORMS")
                return;

            string text = b ? "Setting" : "Clearing";
            Main.Randomizer.Log(text + " flag: " + formatted);

            // Autotracking flags
            if (formatted.StartsWith("ITEM_"))
                Main.Randomizer.tracker.NewItem(id.Substring(5));
            else if (formatted.StartsWith("LOCATION_"))
                Main.Randomizer.tracker.NewLocation(id.Substring(9));
            else if (Main.arrayContains(Main.Randomizer.tracker.SpecialLocations, formatted))
            {
                Main.Randomizer.tracker.NewItem(formatted);
                Main.Randomizer.tracker.NewLocation(formatted);
            }
        }
    }

    // Show validity of save slot on select screen
    [HarmonyPatch(typeof(SelectSaveSlots), "SetAllData")]
    public class SelectSaveSlotsData_Patch
    {
        public static void Postfix(List<SaveSlot> ___slots)
        {
            for (int i = 0; i < ___slots.Count; i++)
            {
                PersistentManager.PublicSlotData slotData = Core.Persistence.GetSlotData(i);
                if (slotData == null)
                    continue;

                // Check if this save file was played in supported version
                string majorVersion = Main.Randomizer.ModVersion;
                majorVersion = majorVersion.Substring(0, majorVersion.LastIndexOf('.'));

                string type = "(Vanilla)";
                if (slotData.flags.flags.ContainsKey(majorVersion))
                    type = "(Randomized)";
                else if (slotData.flags.flags.ContainsKey("RANDOMIZED"))
                    type = "(Outdated)";

                // Send extra info to the slot
                ___slots[i].SetData("ignore", type, 0, false, false, false, 0, SelectSaveSlots.SlotsModes.Normal);
            }
        }
    }
    [HarmonyPatch(typeof(SaveSlot), "SetData")]
    public class SaveSlotData_Patch
    {
        public static bool Prefix(string zoneName, string info, ref Text ___ZoneText, ref bool canConvert)
        {
            canConvert = false;
            if (zoneName == "ignore")
            {
                ___ZoneText.text += "   " + info;
                return false;
            }
            return true;
        }
    }

    // Show settings menu when starting a new game
    [HarmonyPatch(typeof(SelectSaveSlots), "OnAcceptSlots")]
    public class SelectSaveSlotsMenu_Patch
    {
        public static bool Prefix(ref int idxSlot, List<SaveSlot> ___slots)
        {
            if (idxSlot >= 999) // Load new game
            {
                idxSlot -= 999;
                return true;
            }
            if (___slots[idxSlot].IsEmpty) // Show settings menu
            {
                Main.Randomizer.getSettingsMenu().openMenu(idxSlot);
                return false;
            }

            return true;
        }
    }

    // Don't allow playing in sacred sorrows mode
    [HarmonyPatch(typeof(BossRushWidget), "OptionPressed")]
    public class BossRushWidget_Patch
    {
        public static bool Prefix()
        {
            Main.Randomizer.LogDisplay("Sacred Sorrows mode can not be played in randomizer!");
            return false;
        }
    }

    // Only have laudes activated in boss room with verse
    [HarmonyPatch(typeof(EventManager), "GetFlag")]
    public class EventManagerGet_Patch
    {
        public static void Postfix(string id, EventManager __instance, ref bool __result)
        {
            string formatted = __instance.GetFormattedId(id);
            if (formatted == "SANTOS_LAUDES_ACTIVATED")
            {
                string scene = Core.LevelManager.currentLevel.LevelName;
                __result = (scene == "D08Z03S01" || scene == "D08Z03S03") && __instance.GetFlag("ITEM_QI110");
            }
        }
    }

    // Set flag for what miriam portal has been activated
    [HarmonyPatch(typeof(EventManager), "EndMiriamPortalAndReturn")]
    public class EventManagerMiriam_Patch
    {
        public static void Prefix(EventManager __instance)
        {
            if (__instance.AreInMiriamLevel())
            {
                __instance.SetFlag("RMIRIAM_" + __instance.MiriamCurrentScenePortal, true);
            }
        }
    }
}
