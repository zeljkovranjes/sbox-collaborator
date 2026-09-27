namespace Collaborator.UI;

/// <summary>
/// A rounded dark-gray panel on the gray window. Its column layout holds an optional header
/// row (icon, title, then controls) above the content. An accent colour tints the edge and icon
/// (red for blockers, yellow for warnings).
/// </summary>
public class Card : Widget
{
	private Color? _accent;

	public Card( Widget parent, bool row = false ) : base( parent )
	{
		Layout = row ? Layout.Row() : Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;
	}

	public Color? Accent
	{
		get => _accent;
		set
		{
			_accent = value;
			Update();
		}
	}

	/// <summary>Adds the header row: an icon and a title, then whatever the caller adds to the returned row.</summary>
	public Layout Header( string icon, string title, string tooltip = null, Color? iconColor = null )
	{
		var row = Layout.AddRow();
		row.Spacing = 6;
		row.Add( new CardIcon( this, icon, iconColor ?? _accent ?? Theme.Green ) );
		var label = row.Add( new Label( title, this ) { ToolTip = tooltip } );
		label.SetStyles( "font-weight: 600;" );
		return row;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( _accent is { } a ? a.WithAlpha( .55f ) : UiStyle.CardEdge, 1 );
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
		if ( _accent is { } accent )
		{
			Paint.ClearPen();
			Paint.SetBrush( accent.WithAlpha( .05f ) );
			Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
		}
	}

	private sealed class CardIcon : Widget
	{
		private readonly string _icon;
		private readonly Color _color;

		public CardIcon( Widget parent, string icon, Color color ) : base( parent )
		{
			_icon = icon;
			_color = color;
			FixedSize = 18;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( _color );
			Paint.DrawIcon( LocalRect, _icon, 16 );
		}
	}
}

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

/// <summary>Small uppercase caption with a hairline, separating groups inside a card.</summary>
public sealed class SectionHeader : Widget
{
	private readonly string _text;
	private readonly string _count;

	public SectionHeader( Widget parent, string text, int? count = null ) : base( parent )
	{
		_text = text.ToUpperInvariant();
		_count = count?.ToString();
		FixedHeight = 18;
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( 7, 600 );
		Paint.SetPen( Theme.TextLight );
		var size = Paint.MeasureText( _text );
		Paint.DrawText( new Rect( 0, 0, size.x + 2, Height ), _text, TextFlag.LeftCenter );
		var x = size.x + 10;
		if ( _count is not null )
		{
			Paint.SetPen( Theme.TextLight.WithAlpha( .7f ) );
			var countSize = Paint.MeasureText( _count );
			Paint.DrawText( new Rect( x - 4, 0, countSize.x + 2, Height ), _count, TextFlag.LeftCenter );
			x += countSize.x + 6;
		}
		Paint.SetPen( Theme.ControlBackground.Lighten( .45f ), 1 );
		Paint.DrawLine( new Vector2( x, Height * .5f ), new Vector2( Width, Height * .5f ) );
	}
}

/// <summary>Small muted uppercase column caption (list headers).</summary>
public sealed class SectionCaption : Widget
{
	private readonly string _text;
	private readonly TextFlag _align;

	public SectionCaption( Widget parent, string text, float width = 0, TextFlag align = TextFlag.LeftCenter ) : base( parent )
	{
		_text = text.ToUpperInvariant();
		_align = align;
		FixedHeight = 16;
		if ( width > 0 )
			FixedWidth = width;
	}

	protected override void OnPaint()
	{
		Paint.SetDefaultFont( 7, 600 );
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( LocalRect, _text, _align );
	}
}

/// <summary>A tinted 16px icon in a 20px cell.</summary>
public sealed class IconLabel : Widget
{
	private string _icon;
	private Color _color;

	public IconLabel( Widget parent, string icon, Color color, float size = 20 ) : base( parent )
	{
		_icon = icon;
		_color = color;
		FixedSize = size;
	}

	public void Set( string icon, Color color )
	{
		_icon = icon;
		_color = color;
		Update();
	}

	protected override void OnPaint()
	{
		Paint.SetPen( _color );
		Paint.DrawIcon( LocalRect, _icon, Width - 4 );
	}
}

/// <summary>
/// A round badge with a person's initials. The ring shows presence: coloured by status when a
/// status is given (green working, yellow planning/testing, red blocked, gray idle), otherwise a
/// small green/gray dot in the corner.
/// </summary>
public sealed class Avatar : Widget
{
	private readonly string _initials;
	private readonly Color _color;
	private readonly bool _online;
	private readonly string _status;

	public Avatar( Widget parent, string id, string name, bool online, float size = 28, string status = null ) : base( parent )
	{
		FixedSize = size;
		_color = UiStyle.PersonColor( id );
		_online = online;
		_status = status;
		_initials = Initials( id, name );
		ToolTip = online ? $"{name} is online" : $"{name} is offline";
	}

	public static string Initials( string id, string name )
	{
		var parts = (string.IsNullOrWhiteSpace( name ) ? id ?? "?" : name).Split( new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries );
		return parts.Length switch
		{
			0 => "?",
			1 => parts[0][..Math.Min( 2, parts[0].Length )].ToUpperInvariant(),
			_ => $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant(),
		};
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var r = LocalRect.Shrink( 1 );
		Paint.ClearPen();
		Paint.SetBrush( _color.WithAlpha( _online ? .22f : .1f ) );
		Paint.DrawRect( r.Shrink( 2 ), r.Width * .5f - 2 );
		if ( _status is not null )
		{
			Paint.SetPen( _online ? AvatarStack.Ring( _status ) : Theme.TextLight.Darken( .35f ), 2 );
			Paint.SetBrush( Color.Transparent );
			Paint.DrawRect( r, r.Width * .5f );
		}
		Paint.SetDefaultFont( Width > 30 ? 9 : 7, 700 );
		Paint.SetPen( _online ? _color : _color.WithAlpha( .6f ) );
		Paint.DrawText( r, _initials, TextFlag.Center );
		if ( _status is not null )
			return;

		// Presence light in the bottom-right corner.
		var dot = new Rect( Width - 10, Height - 10, 9, 9 );
		Paint.SetPen( Theme.ControlBackground, 2 );
		Paint.SetBrush( _online ? Theme.Green : Theme.TextLight.Darken( .3f ) );
		Paint.DrawRect( dot, 4.5f );
	}
}

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

/// <summary>A centred icon and two lines of muted text for lists with nothing in them.</summary>
public sealed class EmptyState : Widget
{
	private readonly string _icon;
	private readonly string _title;
	private readonly string _text;

	public EmptyState( Widget parent, string icon, string title, string text = null ) : base( parent )
	{
		_icon = icon;
		_title = title;
		_text = text;
		FixedHeight = string.IsNullOrEmpty( text ) ? 64 : 84;
	}

	protected override void OnPaint()
	{
		var c = LocalRect.Center;
		Paint.SetPen( Theme.TextLight.WithAlpha( .5f ) );
		Paint.DrawIcon( new Rect( c.x - 14, 6, 28, 28 ), _icon, 26 );
		Paint.SetDefaultFont( 9, 600 );
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( new Rect( 0, 38, Width, 18 ), _title, TextFlag.Center );
		if ( !string.IsNullOrEmpty( _text ) )
		{
			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextLight.WithAlpha( .7f ) );
			Paint.DrawText( new Rect( 12, 56, Width - 24, 26 ), _text, TextFlag.Center | TextFlag.WordWrap );
		}
	}
}
