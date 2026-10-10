using SysDVR.Client.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SysDVR.Client.Sources
{
    internal class NetworkScan
    {
        public event Action<DeviceInfo>? OnDeviceFound;
        public event Action<string>? OnFailure;
        public event Action<bool>? OnScanningStateChanged;

        private CancellationTokenSource? _cancel;
        private readonly HashSet<string> _devices = new();
        public bool IsScanning { get; private set; }

        public void StartScanning()
        {
            if (IsScanning)
                return;

            IsScanning = true;
            OnScanningStateChanged?.Invoke(true);

            _cancel = new CancellationTokenSource();
            _devices.Clear();

            _ = RunScanAsync(_cancel.Token);
        }

        private async Task RunScanAsync(CancellationToken token)
        {
            try
            {
                var udpTask = Task.Run(() => ListenUdpBroadcastAsync(token), token);
                var tcpTask = Task.Run(() => ProbeSubnetTcpAsync(token), token);

                await Task.WhenAll(udpTask, tcpTask).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    OnFailure?.Invoke(ex.Message);
            }
            finally
            {
                IsScanning = false;
                OnScanningStateChanged?.Invoke(false);
            }
        }

        private async Task ListenUdpBroadcastAsync(CancellationToken token)
        {
            try
            {
                using var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, 19999));
                using var reg = token.Register(() => { try { client.Dispose(); } catch { } });

                while (!token.IsCancellationRequested)
                {
                    var msg = await client.ReceiveAsync().ConfigureAwait(false);
                    if (msg.Buffer != null && msg.Buffer.Length > 0)
                    {
                        var ip = msg.RemoteEndPoint.Address.ToString();
                        bool isNew;
                        lock (_devices)
                        {
                            isNew = _devices.Add(ip);
                        }

                        if (isNew)
                        {
                            var info = DeviceInfo.TryParse(ConnectionType.Net, msg.Buffer, ip);
                            if (info != null)
                            {
                                OnDeviceFound?.Invoke(info);
                            }
                            else
                            {
                                OnDeviceFound?.Invoke(DeviceInfo.ForIp(ip));
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignored on cancellation or socket disposal
            }
        }

        private async Task ProbeSubnetTcpAsync(CancellationToken token)
        {
            var prefixes = GetSubnetPrefixes();
            if (prefixes.Count == 0)
            {
                prefixes.Add("192.168.1.");
                prefixes.Add("192.168.0.");
            }

            using var semaphore = new SemaphoreSlim(30);
            var tasks = new List<Task>();

            foreach (var prefix in prefixes)
            {
                for (int i = 1; i <= 254; i++)
                {
                    if (token.IsCancellationRequested)
                        break;

                    var ip = prefix + i;
                    lock (_devices)
                    {
                        if (_devices.Contains(ip))
                            continue;
                    }

                    await semaphore.WaitAsync(token).ConfigureAwait(false);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (await ProbePortAsync(ip, 9911, 400, token).ConfigureAwait(false))
                            {
                                bool isNew;
                                lock (_devices)
                                {
                                    isNew = _devices.Add(ip);
                                }
                                if (isNew)
                                {
                                    OnDeviceFound?.Invoke(DeviceInfo.ForIp(ip));
                                }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<bool> ProbePortAsync(string ip, int port, int timeoutMs, CancellationToken token)
        {
            try
            {
                using var client = new TcpClient();
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(ip, port);
                var delayTask = Task.Delay(timeoutMs, linkedCts.Token);

                var completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                if (completed == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static List<string> GetSubnetPrefixes()
        {
            var prefixes = new List<string>();
            try
            {
                var hosts = Windows.Networking.Connectivity.NetworkInformation.GetHostNames();
                foreach (var host in hosts)
                {
                    if (host.Type == Windows.Networking.HostNameType.Ipv4 && host.IPInformation != null)
                    {
                        var ip = host.CanonicalName;
                        if (!ip.StartsWith("127.") && !ip.StartsWith("169.254."))
                        {
                            int dot = ip.LastIndexOf('.');
                            if (dot > 0)
                            {
                                string prefix = ip.Substring(0, dot + 1);
                                if (!prefixes.Contains(prefix))
                                    prefixes.Add(prefix);
                            }
                        }
                    }
                }
            }
            catch { }
            return prefixes;
        }

        public void StopScanning()
        {
            if (!IsScanning)
                return;

            _cancel?.Cancel();
            IsScanning = false;
            OnScanningStateChanged?.Invoke(false);
        }

        public void StopScannning() => StopScanning();
    }
}
