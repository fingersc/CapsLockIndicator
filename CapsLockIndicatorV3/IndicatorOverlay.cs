/*
 * Created 09.07.2017 20:22
 * 
 * Copyright (c) Jonas Kohl <https://jonaskohl.de/>
 */
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockIndicatorV3
{
    /// <summary>
    /// A form that indicates if a keystate has changed.
    /// </summary>
    public partial class IndicatorOverlay : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern int SetLayeredWindowAttributes(IntPtr hWnd, int crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLong")]
        static extern int GetWindowLongInt(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, SetWindowPosFlags uFlags);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33
        }

        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        [Flags()]
        private enum SetWindowPosFlags : uint
        {
            AsynchronousWindowPosition = 0x4000,
            DeferErase = 0x2000,
            DrawFrame = 0x0020,
            FrameChanged = 0x0020,
            HideWindow = 0x0080,
            DoNotActivate = 0x0010,
            DoNotCopyBits = 0x0100,
            IgnoreMove = 0x0002,
            DoNotChangeOwnerZOrder = 0x0200,
            DoNotRedraw = 0x0008,
            DoNotReposition = 0x0200,
            DoNotSendChangingEvent = 0x0400,
            IgnoreResize = 0x0001,
            IgnoreZOrder = 0x0004,
            ShowWindow = 0x0040,
        }

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        static readonly IntPtr HWND_TOP = new IntPtr(0);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        public static class HWND
        {
            public static IntPtr
            NoTopMost = new IntPtr(-2),
            TopMost = new IntPtr(-1),
            Top = new IntPtr(0),
            Bottom = new IntPtr(1);
        }

        public static class SWP
        {
            public static readonly int
            NOSIZE = 0x0001,
            NOMOVE = 0x0002,
            NOZORDER = 0x0004,
            NOREDRAW = 0x0008,
            NOACTIVATE = 0x0010,
            DRAWFRAME = 0x0020,
            FRAMECHANGED = 0x0020,
            SHOWWINDOW = 0x0040,
            HIDEWINDOW = 0x0080,
            NOCOPYBITS = 0x0100,
            NOOWNERZORDER = 0x0200,
            NOREPOSITION = 0x0200,
            NOSENDCHANGING = 0x0400,
            DEFERERASE = 0x2000,
            ASYNCWINDOWPOS = 0x4000;
        }


        const uint WS_EX_LAYERED = 0x80000;
        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_EX_TRANSPARENT = 0x00000020;
        const uint LWA_ALPHA = 0x2;
        const uint LWA_COLORKEY = 0x1;
        const int GWL_EXSTYLE = -20;
        const int WM_NCHITTEST = 0x84;
        const int HTTRANSPARENT = -1;

        private Size originalSize;

        /// <summary>
        /// The key the currently displayed notification belongs to. If it isn't
        /// None, the key specific glyph is drawn next to the text.
        /// </summary>
        private IndicatorKey glyphKey = IndicatorKey.None;

        /// <summary>
        /// Whether the key belonging to the glyph is currently active.
        /// </summary>
        private bool glyphActive;

        /// <summary>
        /// Raised whenever the overlay hides itself because its display time elapsed.
        /// The main form uses this event to restore a previously shown persistent
        /// (always visible) overlay that was temporarily covered by a notification.
        /// </summary>
        public event EventHandler OverlayHidden;

        private IndicatorDisplayPosition pos = IndicatorDisplayPosition.BottomRight;

        const int WINDOW_MARGIN = 16;
        private double lastOpacity = 1;
        double opacity_timer_value = 2.0;

        Color BorderColour = Color.FromArgb(
            0xFF,
            0x34,
            0x4D,
            0xB4
        );
        int BorderSize = 4;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            fadeTimer.Stop();
            windowCloseTimer.Stop();
            positionUpdateTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= (int)(WS_EX_TOPMOST | WS_EX_TOOLWINDOW);
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
                m.Result = (IntPtr)HTTRANSPARENT;
            else
                base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            UpdatePosition();

            base.OnShown(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
        }

        void UpdatePosition()
        {
            Rectangle workingArea = Screen.GetWorkingArea(Cursor.Position);

            Size = new Size(Width, Height);

            switch (pos)
            {
                case IndicatorDisplayPosition.TopLeft:
                    Left = workingArea.X + WINDOW_MARGIN;
                    Top = workingArea.Y + WINDOW_MARGIN;
                    break;
                case IndicatorDisplayPosition.TopCenter:
                    Left = workingArea.X + (workingArea.Width / 2 - Width / 2);
                    Top = workingArea.Y + WINDOW_MARGIN;
                    break;
                case IndicatorDisplayPosition.TopRight:
                    Left = workingArea.X + workingArea.Left + (workingArea.Width - Width - WINDOW_MARGIN - workingArea.Left);
                    Top = workingArea.Y + WINDOW_MARGIN;
                    break;
                case IndicatorDisplayPosition.MiddleLeft:
                    Left = workingArea.X + WINDOW_MARGIN;
                    Top = workingArea.Y + (workingArea.Height / 2 - Height / 2);
                    break;
                case IndicatorDisplayPosition.MiddleCenter:
                    Left = workingArea.X + (workingArea.Width / 2 - Width / 2);
                    Top = workingArea.Y + (workingArea.Height / 2 - Height / 2);
                    break;
                case IndicatorDisplayPosition.MiddleRight:
                    Left = workingArea.X + workingArea.Left + (workingArea.Width - Width - WINDOW_MARGIN - workingArea.Left);
                    Top = workingArea.Y + (workingArea.Height / 2 - Height / 2);
                    break;
                case IndicatorDisplayPosition.BottomLeft:
                    Left = workingArea.X + WINDOW_MARGIN;
                    Top = workingArea.Y + workingArea.Top + (workingArea.Height - Height - WINDOW_MARGIN - workingArea.Top);
                    break;
                case IndicatorDisplayPosition.BottomCenter:
                    Left = workingArea.X + (workingArea.Width / 2 - Width / 2);
                    Top = workingArea.Y + workingArea.Top + (workingArea.Height - Height - WINDOW_MARGIN - workingArea.Top);
                    break;
                case IndicatorDisplayPosition.BottomRight:
                    Left = workingArea.X + workingArea.Left + (workingArea.Width - Width - WINDOW_MARGIN - workingArea.Left);
                    Top = workingArea.Y + workingArea.Top + (workingArea.Height - Height - WINDOW_MARGIN - workingArea.Top);
                    break;
                default:
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var factor = DPIHelper.GetScalingFactorPercent(Handle);

            if (BorderSize > 0)
                e.Graphics.DrawRectangle(new Pen(BorderColour, (int)(BorderSize * factor)), e.ClipRectangle);

            //using (var sf = new StringFormat() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            //using (var b = new SolidBrush(contentLabel.ForeColor))
            //    e.Graphics.DrawString(contentLabel.Text, contentLabel.Font, b, new Rectangle(
            //        Padding.Left,
            //        Padding.Top,
            //        Width - Padding.Horizontal,
            //        Height - Padding.Vertical
            //    ), sf);

            var textRect = new Rectangle(
                Padding.Left,
                Padding.Top,
                Width - Padding.Horizontal,
                Height - Padding.Vertical);

            if (textRect.Width <= 0 || textRect.Height <= 0)
                return;

            // The key specific glyph (caps lock: "A", num lock: "5", scroll
            // lock: down arrow) is drawn to the right of the text. If the key
            // is inactive, the glyph is crossed out. Text and glyph are laid
            // out as one group, so that the glyph always sits right next to
            // the text and the whole group stays centered.
            var hasGlyph = KeyStateGlyph.IsSupported(glyphKey);
            var boxSize = hasGlyph ? KeyStateGlyph.GetBoxSize(contentLabel.Font) : 0;
            var gap = hasGlyph ? (int)Math.Round(boxSize * 0.3) : 0;

            var textSize = TextRenderer.MeasureText(contentLabel.Text, contentLabel.Font);
            var groupWidth = textSize.Width + gap + boxSize;
            var groupLeft = textRect.Left + Math.Max(0, (textRect.Width - groupWidth) / 2);

            if (hasGlyph)
            {
                var glyphRect = new Rectangle(
                    groupLeft + textSize.Width + gap,
                    textRect.Top + (textRect.Height - boxSize) / 2,
                    boxSize,
                    boxSize);

                KeyStateGlyph.Draw(e.Graphics, glyphKey, glyphActive, glyphRect, ForeColor);
            }

            // The extra pixels on both sides prevent the text from being
            // clipped, without moving it (the center stays the same).
            TextRenderer.DrawText(e.Graphics, contentLabel.Text, contentLabel.Font, new Rectangle(
                groupLeft - 1,
                textRect.Top,
                textSize.Width + 2,
                textRect.Height
            ), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private int ClickThroughWindow(double opacity = 1d)
        {
            if (IsDisposed)
                return -1;

            uint windowLong = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong32(Handle, GWL_EXSTYLE, windowLong | WS_EX_LAYERED);
            SetLayeredWindowAttributes(Handle, 0, (byte)(opacity * 255), LWA_ALPHA);

            SetWindowStyles();

            return 0;
        }

        private void SetWindowStyles()
        {
            var style = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST);
        }

        public IndicatorOverlay(string content)
        {
            InitializeComponent();
            InitializeCommon();
        }

        private void InitializeCommon()
        {
            // Doesn't look very nice and is Win11 only
            //var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
            //var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            //DwmSetWindowAttribute(Handle, attribute, ref preference, sizeof(uint));
        }

        public IndicatorOverlay(IndicatorTrigger trigger, int timeoutInMs, IndicatorDisplayPosition position)
        {
            pos = position;

            if (IsDisposed)
                return;

            InitializeComponent();
            InitializeCommon();
            ReapplyPadding();

            if (IsDisposed)
                return;

            contentLabel.Text = trigger.DisplayText;
            SetGlyph(trigger);
            originalSize = Size;

            if (timeoutInMs < 0 || IndicatorTrigger.ShowInfinite(trigger))
            {
                windowCloseTimer.Enabled = false;
                fadeTimer.Enabled = false;
            }
            else
            {
                windowCloseTimer.Interval = timeoutInMs;
                fadeTimer.Interval = (int)Math.Floor((decimal)(timeoutInMs / 20));
            }
            ClickThroughWindow();
            RecalculateSize();
            BringToFrontNoFocus();
        }

        public IndicatorOverlay(IndicatorTrigger trigger, int timeoutInMs, Color bgColour, Color fgColour, Color bdColour, int bdSize, Font font, IndicatorDisplayPosition position, int indOpacity, bool alwaysShow)
        {
            var ret = 0;

            pos = position;

            InitializeComponent();
            InitializeCommon();


            contentLabel.Text = trigger.DisplayText;
            SetGlyph(trigger);
            Font = font;
            originalSize = Size;

            ReapplyPadding();
            RecalculateSize();

            var op = indOpacity / 100d;
            lastOpacity = op;
            ret |= SetOpacity(op);
            if (timeoutInMs < 0 || alwaysShow || IndicatorTrigger.ShowInfinite(trigger))
            {
                windowCloseTimer.Enabled = false;
                fadeTimer.Enabled = false;
            }
            else
            {
                windowCloseTimer.Interval = timeoutInMs;
                fadeTimer.Interval = (int)Math.Floor((decimal)(timeoutInMs / 20));
            }
            BackColor = bgColour;
            ForeColor = fgColour;
            BorderColour = bdColour;
            BorderSize = bdSize;
            ret |= ClickThroughWindow(op);
            if (ret != -1)
                Show();

            BringToFrontNoFocus();
        }

        private int SetOpacity(double op)
        {
            if (IsDisposed)
                return -1;

            byte opb = 0xFF;

            try
            {
                opb = (byte)Math.Min(255, Math.Max(op * 0xFF, 0));
            }
            catch (OverflowException) { }
            SetLayeredWindowAttributes(Handle, 0, opb, LWA_ALPHA);
            return 0;
        }

        public void UpdateIndicator(IndicatorTrigger trigger, IndicatorDisplayPosition position)
        {
            pos = position;
            Opacity = 1;
            contentLabel.Text = trigger.DisplayText;
            SetGlyph(trigger);
            opacity_timer_value = 2.0;
            windowCloseTimer.Stop();
            windowCloseTimer.Start();
            fadeTimer.Stop();
            fadeTimer.Start();
            ReapplyPadding();
            Show();
            RecalculateSize();
            UpdatePosition();
            SetWindowStyles();
            BringToFrontNoFocus();
        }

        public void UpdateIndicator(IndicatorTrigger trigger, int timeoutInMs, IndicatorDisplayPosition position)
        {
            pos = position;
            Opacity = 1;
            contentLabel.Text = trigger.DisplayText;
            SetGlyph(trigger);
            opacity_timer_value = 2.0;
            if (timeoutInMs < 0 || IndicatorTrigger.ShowInfinite(trigger))
            {
                windowCloseTimer.Enabled = false;
                fadeTimer.Enabled = false;
            }
            else
            {
                windowCloseTimer.Stop();
                windowCloseTimer.Interval = timeoutInMs;
                windowCloseTimer.Start();
                fadeTimer.Stop();
                fadeTimer.Interval = (int)Math.Floor((decimal)(timeoutInMs / 20));
                fadeTimer.Start();
            }
            ReapplyPadding();
            Show();
            RecalculateSize();
            UpdatePosition();
            SetWindowStyles();
            BringToFrontNoFocus();
        }


        public void UpdateIndicator(IndicatorTrigger trigger, int timeoutInMs, Color bgColour, Color fgColour, Color bdColour, int bdSize, Font font, IndicatorDisplayPosition position, int indOpacity, bool alwaysShow)
        {
            pos = position;
            var op = indOpacity / 100d;
            lastOpacity = op;
            SetOpacity(op);
            contentLabel.Text = trigger.DisplayText;
            SetGlyph(trigger);
            Font = font;
            opacity_timer_value = 2.0;
            if (timeoutInMs < 0 || alwaysShow || IndicatorTrigger.ShowInfinite(trigger))
            {
                windowCloseTimer.Enabled = false;
                fadeTimer.Enabled = false;
            }
            else
            {
                windowCloseTimer.Stop();
                windowCloseTimer.Interval = timeoutInMs;
                windowCloseTimer.Start();
                fadeTimer.Stop();
                fadeTimer.Interval = (int)Math.Floor((decimal)(timeoutInMs / 20));
                fadeTimer.Start();
            }
            BackColor = bgColour;
            ForeColor = fgColour;
            BorderColour = bdColour;
            BorderSize = bdSize;
            ReapplyPadding();
            Show();
            RecalculateSize();
            Invalidate();
            UpdatePosition();
            SetWindowStyles();
            BringToFrontNoFocus();
        }

        void WindowCloseTimerTick(object sender, EventArgs e)
        {
            // The timer is repeating, so it has to be stopped explicitly.
            // Otherwise the overlay would be hidden over and over again and the
            // OverlayHidden event would be raised multiple times.
            windowCloseTimer.Stop();
            fadeTimer.Stop();
            Hide();

            var handler = OverlayHidden;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Hides the overlay without closing or disposing it, so that the instance
        /// can be reused later on.
        /// </summary>
        public void HideOverlay()
        {
            windowCloseTimer.Stop();
            fadeTimer.Stop();
            Hide();
        }

        private void fadeTimer_Tick(object sender, EventArgs e)
        {
            if (opacity_timer_value > 0)
            {
                opacity_timer_value -= 0.1;
                opacity_timer_value = Math.Max(opacity_timer_value, 0);
                if (opacity_timer_value <= 1.0)
                    SetOpacity(opacity_timer_value * lastOpacity);
            }
        }

        private void positionUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdatePosition();
            SetWindowStyles();
        }

        /// <summary>
        /// Remembers the key the given notification belongs to, so that the
        /// key specific glyph can be drawn next to the text. Notifications
        /// without a key (informational messages) don't get a glyph.
        /// </summary>
        private void SetGlyph(IndicatorTrigger trigger)
        {
            glyphKey = trigger.Key;
            glyphActive = trigger.NewState;
        }

        void RecalculateSize()
        {
            var textSize = TextRenderer.MeasureText(contentLabel.Text, contentLabel.Font);
            var width = textSize.Width;
            var height = textSize.Height;

            // Make room for the key specific glyph on the right of the text
            if (KeyStateGlyph.IsSupported(glyphKey))
            {
                var boxSize = KeyStateGlyph.GetBoxSize(contentLabel.Font);
                var gap = (int)Math.Round(boxSize * 0.3);

                width += boxSize + gap;
                height = Math.Max(height, boxSize);
            }

            width += Padding.Horizontal;
            height += Padding.Vertical;

            Size = new Size(width, height);
        }
        void BringToFrontNoFocus()
        {
            SetWindowPos(Handle, HWND_TOP, 0, 0, 0, 0, SetWindowPosFlags.DoNotActivate | SetWindowPosFlags.IgnoreResize | SetWindowPosFlags.IgnoreMove);
        }
        void ReapplyPadding()
        {
            Padding = SettingsManager.Get<Padding>("indPadding");
        }
    }
}
