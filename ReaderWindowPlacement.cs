using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PdfReader
{
    internal sealed class ReaderWindowPlacement
    {
        private readonly Window _window;
        private Rect _bounds;
        private WindowState _state;
        private bool _captured, _changed;
        internal ReaderWindowPlacement(Window window) => _window = window;
        internal void Capture()
        {
            if (_window == null || _captured) return;
            _state = _window.WindowState;
            _bounds = _state == WindowState.Normal ? new Rect(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight) : _window.RestoreBounds;
            _captured = true;
        }
        internal void ShowWindowed()
        {
            if (!_captured || _window == null) return;
            var handle = new WindowInteropHelper(_window).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget == null) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var area = new Rect(transform.Transform(new Point(monitor.Work.Left, monitor.Work.Top)),
                transform.Transform(new Point(monitor.Work.Right, monitor.Work.Bottom)));
            _changed = true;
            Apply(new Rect(area.X + area.Width * .05, area.Y + area.Height * .05, area.Width * .9, area.Height * .9));
        }
        private void Apply(Rect bounds)
        {
            _window.WindowState = WindowState.Normal;
            _window.Left = bounds.X; _window.Top = bounds.Y;
            _window.Width = bounds.Width; _window.Height = bounds.Height;
        }
        internal void Restore()
        {
            if (_changed && !_bounds.IsEmpty && _bounds.Width > 0 && _bounds.Height > 0)
            { Apply(_bounds); _window.WindowState = _state; }
            _captured = _changed = false;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
