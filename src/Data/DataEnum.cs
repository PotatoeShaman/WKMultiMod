using System;
using System.Collections.Generic;
using System.Text;

namespace WKMultiMod.src.Data;
	// 数据包类型枚举 - 定义不同类型的网络消息
	public enum PacketType {
		SeedUpdate = 1,         // 世界种子更新
		PlayerDataUpdate = 4,  // 玩家数据更新
	}