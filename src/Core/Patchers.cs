using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WKMultiMod.src.Core;

// 补丁类: 注入核心对象到 SteamManager
// 在 SteamManager 的 Awake 方法后执行
[HarmonyPatch(typeof(SteamManager))]
[HarmonyPatch("Awake")]
public class Patchers {
	static void Postfix(SteamManager __instance) {
		// 只有当核心对象不存在时才创建, 防止重复注入
		if (MultiPalyerMain.CoreInstance != null) {
			return;
		}

		// 1. 创建一个新的 GameObject
		GameObject coreGameObject = new GameObject("MultiplayerCore_INJECTED_CHILD");

		// 2. 将新对象作为 SteamManager 的子对象
		// 这样它就继承了 SteamManager 的持久性
		coreGameObject.transform.SetParent(__instance.gameObject.transform);

		// 3. 挂载核心脚本
		MultiPalyerMain.CoreInstance = coreGameObject.AddComponent<MultiplayerCore>();

		MultiPalyerMain.Logger.LogInfo("核心对象已成功注入 SteamManager 的 GameObject.");
	}
}
