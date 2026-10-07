namespace Collaborator.EditorTools.UI.Widgets;

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
