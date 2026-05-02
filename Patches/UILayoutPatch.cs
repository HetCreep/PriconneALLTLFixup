using System.Collections;
using Elements;
using HarmonyLib;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class UILayoutPatch
{
    #region 1. Quest Detail & Smart Arrow
    [HarmonyPatch(typeof(PartsQuestDetail), nameof(PartsQuestDetail.Settings))]
    [HarmonyPostfix]
    public static void PostfixQuestDetail(PartsQuestDetail __instance)
    {
        var arrow = __instance.transform.Find("ArrowBlock");
        if (arrow != null)
        {
            // บรรทัดนี้จะพ่นค่าดั้งเดิมออกทาง FLog/Console ให้คุณเห็นเลยครับ!
            FLog.Info($"[Sniffer] ArrowBlock Original Position: {arrow.localPosition.x}, {arrow.localPosition.y}");
        }
    }
    #endregion

    #region 2. Feature Specific Positioning (UIMove 1-4)
    // UIMovePatch 1: ปรับตำแหน่งข้อความ Auto Battle (Battle Info)
    [HarmonyPatch(typeof(PartsEventQuestAutoProgressInBattleInfo), "Initialize")]
    [HarmonyPostfix]
    public static void PostfixAutoBattleUI(PartsEventQuestAutoProgressInBattleInfo __instance)
    {
        if (!__instance.IsSafe()) return;
        var autoObj = __instance.transform.Find("questauto_text_auro") ?? __instance.transform.Find("questauto_text_auto");
        if (autoObj != null)
            autoObj.localPosition = new Vector3(-25f, autoObj.localPosition.y, 0f);
    }

    // UIMovePatch 2: ปรับตำแหน่งหน้า Profile (Tower & Release Label)
    [HarmonyPatch(typeof(PartsDialogUserProfile), "InitializeParam")]
    [HarmonyPostfix]
    public static void PostfixUserProfile(PartsDialogUserProfile __instance)
    {
        if (!__instance.IsSafe()) return;

        // ปรับ Tower Label
        var tower = __instance.transform.Find("ProfileProgressGroup/ScrollView/PartsProfileProgress/GUIGroup_ContentsTower/GUIGroup_title_tower/Label");
        if (tower != null) tower.localPosition = new Vector3(142f, tower.localPosition.y, 0f);

        // ปรับ Release Label และขยายความกว้างเพื่อรองรับภาษาไทย (Rule 8)
        var releaseObj = __instance.transform.Find("ProfileProgressGroup/ScrollView/PartsProfileProgress/GUIGroup_ContentsRelease/GUIGroup_title_Release/Label");
        if (releaseObj != null)
        {
            var label = releaseObj.GetComponent<CustomUILabel>();
            if (label != null) label.lineWidth = 120;
            releaseObj.localPosition = new Vector3(136f, releaseObj.localPosition.y, 0f);
        }
    }

    // UIMovePatch 3: ปรับตำแหน่งสรุปผล Abyss Boss
    [HarmonyPatch(typeof(PartsDialogAbyssBossResult), "StartShow")]
    [HarmonyPostfix]
    public static void PostfixAbyssResult(PartsDialogAbyssBossResult __instance)
    {
        if (!__instance.IsSafe()) return;
        var rewardLabel = __instance.transform.Find("Main/GUIGroup_gauge/ToNextRewardLabel");
        if (rewardLabel != null)
            rewardLabel.localPosition = new Vector3(-192.5f, rewardLabel.localPosition.y, 0f);
    }

    // UIMovePatch 4: ปรับตำแหน่งหน้า Alces (Gold & Points) พร้อมระบบหน่วงเวลาโหลด
    [HarmonyPatch(typeof(ViewAlcesTop), "StartView")]
    [HarmonyPostfix]
    public static void PostfixAlcesTop(ViewAlcesTop __instance)
    {
        if (!__instance.IsSafe()) return;
        CoroutineStarter.Run(WaitAndAdjustAlcesUI(__instance));
    }

    private static IEnumerator WaitAndAdjustAlcesUI(ViewAlcesTop instance)
    {
        float timeout = Time.time + 2f;
        Transform gold = null;

        while (gold == null && Time.time < timeout)
        {
            if (instance == null) yield break;
            gold = instance.transform.Find("Right/TopRightAnchor/GUIGroup_Gold");
            yield return null;
        }

        if (gold != null)
        {
            gold.localPosition += new Vector3(0f, 15f, 0f);
            var pts = instance.transform.Find("Right/TopRightAnchor/GUIGroup_AlcesPt");
            if (pts != null) pts.localPosition = new Vector3(gold.localPosition.x, gold.localPosition.y - 40f, 0f);
        }
    }
    #endregion
}