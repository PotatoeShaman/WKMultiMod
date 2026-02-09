using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;
using static WKMPMod.Data.MPWriterPool;

namespace WKMPMod.NetWork;

public class MPPacketHandlers {

	/// <summary>
	/// 主机接收WorldInitRequest: 请求初始化数据
	/// 发送WorldInitData: 初始化数据给新玩家
	/// </summary>
	[MPPacketHandler(PacketType.WorldInitRequest)]
	private static void HandleWorldInitRequest(ulong steamId, DataReader reader) {
		// 发送世界种子
		var writer = GetWriter(MPSteamworks.Instance.UserSteamId, steamId, PacketType.WorldInitData);
		writer.Put(WorldLoader.instance.seed);
		MPSteamworks.Instance.SendToPeer(steamId, writer);

		// 可以添加其他初始化数据,如游戏状态、物品状态等

		// Debug
		MPMain.LogInfo(Localization.Get("MPMessageHandlers", "SentInitData"));
	}

	/// <summary>
	/// 客户端接收WorldInitData: 新加入玩家,加载世界种子
	/// </summary>
	/// <param name="seed"></param>
	private void HandleWorldInit(ulong steamId, DataReader reader) {
		// 获取种子
		int seed = reader.GetInt();
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "LoadingWorld", seed.ToString()));
		// 种子相同默认为已经联机过,只不过断开了
		if (seed != WorldLoader.instance.seed)
			WorldLoader.ReloadWithSeed(new string[] { seed.ToString() });
		MPCore.MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);
	}


}
