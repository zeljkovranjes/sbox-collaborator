using System.Text.Json;
using Collaborator.Net;

namespace Collaborator;

public enum ConnectionState
{
	/// <summary>No access key yet: the sign-in wizard shows.</summary>
	SignedOut,
	Connecting,
	/// <summary>Signed in, but this s&amp;box project isn't linked to a server project yet.</summary>
	PickProject,
	Online,
	/// <summary>Signed in, server unreachable: retrying with backoff.</summary>
	Offline,
}

/// <summary>
/// The editor's connection to the Collaborator server: who is signed in, the linked project, the
/// editor's own agent session (presence + heartbeats), the shared state the dock shows, and the
/// event stream that keeps it fresh. State only changes on the main thread; widgets watch
/// <see cref="Revision"/> and redraw when it moves.
/// </summary>
public static partial class CollabSession
{
	public const string ClientType = "sbox-editor";

	public static ConnectionState State { get; private set; } = ConnectionState.SignedOut;
	public static CollabClient Client { get; private set; }
	public static Me Me { get; private set; }
	public static List<CollabProject> Projects { get; private set; } = new();
	public static CollabProject Project { get; private set; }
	public static Agent Agent { get; private set; }

	public static Overview Overview { get; private set; }
	public static List<TaskItem> Tasks { get; private set; } = new();
	public static List<Reservation> Reservations { get; private set; } = new();
	public static List<TeamMessage> Messages { get; private set; } = new();
	public static List<Activity> Activity { get; private set; } = new();
	public static List<TestRun> Tests { get; private set; } = new();
	public static List<string> Notices { get; private set; } = new();

	/// <summary>Moves on every change; widgets compare it each frame instead of subscribing.</summary>
	public static int Revision { get; private set; }

	/// <summary>Moves only when shared data (tasks, team, reservations…) changed: lists rebuild on it.</summary>
	public static int DataRevision { get; private set; }

	public static string LastError { get; private set; }
	public static DateTimeOffset? LastRefresh { get; private set; }

	/// <summary>True while the realtime stream is open (otherwise the dock polls).</summary>
	public static bool Live => _stream?.Connected ?? false;

	/// <summary>True before the first frame when a key is stored: the dock shows "connecting", not the wizard.</summary>
	public static bool AboutToConnect => !_started && Settings.HasCredentials;

	public static bool Busy => _busy > 0;
	public static string StatusText { get; private set; } = "";
	public static Color StatusColor { get; private set; } = Color.Gray;

	public static string MyId => Me?.Developer?.Id;
	public static bool CanWrite => Me?.CanWrite ?? false;

	private static EventStream _stream;
	private static bool _started;
	private static bool _connecting;
	private static bool _refreshing;
	private static int _busy;
	private static RealTimeSince _sinceHeartbeat;
	private static RealTimeSince _sinceRefresh;
	private static RealTimeUntil _refreshAt;
	private static bool _refreshQueued;
	private static RealTimeUntil _retryAt;
	private static float _retryDelay = 3f;

	// ------------------------------------------------------------------ frame loop

	[EditorEvent.Frame]
	private static void Frame()
	{
		if ( !_started )
		{
			_started = true;
			if ( Settings.HasCredentials )
				_ = ConnectAsync();
		}

		switch ( State )
		{
			case ConnectionState.Online:
				if ( _sinceHeartbeat > 60 )
					_ = HeartbeatAsync();
				// Live: the stream tells us when to refresh, a slow poll is only a safety net.
				if ( _sinceRefresh > (Live ? 90 : 15) )
					RequestRefresh();
				if ( _refreshQueued && _refreshAt <= 0 && !_refreshing )
					_ = RefreshAsync();
				AssetGuard.Tick();
				AssetSync.Tick();
				break;

			case ConnectionState.Offline:
				if ( !_connecting && _retryAt <= 0 )
					_ = ConnectAsync();
				break;
		}
	}

	/// <summary>A hotload swaps this assembly; the old stream's reader would keep running old code.</summary>
	[EditorEvent.Hotload]
	private static void OnHotload()
	{
		if ( State != ConnectionState.Online || Client is null || Project is null )
			return;
		StartStream();
	}

	private static void Bump() => Revision++;

	private static void BumpData()
	{
		DataRevision++;
		Revision++;
	}

	/// <summary>Tell the pages that something they show changed (asset sync finished…).</summary>
	public static void NotifyChanged() => BumpData();

	public static void SetStatus( string text, Color? color = null )
	{
		StatusText = text ?? "";
		StatusColor = color ?? Theme.TextLight;
		Bump();
	}

	// ------------------------------------------------------------------ connect

	/// <summary>Stores a personal access key (after the device sign-in or a pasted key) and connects.</summary>
	public static async Task SignInAsync( string serverUrl, string token, Developer developer = null )
	{
		Settings.ServerUrl = serverUrl;
		Settings.Token = token;
		Settings.DeveloperName = developer?.Name ?? "";
		await ConnectAsync();
	}

	public static void SignOut()
	{
		StopStream();
		Settings.ClearCredentials();
		Client = null;
		Me = null;
		Project = null;
		Agent = null;
		Overview = null;
		Tasks = new();
		Reservations = new();
		Messages = new();
		Activity = new();
		Tests = new();
		Notices = new();
		State = ConnectionState.SignedOut;
		BumpData();
		SetStatus( "Signed out." );
	}

	public static async Task ConnectAsync()
	{
		if ( _connecting )
			return;
		if ( !Settings.HasCredentials )
		{
			State = ConnectionState.SignedOut;
			Bump();
			return;
		}

		_connecting = true;
		if ( State != ConnectionState.Offline )
			State = ConnectionState.Connecting;
		Bump();

		var client = new CollabClient( Settings.ServerUrl, Settings.Token );
		client.Notices += OnNotices;
		try
		{
			var me = await client.GetAsync<Me>( "/api/me" );
			var projects = await client.ToolAsync<List<CollabProject>>( "project_list" ) ?? new();
			await EditorThread.SwitchToMainThread();

			Client = client;
			Me = me;
			Projects = projects;
			LastError = null;
			_retryDelay = 3f;
			if ( me?.Developer is not null )
				Settings.DeveloperName = me.Developer.Name;

			var project = ChooseProject( projects );
			if ( project is null )
			{
				State = ConnectionState.PickProject;
				SetStatus( projects.Count == 0 ? "The server has no projects yet. Create one in the dashboard." : "Choose the server project for this s&box project." );
				return;
			}
			await OpenProjectAsync( project );
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			HandleConnectFailure( e );
		}
		catch ( Exception e )
		{
			await EditorThread.SwitchToMainThread();
			HandleConnectFailure( new CollabException( "error", e.Message, 0 ) );
		}
		finally
		{
			_connecting = false;
			BumpData();
		}
	}

	private static void HandleConnectFailure( CollabException e )
	{
		LastError = e.Message;
		if ( e.IsAuth || e.Status == 403 )
		{
			StopStream();
			Settings.Token = "";
			State = ConnectionState.SignedOut;
			SetStatus( "Your access key was rejected or revoked. Sign in again.", Theme.Red );
			return;
		}
		StopStream();
		State = ConnectionState.Offline;
		_retryAt = _retryDelay;
		_retryDelay = MathF.Min( _retryDelay * 2, 60 );
		SetStatus( $"{e.Message} Retrying…", Theme.Yellow );
	}

	/// <summary>The remembered project, then one whose package ident matches this s&amp;box project, then the only one.</summary>
	private static CollabProject ChooseProject( List<CollabProject> projects )
	{
		var remembered = Settings.ProjectId;
		if ( !string.IsNullOrEmpty( remembered ) && projects.FirstOrDefault( p => p.Id == remembered ) is { } saved )
			return saved;

		string ident = null;
		try
		{
			var config = Sandbox.Project.Current?.Config;
			ident = config is null ? null : $"{config.Org}.{config.Ident}";
			var bare = config?.Ident;
			var match = projects.FirstOrDefault( p => !string.IsNullOrEmpty( p.PackageIdent ) &&
				(string.Equals( p.PackageIdent, ident, StringComparison.OrdinalIgnoreCase ) || string.Equals( p.PackageIdent, bare, StringComparison.OrdinalIgnoreCase )) )
				?? projects.FirstOrDefault( p => string.Equals( p.Id, bare, StringComparison.OrdinalIgnoreCase ) );
			if ( match is not null )
				return match;
		}
		catch ( Exception )
		{
		}
		return projects.Count == 1 ? projects[0] : null;
	}

	/// <summary>Links this s&amp;box project to <paramref name="project"/>, registers presence and starts the stream.</summary>
	public static async Task OpenProjectAsync( CollabProject project )
	{
		if ( Client is null || project is null )
			return;
		StopStream();
		Project = project;
		Settings.ProjectId = project.Id;
		Overview = null;
		BumpData();

		try
		{
			var agent = await Client.ToolAsync<Agent>( "agent_register", new
			{
				project = project.Id,
				clientType = ClientType,
				machine = Environment.MachineName,
				label = "s&box editor",
				branch = ProjectPaths.GitBranch(),
				resumeAgentId = Agent?.ProjectId == project.Id ? Agent?.Id : null,
			} );
			await EditorThread.SwitchToMainThread();
			Agent = agent;
			Client.AgentId = agent?.Id;
			_sinceHeartbeat = 0;
			State = ConnectionState.Online;
			SetStatus( $"Connected to {project.Title}.", Theme.Green );
			StartStream();
			await RefreshAsync();
			AssetSync.OnConnected();
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			HandleConnectFailure( e );
		}
	}

	private static void OnNotices( List<string> notices )
	{
		EditorThread.Post( () =>
		{
			Notices = notices;
			Bump();
		} );
	}

	// ------------------------------------------------------------------ presence

	private static async Task HeartbeatAsync()
	{
		_sinceHeartbeat = 0;
		var client = Client;
		if ( client is null || Agent is null )
			return;
		var scene = AssetGuard.OpenScenePath;
		try
		{
			var result = await client.ToolAsync<HeartbeatResult>( "agent_heartbeat", new
			{
				status = "working",
				branch = ProjectPaths.GitBranch(),
				files = scene is null ? new string[0] : new[] { scene },
			} );
			await EditorThread.SwitchToMainThread();
			if ( result?.Agent is not null )
				Agent = result.Agent;
			if ( Overview is not null && result is not null )
				Overview.UnreadMessages = result.UnreadMessages;
			if ( result?.Notices is { Count: > 0 } )
				Notices = result.Notices;
			Bump();
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			if ( e.Code == "not_found" && Project is not null )
				await OpenProjectAsync( Project ); // server forgot the session (restart, long sleep): register again
			else if ( e.IsNetwork || e.IsAuth )
				HandleConnectFailure( e );
		}
	}

	// ------------------------------------------------------------------ realtime

	private static void StartStream()
	{
		StopStream();
		if ( Client is null || Project is null )
			return;
		_stream = new EventStream( Client, Project.Id );
		_stream.Received += e => EditorThread.Post( () => OnServerEvent( e ) );
		_stream.ConnectionChanged += _ => EditorThread.Post( Bump );
		_stream.Unauthorized += () => EditorThread.Post( () => HandleConnectFailure( new CollabException( "unauthorized", "The access key was rejected.", 401 ) ) );
		_stream.Start();
	}

	private static void StopStream()
	{
		_stream?.Stop();
		_stream = null;
	}

	private static void OnServerEvent( ServerEvent e )
	{
		if ( State != ConnectionState.Online || e is null )
			return;
		if ( e.ProjectId is not null && Project is not null && e.ProjectId != Project.Id )
			return;

		var mine = e.Actor?.DeveloperId is { } actor && actor == MyId;
		try
		{
			switch ( e.Type )
			{
				case "message_received":
				{
					var message = CollabJson.Deserialize<TeamMessage>( e.Data );
					if ( message is not null && message.FromDeveloperId != MyId && (message.Broadcast || message.ToDeveloperId == MyId || (Agent is not null && message.ToAgentId == Agent.Id)) )
						Toasts.Message( message );
					break;
				}
				case "build_broken":
					if ( !mine )
						Toasts.Show( "Build broken", DescribeTest( e.Data ), "error", Theme.Red, 12 );
					break;
				case "file_reserved":
				{
					var reservation = CollabJson.Deserialize<Reservation>( e.Data );
					if ( reservation is not null && reservation.DeveloperId != MyId )
						AssetGuard.OnTeammateReserved( reservation );
					break;
				}
				case "change_completed":
					if ( !mine && e.Data.ValueKind == JsonValueKind.Object && e.Data.TryGetProperty( "breakingChanges", out var breaking ) && breaking.ValueKind == JsonValueKind.Array && breaking.GetArrayLength() > 0 )
					{
						var change = CollabJson.Deserialize<Change>( e.Data );
						Toasts.Show( "Breaking change", $"{change?.DeveloperName ?? "A teammate"}: {change?.Summary}", "warning", Theme.Yellow, 12 );
					}
					break;
			}
		}
		catch ( JsonException )
		{
			// An event shape we don't know: the refresh below still picks the change up.
		}

		RequestRefresh( 0.4f );
	}

	private static string DescribeTest( JsonElement data )
	{
		var run = CollabJson.Deserialize<TestRun>( data );
		if ( run is null )
			return "A test failed.";
		var commit = string.IsNullOrEmpty( run.CommitSha ) ? "" : $" on {run.CommitSha[..Math.Min( 7, run.CommitSha.Length )]}";
		return $"{run.Description}{commit}";
	}

	// ------------------------------------------------------------------ refresh

	/// <summary>Refreshes everything shortly (several requests within the delay collapse into one).</summary>
	public static void RequestRefresh( float delay = 0f )
	{
		if ( !_refreshQueued || _refreshAt > delay )
			_refreshAt = delay;
		_refreshQueued = true;
	}

	private static Task _refreshRun = Task.CompletedTask;

	/// <summary>
	/// Reloads everything the dock shows. Awaiting it guarantees the data reflects every change made
	/// before the call: a refresh already in flight is awaited, then a fresh one runs.
	/// </summary>
	public static async Task RefreshAsync()
	{
		while ( !_refreshRun.IsCompleted )
		{
			await _refreshRun;
			await EditorThread.SwitchToMainThread();
		}
		_refreshRun = RefreshOnceAsync();
		await _refreshRun;
		await EditorThread.SwitchToMainThread();
	}

	private static async Task RefreshOnceAsync()
	{
		var client = Client;
		var project = Project;
		if ( client is null || project is null )
			return;
		_refreshing = true;
		_refreshQueued = false;
		_sinceRefresh = 0;
		try
		{
			var id = project.Id;
			var overviewTask = client.GetAsync<Overview>( $"/api/overview?project={Uri.EscapeDataString( id )}" );
			var tasksTask = Try( client.ToolAsync<List<TaskItem>>( "task_list", new { project = id, limit = 200 } ) );
			var reservationsTask = Try( client.ToolAsync<List<Reservation>>( "file_list_reservations", new { project = id } ) );
			var messagesTask = Try( client.GetAsync<List<TeamMessage>>( $"/api/messages?project={Uri.EscapeDataString( id )}&limit=50" ) );
			var activityTask = Try( client.ToolAsync<List<Activity>>( "activity_recent", new { project = id, limit = 100 } ) );
			var testsTask = Try( client.ToolAsync<List<TestRun>>( "test_get_recent", new { project = id, limit = 20 } ) );

			var overview = await overviewTask;
			await Task.WhenAll( tasksTask, reservationsTask, messagesTask, activityTask, testsTask );
			await EditorThread.SwitchToMainThread();
			if ( Project?.Id != id )
				return; // switched projects meanwhile

			Overview = overview;
			if ( overview?.Project is not null )
				Project = overview.Project;
			Tasks = tasksTask.Result ?? Tasks;
			Reservations = reservationsTask.Result ?? overview?.Reservations ?? Reservations;
			Messages = messagesTask.Result ?? Messages;
			Activity = activityTask.Result ?? overview?.Activity ?? Activity;
			Tests = testsTask.Result ?? Tests;
			LastRefresh = DateTimeOffset.UtcNow;
			if ( State == ConnectionState.Online && StatusColor == Theme.Yellow )
				SetStatus( "Back online.", Theme.Green );
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			if ( e.IsNetwork || e.IsAuth )
				HandleConnectFailure( e );
			else
				SetStatus( e.Message, Theme.Red );
		}
		finally
		{
			_refreshing = false;
			BumpData();
		}
	}

	/// <summary>A secondary list that may fail on its own (older server, missing scope) without failing the refresh.</summary>
	private static async Task<T> Try<T>( Task<T> task ) where T : class
	{
		try
		{
			return await task;
		}
		catch ( CollabException )
		{
			return null;
		}
	}

	// ------------------------------------------------------------------ actions

	/// <summary>
	/// Runs a change against the server with the status line showing progress, then refreshes.
	/// Returns the result, or default when it failed (the error is shown in the status line).
	/// </summary>
	public static async Task<T> RunAsync<T>( string working, Func<CollabClient, Task<T>> action, string done = null )
	{
		var client = Client;
		if ( client is null )
			return default;
		_busy++;
		SetStatus( working, Theme.TextLight );
		try
		{
			var result = await action( client );
			await EditorThread.SwitchToMainThread();
			SetStatus( done ?? "Done.", Theme.Green );
			RequestRefresh();
			return result;
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			SetStatus( e.Message, e.Code == "conflict" ? Theme.Yellow : Theme.Red );
			if ( e.IsNetwork )
				HandleConnectFailure( e );
			return default;
		}
		catch ( Exception e )
		{
			await EditorThread.SwitchToMainThread();
			SetStatus( e.Message, Theme.Red );
			return default;
		}
		finally
		{
			_busy--;
			Bump();
		}
	}

	public static Task<TaskItem> ClaimTaskAsync( TaskItem task, bool force = false )
		=> RunAsync( $"Claiming #{task.Id}…", c => c.ToolAsync<TaskItem>( "task_claim", new { taskId = task.Id, force = force ? true : (bool?)null, branch = ProjectPaths.GitBranch() } ), $"Claimed #{task.Id} {task.Title}." );

	public static Task<TaskItem> SetTaskStatusAsync( TaskItem task, string status )
		=> RunAsync( $"Updating #{task.Id}…", c => c.ToolAsync<TaskItem>( "task_update", new { taskId = task.Id, status, expectedVersion = task.Version } ), $"#{task.Id} is now {TaskStatusText( status ).ToLowerInvariant()}." );

	public static Task<TaskItem> BlockTaskAsync( TaskItem task, string reason )
		=> RunAsync( $"Blocking #{task.Id}…", c => c.ToolAsync<TaskItem>( "task_block", new { taskId = task.Id, reason } ), $"#{task.Id} marked blocked." );

	public static Task<TaskItem> ReleaseTaskAsync( TaskItem task )
		=> RunAsync( $"Releasing #{task.Id}…", c => c.ToolAsync<TaskItem>( "task_release", new { taskId = task.Id } ), $"Released #{task.Id}." );

	public static Task<TaskItem> CompleteTaskAsync( TaskItem task, string summary )
		=> RunAsync( $"Completing #{task.Id}…", c => c.ToolAsync<TaskItem>( "task_complete", new { taskId = task.Id, summary } ), $"Completed #{task.Id}." );

	public static Task<TaskItem> CreateTaskAsync( string title, string description, string priority )
		=> RunAsync( "Creating task…", c => c.ToolAsync<TaskItem>( "task_create", new { project = Project.Id, title, description = string.IsNullOrWhiteSpace( description ) ? null : description, priority, status = "available" } ), $"Created “{title}”." );

	public static Task<ReserveResult> ReserveAsync( IEnumerable<string> paths, string reason, bool force = false )
		=> RunAsync( "Reserving…", c => c.ToolAsync<ReserveResult>( "file_reserve", new { project = Project.Id, paths = paths.ToArray(), reason, force = force ? true : (bool?)null, branch = ProjectPaths.GitBranch() } ), "Reserved." );

	public static Task<ReleaseResult> ReleaseAsync( IEnumerable<string> paths )
		=> RunAsync( "Releasing…", c => c.ToolAsync<ReleaseResult>( "file_release", new { project = Project.Id, paths = paths.ToArray() } ), "Released." );

	public static Task<ConflictCheck> CheckConflictAsync( IEnumerable<string> paths )
		=> RunAsync( "Checking…", c => c.ToolAsync<ConflictCheck>( "file_check_conflict", new { project = Project.Id, paths = paths.ToArray() } ), "Checked." );

	public static Task<TeamMessage> SendMessageAsync( string to, string type, string body )
		=> to is null
			? RunAsync( "Sending…", c => c.ToolAsync<TeamMessage>( "message_broadcast", new { project = Project.Id, type, body } ), "Sent to everyone." )
			: RunAsync( "Sending…", c => c.ToolAsync<TeamMessage>( "message_send", new { project = Project.Id, to, type, body } ), "Sent." );

	public static Task<JsonElement> AcknowledgeAsync( IEnumerable<long> ids )
		=> RunAsync( "Acknowledging…", c => c.ToolAsync<JsonElement>( "message_acknowledge", new { messageIds = ids.ToArray() } ), "Acknowledged." );

	public static Task<TestRun> LogTestAsync( string description, string status, string scene, string build, List<string> errors )
		=> RunAsync( "Logging test result…", c => c.ToolAsync<TestRun>( "test_result", new
		{
			project = Project.Id,
			description,
			status,
			scene = string.IsNullOrWhiteSpace( scene ) ? null : scene,
			build = string.IsNullOrWhiteSpace( build ) ? null : build,
			branch = ProjectPaths.GitBranch(),
			errors = errors is { Count: > 0 } ? errors : null,
		} ), status == "passed" ? "Test logged: passed." : "Test logged: failed. The team was warned." );

	// ------------------------------------------------------------------ lookups

	/// <summary>Active reservations covering <paramref name="repoPath"/>, excluding the developer's own when <paramref name="othersOnly"/>.</summary>
	public static IEnumerable<Reservation> ReservationsCovering( string repoPath, bool othersOnly = true )
		=> Reservations.Where( r => ProjectPaths.Covers( r.Path, r.IsDirectory, repoPath ) && (!othersOnly || r.DeveloperId != MyId) );

	public static IEnumerable<TeamMessage> Unread => Messages.Where( m => m.AckedAt is null && m.FromDeveloperId != MyId && (m.Broadcast || m.ToDeveloperId == MyId || (Agent is not null && m.ToAgentId == Agent.Id)) );

	public static int UnreadCount => Math.Max( Overview?.UnreadMessages ?? 0, Unread.Count() );

	public static string TaskStatusText( string status ) => status switch
	{
		"backlog" => "Backlog",
		"available" => "Available",
		"claimed" => "Claimed",
		"in_progress" => "In progress",
		"blocked" => "Blocked",
		"review" => "In review",
		"done" => "Done",
		_ => status ?? "",
	};
}
