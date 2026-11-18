using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniFanControl
{
    public class DoubleBufferedPanel : Panel
    {
        public event EventHandler KeyboardActivated;

        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        public void EnableKeyboardActivation(string accessibleName, string accessibleDescription = null)
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            if (!string.IsNullOrEmpty(accessibleName)) AccessibleName = accessibleName;
            if (!string.IsNullOrEmpty(accessibleDescription)) AccessibleDescription = accessibleDescription;
            UpdateStyles();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && KeyboardActivated != null)
            {
                KeyboardActivated(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Focused && TabStop)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4)));
            }
        }
    }
}
