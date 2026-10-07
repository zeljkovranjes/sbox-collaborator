namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>
/// The waiting state: a green spinner arc with a title and the latest message below it, on a
/// card-coloured rounded box (same look as the weapon importer's processing indicator).
/// </summary>
public sealed class ProcessingIndicator : Widget
{
	private string _title = "Working…";
	private string _message = "";
	private readonly bool _boxed;

	public bool Busy { get; set; } = true;

	public ProcessingIndicator( Widget parent, bool boxed = true ) : base( parent )
	{
		_boxed = boxed;
		MinimumSize = new Vector2( 120, 120 );
	}

	public void Set( string title, string message )
	{
		_title = string.IsNullOrEmpty( title ) ? "Working…" : title;
		_message = message ?? "";
		Update();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( Busy && Visible )
			Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var area = LocalRect;
		if ( _boxed )
		{
			Paint.SetPen( Theme.ControlBackground.Lighten( .2f ), 1 );
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( area.Shrink( 1 ), 6 );
		}
		var center = area.Center;
		var width = MathF.Min( 360f, area.Width - 40f );
		if ( Busy )
		{
			var angle = (float)(RealTime.Now * 300 % 360);
			var c = new Vector2( center.x, center.y - 30 );
			Paint.SetPen( Color.White.WithAlpha( .08f ), 4 );
			Paint.DrawArc( c, new Vector2( 18, 18 ), 0, 360 );
			Paint.SetPen( Theme.Green, 4 );
			Paint.DrawArc( c, new Vector2( 18, 18 ), angle, 100 );
		}
		Paint.SetDefaultFont( 10, 600 );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( new Rect( center.x - width / 2, center.y + 2, width, 22 ), _title, TextFlag.Center );
		Paint.SetDefaultFont();
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( new Rect( center.x - width / 2, center.y + 26, width, 40 ), _message, TextFlag.Center | TextFlag.WordWrap );
	}
}
