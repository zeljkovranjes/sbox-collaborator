namespace Collaborator.EditorTools.UI.Widgets;

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
