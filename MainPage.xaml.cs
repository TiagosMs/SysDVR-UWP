using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using SysDVR.Client;
using SysDVR.Client.Core;
using SysDVR.Client.Platform;
using SysDVR.Client.Sources;
using SysDVR.Client.Targets.Player;

namespace SysDVR.UWP
{
    public sealed partial class MainPage : Page
    {
        public static MainPage? Current { get; private set; }

        private CancellationTokenSource? _cts;
        private PlayerManager? _playerManager;
        private bool _isStreaming = false;

        public MainPage()
        {
            this.InitializeComponent();
            Current = this;
        }

        private async void OnConnectClick(object sender, RoutedEventArgs e)
        {
            var ip = IpTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                StatusTextBlock.Text = "Status: Digite um IP válido.";
                return;
            }

            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            IpTextBox.IsEnabled = false;
            StatusTextBlock.Text = $"Conectando ao Switch em {ip}:9911...";

            _cts = new CancellationTokenSource();

            try
            {
                // Initialize SDL context if not already done
                if (Program.SdlCtx == null)
                {
                    Program.SdlCtx = new SDLContext();
                    Program.SdlCtx.CreateWindow("SysDVR UWP");
                }

                var options = new StreamingOptions
                {
                    Kind = AudioToggle.IsOn ? StreamKind.Both : StreamKind.Video,
                    AudioBatching = 3,
                    UseNALReplay = true
                };

                var deviceInfo = new DeviceInfo
                {
                    Source = ConnectionType.Net,
                    ConnectionString = ip
                };

                var connector = new DeviceConnector(deviceInfo, _cts, options);
                connector.OnMessage += (msg) =>
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = $"Status: {msg}";
                    });
                };

                StatusTextBlock.Text = "Estabelecendo handshake TCP...";
                _playerManager = await connector.ConnectForPlayer();
                _playerManager.OnErrorMessage += (err) =>
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = $"Erro: {err}";
                    });
                };

                _playerManager.OnFatalError += (ex) =>
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = $"Erro Fatal: {ex.Message}";
                        StopStreaming();
                    });
                };

                _playerManager.Begin();
                _isStreaming = true;

                IdleOverlay.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Transmitindo de {ip} (TCP 9911)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha na conexão: {ex}");
                StatusTextBlock.Text = $"Erro ao conectar: {ex.Message}";
                StopStreaming();
            }
        }

        private void OnDisconnectClick(object sender, RoutedEventArgs e)
        {
            StopStreaming();
        }

        public void StopStreaming()
        {
            if (!_isStreaming && _cts == null)
                return;

            _isStreaming = false;

            try
            {
                _cts?.Cancel();
                _playerManager?.Dispose();
                _playerManager = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao finalizar streaming: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IdleOverlay.Visibility = Visibility.Visible;
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                IpTextBox.IsEnabled = true;
                StatusTextBlock.Text = "Status: Desconectado";
            });
        }
    }
}
