using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
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

        public MainPage()
        {
            this.InitializeComponent();
            Current = this;

            _bitmap = new WriteableBitmap(StreamInfo.VideoWidth, StreamInfo.VideoHeight);
            VideoImage.Source = _bitmap;
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
                    _videoPlayer = new VideoPlayer(Program.Options.DecoderName);
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
                StatusTextBlock.Text = $"Transmitindo de {ip} (TCP 9911)";
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
            bool isFullScreen = TopHeader.Visibility == Visibility.Collapsed;
            TopHeader.Visibility = isFullScreen ? Visibility.Visible : Visibility.Collapsed;
            StatusBarBorder.Visibility = isFullScreen ? Visibility.Visible : Visibility.Collapsed;
            FullScreenButton.Content = isFullScreen ? "Tela Cheia" : "Sair Tela Cheia";
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
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                IpTextBox.IsEnabled = true;
                FpsTextBlock.Text = "";
                StatusTextBlock.Text = statusMessage ?? "Status: Desconectado";
            });
        }
    }
}
