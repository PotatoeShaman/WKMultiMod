using BepInEx;
using JetBrains.Annotations;
using Steamworks;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Windows;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.RemotePlayer;
using WKMPMod.Util;
using static CommandConsole;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace WKMPMod.Test;

public class Test : MonoBehaviour {
	public const string NO_ITEM_PREFAB_NAME = "None";
	public static float x = 0;
	public static float y = 0;
	public static float z = 0;
	public static ulong id = 0;
	public static void Main(string[] args) {

		if (args.Length == 0) {
			Debug.Log("测试命令需要参数");
			return;
		}

		// 使用 switch 表达式使代码更简洁
		_ = args[0] switch {
			"0" => RunCommand(GetGraphicsAPI),  // 获取图形API信息
			"1" => RunCommand(GetMPStatus),     // 获取联机状态
			"2" => RunCommand(GetMassData),     // 获取Mass数据
			"3" => RunCommand(GetSystemLanguage),   // 获取系统语言
			"4" => RunCommand(() => CreateRemotePlayer(args[1..])), // 创建远程玩家,参数:玩家ID(ulong),预制体工厂ID(string)
			"5" => RunCommand(() => RemoveRemotePlayer(args[1..])), // 移除远程玩家,参数:玩家ID(ulong)
			"6" => RunCommand(() => UpdateRemoteTag(args[1..])),    // 更新远程玩家标签,参数:标签文本(string)
			"7" => RunCommand(GetAllFactoryList),   // 列出所有预制体工厂信息
			"8" => RunCommand(GetPath),             // 获取程序路径信息
			"9" => RunCommand(CreateTestPrefab),    // 创建测试预制体
			"10" => RunCommand(GetHandCosmetic),    // 获取手部皮肤信息
			"11" => RunCommand(CreateDontDestroyGameObject),    // 创建测试对象并设置DontDestroyOnLoad
			"12" => RunCommand(TestSingleton),  // 测试单例模式
			"13" => RunCommand(SimulationPlayerUpdata),  // 模拟玩家数据更新事件
			_ => RunCommand(() => Debug.Log($"未知命令: {args[0]}"))
		};
	}

	// 辅助方法:安全执行命令
	private static bool RunCommand(Action action) {
		action();
		return true;
	}

	public static void GetGraphicsAPI() {
		// 方法1:直接获取当前API
		Debug.Log($"当前图形API: {SystemInfo.graphicsDeviceType}");

		// 方法2:获取详细版本信息
		Debug.Log($"图形API版本: {SystemInfo.graphicsDeviceVersion}");

		// 方法3:获取Shader Model级别
		int smLevel = SystemInfo.graphicsShaderLevel;
		Debug.Log($"Shader Model: {smLevel / 10}.{smLevel % 10}");

		// 方法4:检查具体功能支持
		Debug.Log($"支持计算着色器: {SystemInfo.supportsComputeShaders}");
		Debug.Log($"支持几何着色器: {SystemInfo.supportsGeometryShaders}");
		Debug.Log($"支持曲面细分: {SystemInfo.supportsTessellationShaders}");
		Debug.Log($"支持GPU实例化: {SystemInfo.supportsInstancing}");
	}
	// 输出联机模式状态
	public static void GetMPStatus() {
		Debug.Log($"{((int)(MPCore.MultiPlayerStatus)).ToString()}");
	}
	// 输出Mass数据
	public static void GetMassData() {
		var data = DEN_DeathFloor.instance.GetSaveData();
		Debug.Log($"高度:{data.relativeHeight}, 是否活动:{data.active}, 速度:{data.speed}, 速度乘数:{data.speedMult}");
	}
	// 输出系统语言
	public static void GetSystemLanguage() {
		Debug.Log($"系统语言:{Localization.GetGameLanguage()}");
	}
	// 创建远程玩家
	public static void CreateRemotePlayer(string[] args) {
		id += 1;
		string prefab = "default";
		if (args.Length >= 1 && ulong.TryParse(args[0], out ulong parsedId)) {
			id = parsedId;
		}
		if (args.Length >= 2) {
			prefab = string.Join(" ", args[1..]);
		}
		RPManager.Instance.PlayerCreate(id, prefab);
		RPManager.Instance.Players[id]
			.HandlePlayerData(new PlayerData { Position = new Vector3(x, y, z) });
		y += 4.0f;
	}
	// 移除远程玩家
	public static void RemoveRemotePlayer(string[] args) {
		int id = 1;
		if (args.Length >= 1 && int.TryParse(args[0], out int parsedId)) {
			id = parsedId;
		}
		RPManager.Instance.PlayerRemove((ulong)id);
	}
	// 更新远程玩家名字标签
	public static void UpdateRemoteTag(string[] args) {
		string tagText = args.Length > 0
			? string.Join(" ", args)
			: "中文测试: 斯卡利茨恐虐神选";

		if (RPManager.Instance.Players.TryGetValue(1, out var player)) {
			player.HandleNameTag(tagText);
		} else {
			Debug.LogWarning("玩家ID 1 不存在");
		}
	}
	// 获取程序路径信息
	public static void GetPath() {
		//D:\GAME\Steam\steamapps\common\White Knuckle\BepInEx\plugins
		MPMain.LogInfo(Paths.PluginPath);
		//D:\GAME\Steam\steamapps\common\White Knuckle\BepInEx\plugins\MultiPlayer\WKMultiPlayerMod.dll
		MPMain.LogInfo(Assembly.GetExecutingAssembly().Location);
		//D:\GAME\Steam\steamapps\common\White Knuckle
		MPMain.LogInfo(AppDomain.CurrentDomain.BaseDirectory);
		//D:/GAME/Steam/steamapps/common/White Knuckle/White Knuckle_Data
		MPMain.LogInfo(Application.dataPath);
		//D:\GAME\Steam\steamapps\common\White Knuckle\BepInEx\plugins\MultiPlayer
		MPMain.LogInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty);

		MPMain.LogInfo(MPMain.path);
	}
	// 创建测试预制体
	public static void CreateTestPrefab() {
		var bundle = AssetBundle.LoadFromFile(Path.Combine(MPMain.path, "playerprefab"));
		BaseRemoteFactory.ListAllAssetsInBundle(bundle);
		var rawPrefab = bundle.LoadAsset<GameObject>("cl_player");
		Object.Instantiate(rawPrefab);
	}

	// 列出所有预制体工厂信息
	public static void GetAllFactoryList() {
		RPFactoryManager.Instance.ListAllFactory();
	}

	public static void GetAllPlayerList() {


	}
	// 获取手部皮肤信息
	public static void GetHandCosmetic() {
		MPMain.LogWarning($"左手皮肤id {CL_CosmeticManager.GetCosmeticInHand(0).cosmeticData.id}");
		MPMain.LogWarning($"右手皮肤id {CL_CosmeticManager.GetCosmeticInHand(1).cosmeticData.id}");
	}
	// 创建根对象测试DontDestroyOnLoad
	public static void CreateDontDestroyGameObject() {
		GameObject singleton1 = new GameObject("Test Game Object1");
		DontDestroyOnLoad(singleton1);
		GameObject singleton2 = new GameObject("Test Game Object2");
	}
	// 输出单例测试
	public static void TestSingleton() {
		MPMain.LogWarning(TestMonoSingleton.Instance.TestString);
	}
	// 模拟玩家数据更新事件
	public static void SimulationPlayerUpdata() {
		byte[] data = { 0x01 };
		ArraySegment<byte> segment = new ArraySegment<byte>(data);
		MPEventBusNet.NotifyReceive(1, segment);
	}
}
public class CheatsTest : MonoBehaviour {
	public static void Main(string[] args) {

		if (args.Length == 0) {
			Debug.Log("测试命令需要参数");
			return;
		}

		// 使用 switch 表达式使代码更简洁
		_ = args[0] switch {
			"0" => RunCommand(() => CreateItem(args[1..])),  // 创建物品,参数:物品预制体名称(string)
			"1" => RunCommand(() => AddItemInInventory(args[1..])),  // 创建物品并放入库存,参数:物品预制体名称(string)
			"2" => RunCommand(GetInventoryItems),  // 获取库存信息
			"3" => RunCommand(AddItemInInventoryQuaternionTest),
			_ => RunCommand(() => Debug.Log($"未知命令: {args[0]}"))
		};
	}
	// 辅助方法:安全执行命令
	private static bool RunCommand(Action action) {
		action();
		return true;
	}

	// 创建物品测试
	public static void CreateItem(string[] args) {
		foreach (var arg in args) {
			if (arg != "None") {
				// 从资源管理器获取预制体
				GameObject prefabAsset = CL_AssetManager.GetAssetGameObject(arg);
				if (prefabAsset != null) {
					// 随机位置
					Vector3 randomOffset = new Vector3(
										Random.Range(-1f, 1f),     // X轴随机
										Random.Range(0f, 0.5f),     // Y轴随机(向上)
										Random.Range(-1f, 1f)       // Z轴随机
									);

					// 实例化物品
					var itemObject = GameObject.Instantiate(
						prefabAsset,
						new Vector3(0, 0.5f, 0) + randomOffset,
						Random.rotation  // 随机旋转
					);

					// 获取Rigidbody并添加随机斜上方动量
					Rigidbody rb = itemObject.GetComponent<Rigidbody>();
					if (rb != null) {
						// 随机方向: 斜上方 (XZ随机，Y固定向上)
						Vector3 randomDirection = new Vector3(
							Random.Range(-1f, 1f),  // X轴随机方向
							1f,                     // Y轴向上
							Random.Range(-1f, 1f)   // Z轴随机方向
						).normalized;

						// 随机力度 (3-8之间)
						float randomForce = Random.Range(3f, 8f);

						// 添加冲量(瞬间力)
						rb.AddForce(randomDirection * randomForce, ForceMode.Impulse);

						// 可选: 添加随机旋转扭矩，让物品在空中旋转
						//rb.AddTorque(Random.insideUnitSphere * Random.Range(1f, 5f), ForceMode.Impulse);
					}
				} else {
					MPMain.LogInfo($"[MP Debug] 生成物: {arg} 不存在");
				}
			}
		}
	}
	// 获取库存内全部物品信息
	public static void GetInventoryItems() {
		// 获取库存单例
		var inventory = Inventory.instance;
		if (inventory != null) {
			// 获取库存中的物品列表
			var items = inventory.GetItems();
			foreach (var item in items) {
				MPMain.LogInfo($"物品名称: {item.itemName}, 标签: {item.itemTag}, 预制体名称: {item.prefabName}");
			}
		} else {
			MPMain.LogWarning("库存不存在");
		}
	}
	// 创建物品并放入库存测试
	public static void AddItemInInventory(string[] args) {
		var inventory = Inventory.instance;
		foreach (var arg in args) {
			if (arg != "None") {
				// 从资源管理器获取预制体
				GameObject prefabAsset = CL_AssetManager.GetAssetGameObject(arg);
				if (prefabAsset != null) {
					// 实例化物品在 0,0.5,0 
					var item = Instantiate(prefabAsset, new Vector3(0, 0.5f, 0), Quaternion.identity);
					var item_Object = item.GetComponent<Item_Object>();
					if (item_Object != null) {
						inventory.AddItemToInventoryCenter(item_Object.itemData);
						// 隐藏镜像物品对象，因为它已经被添加到库存中，不需要在场景中显示
						item_Object.gameObject.SetActive(value: false);
					} else {
						MPMain.LogInfo($"[MP Debug] 生成物: {item.name} 不可放入库存");
					}

				} else {
					MPMain.LogInfo($"[MP Debug] 生成物: {arg} 不存在");
				}
			}
		}
	}
	public static void AddItemInInventoryQuaternionTest() {
		var inventory = Inventory.instance;

		void AddItemInInventoryQuaternionTest(string arg, Quaternion quaternion) {
			// 从资源管理器获取预制体
			GameObject prefabAsset = CL_AssetManager.GetAssetGameObject(arg);

			// 实例化物品在 0,0.5,0 
			var item = Instantiate(prefabAsset, new Vector3(0, 0.5f, 0), Quaternion.identity);
			var item_Object = item.GetComponent<Item_Object>();
			item_Object.itemData.bagRotation = quaternion; // 设置物品数据中的旋转
			inventory.AddItemToInventoryCenter(item_Object.itemData);
			// 隐藏镜像物品对象，因为它已经被添加到库存中，不需要在场景中显示
			item_Object.gameObject.SetActive(value: false);
		}
		AddItemInInventoryQuaternionTest("Item_Rebar", Quaternion.Euler(90, 0, 0));
		AddItemInInventoryQuaternionTest("Item_Rebar_Explosive", Quaternion.Euler(90, 0, 0));
		AddItemInInventoryQuaternionTest("Item_RebarRope", Quaternion.Euler(90, 0, 0));
		AddItemInInventoryQuaternionTest("Item_Rebar_Holiday", Quaternion.Euler(90, 0, 0));
		AddItemInInventoryQuaternionTest("Item_RebarRope_Holiday", Quaternion.Euler(90, 0, 0));
	}
}