// ============================================================================
// NetMonitor.cs —— 网速采集
//   取所有非回环、非隧道的活动网卡的 IPv4 累计收发字节数，按时间差算瞬时速率；
//   同时累计本次运行的总量与峰值（总量/峰值均为「本次运行」口径，非系统开机口径）。
// ============================================================================

using System.Net.NetworkInformation;

namespace TrafficWidgetWeb;

sealed class NetMonitor
{
    long _lastSent, _lastRecv;
    DateTime _lastAt = DateTime.UtcNow;
    bool _first = true;

    /// <summary>当前下载速率（字节/秒）</summary>
    public double Dl { get; private set; }
    /// <summary>当前上传速率（字节/秒）</summary>
    public double Ul { get; private set; }
    /// <summary>本次运行累计下载（字节）</summary>
    public double DlTotal { get; private set; }
    /// <summary>本次运行累计上传（字节）</summary>
    public double UlTotal { get; private set; }
    /// <summary>本次运行下载峰值（字节/秒）</summary>
    public double PeakDl { get; private set; }
    /// <summary>本次运行上传峰值（字节/秒）</summary>
    public double PeakUl { get; private set; }

    /// <summary>清零累计量与峰值（不改当前速率）</summary>
    public void Reset()
    {
        DlTotal = UlTotal = 0;
        PeakDl = PeakUl = 0;
    }

    /// <summary>采样一次。间隔过短（&lt;0.2s）时直接跳过，避免除零和抖动。</summary>
    public void Sample()
    {
        var now = DateTime.UtcNow;
        double span = (now - _lastAt).TotalSeconds;
        if (span < 0.2) return;

        long sent = 0, recv = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            try
            {
                var st = ni.GetIPv4Statistics();
                sent += st.BytesSent;
                recv += st.BytesReceived;
            }
            catch { /* 个别虚拟网卡取不到统计值时忽略 */ }
        }

        if (_first)                       // 首帧只建立基线，不算速率
        {
            _first = false;
            _lastSent = sent; _lastRecv = recv; _lastAt = now;
            return;
        }

        long dRecv = recv - _lastRecv;
        long dSent = sent - _lastSent;
        // 网卡重启/计数器回绕时差值为负，按 0 处理
        if (dRecv < 0) dRecv = 0;
        if (dSent < 0) dSent = 0;

        Dl = dRecv / span;
        Ul = dSent / span;
        DlTotal += dRecv;
        UlTotal += dSent;
        if (Dl > PeakDl) PeakDl = Dl;
        if (Ul > PeakUl) PeakUl = Ul;

        _lastSent = sent; _lastRecv = recv; _lastAt = now;
    }
}
