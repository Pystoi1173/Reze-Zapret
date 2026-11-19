using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal static class WorkspaceStyle
{
    public static readonly Color Canvas = Color.FromArgb(11, 13, 25);
    public static readonly Color Rail = Color.FromArgb(15, 17, 32);
    public static readonly Color Card = Color.FromArgb(22, 25, 43);
    public static readonly Color Raised = Color.FromArgb(32, 37, 61);
    public static readonly Color Field = Color.FromArgb(14, 17, 31);
    public static readonly Color Line = Color.FromArgb(49, 54, 81);
    public static readonly Color Text = Color.FromArgb(246, 246, 255);
    public static readonly Color Muted = Color.FromArgb(169, 177, 207);
    public static readonly Color Blue = Color.FromArgb(166, 173, 255);
    public static readonly Color Ice = Color.FromArgb(221, 226, 255);
    public static readonly Color Mint = Color.FromArgb(143, 226, 199);
    public static readonly Color Lavender = Color.FromArgb(209, 176, 250);
    public static Color Highlight { get; private set; }

    static WorkspaceStyle() { Highlight = Blue; }

    public static void SetAccent(string name)
    {
        Highlight = name == "lavender" ? Lavender : name == "mint" ? Mint : Blue;
    }

    public static Color Mix(Color from, Color to, float amount)
    {
        return Color.FromArgb((int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    }

    public static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Glyph(Graphics graphics, string name, int x, int y, Color color, float scale)
    {
        GraphicsState previous = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(scale, scale);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            switch (name)
            {
                case "profiles":
                    graphics.DrawRectangle(pen, 2, 3, 13, 11);
                    graphics.DrawLine(pen, 2, 7, 15, 7);
                    graphics.DrawLine(pen, 6, 7, 6, 14);
                    break;
                case "domains":
                    graphics.DrawEllipse(pen, 2, 2, 13, 13);
                    graphics.DrawEllipse(pen, 6, 2, 5, 13);
                    graphics.DrawLine(pen, 2, 8.5f, 15, 8.5f);
                    break;
                case "journal":
                    graphics.DrawRectangle(pen, 3, 2, 11, 13);
                    graphics.DrawLine(pen, 6, 6, 11, 6);
                    graphics.DrawLine(pen, 6, 10, 11, 10);
                    break;
                case "tests":
                    graphics.DrawLines(pen, new[] { new Point(1, 9), new Point(5, 9), new Point(7, 3),
                        new Point(10, 14), new Point(12, 8), new Point(16, 8) });
                    break;
                case "updates":
                    graphics.DrawArc(pen, 2, 2, 13, 13, 35, 294);
                    graphics.DrawLines(pen, new[] { new Point(10, 5), new Point(15, 5), new Point(15, 1) });
                    break;
                case "home":
                    graphics.DrawLines(pen, new[] { new Point(2, 8), new Point(8, 2), new Point(14, 8), new Point(14, 15), new Point(3, 15), new Point(3, 8) });
                    graphics.DrawLine(pen, 7, 15, 7, 10);
                    break;
                case "settings":
                    graphics.DrawEllipse(pen, 5, 5, 7, 7);
                    graphics.DrawLine(pen, 8, 1, 8, 4);
                    graphics.DrawLine(pen, 8, 13, 8, 16);
                    graphics.DrawLine(pen, 1, 8, 4, 8);
                    graphics.DrawLine(pen, 12, 8, 16, 8);
                    break;
                case "folder":
                    graphics.DrawLines(pen, new[] { new Point(2, 13), new Point(2, 4), new Point(7, 4),
                        new Point(9, 6), new Point(15, 6), new Point(15, 13), new Point(2, 13) });
                    break;
                case "search":
                    graphics.DrawEllipse(pen, 2, 2, 9, 9);
                    graphics.DrawLine(pen, 10, 10, 15, 15);
                    break;
                case "star":
                case "star-filled":
                    Point[] star = { new Point(8, 1), new Point(10, 6), new Point(16, 6), new Point(12, 10),
                        new Point(13, 16), new Point(8, 13), new Point(3, 16), new Point(4, 10), new Point(0, 6), new Point(6, 6) };
                    if (name == "star-filled") using (var fill = new SolidBrush(color)) graphics.FillPolygon(fill, star);
                    graphics.DrawPolygon(pen, star);
                    break;
                case "arrow":
                    graphics.DrawLines(pen, new[] { new Point(6, 4), new Point(11, 8), new Point(6, 12) });
                    break;
                case "play":
                    graphics.DrawPolygon(pen, new[] { new Point(5, 3), new Point(13, 8), new Point(5, 13) });
                    break;
            }
        }
        graphics.Restore(previous);
    }
}

internal sealed class WorkspaceCheckBox : CheckBox
{
    private bool hovered;

    public WorkspaceCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = e.Graphics.DpiX / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? WorkspaceStyle.Card : Parent.BackColor);
        int side = (int)(15 * scale);
        var box = new Rectangle(1, Math.Max(0, (Height - side) / 2), side, side);
        Color accent = WorkspaceStyle.Highlight;
        Color border = Enabled ? (hovered || Focused ? accent : WorkspaceStyle.Line) : WorkspaceStyle.Line;
        Color fill = Checked ? (Enabled ? accent : WorkspaceStyle.Raised) : WorkspaceStyle.Field;
        using (var path = WorkspaceStyle.Rounded(box, 4 * scale))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border, Focused ? 1.5f : 1f))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }
        if (Checked)
        {
            using (var pen = new Pen(Enabled ? WorkspaceStyle.Canvas : WorkspaceStyle.Muted, 1.7f * scale))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[] { new PointF(box.Left + 3 * scale, box.Top + 8 * scale),
                    new PointF(box.Left + 6.5f * scale, box.Top + 11 * scale), new PointF(box.Left + 12 * scale, box.Top + 4 * scale) });
            }
        }
        Color text = Enabled ? WorkspaceStyle.Text : WorkspaceStyle.Muted;
        var textBox = new Rectangle(box.Right + (int)(9 * scale), 0, Math.Max(0, Width - box.Right - (int)(9 * scale)), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBox, text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal class WorkspaceTextBox : TextBox
{
    private const int WsVScroll = 0x00200000;
    private const int WmMouseWheel = 0x020A;
    private const int WmVScroll = 0x0115;
    private const int EmGetLineCount = 0x00BA;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;
    private const int EmSetRectNp = 0x00B4;
    private readonly WorkspaceScrollBar scrollBar = new WorkspaceScrollBar();
    private bool syncingScroll;
    private bool updatingLayout;
    private bool updatingScrollBar;
    private bool layoutCustom;
    private int layoutWidth = -1;
    private int layoutHeight = -1;
    private int layoutReserved = -1;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style &= ~WsVScroll;
            return parameters;
        }
    }

    public WorkspaceTextBox()
    {
        BackColor = WorkspaceStyle.Field;
        ForeColor = WorkspaceStyle.Text;
        BorderStyle = BorderStyle.FixedSingle;
        scrollBar.Visible = false;
        scrollBar.ValueChanged += delegate
        {
            if (syncingScroll || !IsHandleCreated) return;
            ScrollTo(scrollBar.Value);
        };
        Controls.Add(scrollBar);
        scrollBar.BringToFront();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowTheme(Handle, "DarkMode_Explorer", null);
        UpdateTextLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateTextLayout();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTextLayout();
    }

    protected override void OnMultilineChanged(EventArgs e)
    {
        base.OnMultilineChanged(e);
        UpdateTextLayout();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        UpdateScrollBar();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateTextLayout();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseWheel && Multiline && ScrollBars != ScrollBars.None)
        {
            int delta = (short)(message.WParam.ToInt64() >> 16);
            int step = Math.Max(1, Math.Abs(delta) / 120 * 3);
            int current = SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
            ScrollTo(current - Math.Sign(delta) * step);
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == WmVScroll) UpdateScrollBar();
    }

    private void UpdateTextLayout()
    {
        if (!IsHandleCreated || IsDisposed || updatingLayout) return;
        bool custom = Multiline && ScrollBars != ScrollBars.None;
        int reserved = custom ? Math.Max(14, (int)Math.Round(14 * (DeviceDpi / 96f))) : 0;
        if (!custom)
        {
            scrollBar.Visible = false;
            if (layoutCustom || layoutWidth != Width || layoutHeight != Height || layoutReserved != 0)
            {
                var fullRect = new EditRect { Left = 3, Top = 2, Right = Math.Max(4, Width - 3), Bottom = Math.Max(3, Height - 3) };
                SendMessage(Handle, EmSetRectNp, IntPtr.Zero, ref fullRect);
                layoutCustom = false;
                layoutWidth = Width;
                layoutHeight = Height;
                layoutReserved = 0;
            }
            return;
        }
        updatingLayout = true;
        try
        {
            Rectangle bounds = new Rectangle(Math.Max(0, Width - reserved), 2, reserved, Math.Max(0, Height - 4));
            if (!layoutCustom || layoutWidth != Width || layoutHeight != Height || layoutReserved != reserved)
            {
                layoutCustom = true;
                layoutWidth = Width;
                layoutHeight = Height;
                layoutReserved = reserved;
                scrollBar.Bounds = bounds;
                var rect = new EditRect { Left = 3, Top = 2, Right = Math.Max(4, Width - reserved - 1), Bottom = Math.Max(3, Height - 3) };
                SendMessage(Handle, EmSetRectNp, IntPtr.Zero, ref rect);
            }
            scrollBar.BringToFront();
        }
        finally { updatingLayout = false; }
        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        if (updatingScrollBar) return;
        if (!IsHandleCreated || IsDisposed || !Multiline || ScrollBars == ScrollBars.None)
        {
            scrollBar.Visible = false;
            return;
        }
        updatingScrollBar = true;
        try
        {
            int totalLines = Math.Max(1, SendMessage(Handle, EmGetLineCount, IntPtr.Zero, IntPtr.Zero).ToInt32());
            int lineHeight = Math.Max(1, Font.Height);
            int visibleLines = Math.Max(1, (ClientSize.Height - 4) / lineHeight);
            int maximum = Math.Max(0, totalLines - visibleLines);
            if (maximum == 0)
            {
                scrollBar.Visible = false;
                return;
            }
            int position = Math.Max(0, Math.Min(maximum,
                SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32()));
            syncingScroll = true;
            scrollBar.LargeChange = visibleLines;
            scrollBar.Maximum = maximum;
            scrollBar.Value = position;
            syncingScroll = false;
            scrollBar.Visible = true;
            scrollBar.BringToFront();
        }
        finally { updatingScrollBar = false; }
    }

    private void ScrollTo(int target)
    {
        int current = SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (target != current)
            SendMessage(Handle, EmLineScroll, IntPtr.Zero, new IntPtr(target - current));
        UpdateScrollBar();
    }

    [StructLayout(LayoutKind.Sequential)] private struct EditRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref EditRect rect);
}

internal sealed class WorkspaceComboBox : ComboBox
{
    private ComboArrowOverlay arrowOverlay;

    public WorkspaceComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 24;
        BackColor = WorkspaceStyle.Field;
        ForeColor = WorkspaceStyle.Text;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowTheme(Handle, "", "");
        if (arrowOverlay == null)
        {
            arrowOverlay = new ComboArrowOverlay(delegate { if (Enabled) DroppedDown = true; });
            Controls.Add(arrowOverlay);
        }
        UpdateArrowBounds();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateArrowBounds();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (arrowOverlay != null) arrowOverlay.Invalidate();
    }

    private void UpdateArrowBounds()
    {
        if (arrowOverlay == null) return;
        arrowOverlay.Bounds = new Rectangle(Math.Max(0, Width - 29), 1, 28, Math.Max(0, Height - 2));
        arrowOverlay.BringToFront();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using (var fill = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? WorkspaceStyle.Raised : WorkspaceStyle.Field))
            e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
            Rectangle.Inflate(e.Bounds, -8, 0), WorkspaceStyle.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);
}

internal sealed class ComboArrowOverlay : Control
{
    private readonly Action open;
    private bool hovered;

    public ComboArrowOverlay(Action openAction)
    {
        open = openAction;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); open(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(WorkspaceStyle.Field);
        using (var line = new Pen(WorkspaceStyle.Line)) e.Graphics.DrawLine(line, 0, 3, 0, Height - 4);
        Color arrow = hovered ? WorkspaceStyle.Text : WorkspaceStyle.Muted;
        using (var pen = new Pen(arrow, 1.5f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            float x = Width / 2f;
            float y = Height / 2f;
            e.Graphics.DrawLine(pen, x - 4, y - 2, x, y + 2);
            e.Graphics.DrawLine(pen, x, y + 2, x + 4, y - 2);
        }
    }
}


internal sealed class WorkspaceScrollBar : Control
{
    private int minimum;
    private int maximum;
    private int largeChange = 1;
    private int value;
    private bool hovered;
    private bool dragging;
    private int dragOffset;
    private float DpiScale { get { return DeviceDpi / 96f; } }

    public event EventHandler ValueChanged;

    public int Minimum
    {
        get { return minimum; }
        set
        {
            int next = Math.Max(0, value);
            if (minimum == next) return;
            minimum = next;
            Value = this.value;
            Invalidate();
        }
    }
    public int Maximum
    {
        get { return maximum; }
        set
        {
            int next = Math.Max(this.minimum, value);
            if (maximum == next) return;
            maximum = next;
            Value = this.value;
            Invalidate();
        }
    }
    public int LargeChange
    {
        get { return largeChange; }
        set
        {
            int next = Math.Max(1, value);
            if (largeChange == next) return;
            largeChange = next;
            Invalidate();
        }
    }
    public int Value
    {
        get { return value; }
        set
        {
            int next = Math.Max(Minimum, Math.Min(Maximum, value));
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    public WorkspaceScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Width = 10;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Rectangle thumb = ThumbBounds();
        if (thumb.Contains(e.Location))
        {
            dragging = true;
            Capture = true;
            dragOffset = e.Y - thumb.Top;
        }
        else
        {
            Value += e.Y < thumb.Top ? -LargeChange : LargeChange;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            int trackTop = TrackTop();
            int travel = Math.Max(1, TrackHeight() - ThumbBounds().Height);
            int position = Math.Max(0, Math.Min(travel, e.Y - trackTop - dragOffset));
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * position / (double)travel);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e.Delta != 0)
        {
            Value += e.Delta > 0 ? -Math.Max(1, LargeChange / 2) : Math.Max(1, LargeChange / 2);
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) Value--;
        else if (e.KeyCode == Keys.Down) Value++;
        else if (e.KeyCode == Keys.PageUp) Value -= LargeChange;
        else if (e.KeyCode == Keys.PageDown) Value += LargeChange;
        else if (e.KeyCode == Keys.Home) Value = Minimum;
        else if (e.KeyCode == Keys.End) Value = Maximum;
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DpiScale;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? WorkspaceStyle.Field : Parent.BackColor);
        int trackWidth = Math.Max(4, (int)(5 * scale));
        int trackLeft = (Width - trackWidth) / 2;
        int trackTop = TrackTop();
        int trackHeight = TrackHeight();
        using (var track = WorkspaceStyle.Rounded(new RectangleF(trackLeft, trackTop, trackWidth, Math.Max(1, trackHeight)), trackWidth / 2f))
        using (var trackBrush = new SolidBrush(WorkspaceStyle.Mix(WorkspaceStyle.Field, WorkspaceStyle.Line, 0.72f)))
            e.Graphics.FillPath(trackBrush, track);
        Rectangle thumb = ThumbBounds();
        using (var thumbPath = WorkspaceStyle.Rounded(thumb, Math.Max(2, thumb.Width / 2f)))
        using (var thumbBrush = new SolidBrush(hovered || dragging ? WorkspaceStyle.Highlight : WorkspaceStyle.Mix(WorkspaceStyle.Line, WorkspaceStyle.Highlight, 0.38f)))
            e.Graphics.FillPath(thumbBrush, thumbPath);
    }

    private int TrackTop() { return Math.Max(3, (int)Math.Round(4 * DpiScale)); }
    private int TrackHeight() { return Math.Max(1, Height - TrackTop() * 2); }

    private Rectangle ThumbBounds()
    {
        int trackHeight = TrackHeight();
        int thumbHeight = Math.Max((int)Math.Round(30 * DpiScale), trackHeight * LargeChange / Math.Max(1, Maximum + LargeChange));
        int travel = Math.Max(0, trackHeight - thumbHeight);
        int top = TrackTop() + (Maximum <= Minimum ? 0 : travel * (Value - Minimum) / Math.Max(1, Maximum - Minimum));
        int side = Math.Max(6, (int)Math.Round(6 * DpiScale));
        return new Rectangle(Math.Max(1, (Width - side) / 2), top, side, thumbHeight);
    }
}

internal sealed class WorkspaceListBox : ListBox
{
    private const int WsVScroll = 0x00200000;
    private const int WmMouseWheel = 0x020A;
    private readonly WorkspaceScrollBar scrollBar = new WorkspaceScrollBar();
    private bool syncingScroll;
    private int hoveredIndex = -1;
    private float DpiScale { get { return DeviceDpi / 96f; } }

    public int HoveredIndex { get { return hoveredIndex; } }
    public int TextRightInset { get { return scrollBar.Visible ? ScrollBarWidth + (int)Math.Round(5 * DpiScale) : 0; } }
    private int ScrollBarWidth { get { return Math.Max(12, (int)Math.Round(12 * DpiScale)); } }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index != hoveredIndex)
        {
            int previous = hoveredIndex;
            hoveredIndex = index;
            if (previous >= 0) Invalidate(GetItemRectangle(previous));
            if (hoveredIndex >= 0) Invalidate(GetItemRectangle(hoveredIndex));
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        int previous = hoveredIndex;
        hoveredIndex = -1;
        if (previous >= 0) Invalidate(GetItemRectangle(previous));
        base.OnMouseLeave(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style &= ~WsVScroll;
            return parameters;
        }
    }

    public WorkspaceListBox()
    {
        scrollBar.Visible = false;
        scrollBar.ValueChanged += delegate
        {
            if (syncingScroll) return;
            TopIndex = scrollBar.Value;
        };
        Controls.Add(scrollBar);
        scrollBar.BringToFront();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int width = ScrollBarWidth;
        Rectangle bounds = new Rectangle(Math.Max(0, Width - width), 2, width, Math.Max(0, Height - 4));
        if (scrollBar.Bounds != bounds) scrollBar.Bounds = bounds;
        scrollBar.BringToFront();
        UpdateScrollBar();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScrollBar();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateScrollBar();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        UpdateScrollBar();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        UpdateScrollBar();
    }

    public void RefreshScrollBar()
    {
        UpdateScrollBar();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseWheel)
        {
            int delta = (short)(message.WParam.ToInt64() >> 16);
            int step = Math.Max(1, Math.Abs(delta) / 120 * 3);
            TopIndex = Math.Max(0, TopIndex - Math.Sign(delta) * step);
            UpdateScrollBar();
            return;
        }
        base.WndProc(ref message);
    }

    private void UpdateScrollBar()
    {
        if (scrollBar == null || IsDisposed) return;
        int itemHeight = Math.Max(1, ItemHeight);
        int visible = Math.Max(1, ClientSize.Height / itemHeight);
        int maximum = Math.Max(0, Items.Count - visible);
        syncingScroll = true;
        scrollBar.LargeChange = visible;
        scrollBar.Maximum = maximum;
        scrollBar.Value = Math.Max(0, Math.Min(maximum, TopIndex));
        syncingScroll = false;
        scrollBar.Visible = maximum > 0;
        if (scrollBar.Visible) scrollBar.BringToFront();
    }
}

internal sealed class WorkspaceScalePicker : Control
{
    private int minimum = 80, maximum = 140, value = 100;
    private bool dragging;
    private bool pendingNotification;
    public event EventHandler ValueChanged;
    public int Minimum { get { return minimum; } set { minimum = value; this.value = Math.Max(minimum, this.value); Invalidate(); } }
    public int Maximum { get { return maximum; } set { maximum = value; this.value = Math.Min(maximum, this.value); Invalidate(); } }
    public int Value
    {
        get { return value; }
        set
        {
            int next = Math.Max(Minimum, Math.Min(Maximum, value));
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    public WorkspaceScalePicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        Height = 34;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = e.Graphics.DpiX / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? WorkspaceStyle.Card : Parent.BackColor);
        int left, right;
        GetTrackBounds(scale, out left, out right);
        int y = Height / 2;
        using (var track = WorkspaceStyle.Rounded(new RectangleF(left, y - 2 * scale, right - left, 4 * scale), 2 * scale))
        using (var fill = WorkspaceStyle.Rounded(new RectangleF(left, y - 2 * scale, Math.Max(1, (right - left) * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum)), 4 * scale), 2 * scale))
        using (var baseBrush = new SolidBrush(WorkspaceStyle.Mix(WorkspaceStyle.Field, WorkspaceStyle.Line, 0.8f)))
        using (var accentBrush = new SolidBrush(WorkspaceStyle.Mix(WorkspaceStyle.Highlight, WorkspaceStyle.Ice, 0.18f)))
        {
            e.Graphics.FillPath(baseBrush, track);
            e.Graphics.FillPath(accentBrush, fill);
        }
        float position = left + (right - left) * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
        using (var halo = new SolidBrush(Color.FromArgb(28, WorkspaceStyle.Highlight)))
        using (var knob = new SolidBrush(WorkspaceStyle.Highlight))
        using (var inner = new SolidBrush(WorkspaceStyle.Ice))
        using (var outline = new Pen(WorkspaceStyle.Mix(WorkspaceStyle.Highlight, WorkspaceStyle.Ice, 0.35f), 1f))
        {
            e.Graphics.FillEllipse(halo, position - 9 * scale, y - 9 * scale, 18 * scale, 18 * scale);
            e.Graphics.FillEllipse(knob, position - 6 * scale, y - 6 * scale, 12 * scale, 12 * scale);
            e.Graphics.FillEllipse(inner, position - 3 * scale, y - 3 * scale, 6 * scale, 6 * scale);
            e.Graphics.DrawEllipse(outline, position - 6 * scale, y - 6 * scale, 12 * scale, 12 * scale);
        }
        using (var font = new Font("Segoe UI Semibold", 8))
            TextRenderer.DrawText(e.Graphics, Value + "%", font, new Rectangle(right + (int)(8 * scale), 0, Width - right, Height), WorkspaceStyle.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Capture = true;
        dragging = true;
        pendingNotification = false;
        SetValueFromPoint(e.X, true);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) SetValueFromPoint(e.X, true);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            dragging = false;
            Capture = false;
            CommitPendingValue();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            dragging = false;
            CommitPendingValue();
        }
    }

    private void CommitPendingValue()
    {
        if (!pendingNotification) return;
        pendingNotification = false;
        if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
    }

    private void SetValueFromPoint(int x, bool deferNotification)
    {
        float scale = DeviceDpi / 96f;
        int left, right;
        GetTrackBounds(scale, out left, out right);
        int next = Minimum + (int)Math.Round((Maximum - Minimum) * Math.Max(0, Math.Min(right - left, x - left)) / (double)(right - left));
        if (deferNotification)
        {
            int previous = value;
            value = Math.Max(Minimum, Math.Min(Maximum, next));
            pendingNotification = pendingNotification || previous != value;
            Invalidate();
        }
        else Value = next;
    }

    private void GetTrackBounds(float scale, out int left, out int right)
    {
        left = (int)Math.Round(14 * scale);
        right = Math.Max(left + 1, Width - (int)Math.Round(44 * scale));
    }
}

internal sealed class WorkspaceCard : Panel
{
    public WorkspaceCard()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WorkspaceStyle.Canvas;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = WorkspaceStyle.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 17 * e.Graphics.DpiX / 96f))
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), WorkspaceStyle.Card, WorkspaceStyle.Mix(WorkspaceStyle.Card, WorkspaceStyle.Raised, 0.18f), 90f))
        using (var outline = new Pen(WorkspaceStyle.Line))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(outline, path);
        }
    }
}

internal sealed class WorkspaceButton : Button
{
    private bool hovered;
    private bool pressed;
    private string glyph;
    private bool selected;
    public bool Primary { get; set; }
    public bool Navigation { get; set; }
    public string KeyHint { get; set; }
    public string Glyph { get { return glyph; } set { if (glyph != value) { glyph = value; Invalidate(); } } }
    public bool Selected
    {
        get { return selected; }
        set { if (selected != value) { selected = value; Invalidate(); } }
    }

    public WorkspaceButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = g.DpiX / 96f;
        Color parent = Parent == null ? WorkspaceStyle.Card : Parent.BackColor;
        g.Clear(parent);
        Color fill = WorkspaceStyle.Raised;
        Color text = WorkspaceStyle.Text;
        if (Navigation)
        {
            fill = Selected ? WorkspaceStyle.Mix(parent, WorkspaceStyle.Highlight, pressed ? 0.25f : 0.15f) :
                hovered ? WorkspaceStyle.Raised : parent;
            text = Selected || hovered ? WorkspaceStyle.Text : WorkspaceStyle.Muted;
        }
        else if (!Enabled)
        {
            fill = WorkspaceStyle.Field;
            text = WorkspaceStyle.Muted;
        }
        else if (Primary)
        {
            fill = pressed ? WorkspaceStyle.Highlight : hovered ? WorkspaceStyle.Mix(WorkspaceStyle.Ice, WorkspaceStyle.Highlight, 0.22f) :
                WorkspaceStyle.Mix(WorkspaceStyle.Ice, WorkspaceStyle.Highlight, 0.08f);
            text = WorkspaceStyle.Canvas;
        }
        else if (pressed) fill = WorkspaceStyle.Card;
        else if (hovered || Selected) fill = WorkspaceStyle.Mix(WorkspaceStyle.Raised, WorkspaceStyle.Highlight, 0.18f);
        var rect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using (var path = WorkspaceStyle.Rounded(rect, 10 * scale))
        using (var brush = new LinearGradientBrush(ClientRectangle,
            Primary && Enabled ? WorkspaceStyle.Mix(fill, Color.White, 0.22f) : fill,
            Primary && Enabled ? WorkspaceStyle.Mix(fill, WorkspaceStyle.Highlight, 0.62f) : fill, 88f))
        {
            g.FillPath(brush, path);
            if (Focused || (Selected && Navigation) || (!Primary && !Navigation && Enabled))
                using (var pen = new Pen(Focused ? (Primary ? WorkspaceStyle.Rail : WorkspaceStyle.Highlight) : WorkspaceStyle.Line, Focused ? 2 : 1))
                    g.DrawPath(pen, path);
        }
        int left = (int)(12 * scale);
        if (Glyph == "switch")
        {
            float trackY = (Height - 14 * scale) / 2f;
            using (var track = WorkspaceStyle.Rounded(new RectangleF(left, trackY, 28 * scale, 14 * scale), 7 * scale))
            using (var trackBrush = new SolidBrush(Selected ? WorkspaceStyle.Highlight : WorkspaceStyle.Line))
            using (var thumb = new SolidBrush(Selected ? WorkspaceStyle.Canvas : WorkspaceStyle.Muted))
            {
                g.FillPath(trackBrush, track);
                g.FillEllipse(thumb, left + (Selected ? 16 : 2) * scale, trackY + 2 * scale, 10 * scale, 10 * scale);
            }
            left += (int)(37 * scale);
        }
        else if (Glyph != null)
        {
            WorkspaceStyle.Glyph(g, Glyph, left, (Height - (int)(17 * scale)) / 2, text, scale);
            left += (int)(25 * scale);
        }
        int right = (int)(9 * scale);
        if (Navigation && Selected)
        {
            using (var line = WorkspaceStyle.Rounded(new RectangleF(1.5f * scale, Height / 2f - 9 * scale, 3 * scale, 18 * scale), 2 * scale))
            using (var accent = new SolidBrush(WorkspaceStyle.Highlight)) g.FillPath(accent, line);
            right = (int)(24 * scale);
            WorkspaceStyle.Glyph(g, "arrow", Width - right, (Height - (int)(17 * scale)) / 2, WorkspaceStyle.Highlight, scale);
        }
        if (!String.IsNullOrEmpty(KeyHint))
        {
            using (var font = new Font("Segoe UI Semibold", 8))
            {
                int badgeWidth = TextRenderer.MeasureText(g, KeyHint, font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(12 * scale);
                if (Width >= left + badgeWidth + (int)(64 * scale))
                {
                    int badgeHeight = (int)(22 * scale);
                    var badge = new Rectangle(Width - right - badgeWidth, (Height - badgeHeight) / 2, badgeWidth, badgeHeight);
                    using (var outline = WorkspaceStyle.Rounded(badge, 5 * scale))
                    using (var pen = new Pen(WorkspaceStyle.Line)) g.DrawPath(pen, outline);
                    TextRenderer.DrawText(g, KeyHint, font, badge, WorkspaceStyle.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    right += badgeWidth + (int)(6 * scale);
                }
            }
        }
        bool centered = Glyph == null && !Navigation && String.IsNullOrEmpty(KeyHint);
        int inset = centered ? right : left;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(inset, 0, Math.Max(0, Width - inset - right), Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
            (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
    }
}
