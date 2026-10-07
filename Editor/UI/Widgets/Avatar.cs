namespace Collaborator.EditorTools.UI.Widgets;

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
