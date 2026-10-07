namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>
/// Overlapping round avatar chips for the teammates who are online: initials on a tinted disc,
/// with a ring coloured by what their agents are doing. Extra people collapse into "+N".
/// </summary>
public sealed class AvatarStack : Widget
{
	public sealed record Person( string Id, string Name, string Status, string Tip );

	private List<Person> _people = new();
	private const float ChipSize = 26;
	private const float Step = 18;
	private const int Max = 5;

	public AvatarStack( Widget parent ) : base( parent )
	{
		FixedHeight = ChipSize + 2;
		FixedWidth = 0;
		MouseTracking = true;
	}

	public void Set( List<Person> people )
	{
		var key = string.Join( "|", people.Select( p => $"{p.Id}:{p.Status}:{p.Tip}" ) );
		if ( key == string.Join( "|", _people.Select( p => $"{p.Id}:{p.Status}:{p.Tip}" ) ) )
			return;
		_people = people;
		var shown = Math.Min( people.Count, Max ) + (people.Count > Max ? 1 : 0);
		FixedWidth = shown == 0 ? 0 : ChipSize + (shown - 1) * Step + 2;
		ToolTip = people.Count == 0 ? null : string.Join( "\n", people.Select( p => p.Tip ) );
		Update();
	}

	/// <summary>Ring colour: green working, yellow planning/testing/reviewing, red blocked, gray idle.</summary>
	public static Color Ring( string status ) => status switch
	{
		"working" => Theme.Green,
		"planning" or "testing" or "reviewing" => Theme.Yellow,
		"blocked" => Theme.Red,
		_ => Theme.TextLight.Darken( .15f ),
	};

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var shown = _people.Take( Max ).ToList();
		// Draw right-to-left so the first person sits on top.
		for ( var i = shown.Count - 1; i >= 0; i-- )
			DrawChip( new Rect( 1 + i * Step, 1, ChipSize, ChipSize ), shown[i] );
		if ( _people.Count > Max )
		{
			var r = new Rect( 1 + Max * Step, 1, ChipSize, ChipSize );
			Paint.SetPen( Theme.WindowBackground, 2 );
			Paint.SetBrush( Theme.ControlBackground.Lighten( .5f ) );
			Paint.DrawRect( r, ChipSize * .5f );
			Paint.SetDefaultFont( 7, 700 );
			Paint.SetPen( Theme.Text );
			Paint.DrawText( r, $"+{_people.Count - Max}", TextFlag.Center );
		}
	}

	private static void DrawChip( Rect r, Person p )
	{
		var person = UiStyle.PersonColor( p.Id );
		// A background-coloured gap separates overlapping chips.
		Paint.SetPen( Theme.WindowBackground, 3 );
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( r, r.Width * .5f );
		Paint.ClearPen();
		Paint.SetBrush( person.WithAlpha( .25f ) );
		Paint.DrawRect( r.Shrink( 2.5f ), r.Width * .5f - 2.5f );
		Paint.SetPen( Ring( p.Status ), 2 );
		Paint.SetBrush( Color.Transparent );
		Paint.DrawRect( r.Shrink( 1 ), r.Width * .5f - 1 );
		Paint.SetDefaultFont( 7, 700 );
		Paint.SetPen( person );
		Paint.DrawText( r, Avatar.Initials( p.Id, p.Name ), TextFlag.Center );
	}
}
