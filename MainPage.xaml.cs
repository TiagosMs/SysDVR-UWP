using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using SysDVR.Client;
using SysDVR.Client.Core;
using SysDVR.Client.Platform;
using SysDVR.Client.Sources;
using SysDVR.Client.Targets.Player;

namespace SysDVR.UWP
{
    [ComImport]
    [Guid("905A0FE0-BC53-11DF-8C49-001E4FC686DA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal unsafe interface IBufferByteAccess
    {
        [PreserveSig]
        int Buffer(out byte* ppBuffer);
    }

    public sealed partial class MainPage : Page
    {
        public static MainPage? Current { get; private set; }

        private CancellationTokenSource? _cts;
        private PlayerManager? _playerManager;
        private VideoPlayer? _videoPlayer;
        private AudioPlayer? _audioPlayer;
        private WriteableBitmap? _bitmap;
        private Task? _renderTask;
        private CancellationTokenSource? _renderCts;
        private int _renderPending = 0;
        private bool _isStreaming = false;

        private readonly NetworkScan _networkScan = new();
        private readonly List<DeviceInfo> _foundDevices = new();

        public MainPage()
        {
            this.InitializeComponent();
            Current = this;

            _bitmap = new WriteableBitmap(StreamInfo.VideoWidth, StreamInfo.VideoHeight);
            VideoImage.Source = _bitmap;

            _networkScan.OnDeviceFound += OnDeviceFound;
            _networkScan.OnScanningStateChanged += OnScanningStateChanged;
            _networkScan.OnFailure += OnScanFailure;

            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;

            LoadSettings();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            _networkScan.StopScanning();
        }

        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            var focused = FocusManager.GetFocusedElement();
            if (focused is TextBox)
            {
                if (args.VirtualKey == VirtualKey.GamepadB)
                {
                    ConnectButton.Focus(FocusState.Programmatic);
                    args.Handled = true;
                }
                return;
            }

            switch (args.VirtualKey)
            {
                case VirtualKey.GamepadX:
                    args.Handled = true;
                    OnScanClick(this, null!);
                    break;

                case VirtualKey.GamepadA:
                    if (!_isStreaming && ConnectButton.IsEnabled)
                    {
                        args.Handled = true;
                        OnConnectClick(this, null!);
                    }
                    break;

                case VirtualKey.GamepadB:
                    if (_isStreaming)
                    {
                        args.Handled = true;
                        StopStreaming("Desconectado via controle (Gamepad B)");
                    }
                    else if (TopHeader.Visibility == Visibility.Collapsed)
                    {
                        args.Handled = true;
                        OnFullScreenToggleClick(this, null!);
                    }
                    break;

                case VirtualKey.GamepadY:
                case VirtualKey.GamepadMenu:
                case VirtualKey.GamepadView:
                    args.Handled = true;
                    OnFullScreenToggleClick(this, null!);
                    break;
            }
        }

        private void LoadSettings()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (values.TryGetValue("LastIp", out var savedIp) && savedIp is string ip && !string.IsNullOrWhiteSpace(ip))
                {
                    IpTextBox.Text = ip;
                }
                if (values.TryGetValue("AudioEnabled", out var savedAudio) && savedAudio is bool audioOn)
                {
                    AudioToggle.IsOn = audioOn;
                }
                if (values.TryGetValue("LowLatency", out var savedLowLatency) && savedLowLatency is bool lowLatencyOn)
                {
                    LowLatencyToggle.IsOn = lowLatencyOn;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Erro ao carregar config: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values["LastIp"] = IpTextBox.Text?.Trim();
                values["AudioEnabled"] = AudioToggle.IsOn;
                values["LowLatency"] = LowLatencyToggle.IsOn;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Erro ao salvar config: {ex.Message}");
            }
        }

        private void OnIpTextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void OnOptionToggled(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void OnScanClick(object sender, RoutedEventArgs e)
        {
            if (_networkScan.IsScanning)
            {
                _networkScan.StopScanning();
                ScanButton.Content = "[X] Buscar Switch";
                StatusTextBlock.Text = "Busca de rede cancelada.";
            }
            else
            {
                _foundDevices.Clear();
                DeviceComboBox.Items.Clear();
                DeviceComboBox.Visibility = Visibility.Collapsed;
                ScanButton.Content = "[X] Parar Busca";
                StatusTextBlock.Text = "Buscando Switch na rede local (UDP 19999 + TCP 9911)...";
                _networkScan.StartScanning();
            }
        }

        private void OnDeviceFound(DeviceInfo info)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_foundDevices.Any(d => d.ConnectionString == info.ConnectionString))
                {
                    _foundDevices.Add(info);
                    DeviceComboBox.Items.Add($"{info.ConnectionString} ({info.TextRepresentation})");
                    DeviceComboBox.Visibility = Visibility.Visible;

                    // Se campo IP estiver vazio ou com prefixo padrão, preenche automaticamente
                    if (string.IsNullOrWhiteSpace(IpTextBox.Text) || IpTextBox.Text == "192.168.1." || IpTextBox.Text == "192.168.0.")
                    {
                        IpTextBox.Text = info.ConnectionString;
                        DeviceComboBox.SelectedIndex = _foundDevices.Count - 1;
                        SaveSettings();
                    }

                    StatusTextBlock.Text = $"Switch encontrada em {info.ConnectionString}!";
                }
            });
        }

        private void OnScanningStateChanged(bool isScanning)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!isScanning)
                {
                    ScanButton.Content = "[X] Buscar Switch";
                    if (_foundDevices.Count == 0)
                    {
                        StatusTextBlock.Text = "Nenhuma Switch encontrada. Verifique se o SysDVR está rodando na mesma rede.";
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Busca concluída: {_foundDevices.Count} Switch(es) encontrada(s).";
                    }
                }
            });
        }

        private void OnScanFailure(string error)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                StatusTextBlock.Text = $"Erro na busca de rede: {error}";
            });
        }

        private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceComboBox.SelectedIndex >= 0 && DeviceComboBox.SelectedIndex < _foundDevices.Count)
            {
                var selected = _foundDevices[DeviceComboBox.SelectedIndex];
                IpTextBox.Text = selected.ConnectionString;
                SaveSettings();
            }
        }

        private async void OnConnectClick(object sender, RoutedEventArgs e)
        {
            var ip = IpTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                StatusTextBlock.Text = "Status: Digite um IP válido.";
                return;
            }

            SaveSettings();

            if (_networkScan.IsScanning)
            {
                _networkScan.StopScanning();
            }

            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            IpTextBox.IsEnabled = false;
            ScanButton.IsEnabled = false;
            DeviceComboBox.IsEnabled = false;
            StatusTextBlock.Text = $"Conectando ao Switch em {ip}:9911...";

            _cts = new CancellationTokenSource();

            try
            {
                if (SysDVR.Client.Program.SdlCtx == null)
                {
                    SysDVR.Client.Program.SdlCtx = new SDLContext();
                }

                var options = new StreamingOptions
                {
                    Kind = AudioToggle.IsOn ? StreamKind.Both : StreamKind.Video,
                    AudioBatching = 3,
                    UseNALReplay = true
                };

                var deviceInfo = DeviceInfo.ForIp(ip);

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
                        Debug.WriteLine($"[SysDVR Fatal Error] {ex}");
                        SysDVR.Client.Program.DebugLog($"[SysDVR Fatal Error] {ex}");
                        StopStreaming($"Erro Fatal ({ex.GetType().Name}): {ex.Message}");
                    });
                };

                var sync = new StreamSynchronizationHelper(_playerManager.HasAudio && _playerManager.HasVideo);

                if (_playerManager.HasVideo)
                {
                    _videoPlayer = new VideoPlayer(null, LowLatencyToggle.IsOn);
                    _videoPlayer.Decoder.SyncHelper = sync;
                    _playerManager.VideoTarget.UseContext(_videoPlayer.Decoder);
                }

                if (_playerManager.HasAudio)
                {
                    _audioPlayer = new AudioPlayer(_playerManager.AudioTarget);
                    _playerManager.AudioTarget.Volume = 1.0f;
                    _audioPlayer.Resume();
                }

                _playerManager.UseSyncManager(sync);

                _playerManager.Begin();
                _isStreaming = true;

                _renderCts = new CancellationTokenSource();
                var renderToken = _renderCts.Token;
                _renderTask = Task.Run(() => StartRenderLoop(renderToken));

                IdleOverlay.Visibility = Visibility.Collapsed;
                TopHeader.Visibility = Visibility.Visible;
                StatusTextBlock.Text = $"Transmitindo de {ip} (TCP 9911) | Latência: {(LowLatencyToggle.IsOn ? "Baixa" : "Padrão")}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SysDVR Connect Error] {ex}");
                SysDVR.Client.Program.DebugLog($"[SysDVR Connect Error] {ex}");

                string errMessage;
                if (ex is DllNotFoundException || ex is BadImageFormatException)
                {
                    errMessage = $"Erro DLL ({ex.GetType().Name}): {ex.Message}";
                }
                else
                {
                    errMessage = $"Erro ({ex.GetType().Name}): {ex.Message}";
                }

                StopStreaming(errMessage);
            }
        }

        private unsafe void StartRenderLoop(CancellationToken token)
        {
            if (_bitmap == null || _videoPlayer == null)
                return;

            byte* pPixelBuffer = null;
            try
            {
                var byteAccess = (IBufferByteAccess)_bitmap.PixelBuffer;
                int hr = byteAccess.Buffer(out pPixelBuffer);
                if (hr != 0)
                {
                    pPixelBuffer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get IBufferByteAccess: {ex}");
                pPixelBuffer = null;
            }

            int width = StreamInfo.VideoWidth;
            int height = StreamInfo.VideoHeight;
            int stride = width * 4;

            byte[]? fallbackBuffer = pPixelBuffer == null ? new byte[stride * height] : null;

            var fpsWatch = Stopwatch.StartNew();
            int frameCount = 0;
            long lastFpsTime = 0;

            while (!token.IsCancellationRequested)
            {
                bool decoded = false;

                if (pPixelBuffer != null)
                {
                    decoded = _videoPlayer.DecodeFrameToBuffer(pPixelBuffer, stride);
                }
                else if (fallbackBuffer != null)
                {
                    fixed (byte* pFallback = fallbackBuffer)
                    {
                        decoded = _videoPlayer.DecodeFrameToBuffer(pFallback, stride);
                    }
                }

                if (decoded)
                {
                    frameCount++;

                    if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                    {
                        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            try
                            {
                                if (fallbackBuffer != null && _bitmap != null)
                                {
                                    using (var stream = _bitmap.PixelBuffer.AsStream())
                                    {
                                        stream.Write(fallbackBuffer, 0, fallbackBuffer.Length);
                                    }
                                }
                                _bitmap?.Invalidate();
                            }
                            catch { }
                            finally
                            {
                                Interlocked.Exchange(ref _renderPending, 0);
                            }
                        });
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }

                if (fpsWatch.ElapsedMilliseconds - lastFpsTime >= 1000)
                {
                    int fps = frameCount;
                    frameCount = 0;
                    lastFpsTime = fpsWatch.ElapsedMilliseconds;

                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        FpsTextBlock.Text = $"{fps} FPS";
                    });
                }
            }
        }

        private void OnDisconnectClick(object sender, RoutedEventArgs e)
        {
            StopStreaming();
        }

        private void OnFullScreenToggleClick(object sender, RoutedEventArgs e)
        {
            bool isVisible = TopHeader.Visibility == Visibility.Visible;
            TopHeader.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            FullScreenButton.Content = isVisible ? "[Y] Exibir Barra" : "[Y] Ocultar Barra";
        }

        private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            OnFullScreenToggleClick(sender, e);
        }

        public void StopStreaming(string? statusMessage = null)
        {
            if (!_isStreaming && _cts == null)
            {
                if (statusMessage != null)
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = statusMessage;
                    });
                }
                return;
            }

            _isStreaming = false;

            try
            {
                _renderCts?.Cancel();
                try { _renderTask?.Wait(500); } catch { }
                _renderCts?.Dispose();
                _renderCts = null;
                _renderTask = null;

                _audioPlayer?.Dispose();
                _audioPlayer = null;

                _videoPlayer?.Dispose();
                _videoPlayer = null;

                _cts?.Cancel();
                _playerManager?.Dispose();
                _playerManager = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao finalizar streaming: {ex.Message}");
                SysDVR.Client.Program.DebugLog($"Erro ao finalizar streaming: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IdleOverlay.Visibility = Visibility.Visible;
                TopHeader.Visibility = Visibility.Collapsed;
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                IpTextBox.IsEnabled = true;
                ScanButton.IsEnabled = true;
                DeviceComboBox.IsEnabled = true;
                FpsTextBlock.Text = "";
                StatusTextBlock.Text = statusMessage ?? "Status: Desconectado";
            });
        }
    }
}
