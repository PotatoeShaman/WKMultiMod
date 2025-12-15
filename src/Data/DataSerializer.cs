using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using static WKMultiMod.src.Data.PlayerData;

namespace WKMultiMod.src.Data;
// 封装的读取方法
public static class MPDataSerializer {
	/// <summary>
	/// 创建一个玩家数据
	/// </summary>
	/// <param name="Id"></param>
	/// <returns></returns>
	public static PlayerData CreateLocalPlayerData(ulong Id) {
		var player = ENT_Player.GetPlayer();
		if (player == null) return null;

		var data = new PlayerData {
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

	/// <summary>
	/// 获取手部数据
	/// </summary>
	/// <param name="hand"></param>
	/// <returns></returns>
	private static HandData GetHandData(ENT_Player.Hand hand) {
		var handData = new HandData();
		handData.IsFree = hand.IsFree();

		if (!handData.IsFree) {
			//handData.Position = hand.GetHoldPosition();
			handData.Position = hand.GetHoldWorldPosition();
		}

		return handData;
	}

	/// <summary>
	/// 序列化到NetDataWriter (无数据包类型)
	/// </summary>
	/// <param name="writer"></param>
	/// <param name="data"></param>
	public static void WriteToNetData(NetDataWriter writer, PlayerData data) {
		// 基础信息
		writer.Put(data.playId);
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

	/// <summary>
	/// 反序列化从NetDataReader (无数据包类型)
	/// </summary>
	/// <param name="reader"></param>
	/// <returns></returns>
	public static PlayerData ReadFromNetData(NetDataReader reader) {
		var data = new PlayerData();

		data.playId = reader.GetInt();
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

	/// <summary>
	/// NetDataWriter 转 byte[]
	/// </summary>
	public static byte[] WriterToBytes(NetDataWriter writer) {
		// 简单直接的方法
		return writer.AsReadOnlySpan().ToArray();
	}

	/// <summary>
	/// byte[] 转 NetDataReader
	/// </summary>
	public static NetDataReader BytesToReader(byte[] data) {
		var reader = new NetDataReader();
		reader.SetSource(data);
		return reader;
	}
}



