namespace Collaborator.UI;

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

/// <summary>
/// The sign-in wizard's progress: numbered circles joined by a line. Done steps are filled green
/// with a check, the current step is outlined green, later steps are gray.
/// </summary>
public sealed class StepIndicator : Widget
{
	private readonly string[] _labels;
	private int _current;

	public StepIndicator( Widget parent, params string[] labels ) : base( parent )
	{
		_labels = labels;
		FixedHeight = 52;
		MinimumWidth = 240;
	}

	public int Current
	{
		get => _current;
		set
		{
			_current = value;
			Update();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var n = _labels.Length;
		const float size = 24;
		var spacing = (Width - 40) / MathF.Max( 1, n - 1 );
		float X( int i ) => 20 + i * spacing;
		const float y = 14;

		// Connecting lines first, so the circles sit on top.
		for ( var i = 0; i < n - 1; i++ )
		{
			Paint.SetPen( i < _current ? Theme.Green.WithAlpha( .8f ) : Theme.ControlBackground.Lighten( .5f ), 2 );
			Paint.DrawLine( new Vector2( X( i ) + size * .5f + 4, y ), new Vector2( X( i + 1 ) - size * .5f - 4, y ) );
		}

		for ( var i = 0; i < n; i++ )
		{
			var circle = new Rect( X( i ) - size * .5f, y - size * .5f, size, size );
			var done = i < _current;
			var current = i == _current;
			if ( done )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green );
				Paint.DrawRect( circle, size * .5f );
				Paint.SetPen( Theme.ControlBackground );
				Paint.DrawIcon( circle, "check", 16 );
			}
			else
			{
				Paint.SetPen( current ? Theme.Green : Theme.ControlBackground.Lighten( .6f ), current ? 2 : 1 );
				Paint.SetBrush( current ? Theme.Green.WithAlpha( .12f ) : Theme.WindowBackground );
				Paint.DrawRect( circle.Shrink( 1 ), size * .5f );
				Paint.SetDefaultFont( 8, 700 );
				Paint.SetPen( current ? Theme.Green : Theme.TextLight );
				Paint.DrawText( circle, (i + 1).ToString(), TextFlag.Center );
			}
			Paint.SetDefaultFont( 7, current ? 700 : 600 );
			Paint.SetPen( current ? Theme.Text : done ? Theme.Green : Theme.TextLight );
			Paint.DrawText( new Rect( X( i ) - 60, y + size * .5f + 3, 120, 14 ), _labels[i].ToUpperInvariant(), TextFlag.Center );
		}
	}
}
