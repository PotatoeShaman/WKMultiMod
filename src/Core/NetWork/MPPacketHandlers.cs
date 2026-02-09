using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.RemotePlayer;
using WKMPMod.Util;
using static WKMPMod.Data.MPWriterPool;

namespace WKMPMod.NetWork;

public class MPPacketHandlers {

	/// <summary>
	/// 主机接收WorldInitRequest: 请求初始化数据
	/// 发送WorldInitData: 初始化数据给新玩家
	/// </summary>
	[MPPacketHandler(PacketType.WorldInitRequest)]
	private static void HandleWorldInitRequest(ulong senderId, DataReader reader) {
		// 发送世界种子
		var writer = GetWriter(MPSteamworks.Instance.UserSteamId, senderId, PacketType.WorldInitData);
		writer.Put(WorldLoader.instance.seed);
		MPSteamworks.Instance.SendToPeer(senderId, writer);

		// 可以添加其他初始化数据,如游戏状态、物品状态等

		// Debug
		MPMain.LogInfo(Localization.Get("MPMessageHandlers", "SentInitData"));
	}

	/// <summary>
	/// 客户端接收WorldInitData: 新加入玩家,加载世界种子
	/// </summary>
	/// <param name="seed"></param>
	[MPPacketHandler(PacketType.WorldInitData)]
	private void HandleWorldInit(ulong senderId, DataReader reader) {
		// 获取种子
		int seed = reader.GetInt();
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "LoadingWorld", seed.ToString()));
		// 种子相同默认为已经联机过,只不过断开了
		if (seed != WorldLoader.instance.seed)
			WorldLoader.ReloadWithSeed(new string[] { seed.ToString() });
		MPCore.MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);
	}

	/// <summary>
	/// 主机/客户端接收PlayerDataUpdate: 处理玩家数据更新
	/// </summary>
	[MPPacketHandler(PacketType.PlayerDataUpdate)]
	private void HandlePlayerDataUpdate(ulong senderId, DataReader reader) {
		// 如果是从转发给自己的,忽略
		var playerData = MPDataSerializer.ReadFromNetData(reader);
		var playerId = playerData.playId;
		if (playerId == MPSteamworks.Instance.UserSteamId) {
			return;
		}
		RPManager.Instance.ProcessPlayerData(playerId, playerData);
	}

	/// <summary>
	/// 主机/客户端接收BroadcastMessage: 处理玩家标签更新
	/// </summary>
	[MPPacketHandler(PacketType.BroadcastMessage)]
	private void HandlePlayerTagUpdate(ulong senderId, DataReader reader) {
		string msg = reader.GetString();    // 读取消息
		string playerName = new Friend(senderId).Name;
		CommandConsole.Log($"{playerName}: {msg}");
		RPManager.Instance.ProcessPlayerTag(senderId, msg);
	}


	[MPPacketHandler(PacketType.WorldStateSync)]
	private void HandleWorldStateSync(ulong senderId, DataReader reader) {

	}

	/// <summary>
	/// 主机/客户端接收PlayerDamage: 受到伤害
	/// </summary>
	[MPPacketHandler(PacketType.PlayerDamage)]
	private void HandlePlayerDamage(ulong senderId, DataReader reader) {
		float amount = reader.GetFloat();
		string type = reader.GetString();
		var baseDamage = amount * MPConfig.AllPassive;
		switch (type) {
			case "Hammer":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.HammerPassive, type);
				break;
			case "rebar":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.RebarPassive, type);
				break;
			case "returnrebar":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.ReturnRebarPassive, type);
				break;
			case "rebarexplosion":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.RebarExplosionPassive, type);
				break;
			case "explosion":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.ExplosionPassive, type);
				break;
			case "piton":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.PitonPassive, type);
				break;
			case "flare":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.FlarePassive, type);
				break;
			case "ice":
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.IcePassive, type);
				break;
			default:
				ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.OtherPassive, type);
				break;
		}
	}

	/// <summary>
	/// 主机/客户端接收PlayerAddForce: 受到冲击力
	/// </summary>
	[MPPacketHandler(PacketType.PlayerAddForce)]
	private void HandlePlayerAddForce(ulong senderId, DataReader reader) {
		Vector3 force = new Vector3 {
			x = reader.GetFloat(),
			y = reader.GetFloat(),
			z = reader.GetFloat(),
		};
		string source = reader.GetString();
		ENT_Player.GetPlayer().AddForce(force, source);
	}

	/// <summary>
	/// 主机/客户端接收PlayerDeath: 玩家死亡
	/// </summary>
	[MPPacketHandler(PacketType.PlayerDeath)]
	private void HandlePlayerDeath(ulong senderId, DataReader reader) {
		string type = reader.GetString();
		string playerName = new Friend(senderId).Name;
		CommandConsole.Log(Localization.Get("CommandConsole", "PlayerDeath", playerName, type));
	}

	/// <summary>
	/// 主机/客户端接收PlayerCreateRequest<br/>
	/// 发送PlayerCreateResponse: 携带远程玩家工厂ID,让请求方创建远程玩家对象
	/// </summary>
	[MPPacketHandler(PacketType.PlayerCreateRequest)]
	private void HandlePlayerCreateRequest(ulong senderId, DataReader reader) {
		var writer = GetWriter(MPSteamworks.Instance.UserSteamId, senderId, PacketType.PlayerCreateResponse);
		writer.Put(LocalPlayer.Instance.FactoryId);
		MPSteamworks.Instance.SendToPeer(senderId, writer);
	}

	/// <summary>
	/// 主机/客户端接收PlayerCreateResponse: 创建玩家对象
	/// </summary>
	[MPPacketHandler(PacketType.PlayerCreateResponse)]
	private void HandlePlayerCreateResponse(ulong senderId, DataReader reader) {
		string factoryId = reader.GetString();
		RPManager.Instance.PlayerCreate(senderId, factoryId);
	}

	/// <summary>
	/// 主机/客户端接收PlayerTeleportRequest<br/>
	/// 发送PlayerTeleportRespond: 有Mess环境则携带Mess数据
	/// </summary>
	/// <param name="senderId">发送方ID</param>
	[MPPacketHandler(PacketType.PlayerTeleportRequest)]
	private void HandlePlayerTeleport(ulong senderId, DataReader reader) {
		// 获取数据
		var positionData = ENT_Player.GetPlayer().transform.position;
		var writer = GetWriter(MPSteamworks.Instance.UserSteamId, senderId, PacketType.PlayerTeleportRespond);
		writer.Put(positionData.x);
		writer.Put(positionData.y);
		writer.Put(positionData.z);

		// 没有Mess环境则直接发送位置数据,有则发送位置数据和Mess数据
		if (DEN_DeathFloor.instance == null) {
			writer.Put(false);
		} else {
			var deathFloorData = DEN_DeathFloor.instance.GetSaveData();
			writer.Put(true);
			writer.Put(deathFloorData.relativeHeight);
			writer.Put(deathFloorData.active);
			writer.Put(deathFloorData.speed);
			writer.Put(deathFloorData.speedMult);
		}
		MPSteamworks.Instance.SendToPeer(senderId, writer);
	}

	/// <summary>
	/// 主机/客户端接收PlayerTeleportRespond: 传送并同步Mess数据
	/// </summary>
	/// <param name="senderId">发送ID</param>
	[MPPacketHandler(PacketType.PlayerTeleportRespond)]
	private void HandleRespondPlayerTeleport(ulong senderId, DataReader reader) {
		var posX = reader.GetFloat();
		var posY = reader.GetFloat();
		var posZ = reader.GetFloat();
		if (reader.GetBool()) {
			var deathFloorData = new DEN_DeathFloor.SaveData {
				relativeHeight = reader.GetFloat(),
				active = reader.GetBool(),
				speed = reader.GetFloat(),
				speedMult = reader.GetFloat(),
			};

			// 关闭可击杀效果
			DEN_DeathFloor.instance.SetCanKill(new string[] { "false" });
			// 重设计数器,期间位移视为传送
			LocalPlayer.Instance.TriggerTeleport();
			ENT_Player.GetPlayer().Teleport(new Vector3(posX, posY, posZ));
			DEN_DeathFloor.instance.LoadDataFromSave(deathFloorData);
			DEN_DeathFloor.instance.SetCanKill(new string[] { "true" });
		} else {
			// 重设计数器,期间位移视为传送
			LocalPlayer.Instance.TriggerTeleport();
			ENT_Player.GetPlayer().Teleport(new Vector3(posX, posY, posZ));
		}

	}
}
