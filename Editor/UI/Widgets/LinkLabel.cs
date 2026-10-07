namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>Muted text that acts as a link (underlines on hover).</summary>
public sealed class LinkLabel : Widget
{
	private readonly string _text;
	private readonly Color _color;
	public Action Clicked { get; set; }

	public LinkLabel( Widget parent, string text, Action clicked, Color? color = null ) : base( parent )
	{
		_text = text;
		_color = color ?? Theme.TextLight;
		Clicked = clicked;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		FixedHeight = 20;
		Paint.SetDefaultFont( 8 );
		FixedWidth = MathF.Ceiling( 6.4f * text.Length + 6 );
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || Clicked is null )
			return;
		e.Accepted = true;
		Clicked();
	}

	protected override void OnPaint()
	{
		var hover = Paint.HasMouseOver;
		Paint.SetDefaultFont( 8 );
		var width = Paint.MeasureText( _text ).x;
		if ( MathF.Abs( width + 4 - FixedWidth ) > 1 )
			FixedWidth = MathF.Ceiling( width + 4 );
		Paint.SetPen( hover ? Theme.Green : _color );
		Paint.DrawText( LocalRect, _text, TextFlag.LeftCenter );
		if ( hover )
			Paint.DrawLine( new Vector2( 0, Height * .5f + 7 ), new Vector2( width, Height * .5f + 7 ) );
	}
}
