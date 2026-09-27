using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>The list rows every page shares: tasks, reservations, commits, activity, messages.</summary>
public static class Rows
{
	/// <summary>Title line over a muted detail line, stretching in a row.</summary>
	public static Layout TwoLines( Widget owner, Layout row, string title, string detail, bool boldTitle = false, Color? titleColor = null )
	{
		var column = row.AddColumn( 1 );
		column.Spacing = 1;
		var top = column.Add( new Label( title ?? "", owner ) { WordWrap = true } );
		if ( boldTitle )
			UiStyle.Bold( top );
		if ( titleColor is { } c )
			UiStyle.Colored( top, c, boldTitle );
		if ( !string.IsNullOrEmpty( detail ) )
			column.Add( UiStyle.Muted( new Label( detail, owner ) { WordWrap = true }, small: true ) );
		return column;
	}

	public static ClickRow Task( Widget parent, TaskItem task, bool selected, Action clicked )
	{
		var row = new ClickRow( parent, clicked, task.Description ) { Selected = selected, Bar = UiStyle.PriorityColor( task.Priority ) };
		row.Layout.Add( UiStyle.Mono( new Label( $"#{task.Id}", row ) { FixedWidth = 38, Alignment = TextFlag.LeftCenter } ) );
		var detail = new List<string>();
		if ( !string.IsNullOrEmpty( task.OwnerName ?? task.OwnerId ) )
			detail.Add( task.OwnerName ?? task.OwnerId );
		if ( task.Priority is "high" or "urgent" )
			detail.Add( task.Priority );
		if ( !string.IsNullOrEmpty( task.Branch ) )
			detail.Add( task.Branch );
		if ( task.UpdatedAt is not null )
			detail.Add( UiStyle.Ago( task.UpdatedAt ) );
		if ( task.Stale )
			detail.Add( "owner offline" );
		TwoLines( row, row.Layout, task.Title, string.Join( " · ", detail ) );
		row.Layout.Add( new Pill( row, CollabSession.TaskStatusText( task.Status ), UiStyle.TaskColor( task.Status ), column: true ) );
		return row;
	}

	public static ClickRow Reservation( Widget parent, Reservation r, Action release = null )
	{
		var mine = r.DeveloperId == CollabSession.MyId;
		var row = new ClickRow( parent, null, r.Reason ) { Bar = mine ? Theme.Green : UiStyle.PersonColor( r.DeveloperId ) };
		row.Layout.Add( new IconLabel( row, r.IsDirectory || r.Path.EndsWith( '/' ) ? "folder" : "description", mine ? Theme.Green : UiStyle.PersonColor( r.DeveloperId ) ) );
		var who = mine ? "You" : r.DeveloperName ?? r.DeveloperId;
		var agent = string.IsNullOrEmpty( r.AgentLabel ) ? "" : $" ({r.AgentLabel})";
		var what = r.TaskId is { } id ? $" · #{id} {r.TaskTitle}" : string.IsNullOrEmpty( r.Reason ) ? "" : $" · {r.Reason}";
		TwoLines( row, row.Layout, UiStyle.Breakable( r.Path ), $"{who}{agent}{what}" );
		row.Layout.Add( UiStyle.Muted( new Label( UiStyle.Until( r.ExpiresAt ), row ) { Alignment = TextFlag.RightCenter, MinimumWidth = 60 }, small: true ) );
		if ( release is not null )
			row.Layout.Add( UiStyle.Icon( row, "lock_open", release, "Release this reservation", 24 ) );
		return row;
	}

	public static ClickRow Commit( Widget parent, Commit c )
	{
		var row = new ClickRow( parent, string.IsNullOrEmpty( c.Url ) ? null : () => Browser.Open( c.Url ), c.Message );
		row.Layout.Add( UiStyle.Mono( new Label( c.Short ?? "", row ) { FixedWidth = 58, Alignment = TextFlag.LeftCenter }, Theme.Green ) );
		var branch = string.IsNullOrEmpty( c.Branch ) ? "" : $" · {c.Branch}";
		var task = c.TaskId is { } id ? $" · #{id}" : "";
		TwoLines( row, row.Layout, c.Headline, $"{c.AuthorName ?? c.AuthorLogin}{branch}{task} · {UiStyle.Ago( c.At )}" );
		return row;
	}

	public static Widget Activity( Widget parent, Activity a )
	{
		var row = new ClickRow( parent );
		row.Layout.Add( UiStyle.Mono( new Label( UiStyle.Clock( a.At ), row ) { FixedWidth = 38, Alignment = TextFlag.LeftTop } ) );
		var (icon, color) = ActivityStyle( a.Kind );
		row.Layout.Add( new IconLabel( row, icon, a.Importance >= 2 ? color : color.WithAlpha( .75f ) ) );
		var actor = string.IsNullOrEmpty( a.AgentLabel ) ? a.ActorName : $"{a.ActorName} · {a.AgentLabel}";
		TwoLines( row, row.Layout, a.Summary, actor, boldTitle: a.Importance >= 3 );
		return row;
	}

	public static (string Icon, Color Color) ActivityStyle( string kind )
	{
		kind ??= "";
		if ( kind.StartsWith( "task" ) ) return (kind.EndsWith( "completed" ) ? "task_alt" : "assignment", Theme.Blue);
		if ( kind.StartsWith( "file" ) || kind.StartsWith( "reservation" ) ) return (kind.Contains( "releas" ) || kind.Contains( "expir" ) ? "lock_open" : "lock", Theme.Yellow);
		if ( kind.StartsWith( "commit" ) || kind.StartsWith( "push" ) || kind.StartsWith( "branch" ) ) return ("commit", Theme.Green);
		if ( kind.StartsWith( "pull" ) ) return ("merge", Theme.Pink);
		if ( kind.StartsWith( "issue" ) ) return ("bug_report", Theme.Pink);
		if ( kind.StartsWith( "change" ) ) return ("published_with_changes", Theme.Green);
		if ( kind.StartsWith( "decision" ) ) return ("gavel", Theme.Blue);
		if ( kind.StartsWith( "message" ) ) return ("chat", Theme.TextLight);
		if ( kind.StartsWith( "test" ) || kind.StartsWith( "build" ) ) return (kind.Contains( "fail" ) || kind.Contains( "broken" ) ? "error" : "science", kind.Contains( "fail" ) || kind.Contains( "broken" ) ? Theme.Red : Theme.Blue);
		if ( kind.StartsWith( "agent" ) ) return ("smart_toy", Theme.TextLight);
		if ( kind.StartsWith( "knowledge" ) ) return ("menu_book", Theme.Blue);
		if ( kind.StartsWith( "asset" ) ) return ("category", Theme.Yellow);
		return ("bolt", Theme.TextLight);
	}
}

/// <summary>Opens links in the system browser.</summary>
public static class Browser
{
	public static void Open( string url )
	{
		if ( string.IsNullOrEmpty( url ) )
			return;
		try
		{
			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo( url ) { UseShellExecute = true } );
		}
		catch ( Exception )
		{
			EditorUtility.OpenFolder( url );
		}
	}
}
