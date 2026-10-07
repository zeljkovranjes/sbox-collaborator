namespace Collaborator.EditorTools.UI.Widgets;

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
