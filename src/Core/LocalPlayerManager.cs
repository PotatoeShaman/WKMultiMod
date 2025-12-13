using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WKMultiMod.src.Data;
using WKMultiMod.src.Main;

namespace WKMultiMod.src.Core;

//仅获取本地玩家信息并触发事件给其他系统使用
//仅在联机时创建一个实例
public class LocalPlayerManager: MonoBehaviour {
	public ulong LocalPlayerId { get; set; } = 0;
	private float _lastSendTime;
	void Start() {
		if (MultiPlayerCore.IsMultiplayerActive == false) {
			MPMain.Logger.LogInfo("[MP Mod LPManager] 非联机状态, 自毁");
			Destroy(this);
		} else { 
			MPMain.Logger.LogInfo("[MP Mod LPManager] 联机状态, 保持运行");
		}
	}
	void Update() {
		// 没有被分配ID 或 没有开启多人时停止更新
		if (LocalPlayerId == 0||MultiPlayerCore.IsMultiplayerActive == false) 
			return;
		// 限制发送频率(20Hz)
		if (Time.time - _lastSendTime < 0.05f) 
			return;
		_lastSendTime = Time.time;
		// 创建玩家数据
		var playerData = PlayerDataSerializer.CreateLocalPlayerData(LocalPlayerId);
		if (playerData == null) {
			MPMain.Logger.LogError("[MP Mod LPManager] 本地玩家信息异常");
			return;
		}

		// 进行数据写入
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.PlayerDataUpdate);
		PlayerDataSerializer.WriteToNetData(writer, playerData);

		// 触发事件
		NetworkEvents.TriggerSendData(writer);
	}
}

