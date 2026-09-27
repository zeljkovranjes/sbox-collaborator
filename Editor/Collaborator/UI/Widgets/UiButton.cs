namespace Collaborator.UI;

/// <summary>
/// The secondary button: dark fill a step lighter than the card, a hairline border, light text
/// and a hover lighten. Every non-primary button uses it so they all look the same. An optional
/// tint colours the text and edge (red for destructive actions).
/// </summary>
public sealed class UiButton : Widget
{
	private string _text;
	private string _icon;
	private Color? _tint;

	public Action Clicked { get; set; }

	public string Text
	{
		get => _text;
		set
		{
			_text = value ?? "";
			Measure();
			Update();
		}
	}

	public Color? Tint
	{
		get => _tint;
		set
		{
			_tint = value;
			Update();
		}
	}

	public UiButton( Widget parent, string text, string icon = null, Action clicked = null, string tooltip = null, float height = UiStyle.ControlHeight ) : base( parent )
	{
		_text = text ?? "";
		_icon = icon;
		Clicked = clicked;
		ToolTip = tooltip;
		FixedHeight = height;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		FocusMode = FocusMode.None;
		Measure();
	}

	private const float PadX = 11f;
	private const float IconSize = 16f;
	private const float IconGap = 6f;

	/// <summary>Estimated width until the first paint measures the real text.</summary>
	private void Measure() => FixedWidth = WidthFor( _text.Length == 0 ? 0 : 6.2f * _text.Length );

	private float WidthFor( float textWidth )
	{
		if ( _text.Length == 0 )
			return string.IsNullOrEmpty( _icon ) ? PadX * 2 : FixedHeight;
		var icon = string.IsNullOrEmpty( _icon ) ? 0 : IconSize + IconGap;
		return MathF.Ceiling( PadX * 2 + icon + textWidth );
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( !Enabled || !e.LeftMouseButton || !LocalRect.IsInside( e.LocalPosition ) )
			return;
		Clicked?.Invoke();
		e.Accepted = true;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
			e.Accepted = true;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var hover = Paint.HasMouseOver && Enabled;
		var fill = hover ? Color.Lerp( UiStyle.ButtonFill, Color.White, .06f ) : UiStyle.ButtonFill;
		var edge = _tint is { } t ? t.WithAlpha( hover ? .9f : .55f ) : hover ? Color.Lerp( UiStyle.ButtonEdge, Color.White, .1f ) : UiStyle.ButtonEdge;
		Paint.SetPen( edge, 1 );
		Paint.SetBrush( fill );
		Paint.DrawRect( LocalRect.Shrink( .5f ), UiStyle.Radius );

		var color = !Enabled ? Theme.TextDisabled : _tint ?? Theme.Text;
		Paint.SetPen( color );
		if ( _text.Length == 0 )
		{
			if ( !string.IsNullOrEmpty( _icon ) )
				Paint.DrawIcon( LocalRect, _icon, 15, TextFlag.Center );
			return;
		}

		// Fit the width to the measured text so the padding matches on both sides.
		Paint.SetDefaultFont( 8 );
		var textWidth = Paint.MeasureText( _text ).x;
		var wanted = WidthFor( textWidth );
		if ( MathF.Abs( wanted - FixedWidth ) > 0.5f )
			FixedWidth = wanted;

		var hasIcon = !string.IsNullOrEmpty( _icon );
		var group = textWidth + (hasIcon ? IconSize + IconGap : 0);
		var x = LocalRect.Left + MathF.Max( PadX, (LocalRect.Width - group) * 0.5f );
		if ( hasIcon )
		{
			Paint.DrawIcon( new Rect( x, LocalRect.Top, IconSize, LocalRect.Height ), _icon, 15, TextFlag.Center );
			x += IconSize + IconGap;
		}
		Paint.DrawText( new Rect( x, LocalRect.Top, LocalRect.Right - x, LocalRect.Height ), _text, TextFlag.LeftCenter );
	}
}

/// <summary>
/// A row that is itself the control: click anywhere on it. Hover tint, and when selected a green
/// outline with a green bar on the left.
/// </summary>
public sealed class ClickRow : Widget
{
	private bool _selected;
	private Color? _bar;

	public Action Clicked { get; set; }

	public ClickRow( Widget parent, Action clicked = null, string tooltip = null ) : base( parent )
	{
		Clicked = clicked;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 10, 4, 6, 4 );
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = true;
		Cursor = clicked is null ? CursorShape.Arrow : CursorShape.Finger;
		ToolTip = tooltip;
	}

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value )
				return;
			_selected = value;
			Update();
		}
	}

	/// <summary>A thin coloured bar on the left edge even when not selected (priority, status).</summary>
	public Color? Bar
	{
		get => _bar;
		set
		{
			_bar = value;
			Update();
		}
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || Clicked is null )
			return;
		Clicked();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		if ( _selected )
		{
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.Green.WithAlpha( .08f ) );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 4 );
		}
		else if ( Paint.HasMouseOver && Clicked is not null )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
			Paint.DrawRect( LocalRect, 4 );
		}
		var bar = _selected ? Theme.Green : _bar;
		if ( bar is { } color )
		{
			Paint.ClearPen();
			Paint.SetBrush( color );
			Paint.DrawRect( new Rect( 0, 6, 3, Height - 12 ), 1.5f );
		}
	}
}
