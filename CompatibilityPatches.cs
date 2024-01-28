using HarmonyLib;
using System.Linq;
using TootTallyAccounts;
using TootTallyCore.APIServices;
using TootTallyCore.Graphics;
using TootTallyCore.Utils.Assets;
using TootTallyGameModifiers;
using TootTallyTrombuddies;
using UnityEngine;

namespace TootTallySpectator
{
    public static class CompatibilityPatches
    {
        [HarmonyPatch(typeof(TrombuddiesGameObjectFactory), nameof(TrombuddiesGameObjectFactory.CreateUserCard))]
        [HarmonyPostfix]

        public static void InitOverlay(SerializableClass.User user, ref GameObject __result)
        {
            var rightContent = __result.transform.Find("LatencyFG/RightContent").gameObject;
            if (!SpectatingManager.IsHosting)
                if (user.id == TootTallyUser.userInfo.id)
                    GameObjectFactory.CreateCustomButton(rightContent.transform, Vector2.zero, Vector2.one * 45, AssetManager.GetSprite("SpectatorIcon.png"), "SpectateUserButton", delegate { SpectatingManager.OnSpectateButtonPress(user.id, user.username); });

            if (user.id != TootTallyUser.userInfo.id && SpectatingManager.currentSpectatorIDList.Contains(user.id))
                GameObjectFactory.CreateCustomButton(rightContent.transform, Vector2.zero, Vector2.one * 45, AssetManager.GetSprite("SpectatorIcon.png"), "SpectateUserButton", delegate { SpectatingManager.OnSpectateButtonPress(user.id, user.username); });
        }

        [HarmonyPatch(typeof(UserStatusUpdater), nameof(UserStatusUpdater.SetPlayingUserStatus))]
        [HarmonyPrefix]
        public static bool OverwritePlayingUserStatus()
        {
            if (!SpectatingManager.IsSpectating) return true;

            UserStatusManager.SetUserStatus(UserStatusManager.UserStatus.Spectating);

            return false;
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.pauseQuitLevel))]
        [HarmonyPostfix]
        public static void OnQuitLoadBackedupModifiers()
        {
            if (SpectatingManager.IsSpectating)
                GameModifierManager.LoadBackedupModifiers();
        }

        [HarmonyPatch(typeof(PointSceneController), nameof(PointSceneController.updateSave))]
        [HarmonyPrefix]
        public static bool AvoidSaveChange() => !SpectatingManager.IsSpectating; // Don't touch the savefile if we just did a replay

        [HarmonyPatch(typeof(PointSceneController), nameof(PointSceneController.checkScoreCheevos))]
        [HarmonyPrefix]
        public static bool AvoidAchievementCheck() => !SpectatingManager.IsSpectating; // Don't check for achievements if we just did a replay

        [HarmonyPatch(typeof(UserStatusManager), nameof(UserStatusManager.OnHeartBeatRequestResponse))]
        [HarmonyPostfix]
        public static void UpdateSpecList() => SpectatingManager.UpdateSpectatorIDList();
    }
}
