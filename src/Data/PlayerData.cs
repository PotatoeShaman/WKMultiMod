using LiteNetLib.Utils;
using Steamworks;
using System;
using UnityEngine;
using static WKMultiMod.src.Data.PlayerData;

namespace WKMultiMod.src.Data;

// 数据包类型枚举 - 定义不同类型的网络消息
public enum PacketType {
	ConnectedToServer = 0,  // 连接成功通知
	SeedUpdate = 1,         // 世界种子更新
	CreatePlayer = 2,       // 创建新玩家
	RemovePlayer = 3,       // 移除玩家
	PlayerDataUpdate = 4,  // 玩家数据更新
}

[System.Serializable]
public class PlayerData {
	public enum HandType { Left = 0, Right = 1 }

	// 使用 ulong 存储 SteamID 或 LiteNet赋予的ID
	public ulong PlayerId;  // Steam 唯一标识

	// 时间戳(网络同步关键)
	public long TimestampTicks;

	// 位置和旋转(直接用float字段)
	public float PosX, PosY, PosZ;
	public float RotX, RotY, RotZ, RotW;

	// 手部数据
	public HandData LeftHand;
	public HandData RightHand;

	// 特殊标志
	public bool IsTeleport;

	// 辅助属性(不序列化)
	[System.NonSerialized] private Vector3 _positionCache;
	[System.NonSerialized] private Quaternion _rotationCache;

	public Vector3 Position {
		get {
			// 避免每次都new,但要注意线程安全
			if (_positionCache == default)
				_positionCache = new Vector3(PosX, PosY, PosZ);
			return _positionCache;
		}
		set {
			_positionCache = value;
			PosX = value.x; PosY = value.y; PosZ = value.z;
		}
	}

	public Quaternion Rotation {
		get {
			if (_rotationCache == default)
				_rotationCache = new Quaternion(RotX, RotY, RotZ, RotW);
			return _rotationCache;
		}
		set {
			_rotationCache = value;
			RotX = value.x; RotY = value.y; RotZ = value.z; RotW = value.w;
		}
	}

	public DateTime Timestamp {
		get => new DateTime(TimestampTicks);
		set => TimestampTicks = value.Ticks;
	}

	// 构造函数
	public PlayerData() {
		LeftHand = new HandData { handType = HandType.Left };
		RightHand = new HandData { handType = HandType.Right };
	}
}

[System.Serializable]
public class HandData {
	// 手部类型
	public PlayerData.HandType handType;
	// 是否空闲
	public bool IsFree;
	// 位置
	public float PosX;
	public float PosY;
	public float PosZ;

	[System.NonSerialized] private Vector3 _positionCache;

	public Vector3 Position {
		get {
			if (_positionCache == default)
				_positionCache = new Vector3(PosX, PosY, PosZ);
			return _positionCache;
		}
		set {
			_positionCache = value;
			PosX = value.x;
			PosY = value.y;
			PosZ = value.z;
		}
	}
}

// 封装的读取方法
public static class PlayerDataSerializer {
	// 序列化
	public static void WriteToNetData(NetDataWriter writer, PlayerData data) {
		// 基础信息
		writer.Put(data.PlayerId);          // ulong
		writer.Put(data.TimestampTicks);   // long

		// 变换信息
		writer.Put(data.PosX);
		writer.Put(data.PosY);
		writer.Put(data.PosZ);

		writer.Put(data.RotX);
		writer.Put(data.RotY);
		writer.Put(data.RotZ);
		writer.Put(data.RotW);

		// 左手数据
		writer.Put(data.LeftHand.IsFree);
		if (!data.LeftHand.IsFree) {
			writer.Put(data.LeftHand.PosX);
			writer.Put(data.LeftHand.PosY);
			writer.Put(data.LeftHand.PosZ);
		}

		// 右手数据
		writer.Put(data.RightHand.IsFree);
		if (!data.RightHand.IsFree) {
			writer.Put(data.RightHand.PosX);
			writer.Put(data.RightHand.PosY);
			writer.Put(data.RightHand.PosZ);
		}

		// 状态标志
		writer.Put(data.IsTeleport);
	}
	// 反序列化
	public static PlayerData ReadFromNetData(NetDataReader reader) {
		var data = new PlayerData();

		// 基础信息
		data.PlayerId = reader.GetULong();
		data.TimestampTicks = reader.GetLong();

		// 变换信息
		data.PosX = reader.GetFloat();
		data.PosY = reader.GetFloat();
		data.PosZ = reader.GetFloat();

		data.RotX = reader.GetFloat();
		data.RotY = reader.GetFloat();
		data.RotZ = reader.GetFloat();
		data.RotW = reader.GetFloat();

		// 左手数据
		bool leftFree = reader.GetBool();
		data.LeftHand.IsFree = leftFree;
		if (!leftFree) {
			data.LeftHand.PosX = reader.GetFloat();
			data.LeftHand.PosY = reader.GetFloat();
			data.LeftHand.PosZ = reader.GetFloat();
		}

		// 右手数据
		bool rightFree = reader.GetBool();
		data.RightHand.IsFree = rightFree;
		if (!rightFree) {
			data.RightHand.PosX = reader.GetFloat();
			data.RightHand.PosY = reader.GetFloat();
			data.RightHand.PosZ = reader.GetFloat();
		}

		// 状态标志
		data.IsTeleport = reader.GetBool();

		return data;
	}

	
	public static PlayerData CreateLocalPlayerData(ulong Id) {
		var player = ENT_Player.GetPlayer();
		if (player == null) return null;

		var data = new PlayerData { 
			PlayerId = Id,
			TimestampTicks = DateTime.UtcNow.Ticks 
		};

		// 位置和旋转
		data.Position = player.transform.position;
		data.Rotation = player.transform.rotation;

		// 手部数据
		data.LeftHand = GetHandData(player.hands[(int)HandType.Left]);
		data.RightHand = GetHandData(player.hands[(int)HandType.Right]);

		return data;
	}

	private static HandData GetHandData(ENT_Player.Hand hand) {
		var handData = new HandData();
		handData.IsFree = hand.IsFree();

		if (!handData.IsFree) {
			//handData.Position = hand.GetHoldPosition();
			handData.Position = hand.GetHoldWorldPosition();
		}

		return handData;
	}
}

