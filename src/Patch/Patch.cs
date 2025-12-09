using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WKMultiMod.Main;
using WKMultiMod.Core;

namespace WKMultiMod.src.Patch;

// 补丁类: 注入核心对象到 SteamManager
// 在 SteamManager 的 Awake 方法后执行
[HarmonyPatch(typeof(SteamManager))]
[HarmonyPatch("Awake")]
public class Patch_SteamManager_Awake {
	//void 类型: 总是执行原方法
	public static void Postfix(SteamManager __instance) {
		// 只有当核心对象不存在时才创建, 防止重复注入
		if (MultiPlayerMain.CoreInstance != null) {
			return;
		}

		// 1. 创建一个新的 GameObject
		GameObject coreGameObject = new GameObject("MultiplayerCore_INJECTED_CHILD");

		// 2. 将新对象作为 SteamManager 的子对象
		// 这样它就继承了 SteamManager 的持久性
		coreGameObject.transform.SetParent(__instance.gameObject.transform);

		// 3. 挂载核心脚本
		MultiPlayerMain.CoreInstance = coreGameObject.AddComponent<MultiPlayerCore>();

		MultiPlayerMain.Logger.LogInfo("[MP Mod Loading] 核心对象已成功注入 SteamManager 的 GameObject.");
	}
}

// 补丁类: 强制解锁所有进度
[HarmonyPatch(typeof(CL_ProgressionManager), "HasProgressionUnlock")]
class Patch_Progression_ForceUnlock {
	//bool 类型: 控制是否执行原方法 true=执行 false=跳过
	static bool Prefix(ref bool __result) {
		if (MultiPlayerMain.IsMultiplayerActive) {
			__result = true; // 强制所有解锁检查通过
			return false;    // 跳过原始的解锁检查逻辑
		}
		return true; // 非联机模式，执行原始的解锁检查
	}
}

// 补丁类: 禁用关卡翻转功能
// Copy自WK_IShowSeed Mod GitHub仓库地址: https://github.com/shishyando/WK_IShowSeed
[HarmonyPatch(typeof(M_Level), "Awake")]
public static class Patch_M_Level_Awake {
	public static void Prefix(M_Level __instance) {
		// 仅在联机模式下禁用关卡翻转
		if (MultiPlayerMain.IsMultiplayerActive) {
			// 禁用关卡翻转功能
			__instance.canFlip = false;
		}
	}
}

// 补丁类: 标准化关卡生成几率
// 会一直生成稀有事件和稀有关卡 过于搞笑 所以注释掉了
//[HarmonyPatch(typeof(SpawnTable.SpawnSettings), "GetEffectiveSpawnChance")]
//class Patch_SpawnSettings_StandardizeChance {
//	public static bool Prefix(SpawnTable.SpawnSettings __instance, ref float __result) {
//		// 联机模式下, 强制关卡生成几率为 1.0f (100%)
//		if (MultiPlayerMain.IsMultiplayerActive) {
//			// 唯一需要保留的检查是 hardModeOnly, 如果是非硬核模式, 并且该关卡设定为 hardModeOnly, 则仍需排除.
//			if (__instance.useHardMode && __instance.hardModeOnly && !CL_GameManager.IsHardmode()) {
//				__result = 0f; // 仅硬核模式可用, 非硬核时排除
//				return false;
//			}
//			// 其他情况, 强制 1.0f
//			__result = 1f;
//			return false; // 跳过原始复杂的计算和过滤
//		}
//		return true; // 非联机模式, 执行原始方法
//	}
//}

// 补丁类: 标准化关卡生成
// 貌似没用
//[HarmonyPatch(typeof(M_Subregion), "CanSpawn")]
//class Patch_MSubregion_CanSpawn {
//	static bool Prefix(ref bool __result) {
//		// 检查联机状态变量：
//		if (MultiPlayerMain.IsMultiplayerActive) {
//			__result = true; // 如果联机, 强制返回 True (启用标准化)
//			return false;    // 跳过原始方法
//		}
//		return true; // 如果未联机, 继续执行原始方法 (禁用标准化)
//	}
//}
