using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Shared.MK_Component;
using Object = UnityEngine.Object;

namespace WKMPMod.Component;

public static class PrefabComponentMapper {
	public static void ProcessPrefabMarkers(GameObject prefab) {
		Stack<Transform> stack = new Stack<Transform>();

		stack.Push(prefab.transform);

		while (stack.Count > 0) {
			Transform current = stack.Pop();

			try {
				MapMarkersToRealComponents(current.gameObject);
			} catch (Exception ex) {
				MPMain.LogError(
					$"[MPPrefab] 处理节点 {current.name} 时崩溃: {ex.Message}",
					$"[MPPrefab] Collapse while processing node {current.name} Error Massage: {ex.Message}");
			}
			// 遍历直接子级
			// 这里不需要 Cast，直接循环最快
			for (int i = 0; i < current.childCount; i++) {
				stack.Push(current.GetChild(i));
			}
		}
		return ;
	}

	// 将标记组件替换为真实组件
	private static void MapMarkersToRealComponents(GameObject prefab) {
		InitializeRemoteEntity(prefab);
		InitializeObjectTagger(prefab);
		InitializeCL_Handhold(prefab);
	}

	#region 替换组件实际函数
	private static void InitializeRemoteEntity(GameObject prefab) {
		MK_RemoteEntity mk_component = prefab.GetComponent<MK_RemoteEntity>();
		if (mk_component == null) {
			return;
		}
		var component = prefab.AddComponent<RemoteEntity>();
		if (component != null) {
			component.AllActive = MPConfig.AllActive;
			component.HammerActive = MPConfig.HammerActive;
			component.RebarActive = MPConfig.RebarActive;
			component.ReturnRebarActive = MPConfig.ReturnRebarActive;
			component.RebarExplosionActive = MPConfig.RebarExplosionActive;
			component.ExplosionActive = MPConfig.ExplosionActive;
			component.PitonActive = MPConfig.PitonActive;
			component.FlareActive = MPConfig.FlareActive;
			component.IceActive = MPConfig.IceActive;
			component.OtherActive = MPConfig.OtherActive;
		} else {
			MPMain.LogError(
				"[MPPrefab] 预制体RemoteEntity组件添加失败, 我也不知道怎么办, 建议重启?",
				"[MPPrefab] Failed to add the RemoteEntity component to preform");
		}
		Object.DestroyImmediate(mk_component);
	}

	private static void InitializeObjectTagger(GameObject prefab) {
		MK_ObjectTagger mk_component = prefab.GetComponent<MK_ObjectTagger>();
		if (mk_component == null) {
			return;
		}
		// 先找是否已有, 没有再加
		var component = prefab.GetComponent<ObjectTagger>() ?? prefab.AddComponent<ObjectTagger>();
		if (component != null) {
			// 使用for循环添加标签
			foreach (var t in mk_component.tags) {
				if (!component.tags.Contains(t)) {
					component.tags.Add(t);
				}
			}
		} else {
			MPMain.LogError(
				"[MPPrefab] 预制体ObjectTagger组件添加失败, 我也不知道怎么办, 建议重启?",
				"[MPPrefab] Failed to add the ObjectTagger component to preform");
		}
		Object.DestroyImmediate(mk_component);
	}

	private static void InitializeCL_Handhold(GameObject prefab) {
		MK_CL_Handhold mk_component = prefab.GetComponent<MK_CL_Handhold>();
		if (mk_component == null) {
			return;
		}
		var component = prefab.AddComponent<CL_Handhold>();
		if (component != null) {
			component.activeEvent = mk_component.activeEvent;
			component.stopEvent = mk_component.stopEvent;

			if (component.handholdRenderer == null) {
				component.handholdRenderer = prefab.GetComponent<Renderer>();
			}
		} else {
			MPMain.LogError(
				"[MPPrefab] 预制体CL_Handhold组件添加失败, 我也不知道怎么办, 建议重启?",
				"[MPPrefab] Failed to add the CL_Handhold component to preform");
		}
		Object.DestroyImmediate(mk_component);
	}
	#endregion
}

