using System;
using System.Collections.Generic;

// 用真机 diag.txt 抓到的消息序列驱动 Classify,验证托盘点击判定。
static class TestClicks
{
    const uint MOVE = 0x0200, LDOWN = 0x0201, LUP = 0x0202, DBL = 0x0203;
    const uint RDOWN = 0x0204, RUP = 0x0205, MUP = 0x0208;

    static int failed;
    static int total;

    static void Seq(string name, uint[] lows, int[] times, Audiolite.TrayAction[] expect)
    {
        total++;
        Audiolite.ResetClicks();
        var got = new List<Audiolite.TrayAction>();
        for (int i = 0; i < lows.Length; i++) got.Add(Audiolite.Classify(lows[i], times[i]));

        bool ok = got.Count == expect.Length;
        if (ok)
            for (int i = 0; i < expect.Length; i++)
                if (got[i] != expect[i]) { ok = false; break; }

        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "   实际=[" + string.Join(",", got.ConvertAll(Describe).ToArray()) + "]");
        if (!ok) failed++;
    }

    static string Describe(Audiolite.TrayAction a) { return a.ToString(); }

    static readonly Audiolite.TrayAction N = Audiolite.TrayAction.None;
    static readonly Audiolite.TrayAction T = Audiolite.TrayAction.Toggle;
    static readonly Audiolite.TrayAction M = Audiolite.TrayAction.Menu;

    static void FadeSequenceMustOnlyShrink()
    {
        total++;
        byte a = 240;
        bool ok = true;
        var seen = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            seen.Add(a.ToString());
            byte next = Audiolite.NextFade(a);
            if (next > a) { ok = false; }          // 淡出途中亮度回升 = 闪回
            a = next;
            if (a == 0) break;
        }
        if (a != 0) ok = false;                     // 收敛不到 0 = 永远不消失
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + "淡出序列必须单调变暗并收敛到 0   序列=["
                        + string.Join(">", seen.ToArray()) + "]");
        if (!ok) failed++;
    }

    static void Case(string what, string got, string want)
    {
        total++;
        bool ok = got == want;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + " -> [" + got + "]" + (ok ? "" : "   期望 [" + want + "]"));
        if (!ok) failed++;
    }

    static Audiolite.Entry E(string id, string cat, string desc, string install)
    {
        return new Audiolite.Entry { Id = id, Category = cat, Desc = desc, Install = DateTime.Parse(install) };
    }

    static Audiolite.Entry EA(string id, string cat, string desc, string install, string arrive)
    {
        Audiolite.Entry e = E(id, cat, desc, install);
        e.Arrive = DateTime.Parse(arrive);
        return e;
    }

    static void Names()
    {
        Case("编号 2/4", Audiolite.Label("耳机", "蓝牙耳机A", 2, 4), "耳机 2 (蓝牙耳机A)");
        Case("组内第一台不编号", Audiolite.Label("耳机", "内置声卡", 1, 4), "耳机 (内置声卡)");
        Case("组内只有一台不编号", Audiolite.Label("耳机", "X", 1, 1), "耳机 (X)");
        Case("无类别名", Audiolite.Label("", "蓝牙耳机A", 1, 3), "蓝牙耳机A");

        Case("剥掉 Windows 前缀", Audiolite.StripWinPrefix("2- 蓝牙耳机A"), "蓝牙耳机A");
        Case("两位数前缀", Audiolite.StripWinPrefix("12- 虚拟设备X"), "虚拟设备X");
        Case("无前缀原样", Audiolite.StripWinPrefix("内置声卡"), "内置声卡");
        Case("非数字前缀不动", Audiolite.StripWinPrefix("a- x"), "a- x");
        Case("空描述不动", Audiolite.StripWinPrefix("2- "), "2- ");

        // 典型形状:4 个"耳机",Windows 只给其中一个编了号(名称与日期均为虚构测试数据)
        var all = new List<Audiolite.Entry>
        {
            E("{a}", "耳机", "内置声卡", "2020-01-01"),
            E("{b}", "耳机", "蓝牙耳机A", "2021-01-01"),
            E("{c}", "耳机", "蓝牙耳机B", "2022-01-01"),
            E("{d}", "耳机", "蓝牙耳机C", "2023-01-01"),
            E("{e}", "扬声器", "内置声卡", "2020-01-01"),
        };
        Audiolite.AssignRanks(all);
        Case("按安装顺序编号 1", all[0].Rank + "/" + all[0].Total, "1/4");
        Case("按安装顺序编号 2", all[1].Rank + "/" + all[1].Total, "2/4");
        Case("按安装顺序编号 3", all[2].Rank + "/" + all[2].Total, "3/4");
        Case("按安装顺序编号 4", all[3].Rank + "/" + all[3].Total, "4/4");
        Case("独此一台的扬声器", all[4].Rank + "/" + all[4].Total, "1/1");
        Case("扬声器标签", Audiolite.Label(all[4].Category, all[4].Desc, all[4].Rank, all[4].Total), "扬声器 (内置声卡)");

        // 编号必须按"当前要显示出来的这一批"算:4 台耳机里只连了 2 台,
        // 结果应是 耳机 / 耳机 2,不能出现"耳机 / 耳机 3"这种空洞。
        var online = new List<Audiolite.Entry> { all[0], all[2] };
        Audiolite.AssignRanks(online);
        Case("只连 2 台时不跳号 1", Audiolite.Label(online[0].Category, online[0].Desc, online[0].Rank, online[0].Total),
             "耳机 (内置声卡)");
        Case("只连 2 台时不跳号 2", Audiolite.Label(online[1].Category, online[1].Desc, online[1].Rank, online[1].Total),
             "耳机 2 (蓝牙耳机B)");

        // 排序键必须是"接入时间",不是安装时间:
        // 后装的那台如果先连上,它就该排在前面。
        var byArrive = new List<Audiolite.Entry>
        {
            EA("{x}", "耳机", "后装但先连", "2026-01-01", "2026-09-21 09:00"),
            EA("{y}", "耳机", "先装但后连", "2024-01-01", "2026-09-21 18:00"),
        };
        byArrive.Sort(Audiolite.ByOrder);
        Case("接入时间优先于安装时间", byArrive[0].Desc, "后装但先连");
        Case("接入时间决定编号", Audiolite.Label(byArrive[1].Category, byArrive[1].Desc, 2, 2), "耳机 2 (先装但后连)");

        // 拿不到接入时间时(属性缺失 -> MaxValue 哨兵)必须退回安装时间,
        // 否则顺序会变得不确定。
        var fallback = new List<Audiolite.Entry>
        {
            E("{p}", "耳机", "后装的那台", "2024-01-01"),
            E("{q}", "耳机", "先装的那台", "2020-01-01"),
        };
        fallback[0].Arrive = DateTime.MaxValue;
        fallback[1].Arrive = DateTime.MaxValue;
        fallback.Sort(Audiolite.ByOrder);
        Case("缺失接入时间时退回安装时间", fallback[0].Desc, "先装的那台");
    }

    // 实验:反复枚举 -> 看句柄是否累积 -> 强制 GC -> 看句柄是否回落。
    // 回落 = 句柄是 COM RCW 等终结器造成的,假设成立。
    static void LeakProbe()
    {
        var self = System.Diagnostics.Process.GetCurrentProcess();
        Audiolite.EnumOnce();                                   // 先热身,避开首次 JIT/加载的干扰
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        self.Refresh();
        int baseH = self.HandleCount;

        for (int round = 1; round <= 3; round++)
        {
            for (int i = 0; i < 60; i++) Audiolite.EnumOnce();
            self.Refresh();
            int h = self.HandleCount;
            Console.WriteLine("  第" + round + " 轮 60 次枚举后: 句柄=" + h + "  相对基线+" + (h - baseH)
                              + "  私有=" + (self.PrivateMemorySize64 / 1048576) + "MB");
        }

        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        self.Refresh();
        Console.WriteLine("  强制GC后: 句柄=" + self.HandleCount + "  私有=" + (self.PrivateMemorySize64 / 1048576) + "MB");
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--leak") { LeakProbe(); return 0; }

        Names();
        FadeSequenceMustOnlyShrink();
        // 真机日志:15:45:00.172 按下 / 15:45:00.172 抬起 —— 同一毫秒
        Seq("左键 按下+抬起(同一毫秒)",
            new uint[] { LDOWN, LUP }, new[] { 1000, 1000 }, new[] { N, T });

        // 真机日志:15:44:59.310 按下 / .311 抬起 —— 差 1ms
        Seq("右键 按下+抬起(差 1ms)",
            new uint[] { RDOWN, RUP }, new[] { 2000, 2001 }, new[] { N, M });

        // 真机日志:0201,0202,0203,0202 —— 视觉双击应只算一次切换
        Seq("视觉双击只算一次切换",
            new uint[] { LDOWN, LUP, DBL, LUP }, new[] { 3000, 3000, 3010, 3011 }, new[] { N, T, N, N });

        // 移动洪水不能吃掉后续真实点击
        var lows = new List<uint>();
        var ts = new List<int>();
        var ex = new List<Audiolite.TrayAction>();
        for (int i = 0; i < 50; i++) { lows.Add(MOVE); ts.Add(4000 + i); ex.Add(N); }
        lows.Add(LDOWN); ts.Add(4060); ex.Add(N);
        lows.Add(LUP); ts.Add(4060); ex.Add(T);
        Seq("移动洪水之后左键仍有效",
            lows.ToArray(), ts.ToArray(), ex.ToArray());

        // 中键抬起也要触发切换
        Seq("中键 按下+抬起",
            new uint[] { 0x0207, MUP }, new[] { 4500, 4500 }, new[] { N, T });

        // 两次间隔足够久的独立点击应当各算一次
        Seq("两次独立点击(间隔 400ms)",
            new uint[] { LDOWN, LUP, LDOWN, LUP }, new[] { 5000, 5000, 5400, 5400 }, new[] { N, T, N, T });

        Console.WriteLine("");
        Console.WriteLine(failed == 0 ? total + "/" + total + " 全部通过" : failed + "/" + total + " 失败");
        return failed == 0 ? 0 : 1;
    }
}
