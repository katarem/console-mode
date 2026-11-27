namespace ConsoleMode.UI;

/// <summary>
/// Label whose text wraps to the width its container gives it and whose height follows the wrapped text.
/// </summary>
/// <remarks>
/// An AutoSize label only wraps with a fixed MaximumSize, which follows neither the window width nor the monitor scale,
/// so texts ended up cut when the window moved to another screen. Place it with Anchor Left | Right (the default) or
/// Dock so that the container decides the width; the height is measured from the text.
/// </remarks>
internal sealed class WrapLabel : Label
{
    private const TextFormatFlags MeasureFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

    public WrapLabel()
    {
        AutoSize = false;
        UseMnemonic = false;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Without a width constraint the current width is kept: asking for the single-line width would push the
        // container wider than the window.
        var width = proposedSize.Width is > 1 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        return new Size(width, MeasureHeight(width));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitHeight();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        FitHeight();
    }

    private void FitHeight()
    {
        var height = MeasureHeight(Width);
        if (height != Height)
        {
            Height = height;
        }
    }

    private int MeasureHeight(int width)
    {
        var textWidth = Math.Max(1, width - Padding.Horizontal);
        // An empty label keeps the height of one line so that a status text appearing does not move the layout.
        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        return TextRenderer.MeasureText(text, Font, new Size(textWidth, int.MaxValue), MeasureFlags).Height + Padding.Vertical;
    }
}
