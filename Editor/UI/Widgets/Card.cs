namespace Collaborator.EditorTools.UI.Widgets;

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
