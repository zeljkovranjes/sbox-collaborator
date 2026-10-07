namespace Collaborator.EditorTools.UI.Widgets;

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
