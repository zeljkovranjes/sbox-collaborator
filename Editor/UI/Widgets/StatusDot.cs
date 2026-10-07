namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>A small round light: green online, yellow reconnecting, red offline.</summary>
public sealed class StatusDot : Widget
{
	private Color _color = Theme.TextLight;
	private bool _pulse;

	public StatusDot( Widget parent, float size = 10 ) : base( parent )
	{
		FixedSize = size;
	}

	public Color Color
	{
		get => _color;
		set
		{
			if ( _color == value )
				return;
			_color = value;
			Update();
		}
	}

	/// <summary>A soft halo that breathes (live connection).</summary>
	public bool Pulse
	{
		get => _pulse;
		set
		{
			_pulse = value;
			Update();
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( _pulse && Visible )
			Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		if ( _pulse )
		{
			var t = (MathF.Sin( RealTime.Now * 2.4f ) + 1f) * .5f;
			Paint.SetBrush( _color.WithAlpha( .12f + .18f * t ) );
			Paint.DrawRect( LocalRect, Width * .5f );
		}
		Paint.SetBrush( _color );
		Paint.DrawRect( LocalRect.Shrink( _pulse ? 2.5f : 1 ), Width * .5f );
	}
}
