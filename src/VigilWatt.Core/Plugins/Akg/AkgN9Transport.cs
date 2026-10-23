using System.Diagnostics;

namespace VigilWatt.Core.Plugins.Akg;

/// <summary>
/// 功 能： 执行一次 N9 电量查询，隔离陈旧队列并等待匹配结果。
/// 2026/10/23 21:07:10 30149
/// 原生命令超时后不重发，失败退避交给插件基类。
/// </summary>
internal static class AkgN9Transport
{
    public static (int Percent, byte[] Message)? Query(Func<bool> enabled, Func<byte[], bool> write,
        Func<byte[]?> read, Action<TimeSpan> delay)
    {
        if (!enabled()) return null;
        for (int i = 0; ; i++)
        {
            if (i == 20 || !enabled()) return null;
            var queued = read();
            if (queued == null || !AkgN9Codec.TryGetPayloadLength(queued, out int length)) return null;
            if (length == 0) break;
            delay(TimeSpan.FromMilliseconds(50));
        }
        if (!enabled() || !write(AkgN9Codec.CreateBatteryQuery())) return null;

        var assembler = new AkgN9PacketAssembler();
        bool acknowledged = false;
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 60 && elapsed.Elapsed < TimeSpan.FromSeconds(3); i++)
        {
            delay(TimeSpan.FromMilliseconds(50));
            if (!enabled()) return null;
            var report = read();
            if (report == null || !assembler.TryAccept(report, out var messages)) return null;
            foreach (var message in messages)
            {
                if (!AkgN9Codec.IsBatteryMessage(message)) continue;
                if (message[1] == 0x5B)
                {
                    if (!AkgN9Codec.IsSuccessfulAck(message)) return null;
                    acknowledged = true;
                }
                if (message[1] == 0x5D && acknowledged)
                    return AkgN9Codec.TryParseBattery(message, out int percent) ? (percent, message) : null;
            }
        }
        return null;
    }
}
