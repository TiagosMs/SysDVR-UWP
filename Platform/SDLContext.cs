using SDL2;
using SysDVR.Client.Core;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static SDL2.SDL;

namespace SysDVR.Client.Platform
{
    public enum GuiMessage
    {
        None,
        Other,
        Quit,
        Resize,
        KeyUp,
        KeyDown,
        FullScreen,
        BackButton
    }

    public class SDLContext
    {
        int SDLThreadId;

        internal IntPtr WindowHandle { get; private set; }
        internal IntPtr RendererHandle { get; private set; }

        public bool IsFullscreen { get; private set; } = false;
        public Vector2 WindowSize { get; private set; }

        internal bool AcceptControllerInput = true;
        internal bool DebugPrintSdlEvents = false;

        Vector2 PixelSize;
        Vector2 WantedDPIScale;
        bool IsWantedScale;
        bool CursorState = true;

        public void BugCheckThreadId()
        {
            if (SDLThreadId != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException($"SDL Bug check on thread {Thread.CurrentThread.ManagedThreadId} instead of {SDLThreadId}.");
        }

        public void SetNewThreadOwner()
        {
            SDLThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public SDLContext()
        {
            Debug.WriteLine("Initializing SDL Audio");

            SDL_SetMainReady();
            SDL_Init(SDL_INIT_AUDIO).AssertZero(SDL_GetError);

            SDLThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void CreateWindow(string? windowTitle)
        {
            // Video rendering in UWP is handled natively by XAML Image / WriteableBitmap.
            // SDL is used exclusively for audio on UWP to prevent swap chain conflicts with XAML CoreWindow.
            Debug.WriteLine("SDL CreateWindow: skipped on UWP (rendering via XAML)");
        }

        private bool UpdateSize()
        {
            var cur = WindowSize;
            SDL_GetWindowSize(WindowHandle, out int w, out int h);

            if (cur == new Vector2(w, h))
                return false;

            WindowSize = new(w, h);

            SDL_GetRendererOutputSize(RendererHandle, out int pixelWidth, out int pixelHeight);
            PixelSize = new(pixelWidth, pixelHeight);

            float dpiScaleX = pixelWidth / (float)w;
            float spiScaleY = pixelHeight / (float)h;
            WantedDPIScale = new(dpiScaleX, spiScaleY);

            IsWantedScale = SDL_RenderSetScale(RendererHandle, dpiScaleX, spiScaleY) == 0;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe string DebugSdlEvent(in SDL_Event evt)
        {
            StringBuilder sb = new();
            fixed (byte* data = evt.padding)
            {
                sb.Append($"SDL_Event: {evt.type} ");
                for (int i = 0; i < SDL_Event.SizeOf; i++)
                    sb.Append($"{data[i]:X2} ");
            }
            return sb.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GuiMessage PumpEvents(out SDL_Event evt)
        {
            if (SDL_PollEvent(out evt) == 0)
                return GuiMessage.None;

            if (evt.type == SDL_EventType.SDL_JOYBUTTONDOWN && !AcceptControllerInput)
                return GuiMessage.None;

            if (DebugPrintSdlEvents)
                Debug.WriteLine(DebugSdlEvent(evt));

            if (evt.type is SDL_EventType.SDL_KEYDOWN or SDL_EventType.SDL_KEYUP)
            {
                if (evt.key.keysym.sym == SDL_Keycode.SDLK_SELECT)
                {
                    evt.key.keysym.sym = SDL_Keycode.SDLK_RETURN;
                    evt.key.keysym.scancode = SDL_Scancode.SDL_SCANCODE_RETURN;
                }
            }

            if (evt.type == SDL_EventType.SDL_QUIT ||
                    (evt.type == SDL_EventType.SDL_WINDOWEVENT && evt.window.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_CLOSE))
            {
                return GuiMessage.Quit;
            }
            else if (evt.type == SDL_EventType.SDL_WINDOWEVENT && evt.window.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED)
            {
                return UpdateSize() ? GuiMessage.Resize : GuiMessage.Other;
            }
            else if (evt.type == SDL_EventType.SDL_KEYDOWN && evt.key.keysym.sym == SDL_Keycode.SDLK_F11)
            {
                SetFullScreen(!IsFullscreen);
                return GuiMessage.FullScreen;
            }
            else if (evt.type == SDL_EventType.SDL_KEYDOWN && evt.key.keysym.sym is SDL_Keycode.SDLK_ESCAPE or SDL_Keycode.SDLK_AC_BACK)
            {
                return GuiMessage.BackButton;
            }
            else if (evt.type == SDL_EventType.SDL_KEYDOWN)
            {
                return GuiMessage.KeyDown;
            }
            else if (evt.type == SDL_EventType.SDL_KEYUP)
            {
                return GuiMessage.KeyUp;
            }
            else if (evt.type == SDL_EventType.SDL_RENDER_DEVICE_RESET)
            {
                Debug.WriteLine("SDL failed to resume, terminating.");
                return GuiMessage.Quit;
            }
            else if (evt.type == SDL_EventType.SDL_JOYBUTTONDOWN && evt.jbutton.button == 1)
            {
                return GuiMessage.BackButton;
            }

            return GuiMessage.Other;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render()
        {
            if (RendererHandle != IntPtr.Zero)
                SDL_RenderPresent(RendererHandle);
        }

        public void DestroyWindow()
        {
            if (RendererHandle != IntPtr.Zero)
            {
                SDL_DestroyRenderer(RendererHandle);
                RendererHandle = IntPtr.Zero;
            }

            if (WindowHandle != IntPtr.Zero)
            {
                SDL_DestroyWindow(WindowHandle);
                WindowHandle = IntPtr.Zero;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearScreen()
        {
            if (RendererHandle != IntPtr.Zero)
            {
                SDL_SetRenderDrawColor(RendererHandle, 0x0, 0x0, 0x0, 0xFF);
                SDL_RenderClear(RendererHandle);
            }
        }

        public void SetFullScreen(bool enableFullScreen)
        {
            IsFullscreen = enableFullScreen;
            SDL_SetWindowFullscreen(WindowHandle, enableFullScreen ? (uint)SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0);
            ApplyCursorState();
        }

        public void ShowCursor(bool show)
        {
            if (show == CursorState)
                return;

            CursorState = show;
            ApplyCursorState();
        }

        private void ApplyCursorState()
        {
            if (IsFullscreen)
                SDL_ShowCursor(CursorState ? SDL_ENABLE : SDL_DISABLE);
            else
                SDL_ShowCursor(SDL_ENABLE);
        }

        public string GetDebugInfo()
        {
            StringBuilder sb = new();
            sb.AppendLine($"SDLThreadId: {SDLThreadId} Window Size: {WindowSize}");
            sb.AppendLine($"Pixel Size: {PixelSize} DPI Scale: {WantedDPIScale} ({IsWantedScale})");
            return sb.ToString();
        }
    }
}
