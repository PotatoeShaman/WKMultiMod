using BepInEx;
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
using Vector3 = UnityEngine.Vector3;

namespace WKMPMod.Test;

public class Test : MonoBehaviour {
	public static float x = 0;
	public static float y = 0;
	public static float z = 0;
	public static void Main(string[] args) {

		if (args.Length == 0) {
			Debug.Log("测试命令需要参数,可用参数:0-8");
			return;
		}

		// 使用 switch 表达式使代码更简洁
		_ = args[0] switch {
			"0" => RunCommand(GetGraphicsAPI),
			"1" => RunCommand(GetMPStatus),
			"2" => RunCommand(GetMassData),
			"3" => RunCommand(GetSystemLanguage),
			"4" => RunCommand(() => CreateRemotePlayer(args[1..])),
			"5" => RunCommand(() => RemoveRemotePlayer(args[1..])),
			"6" => RunCommand(() => UpdateRemoteTag(args[1..])),
			"7" => RunCommand(GetAllFactoryList),
			"8" => RunCommand(GetPath),
			"9" => RunCommand(CreateTestPrefab),
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
		ulong id = 1;
		string prefab = "default";

		if (args.Length >= 1 && ulong.TryParse(args[0], out ulong parsedId)) {
			id = parsedId;
		}

		if (args.Length >= 2) {
			prefab = string.Join(" ", args[1..]);
		}

		MPCore.Instance.RPManager.PlayerCreate(id, prefab);
		MPCore.Instance.RPManager.Players[id]
			.UpdatePlayerData(new PlayerData { Position = new Vector3(x, y, z) });
		y += 4.0f;

	}
	// 移除远程玩家
	public static void RemoveRemotePlayer(string[] args) {
		int id = 1;
		if (args.Length >= 1 && int.TryParse(args[0], out int parsedId)) {
			id = parsedId;
		}
		MPCore.Instance.RPManager.PlayerRemove((ulong)id);
	}
	// 更新远程玩家名字标签
	public static void UpdateRemoteTag(string[] args) {
		string tagText = args.Length > 0
			? string.Join(" ", args)
			: "中文测试: 斯卡利茨恐虐神选";

		if (MPCore.Instance.RPManager.Players.TryGetValue(1, out var player)) {
			player.UpdateNameTag(tagText);
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

	public static void GetAllFactoryList() {
		RPFactoryManager.Instance.ListAllFactory();
	}
}