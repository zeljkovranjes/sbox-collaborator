namespace Collaborator.EditorTools.UI.Widgets;

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
