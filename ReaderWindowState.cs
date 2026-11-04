using System;

namespace PdfReader
{
    /// <summary>Owns the reader's temporary window state; repeated close is harmless.</summary>
    internal sealed class ReaderWindowState
    {
        private readonly Func<bool> _getFullscreen;
        private readonly Action<bool> _setFullscreen;
        private readonly Action _selectMouse;
        private bool _active;
        private bool _previousFullscreen;
        private readonly ReaderWindowPlacement _placement;

        internal ReaderWindowState(Func<bool> getFullscreen, Action<bool> setFullscreen, Action selectMouse,
            ReaderWindowPlacement placement = null)
        {
            _getFullscreen = getFullscreen;
            _setFullscreen = setFullscreen;
            _selectMouse = selectMouse;
            _placement = placement;
        }

        internal void Begin()
        {
            if (_active) return;
            _previousFullscreen = _getFullscreen();
            _placement?.Capture();
            _active = true;
        }

        internal void Show(bool fullscreen = true, bool maximizeWindowed = false)
        {
            if (!_active) return;
            _placement?.KeepReadingWindowNonTopmost();
            if (fullscreen) _placement?.HideWindowedControls();
            _setFullscreen(fullscreen);
            if (!fullscreen) _placement?.ShowWindowed(maximizeWindowed);
        }

        internal void ToggleFullscreen()
        {
            if (_active) Show(!_getFullscreen());
        }

        internal void End(bool selectMouse)
        {
            if (!_active) return;
            _active = false;
            try
            {
                _placement?.HideWindowedControls();
                _setFullscreen(_previousFullscreen);
            }
            finally
            {
                // Selecting the desktop tool can itself restore native window
                // placement. Restore our original geometry after that callback.
                try { if (selectMouse) _selectMouse(); }
                finally { _placement?.Restore(); }
            }
        }
    }
}
