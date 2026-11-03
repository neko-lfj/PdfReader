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
        private readonly Action _close;
        private bool _nativeCaption;
        private WindowStyle _style;
        private ResizeMode _resize;
        private System.Windows.Shell.WindowChrome _chrome;

        private FrameworkElement _toolbar;
        private Thickness _toolbarMargin;
        private bool _windowed;
        internal ReaderWindowPlacement(Window window, Action close = null)
        { _window = window; _close = close; }
        internal void Capture()
        {
            if (_window == null || _captured) return;
            _state = _window.WindowState;
            _style = _window.WindowStyle; _resize = _window.ResizeMode;
            _chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(_window);
            _bounds = _state == WindowState.Normal ? new Rect(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight) : _window.RestoreBounds;
            _captured = true;
            _toolbar = _window.FindName("ViewboxFloatingBar") as FrameworkElement;
            if (_toolbar != null) _toolbarMargin = _toolbar.Margin;
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
            _windowed = true;
            ShowControls();
            _window.LayoutUpdated -= KeepToolbarVisible;
            _window.LayoutUpdated += KeepToolbarVisible;
            KeepToolbarVisible(null, EventArgs.Empty);
        }
        private void ShowControls()
        {
            if (_nativeCaption) return;
            if (_window.AllowsTransparency)
            {
                System.Windows.MessageBox.Show(_window, Strings.NativeCaptionRequiresRestart, Strings.PluginName);
                return;
            }
            _nativeCaption = true;
            System.Windows.Shell.WindowChrome.SetWindowChrome(_window, null);
            _window.ResizeMode = ResizeMode.CanResize;
            _window.WindowStyle = WindowStyle.SingleBorderWindow;
            _window.Closing += WindowClosing;
        }
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_windowed || _close == null) return;
            e.Cancel = true;
            _close();
        }
        private void KeepToolbarVisible(object sender, EventArgs e)
        {
            if (!_windowed || _toolbar == null || _toolbar.Visibility != Visibility.Visible
                || !(_toolbar.Parent is FrameworkElement parent) || parent.ActualWidth <= 0 || parent.ActualHeight <= 0) return;
            var margin = _toolbar.Margin;
            double x = Math.Max(0, (parent.ActualWidth - _toolbar.ActualWidth) / 2);
            double y = Math.Max(0, Math.Min(margin.Top, Math.Max(0, parent.ActualHeight - _toolbar.ActualHeight - 8)));
            if (Math.Abs(x - margin.Left) < .1 && Math.Abs(y - margin.Top) < .1) return;
            _toolbar.BeginAnimation(FrameworkElement.MarginProperty, null);
            _toolbar.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(x, y, margin.Right, margin.Bottom));
        }
        internal void HideWindowedControls()
        {
            _windowed = false;
            if (_window != null) _window.LayoutUpdated -= KeepToolbarVisible;
            RestoreCaption();
            if (_toolbar != null) _toolbar.SetCurrentValue(FrameworkElement.MarginProperty, _toolbarMargin);
        }
        private void RestoreCaption()
        {
            if (!_nativeCaption) return;
            _window.Closing -= WindowClosing;
            _window.WindowStyle = _style; _window.ResizeMode = _resize;
            System.Windows.Shell.WindowChrome.SetWindowChrome(_window, _chrome);
            _nativeCaption = false;
        }
        private void Apply(Rect bounds)
        {
            _window.WindowState = WindowState.Normal;
            _window.Left = bounds.X; _window.Top = bounds.Y;
            _window.Width = bounds.Width; _window.Height = bounds.Height;
        }
        internal void Restore()
        {
            HideWindowedControls();
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


