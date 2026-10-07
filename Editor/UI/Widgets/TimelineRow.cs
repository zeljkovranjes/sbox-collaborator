namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>
/// One entry of a vertical timeline: a thin line down the left with a coloured dot for this
/// entry, then the content. First/last entries stop the line at their dot.
/// </summary>
public sealed class TimelineRow : Widget
{
	private readonly Color _dot;
	private readonly bool _first;
	private readonly bool _last;
	private readonly bool _hollow;
	public Action Clicked { get; set; }

	public TimelineRow( Widget parent, Color dot, bool first, bool last, bool hollow = false, Action clicked = null ) : base( parent )
	{
		_dot = dot;
		_first = first;
		_last = last;
		_hollow = hollow;
		Clicked = clicked;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 26, 5, 4, 5 );
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = clicked is not null;
		Cursor = clicked is null ? CursorShape.Arrow : CursorShape.Finger;
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
		if ( Clicked is not null && Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
			Paint.DrawRect( new Rect( 18, 0, Width - 18, Height ), 4 );
		}
		const float x = 9;
		const float dotY = 14;
		Paint.SetPen( Theme.ControlBackground.Lighten( .55f ), 1.5f );
		Paint.DrawLine( new Vector2( x, _first ? dotY : 0 ), new Vector2( x, _last ? dotY : Height ) );
		var dot = new Rect( x - 4.5f, dotY - 4.5f, 9, 9 );
		Paint.SetPen( _dot, 2 );
		Paint.SetBrush( _hollow ? Theme.ControlBackground : _dot );
		Paint.DrawRect( dot, 4.5f );
	}
}
