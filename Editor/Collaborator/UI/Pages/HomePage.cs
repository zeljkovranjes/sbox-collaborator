using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>
/// Home reads like a live feed: "Right now" (one row per connected agent), a red blockers banner,
/// tasks in progress, then a merged "Recent" timeline of commits, completed changes and the last
/// test.
/// </summary>
public sealed class HomePage : Page
{
	public HomePage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Home";

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var o = CollabSession.Overview;
		if ( o is null )
		{
			layout.Add( new EmptyState( host, "hourglass_empty", "Loading the team's state…" ) );
			return;
		}

		if ( CollabSession.CatchUp is { } catchUp )
			BuildCatchUp( host, layout, catchUp );
		BuildRightNow( host, layout, o );
		if ( o.Blockers is { Count: > 0 } )
			BuildBlockers( host, layout, o.Blockers );
		BuildInProgress( host, layout, o.TasksInProgress );
		BuildRecent( host, layout, o );
	}

	// ------------------------------------------------------------------ while you were away

	private static void BuildCatchUp( Widget host, Layout layout, CatchUp c )
	{
		var card = layout.Add( new Card( host ) { Accent = Theme.Blue } );
		card.Layout.Spacing = 4;
		var head = card.Header( "history", "While you were away", iconColor: Theme.Blue );
		head.Add( UiStyle.Muted( new Label( c.Since is { } since ? $"since {UiStyle.Ago( since )}" : "", card ), small: true ) );
		head.AddStretchCell();
		head.Add( UiStyle.Icon( card, "close", CollabSession.DismissCatchUp, "Dismiss", 22 ) );

		var lines = (c.Summary ?? "").Replace( "\r", "" ).Split( '\n' ).Select( l => l.TrimEnd() ).Where( l => l.Length > 0 ).ToList();
		if ( lines.Count == 0 || (c.Counts?.Total ?? 0) == 0 )
		{
			card.Layout.Add( UiStyle.Muted( new Label( "Nothing new – you're up to date.", card ) ) );
			return;
		}
		var shown = 0;
		foreach ( var raw in lines )
		{
			if ( shown >= 16 )
			{
				card.Layout.Add( UiStyle.Muted( new Label( $"… {lines.Count - shown} more lines", card ), small: true ) );
				break;
			}
			var line = raw.Trim();
			if ( line.StartsWith( '#' ) )
			{
				var title = line.TrimStart( '#' ).Trim();
				if ( shown > 0 || !title.StartsWith( "Catch", StringComparison.OrdinalIgnoreCase ) )
					card.Layout.Add( UiStyle.Bold( new Label( Plain( title ), card ) { WordWrap = true } ) );
			}
			else if ( line.StartsWith( "- " ) || line.StartsWith( "* " ) )
			{
				var row = card.Layout.AddRow();
				row.Spacing = 6;
				row.Add( UiStyle.Muted( new Label( "•", card ) { FixedWidth = 10, Alignment = TextFlag.CenterTop } ) );
				row.Add( new Label( Plain( line[2..] ), card ) { WordWrap = true }, 1 );
			}
			else
				card.Layout.Add( UiStyle.Muted( new Label( Plain( line ), card ) { WordWrap = true } ) );
			shown++;
		}
	}

	/// <summary>Markdown emphasis and code marks are noise in a label.</summary>
	private static string Plain( string text ) => UiStyle.Breakable( text.Replace( "**", "" ).Replace( "`", "" ) );

	// ------------------------------------------------------------------ right now

	private void BuildRightNow( Widget host, Layout layout, Overview o )
	{
		var agents = o.Team
			.Where( t => t.Developer is not null )
			.SelectMany( t => t.Agents.Where( a => a.Online ).Select( a => (Dev: t.Developer, Agent: a) ) )
			.OrderBy( x => x.Dev.Id == CollabSession.MyId )
			.ThenBy( x => x.Agent.Status == "idle" )
			.ToList();

		layout.Add( new SectionHeader( host, "Right now", agents.Count ) );
		var column = layout.AddColumn();
		column.Spacing = 2;
		if ( agents.Count == 0 )
		{
			column.Add( UiStyle.Muted( new Label( "Nobody is connected. Agents appear here when they register.", host ) { WordWrap = true } ) );
			return;
		}
		foreach ( var (dev, agent) in agents )
			column.Add( AgentRow( host, dev, agent ) );

		var offline = o.Team.Where( t => t.Developer is not null && !t.Developer.Online && !t.Agents.Any( a => a.Online ) ).Select( t => t.Developer.Name ).ToList();
		if ( offline.Count > 0 )
			column.Add( UiStyle.Muted( new Label( $"Offline: {string.Join( ", ", offline )}", host ), small: true ) );
	}

	private static Widget AgentRow( Widget parent, Developer dev, Agent a )
	{
		var you = dev.Id == CollabSession.MyId;
		var row = new ClickRow( parent, null, $"{a.ClientType} on {a.Machine}{(string.IsNullOrEmpty( a.Model ) ? "" : $" · {a.Model}")} · seen {UiStyle.Ago( a.LastHeartbeatAt )}" );
		row.Layout.Margin = new Sandbox.UI.Margin( 4, 5, 4, 5 );
		row.Layout.Spacing = 10;
		row.Layout.Add( new Avatar( row, dev.Id, dev.Name, true, 32, a.Status ) );

		var column = row.Layout.AddColumn( 1 );
		column.Spacing = 1;
		var top = column.AddRow();
		top.Spacing = 6;
		top.Add( UiStyle.Bold( new Label( you ? $"{dev.Name} (you)" : dev.Name, row ) ) );
		// The client label wraps and takes the slack, so a narrow dock never forces a wider page.
		var client = top.Add( UiStyle.Muted( new Label( UiStyle.Breakable( a.DisplayLabel ), row ) { WordWrap = true }, small: true ), 1 );
		client.Alignment = TextFlag.LeftCenter;

		var what = a.CurrentTaskId is { } id ? $"#{id} {a.CurrentTaskTitle}" : a.CurrentTaskTitle;
		if ( string.IsNullOrEmpty( what ) )
			what = a.StatusNote;
		column.Add( new Label( string.IsNullOrEmpty( what ) ? "No task" : what, row ) { WordWrap = true } );
		if ( !string.IsNullOrEmpty( a.StatusNote ) && a.StatusNote != what )
			column.Add( UiStyle.Muted( new Label( a.StatusNote, row ) { WordWrap = true }, small: true ) );

		var where = new List<string>();
		if ( !string.IsNullOrEmpty( a.Branch ) )
			where.Add( $"⎇ {a.Branch}" );
		if ( a.Files is { Count: > 0 } )
			where.Add( string.Join( ", ", a.Files.Take( 3 ).Select( System.IO.Path.GetFileName ) ) + (a.Files.Count > 3 ? $" +{a.Files.Count - 3}" : "") );
		if ( where.Count > 0 )
			column.Add( UiStyle.Mono( new Label( UiStyle.Breakable( string.Join( "  ·  ", where ) ), row ) { WordWrap = true } ) );

		row.Layout.Add( new Pill( row, a.Status, UiStyle.StatusColor( a.Status ), column: true ) );
		return row;
	}

	// ------------------------------------------------------------------ blockers

	private static void BuildBlockers( Widget host, Layout layout, List<Blocker> blockers )
	{
		layout.AddSpacingCell( 4 );
		var banner = layout.Add( new Card( host ) { Accent = Theme.Red } );
		banner.Layout.Spacing = 6;
		banner.Header( "report", blockers.Count == 1 ? "1 blocker" : $"{blockers.Count} blockers", iconColor: Theme.Red );
		foreach ( var b in blockers.Take( 6 ) )
		{
			var row = banner.Layout.AddRow();
			row.Spacing = UiStyle.RowSpacing;
			row.AddSpacingCell( 24 );
			Rows.TwoLines( banner, row, b.Title, $"{b.Detail}{(b.At is null ? "" : $" · {UiStyle.Ago( b.At )}")}", boldTitle: true );
		}
	}

	// ------------------------------------------------------------------ in progress

	private void BuildInProgress( Widget host, Layout layout, List<TaskItem> tasks )
	{
		layout.AddSpacingCell( 4 );
		layout.Add( new SectionHeader( host, "In progress", tasks?.Count ?? 0 ) );
		if ( tasks is not { Count: > 0 } )
		{
			layout.Add( UiStyle.Muted( new Label( "Nothing is in progress.", host ) ) );
			return;
		}
		var column = layout.AddColumn();
		column.Spacing = 2;
		foreach ( var task in tasks.Take( 8 ) )
		{
			var t = task;
			column.Add( Rows.Task( host, t, false, () => Main.ShowTask( t.Id ) ) );
		}
	}

	// ------------------------------------------------------------------ recent timeline

	private static void BuildRecent( Widget host, Layout layout, Overview o )
	{
		var entries = new List<(DateTimeOffset? At, Color Dot, bool Hollow, string Title, string Detail, string Mono, Color? MonoColor, Action Clicked)>();

		foreach ( var c in o.RecentCommits ?? new() )
		{
			var url = c.Url;
			entries.Add( (c.At, Theme.Green, true, c.Headline, $"{c.AuthorName ?? c.AuthorLogin}{(string.IsNullOrEmpty( c.Branch ) ? "" : $" · {c.Branch}")}{(c.TaskId is { } id ? $" · #{id}" : "")}", c.Short, Theme.Green, string.IsNullOrEmpty( url ) ? null : () => Browser.Open( url )) );
		}
		foreach ( var c in (o.RecentChanges ?? new()).Where( c => c.Status is null or "completed" ) )
		{
			var breaking = c.BreakingChanges is { Count: > 0 };
			var detail = $"{c.DeveloperName ?? c.DeveloperId}{(c.TaskId is { } id ? $" · #{id}" : "")}";
			entries.Add( (c.CompletedAt, breaking ? Theme.Yellow : Theme.Blue, false, c.Summary, detail, breaking ? $"Breaking: {string.Join( "; ", c.BreakingChanges )}" : null, Theme.Yellow, null) );
		}
		if ( o.LastTest is { } t )
		{
			var passed = t.Status == "passed";
			entries.Add( (t.FinishedAt ?? t.StartedAt, passed ? Theme.Green : Theme.Red, false, $"Test {t.Status}: {t.Description}", string.Join( " · ", new[] { t.Scene, t.Branch }.Where( s => !string.IsNullOrEmpty( s ) ) ), t.Errors.FirstOrDefault(), Theme.Red, null) );
		}

		layout.AddSpacingCell( 4 );
		layout.Add( new SectionHeader( host, "Recent" ) );
		var sorted = entries.OrderByDescending( e => e.At ).Take( 14 ).ToList();
		if ( sorted.Count == 0 )
		{
			layout.Add( UiStyle.Muted( new Label( "No commits or changes yet. Set up the GitHub webhook to see pushes here.", host ) { WordWrap = true } ) );
			return;
		}
		var column = layout.AddColumn();
		column.Spacing = 0;
		for ( var i = 0; i < sorted.Count; i++ )
		{
			var e = sorted[i];
			var row = new TimelineRow( host, e.Dot, i == 0, i == sorted.Count - 1, e.Hollow, e.Clicked );
			var text = row.Layout.AddColumn( 1 );
			text.Spacing = 1;
			var top = text.AddRow();
			top.Spacing = 6;
			if ( e.Hollow && !string.IsNullOrEmpty( e.Mono ) )
			{
				top.Add( UiStyle.Mono( new Label( e.Mono, row ), e.MonoColor ) );
				top.Add( new Label( e.Title ?? "", row ) { WordWrap = true }, 1 );
			}
			else
				top.Add( new Label( e.Title ?? "", row ) { WordWrap = true }, 1 );
			top.Add( UiStyle.Muted( new Label( UiStyle.Ago( e.At ), row ) { Alignment = TextFlag.RightTop }, small: true ) );
			if ( !string.IsNullOrEmpty( e.Detail ) )
				text.Add( UiStyle.Muted( new Label( e.Detail, row ) { WordWrap = true }, small: true ) );
			if ( !e.Hollow && !string.IsNullOrEmpty( e.Mono ) )
				text.Add( UiStyle.Mono( new Label( e.Mono, row ) { WordWrap = true }, e.MonoColor ) );
			column.Add( row );
		}
	}
}
