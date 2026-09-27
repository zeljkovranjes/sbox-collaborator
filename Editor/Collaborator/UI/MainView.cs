namespace Collaborator.UI;

/// <summary>
/// The signed-in dock, top to bottom: a compact presence header (project and milestone, the
/// online teammates as overlapping avatar chips, the connection light), a tab strip, banners when
/// something needs attention, the selected page as one scrolling column, and the status line.
/// </summary>
public sealed class MainView : Widget
{
	public const int HomePage = 0;
	public const int TasksPage = 1;
	public const int FilesPage = 2;
	public const int MessagesPage = 3;
	public const int ActivityPage = 4;
	public const int TestsPage = 5;
	public const int SettingsPage = 6;

	private readonly TabStrip _tabs;
	private readonly ScrollArea _scroll;
	private readonly List<Page> _pages = new();
	private readonly Label _projectName;
	private readonly Label _projectDetail;
	private readonly AvatarStack _team;
	private readonly StatusDot _connectionDot;
	private readonly Card _offlineBanner;
	private readonly Label _offlineText;
	private readonly Card _noticeBanner;
	private readonly Label _noticeText;
	private readonly StatusDot _statusDot;
	private readonly Label _status;
	private readonly Label _refreshed;
	private string _dismissedNotices;
	private int _seenRevision = -1;

	public int CurrentPage { get; private set; }

	/// <summary>How many pixels the page is wider than the dock can show (0 when it fits).</summary>
	internal float CanvasOverflow => _scroll.IsValid() && _scroll.Canvas.IsValid() ? MathF.Max( 0, _scroll.Canvas.Width - _scroll.Width ) : 0;

	public MainView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 12, 10, 12, 8 );
		Layout.Spacing = 0;

		// Presence header.
		var header = Layout.AddRow();
		header.Spacing = 10;
		var titles = header.AddColumn( 1 );
		titles.Spacing = 0;
		_projectName = titles.Add( UiStyle.Bold( new Label( "", this ), 14 ) );
		_projectDetail = titles.Add( UiStyle.Muted( new Label( "", this ), small: true ) );
		_team = header.Add( new AvatarStack( this ) );
		_connectionDot = header.Add( new StatusDot( this, 12 ) );
		header.Add( UiStyle.Icon( this, "more_horiz", ShowMenu, "More", 24 ) );
		Layout.AddSpacingCell( 8 );

		// Tabs.
		_tabs = Layout.Add( new TabStrip( this ) );
		_tabs.Add( "space_dashboard", "Home", "What's happening right now" );
		_tabs.Add( "task_alt", "Tasks", "The shared task board" );
		_tabs.Add( "lock", "Files", "File and asset reservations" );
		_tabs.Add( "forum", "Messages", "Messages from teammates and agents" );
		_tabs.Add( "timeline", "Activity", "Everything that happened, in order" );
		_tabs.Add( "science", "Tests", "Playtest and build results" );
		_tabs.Add( "settings", "Settings", "Account, project and asset sync", trailing: true );
		_tabs.OnSelected = Show;
		Layout.AddSpacingCell( 10 );

		_offlineBanner = Layout.Add( Banner( "cloud_off", out _offlineText ) );
		_offlineBanner.Layout.Add( UiStyle.Secondary( _offlineBanner, "Retry", "refresh", () => _ = CollabSession.ConnectAsync() ) );
		_noticeBanner = Layout.Add( Banner( "campaign", out _noticeText ) );
		_noticeBanner.Layout.Add( UiStyle.Icon( _noticeBanner, "close", () =>
		{
			_dismissedNotices = string.Join( "|", CollabSession.Notices );
			_noticeBanner.Visible = false;
		}, "Dismiss", 22 ) );

		// The page: one scrolling column.
		_scroll = Layout.Add( new ScrollArea( this ), 1 );
		_scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		var canvas = new Widget( _scroll );
		canvas.Layout = Layout.Column();
		canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 10, 8 ); // clear of the overlay scrollbar
		_pages.Add( canvas.Layout.Add( new HomePage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new TasksPage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new FilesPage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new MessagesPage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new ActivityPage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new TestsPage( canvas, this ) ) );
		_pages.Add( canvas.Layout.Add( new SettingsPage( canvas, this ) ) );
		canvas.Layout.AddStretchCell();
		_scroll.Canvas = canvas;

		// Status line.
		Layout.AddSpacingCell( 6 );
		var status = Layout.AddRow();
		status.Spacing = 8;
		_statusDot = status.Add( new StatusDot( this, 8 ) );
		_status = status.Add( UiStyle.Muted( new Label( "", this ) { WordWrap = false, FixedHeight = 20, MinimumWidth = 40 }, small: true ), 1 );
		_refreshed = status.Add( UiStyle.Muted( new Label( "", this ) { Alignment = TextFlag.RightCenter }, small: true ) );

		Show( HomePage );
	}

	private Card Banner( string icon, out Label text )
	{
		var card = new Card( this, row: true ) { Accent = Theme.Yellow, Visible = false };
		card.Layout.Margin = new Sandbox.UI.Margin( 10, 6, 6, 6 );
		card.Layout.Spacing = 8;
		card.Layout.Add( new IconLabel( card, icon, Theme.Yellow ) );
		text = card.Layout.Add( new Label( "", card ) { WordWrap = true }, 1 );
		return card;
	}

	public void Show( int page )
	{
		page = Math.Clamp( page, 0, _pages.Count - 1 );
		CurrentPage = page;
		if ( _tabs.Selected != page )
			_tabs.Select( page, notify: false );
		for ( var i = 0; i < _pages.Count; i++ )
			_pages[i].Visible = i == page;
		_pages[page].MarkDirty();
		_pages[page].Flush();
	}

	public void Navigate( int page ) => Show( page );

	/// <summary>Opens the task board with <paramref name="taskId"/> expanded.</summary>
	public void ShowTask( long taskId )
	{
		if ( _pages[TasksPage] is TasksPage tasks )
			tasks.SelectedTaskId = taskId;
		Show( TasksPage );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !Visible )
			return;
		if ( _pages.Count > CurrentPage )
			_pages[CurrentPage].Flush();
		if ( _seenRevision == CollabSession.Revision )
			return;
		_seenRevision = CollabSession.Revision;
		Sync();
	}

	/// <summary>Header, banners, badges and the status line from the session.</summary>
	private void Sync()
	{
		var project = CollabSession.Project;
		_projectName.Text = project?.Title ?? "Collaborator";
		var detail = new List<string>();
		if ( !string.IsNullOrEmpty( project?.Milestone ) )
			detail.Add( project.Milestone );
		if ( ProjectPaths.GitBranch() is { } branch )
			detail.Add( $"⎇ {branch}" );
		_projectDetail.Text = string.Join( "  ·  ", detail );
		_projectDetail.Visible = detail.Count > 0;

		// Teammates online (you are implied), each with their busiest agent's status.
		var people = new List<AvatarStack.Person>();
		foreach ( var entry in CollabSession.Overview?.Team ?? new() )
		{
			var dev = entry.Developer;
			var agents = entry.Agents.Where( a => a.Online ).ToList();
			if ( dev is null || dev.Id == CollabSession.MyId || (!dev.Online && agents.Count == 0) )
				continue;
			var busiest = agents.OrderBy( a => StatusRank( a.Status ) ).FirstOrDefault();
			var status = busiest?.Status ?? "idle";
			var doing = busiest is null ? "online" : !string.IsNullOrEmpty( busiest.CurrentTaskTitle ) ? $"{status}: {busiest.CurrentTaskTitle}" : status;
			people.Add( new AvatarStack.Person( dev.Id, dev.Name, status, $"{dev.Name} · {doing}" ) );
		}
		_team.Set( people );

		var state = CollabSession.State;
		var (color, tip) = state switch
		{
			ConnectionState.Online when CollabSession.Live => (Theme.Green, "Live: updates arrive instantly"),
			ConnectionState.Online => (Theme.Blue, "Connected; live stream reconnecting, refreshing every 15 s"),
			ConnectionState.Connecting => (Theme.Yellow, "Connecting…"),
			ConnectionState.Offline => (Theme.Red, CollabSession.LastError ?? "Can't reach the server"),
			_ => (Theme.TextLight, ""),
		};
		_connectionDot.Color = color;
		_connectionDot.Pulse = state == ConnectionState.Online && CollabSession.Live;
		_connectionDot.ToolTip = $"{tip}\n{Settings.ServerUrl}";

		_offlineBanner.Visible = state == ConnectionState.Offline;
		var reason = string.IsNullOrWhiteSpace( CollabSession.LastError ) ? "Can't reach the server." : CollabSession.LastError.TrimEnd( '.' ) + ".";
		_offlineText.Text = $"{reason} What you see may be out of date; retrying automatically.";

		var notices = CollabSession.Notices;
		var showNotices = notices is { Count: > 0 } && string.Join( "|", notices ) != _dismissedNotices;
		_noticeBanner.Visible = showNotices;
		if ( showNotices )
			_noticeText.Text = string.Join( "\n", notices.Take( 4 ) );

		_tabs.SetBadge( HomePage, CollabSession.Overview?.Blockers?.Count ?? 0, Theme.Red );
		_tabs.SetBadge( TasksPage, CollabSession.Tasks.Count( t => t.OwnerId == CollabSession.MyId && t.IsActive ), Theme.Green );
		_tabs.SetBadge( MessagesPage, CollabSession.UnreadCount, CollabSession.Unread.Any( m => m.Type is "blocker" or "warning" ) ? Theme.Red : Theme.Green );
		var lastTest = CollabSession.Overview?.LastTest;
		_tabs.SetBadge( TestsPage, lastTest is not null && lastTest.Status is "failed" or "error" ? 1 : 0, Theme.Red );

		_statusDot.Color = CollabSession.Busy ? Theme.Yellow : CollabSession.StatusColor;
		_status.Text = CollabSession.StatusText;
		_status.ToolTip = CollabSession.StatusText;
		_refreshed.Text = CollabSession.LastRefresh is { } at ? $"updated {UiStyle.Clock( at )}" : "";
	}

	/// <summary>"What changed while I was away?" – shown at the top of Home.</summary>
	public async Task CatchMeUp()
	{
		Show( HomePage );
		await CollabSession.LoadCatchUpAsync();
	}

	private static int StatusRank( string s ) => s switch { "blocked" => 0, "working" => 1, "testing" => 2, "planning" => 3, "reviewing" => 4, _ => 5 };

	private void ShowMenu()
	{
		var menu = new Menu( this );
		if ( CollabSession.Projects.Count > 1 )
		{
			var switcher = menu.AddMenu( "Switch project", "swap_horiz" );
			foreach ( var p in CollabSession.Projects )
			{
				var target = p;
				var option = switcher.AddOption( p.Title, p.Kind == "library" ? "extension" : "sports_esports", () => _ = CollabSession.OpenProjectAsync( target ) );
				option.Checkable = true;
				option.Checked = CollabSession.Project?.Id == p.Id;
			}
		}
		menu.AddOption( "Catch me up", "history", () => _ = CatchMeUp() );
		menu.AddOption( "Refresh", "refresh", () => _ = CollabSession.RefreshAsync() );
		menu.AddOption( "Sync assets now", "sync", () => _ = AssetSync.SyncAsync() ).Enabled = CollabSession.CanWrite && !AssetSync.Running;
		menu.AddOption( "Open dashboard", "open_in_new", () => Browser.Open( Settings.ServerUrl ) );
		menu.AddSeparator();
		menu.AddOption( "Settings", "settings", () => Show( SettingsPage ) );
		menu.AddOption( "Sign out", "logout", () => Dialog.AskConfirm( CollabSession.SignOut, "Sign out of Collaborator? The editor forgets its access key." ) );
		menu.OpenAtCursor();
	}
}
