using FFmpeg.AutoGen;
using SysDVR.Client.Core;
using SysDVR.Client.Sources;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using static FFmpeg.AutoGen.ffmpeg;
using static SDL2.SDL;

namespace SysDVR.Client.Targets.Player
{
    unsafe class DecoderContext
    {
        public AVCodecContext* CodecCtx { get; init; }

        public AVFrame* RenderFrame;
        public AVFrame* ReceiveFrame;

        public AVFrame* Frame1 { get; init; }
        public AVFrame* Frame2 { get; init; }

        public object CodecLock { get; init; }

        public StreamSynchronizationHelper SyncHelper;
        public AutoResetEvent OnFrameEvent;
    }

    unsafe struct FormatConverterContext
    {
        public SwsContext* Converter { get; init; }
        public AVFrame* Frame { get; init; }
    }

    // Guarantees that the audio and video stream are compatible with the player
    class PlayerManager : StreamManager
    {
        internal new DecodeH264Target VideoTarget;
        internal new BaseAudioPlayerTarget AudioTarget;

        readonly public bool IsCompatibleAudioStream;

        static OutStream? MakeAudioStream(bool hasAudio) 
        {
            if (!hasAudio)
                return null;

            var useCompat = Program.Options.AudioPlayerMode switch
            {
                SDLAudioMode.Compatible => true,
                SDLAudioMode.Default => false,
                // Currently only mac os seems to need the compatible mode by default
                _ => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            };

            if (useCompat)
                return new QueuedStreamAudioTarget();
            else
                return new AudioPlayerTarget(); 
        }

        static OutStream? MakeVideoStream(bool hasVideo)
        {
            if (!hasVideo)
                return null;

            return new DecodeH264Target();
        }

        public void UseSyncManager(StreamSynchronizationHelper manager)
        {
            if (AudioTarget is AudioPlayerTarget)
            {
                ((AudioPlayerTarget)AudioTarget).SyncHelper = manager;
            }
        }

        public PlayerManager(StreamingSource source, CancellationTokenSource cancel) :
            base(source, MakeVideoStream(source.Options.HasVideo), MakeAudioStream(source.Options.HasAudio), cancel)
        {
            VideoTarget = base.VideoTarget as DecodeH264Target;
            AudioTarget = base.AudioTarget as BaseAudioPlayerTarget;
            IsCompatibleAudioStream = AudioTarget is QueuedStreamAudioTarget;
        }
    }

    class AudioPlayer : IDisposable
    {
        readonly uint DeviceID;
        
        // Are we using the MacOS strategy ?
        readonly bool IsCompatiblePlayer;
        
        // Keep a reference to the callback to prevent GC from collecting it
        SDL_AudioCallback? CallbackDelegate;

        // Manually pin the Target object so it can be used as opaque pointer for the native code
        GCHandle TargetHandle;

        public const ushort AudioFormat = AUDIO_S16LSB;

        public AudioPlayer(BaseAudioPlayerTarget target) 
        {
            // Relax thread check for UWP async initialization
            // Program.SdlCtx.BugCheckThreadId();

            IsCompatiblePlayer = target is QueuedStreamAudioTarget;

            SDL_AudioSpec wantedSpec = new SDL_AudioSpec()
            {
                channels = StreamInfo.AudioChannels,
                format = AudioFormat,
                freq = StreamInfo.AudioSampleRate,
                // StreamInfo.MinAudioSamplesPerPayload * 2 was the default until sysdvr 5.4
                // however SDL will pick its preferred buffer size since we pass SDL_AUDIO_ALLOW_SAMPLES_CHANGE,
                // this is fine since we have our own buffering.
                samples = StreamInfo.MinAudioSamplesPerPayload,
            };

            if (!IsCompatiblePlayer)
            {
                TargetHandle = GCHandle.Alloc(target, GCHandleType.Normal);
                CallbackDelegate = AudioStreamTargetNative.SDLCallback;
                wantedSpec.callback = CallbackDelegate;
                wantedSpec.userdata = GCHandle.ToIntPtr(TargetHandle);
            }

            DeviceID = SDL_OpenAudioDevice(IntPtr.Zero, 0, ref wantedSpec, out var obtained, (int)SDL_AUDIO_ALLOW_SAMPLES_CHANGE);

            DeviceID.AssertNotZero(SDL_GetError);

			Program.DebugLog($"SDL_Audio: requested samples per callback={wantedSpec.samples} obtained={obtained.samples}");

            if (IsCompatiblePlayer)
            {
                ((QueuedStreamAudioTarget)(target)).DeviceID = DeviceID;
            }
        }

        public void Pause() 
        {
            SDL_PauseAudioDevice(DeviceID, 1);
        }

        public void Resume() 
        {
            SDL_PauseAudioDevice(DeviceID, 0);
        }

        public void Dispose()
        {
            Pause();
            SDL_CloseAudioDevice(DeviceID);
            
            if (TargetHandle.IsAllocated)
                TargetHandle.Free();
        }
    }

    class VideoPlayer : IDisposable
    {
        public DecoderContext Decoder { get; private set; }
        public string DecoderName { get; private set; } = "h264";
        public bool UsingCustomDecoder { get; private set; }

        private unsafe SwsContext* _bgraConverter = null;
        private int _lastWidth = 0;
        private int _lastHeight = 0;
        private AVPixelFormat _lastFormat = AVPixelFormat.AV_PIX_FMT_NONE;

        private readonly unsafe byte*[] _dstData = new byte*[4];
        private readonly int[] _dstLinesizes = new int[4];
        private readonly unsafe byte*[] _srcData = new byte*[4];
        private readonly int[] _srcLinesizes = new int[4];

        public VideoPlayer(string? preferredDecoderName)
        {
            InitVideoDecoder(preferredDecoderName);
        }

        unsafe void InitVideoDecoder(string? name)
        {
            AVCodec* codec = null;

            if (name is not null)
            {
                codec = avcodec_find_decoder_by_name(name);
                
                if (codec != null)
                    UsingCustomDecoder = true;
            }

            if (codec == null)
                codec = avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);

            if (codec == null)
                throw new Exception("Couldn't find any compatible video codecs");

            Decoder = CreateDecoderContext(codec);
            DecoderName = Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "h264";
        }

        static unsafe DecoderContext CreateDecoderContext(AVCodec* codec)
        {
            if (codec == null)
                throw new Exception("Codec can't be null");

            string codecName = Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown";

            System.Diagnostics.Debug.WriteLine(string.Format(Program.Strings.Player.PlayerInitializationMessage, codecName));

            var codectx = avcodec_alloc_context3(codec);
            if (codectx == null)
                throw new Exception("Couldn't allocate a codec context");

            // These are set in ffplay
            codectx->codec_id = codec->id;
            codectx->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            codectx->bit_rate = 0;

            // Some decoders break without this
            codectx->width = StreamInfo.VideoWidth;
            codectx->height = StreamInfo.VideoHeight;

            var (ex, sz) = LibavUtils.AllocateH264Extradata();
            codectx->extradata_size = sz;
            codectx->extradata = (byte*)ex.ToPointer();

            avcodec_open2(codectx, codec, null).AssertZero("Couldn't open the codec.");

            var pic = av_frame_alloc();
            if (pic == null)
                throw new Exception("Couldn't allocate the decoding frame");

            var pic2 = av_frame_alloc();
            if (pic2 == null)
                throw new Exception("Couldn't allocate the decoding frame");

            return new DecoderContext()
            {
                CodecCtx = codectx,
                Frame1 = pic,
                Frame2 = pic2,
                ReceiveFrame = pic,
                RenderFrame = pic2,
                CodecLock = new object(),
                OnFrameEvent = new AutoResetEvent(true)
            };
        }

        public unsafe bool DecodeFrameToBuffer(byte* dstPixels, int dstStride)
        {
            int ret = 0;

            lock (Decoder.CodecLock)
                ret = avcodec_receive_frame(Decoder.CodecCtx, Decoder.ReceiveFrame);

            if (ret == AVERROR(EAGAIN))
            {
                return false;
            }
            else if (ret != 0)
            {
                Program.DebugLog($"avcodec_receive_frame {ret}");
                return false;
            }

            var frame = Decoder.ReceiveFrame;
            if (frame == null || frame->width <= 0 || frame->height <= 0)
            {
                Decoder.OnFrameEvent.Set();
                return false;
            }

            if (_bgraConverter == null || _lastWidth != frame->width || _lastHeight != frame->height || _lastFormat != Decoder.CodecCtx->pix_fmt)
            {
                if (_bgraConverter != null)
                {
                    sws_freeContext(_bgraConverter);
                    _bgraConverter = null;
                }

                _lastWidth = frame->width;
                _lastHeight = frame->height;
                _lastFormat = Decoder.CodecCtx->pix_fmt;

                _bgraConverter = sws_getContext(
                    frame->width, frame->height, Decoder.CodecCtx->pix_fmt,
                    frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    SWS_FAST_BILINEAR, null, null, null);
            }

            if (_bgraConverter != null)
            {
                _srcData[0] = frame->data[0];
                _srcData[1] = frame->data[1];
                _srcData[2] = frame->data[2];
                _srcData[3] = null;

                _srcLinesizes[0] = frame->linesize[0];
                _srcLinesizes[1] = frame->linesize[1];
                _srcLinesizes[2] = frame->linesize[2];
                _srcLinesizes[3] = 0;

                _dstData[0] = dstPixels;
                _dstData[1] = null;
                _dstData[2] = null;
                _dstData[3] = null;

                _dstLinesizes[0] = dstStride;
                _dstLinesizes[1] = 0;
                _dstLinesizes[2] = 0;
                _dstLinesizes[3] = 0;

                sws_scale(_bgraConverter, _srcData, _srcLinesizes, 0, frame->height, _dstData, _dstLinesizes);
            }

            Decoder.OnFrameEvent.Set();
            return true;
        }

        public unsafe void Dispose()
        {
            if (_bgraConverter != null)
            {
                sws_freeContext(_bgraConverter);
                _bgraConverter = null;
            }

            var ptr = Decoder.Frame1;
            if (ptr != null) av_frame_free(&ptr);

            var ptr2 = Decoder.Frame2;
            if (ptr2 != null) av_frame_free(&ptr2);

            var ptr3 = Decoder.CodecCtx;
            if (ptr3 != null) avcodec_free_context(&ptr3);

            Decoder.OnFrameEvent.Dispose();
        }
    }
}
