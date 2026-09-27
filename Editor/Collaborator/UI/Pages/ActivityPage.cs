namespace Collaborator.UI;

/// <summary>The chronological feed: claims, reservations, commits, API changes, tests, grouped by day.</summary>
public sealed class ActivityPage : Page
{
	private bool _importantOnly;
	private Chip _all;
	private Chip _important;

	public ActivityPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Activity";

	protected override void BuildStatic( Layout layout )
	{
		var bar = layout.AddRow();
		bar.Spacing = 6;
		_all = bar.Add( new Chip( this, "Everything", "list" ) { Active = true, Clicked = () => Set( false ) } );
		_important = bar.Add( new Chip( this, "Important", "priority_high" ) { Clicked = () => Set( true ) } );
		bar.AddStretchCell();
	}

	private void Set( bool important )
	{
		_importantOnly = important;
		_all.Active = !important;
		_important.Active = important;
		MarkDirty();
	}

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var items = CollabSession.Activity.Where( a => !_importantOnly || a.Importance >= 2 ).OrderByDescending( a => a.At ).Take( 120 ).ToList();
		var card = layout.Add( new Card( host ) );
		card.Layout.Spacing = 2;
		if ( items.Count == 0 )
		{
			card.Layout.Add( new EmptyState( card, "timeline", "Nothing has happened yet", "Claims, reservations, commits and test results show up here." ) );
			return;
		}
		string day = null;
		foreach ( var a in items )
		{
			var local = a.At?.ToLocalTime();
			var label = local is null ? "Earlier" : local.Value.Date == DateTime.Today ? "Today" : local.Value.Date == DateTime.Today.AddDays( -1 ) ? "Yesterday" : local.Value.ToString( "dddd d MMM" );
			if ( label != day )
			{
				day = label;
				card.Layout.Add( new SectionHeader( card, label ) );
			}
			card.Layout.Add( Rows.Activity( card, a ) );
		}
	}
}
