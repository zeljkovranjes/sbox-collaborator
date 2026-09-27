// Editor gate: drives the library end to end inside a real sbox-dev.exe session against a live
// Collaborator server, the way a developer uses it.
//
// Only runs when dev/editor-gate/run_editor_gate.ps1 launched the editor: COLLAB_GATE_RESULT names
// the result file and a one-shot "<result>.arm" marker next to it must exist (it is consumed on
// first use, so a leaked environment variable never arms a normal session). While armed, the
// per-user settings live in memory only (Settings.InMemory): the run never reads or overwrites the
// developer's real server address and access key. It also refuses to run outside the scratch
// project the driver creates.
//
// Every check is written to the result file the moment it finishes, so the driver reports (and
// stops on) the first failure immediately.

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Collaborator.Net;
using Collaborator.UI;

namespace Collaborator.Dev;

public static class EditorGate
{
	private static bool? _armed;
	private static bool _started;
	private static string _resultPath;
	private static string _outDir;
	private static readonly GateResult Result = new();
	private static Dialog _window;
	private static CollaboratorView _dock;

	/// <summary>True only in an editor launched by the gate driver (checked once, marker consumed).</summary>
	public static bool Armed
	{
		get
		{
			if ( _armed is { } known )
				return known;
			_armed = false;
			try
			{
				var path = Environment.GetEnvironmentVariable( "COLLAB_GATE_RESULT" );
				if ( string.IsNullOrWhiteSpace( path ) )
					return false;
				var marker = path + ".arm";
				if ( !File.Exists( marker ) )
					return false; // leaked environment variable (e.g. a Steam started by an earlier run)
				File.Delete( marker );
				_resultPath = path;
				_outDir = Path.GetDirectoryName( path );
				_armed = true;
			}
			catch ( Exception )
			{
				_armed = false;
			}
			return _armed.Value;
		}
	}

	[EditorEvent.Frame]
	public static void Tick()
	{
		if ( _started )
			return;
		_started = true;
		if ( !Armed )
			return;
		_ = RunAsync();
	}

	// ------------------------------------------------------------------ result file

	private sealed class Check
	{
		public string Name { get; set; }
		public bool Ok { get; set; }
		public string Detail { get; set; }
		public long Ms { get; set; }
	}

	private sealed class GateResult
	{
		public bool Started { get; set; }
		public bool Completed { get; set; }
		public bool Passed { get; set; }
		public string Fatal { get; set; }
		public List<Check> Checks { get; set; } = new();
		public List<string> Screenshots { get; set; } = new();
		public List<string> Toasts { get; set; } = new();
		public List<string> Notes { get; set; } = new();
	}

	private static void Flush()
	{
		try
		{
			var json = JsonSerializer.Serialize( Result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase } );
			File.WriteAllText( _resultPath + ".tmp", json );
			File.Move( _resultPath + ".tmp", _resultPath, true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[collab-gate] cannot write result: {e.Message}" );
		}
	}

	private static void Note( string text )
	{
		Log.Info( $"[collab-gate] {text}" );
		Result.Notes.Add( text );
		Flush();
	}

	private static bool Record( string name, bool ok, string detail, Stopwatch watch )
	{
		Result.Checks.Add( new Check { Name = name, Ok = ok, Detail = detail ?? "", Ms = watch?.ElapsedMilliseconds ?? 0 } );
		Log.Info( $"[collab-gate] {(ok ? "PASS" : "FAIL")} {name}: {detail}" );
		Flush();
		return ok;
	}

	/// <summary>Runs one named check; an exception is a failure with the exception text.</summary>
	private static async Task<bool> Step( string name, Func<Task<string>> body )
	{
		var watch = Stopwatch.StartNew();
		try
		{
			var detail = await body();
			await EditorThread.SwitchToMainThread();
			return Record( name, true, detail, watch );
		}
		catch ( GateFailure e )
		{
			await EditorThread.SwitchToMainThread();
			return Record( name, false, e.Message, watch );
		}
		catch ( Exception e )
		{
			await EditorThread.SwitchToMainThread();
			return Record( name, false, $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}", watch );
		}
	}

	private sealed class GateFailure : Exception
	{
		public GateFailure( string message ) : base( message ) { }
	}

	private static void Expect( bool condition, string message )
	{
		if ( !condition )
			throw new GateFailure( message );
	}

	/// <summary>Waits on the main thread until <paramref name="condition"/> holds.</summary>
	private static async Task<bool> WaitFor( Func<bool> condition, float seconds )
	{
		var watch = Stopwatch.StartNew();
		while ( watch.Elapsed.TotalSeconds < seconds )
		{
			await EditorThread.SwitchToMainThread();
			if ( condition() )
				return true;
			await Task.Delay( 100 );
		}
		await EditorThread.SwitchToMainThread();
		return condition();
	}

	private static async Task Frames( int milliseconds )
	{
		await Task.Delay( milliseconds );
		await EditorThread.SwitchToMainThread();
	}

	// ------------------------------------------------------------------ driver requests

	/// <summary>Asks the driver to do something outside the editor and waits for its answer.</summary>
	private static async Task<bool> Request( string name, string payload, float seconds )
	{
		var req = Path.Combine( _outDir, name + ".req" );
		var ok = Path.Combine( _outDir, name + ".ok" );
		File.Delete( ok );
		File.WriteAllText( req, payload ?? "" );
		var answered = await WaitFor( () => File.Exists( ok ), seconds );
		if ( answered )
			File.Delete( ok );
		return answered;
	}

	/// <summary>Asks the driver for a picture of a window (the main Collaborator window by default).</summary>
	private static async Task Screenshot( string name, Action prepare = null, string windowTitle = null )
	{
		await EditorThread.SwitchToMainThread();
		prepare?.Invoke(); // re-assert what the picture should show
		await Frames( 700 ); // let layout and paint settle
		var target = Path.Combine( _outDir, name + ".png" );
		var payload = windowTitle is null ? target : $"{target}|{windowTitle}";
		if ( await Request( "shot_" + name, payload, 10 ) && File.Exists( target ) )
			Result.Screenshots.Add( name + ".png" );
		else
			Result.Notes.Add( $"screenshot {name} was not captured" );
		Flush();
	}

	// ------------------------------------------------------------------ the run

	private static async Task RunAsync()
	{
		Result.Started = true;
		Flush();
		try
		{
			await RunChecksAsync();
		}
		catch ( Exception e )
		{
			Result.Fatal = $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}";
			Log.Warning( $"[collab-gate] fatal: {e}" );
		}

		await EditorThread.SwitchToMainThread();
		try
		{
			CollabSession.SignOut();
		}
		catch ( Exception )
		{
		}
		Result.Toasts = Toasts.Recent.Select( t => $"{t.Title} | {t.Subtitle}" ).ToList();
		Result.Completed = true;
		Result.Passed = Result.Fatal is null && Result.Checks.Count > 0 && Result.Checks.All( c => c.Ok );
		Flush();
		Note( Result.Passed ? "gate passed, quitting" : "gate failed, quitting" );
		try
		{
			EditorUtility.Quit( true );
		}
		catch ( Exception e )
		{
			Note( $"EditorUtility.Quit threw: {e.Message}" );
		}
		await Task.Delay( 15000 );
		Environment.Exit( Result.Passed ? 0 : 1 ); // backstop when Quit did not end the process
	}

	/// <summary>Reservations the editor made on its own for open scenes/prefabs (not the test's).</summary>
	private static bool IsAutoReserved( Reservation r ) => r.Reason == EditorAutomation.OpenReason || EditorAutomation.AutoReserved.Contains( r.Path, StringComparer.OrdinalIgnoreCase );

	private static string Env( string name ) => Environment.GetEnvironmentVariable( name ) ?? "";

	private static async Task RunChecksAsync()
	{
		var server = Env( "COLLAB_GATE_SERVER" );
		var serverKey = Env( "COLLAB_GATE_SERVER_KEY" );
		var adminToken = Env( "COLLAB_GATE_ADMIN_TOKEN" );
		var mateToken = Env( "COLLAB_GATE_MATE_TOKEN" );
		var projectId = Env( "COLLAB_GATE_PROJECT" );
		var scratchRoot = Env( "COLLAB_GATE_SCRATCH" );

		// 0. Safety: only ever inside the driver's scratch project.
		var ready = await WaitFor( () => Project.Current is not null && !string.IsNullOrEmpty( ProjectPaths.Root ), 120 );
		var root = ProjectPaths.Root ?? "";
		if ( !ready || string.IsNullOrEmpty( scratchRoot ) || !Path.GetFullPath( root ).TrimEnd( '\\' ).Equals( Path.GetFullPath( scratchRoot ).TrimEnd( '\\' ), StringComparison.OrdinalIgnoreCase ) )
		{
			Result.Fatal = $"refusing to run: open project '{root}' is not the gate scratch project '{scratchRoot}'";
			return;
		}
		await Frames( 2000 ); // first frames: asset system and docks settle

		// 1. Isolation of the user's real settings.
		if ( !await Step( "settings.isolated", async () =>
		{
			await Task.CompletedTask;
			Expect( Settings.InMemory, "per-user settings are not in memory during a gate run" );
			Expect( !Settings.HasCredentials, "a gate run started with stored credentials" );
			Expect( CollabSession.State == ConnectionState.SignedOut, $"session state is {CollabSession.State}, expected SignedOut" );
			return "real server/key untouched; session signed out";
		} ) )
			return;

		// 2. The dock, as the user opens it.
		await Step( "window.opens", async () =>
		{
			await EditorThread.SwitchToMainThread();
			// The same path as View ▸ Collaborator.
			_dock = CollaboratorWindow.Open();
			_window = CollaboratorWindow.Dialog;
			Expect( _window.IsValid(), "the Collaborator window was not created" );
			_window.Window.Size = new Vector2( 520, 900 );
			var shown = await WaitFor( () => _dock.IsValid() && _dock.Visible, 5 );
			Expect( shown, "window did not show" );
			Expect( _window.Window.Title == CollaboratorWindow.Title, $"window title is '{_window.Window.Title}'" );
			Expect( CollaboratorWindow.Logo is not null, "the logo icon did not load" );
			Expect( ReferenceEquals( CollaboratorWindow.Open(), _dock ), "opening again created a second window" );
			return "floating window with logo; reopening raises the same window";
		} );
		await Screenshot( "01_signin" );

		// 3. Sign-in wizard backends: server address, server key, device sign-in.
		var probe = new CollabClient( CollabClient.NormalizeAddress( server ) );
		if ( !await Step( "signin.server_address", async () =>
		{
			var info = await probe.GetAsync<ServerInfo>( "/api/server-info" );
			Expect( info is not null && !string.IsNullOrEmpty( info.Name ), "server-info returned nothing" );
			return $"{info.Name} v{info.Version} at {probe.BaseUrl}";
		} ) )
			return;

		await Step( "signin.server_key", async () =>
		{
			var ok = await probe.PostAsync<ServerKeyCheck>( "/api/auth/server-key/check", new { serverKey } );
			Expect( ok?.Valid == true, "valid server key was not accepted" );
			try
			{
				await probe.PostAsync<ServerKeyCheck>( "/api/auth/server-key/check", new { serverKey = "sbj_000000000000_wrongwrongwrongwrongxx" } );
				throw new GateFailure( "a wrong server key was accepted" );
			}
			catch ( CollabException e )
			{
				Expect( e.Code == "invalid_server_key", $"wrong key gave '{e.Code}', expected invalid_server_key" );
			}
			return "valid key accepted, wrong key rejected";
		} );

		string token = null;
		var realGitHub = Env( "COLLAB_GATE_REAL_GITHUB" ) == "1";
		if ( realGitHub && !await Step( "signin.github_real", async () =>
		{
			// A person finishes the GitHub login in the browser that opens (the real product path).
			var start = await probe.PostAsync<DeviceStart>( "/api/auth/device", new { clientName = $"s&box editor on {Environment.MachineName}", clientType = CollabSession.ClientType, serverKey } );
			Expect( !string.IsNullOrEmpty( start?.VerificationUriComplete ), "no verification URL" );
			await EditorThread.SwitchToMainThread();
			Browser.Open( start.VerificationUriComplete );
			Note( $"waiting up to 240 s for a GitHub login in the browser (code {start.UserCode})" );
			var interval = Math.Max( 2, start.Interval );
			var watch = Stopwatch.StartNew();
			while ( watch.Elapsed.TotalSeconds < 240 )
			{
				await Task.Delay( TimeSpan.FromSeconds( interval ) );
				try
				{
					var granted = await probe.PostAsync<DeviceToken>( "/api/auth/device/token", new { deviceCode = start.DeviceCode } );
					if ( granted?.Token?.StartsWith( "sbc_" ) == true )
					{
						token = granted.Token;
						return $"GitHub login completed in {watch.Elapsed.TotalSeconds:0} s; key for {granted.Developer?.Name}";
					}
				}
				catch ( CollabException e ) when ( e.Code is "authorization_pending" )
				{
				}
				catch ( CollabException e ) when ( e.Code is "slow_down" )
				{
					interval += 5;
				}
			}
			throw new GateFailure( "nobody completed the GitHub login within 240 s" );
		} ) )
			return;

		if ( !realGitHub && !await Step( "signin.device_flow", async () =>
		{
			var start = await probe.PostAsync<DeviceStart>( "/api/auth/device", new { clientName = $"s&box editor on {Environment.MachineName}", clientType = CollabSession.ClientType } );
			Expect( !string.IsNullOrEmpty( start?.DeviceCode ) && !string.IsNullOrEmpty( start.UserCode ), "no device code" );
			try
			{
				await probe.PostAsync<DeviceToken>( "/api/auth/device/token", new { deviceCode = start.DeviceCode } );
				throw new GateFailure( "token handed out before approval" );
			}
			catch ( CollabException e )
			{
				Expect( e.Code is "authorization_pending" or "slow_down", $"pending poll gave '{e.Code}'" );
			}
			// The browser half: approve as the gate developer (the dashboard does this after GitHub login).
			var approver = new CollabClient( probe.BaseUrl, adminToken );
			await approver.PostAsync<JsonElement>( "/api/auth/device/approve", new { userCode = start.UserCode } );
			await Task.Delay( 5200 ); // respect the polling interval
			var granted = await probe.PostAsync<DeviceToken>( "/api/auth/device/token", new { deviceCode = start.DeviceCode } );
			Expect( granted?.Token?.StartsWith( "sbc_" ) == true, "approved sign-in returned no access key" );
			token = granted.Token;
			return $"code {start.UserCode} approved; key for {granted.Developer?.Name}";
		} ) )
			return;

		// 4. Connect: presence, project auto-link by package ident, realtime stream.
		if ( !await Step( "session.connects", async () =>
		{
			await EditorThread.SwitchToMainThread();
			await CollabSession.SignInAsync( probe.BaseUrl, token );
			var online = await WaitFor( () => CollabSession.State == ConnectionState.Online, 20 );
			Expect( online, $"state {CollabSession.State}: {CollabSession.LastError}" );
			Expect( CollabSession.Project?.Id == projectId, $"linked project '{CollabSession.Project?.Id}', expected '{projectId}' (package ident match)" );
			Expect( CollabSession.Agent?.ClientType == CollabSession.ClientType, "editor agent was not registered as sbox-editor" );
			Expect( Settings.Token == token && Settings.InMemory, "token not stored in the in-memory settings" );
			return $"online as {CollabSession.Me?.Developer?.Name}, agent {CollabSession.Agent?.Id}, project {CollabSession.Project?.Title}";
		} ) )
			return;

		await Step( "session.live_stream", async () =>
		{
			var live = await WaitFor( () => CollabSession.Live, 15 );
			Expect( live, "the event stream did not connect" );
			var loaded = await WaitFor( () => CollabSession.Overview is not null, 15 );
			Expect( loaded, "overview never loaded" );
			return "SSE connected, overview loaded";
		} );
		await Screenshot( "02_home_empty" );

		// 5. A teammate (another developer's agent) starts working.
		var mate = new CollabClient( probe.BaseUrl, mateToken );
		await Step( "teammate.appears", async () =>
		{
			var agent = await mate.ToolAsync<Agent>( "agent_register", new { project = projectId, clientType = "codex", machine = "LAPTOP", model = "gpt-5-codex", branch = "feat/storms" } );
			mate.AgentId = agent.Id;
			var task = await mate.ToolAsync<TaskItem>( "task_create", new { project = projectId, title = "Storm weather system", priority = "high" } );
			await mate.ToolAsync<JsonElement>( "task_claim", new { taskId = task.Id, branch = "feat/storms" } );
			await mate.ToolAsync<JsonElement>( "agent_set_status", new { status = "working", currentTaskId = task.Id, note = "storm particles", files = new[] { "Assets/weather/storm.vpcf" } } );
			await mate.ToolAsync<JsonElement>( "file_reserve", new { project = projectId, paths = new[] { "Assets/weather/" }, reason = "reworking storms", taskId = task.Id } );
			var seen = await WaitFor( () => CollabSession.Overview?.Team?.Any( t => t.Agents.Any( a => a.Id == agent.Id ) ) == true
				&& CollabSession.Reservations.Any( r => r.Path == "Assets/weather/" ), 10 );
			Expect( seen, "the teammate's agent / reservation did not reach the editor within 10 s" );
			return "teammate agent, claimed task and reservation shown live";
		} );

		await Step( "teammate.message_toast", async () =>
		{
			var before = Toasts.Recent.Count;
			await mate.ToolAsync<JsonElement>( "message_send", new { project = projectId, to = CollabSession.MyId, type = "warning", body = "Avoid Assets/weather/ until the storm task is done." } );
			var toasted = await WaitFor( () => Toasts.Recent.Skip( before ).Any( t => t.Subtitle.Contains( "Avoid Assets/weather/" ) ), 10 );
			Expect( toasted, "no toast for the teammate's message" );
			var unread = await WaitFor( () => CollabSession.UnreadCount > 0, 10 );
			Expect( unread, "unread count did not rise" );
			return "toast shown, unread badge up";
		} );
		await Screenshot( "03_home_team" );

		// 6. The asset guard warns about a teammate's reserved files.
		await Step( "guard.warns_on_reserved_file", async () =>
		{
			var folder = Path.Combine( ProjectPaths.Root, "Assets", "weather" );
			Directory.CreateDirectory( folder );
			var file = Path.Combine( folder, "storm.vmat" );
			var before = Toasts.Recent.Count;
			await File.WriteAllTextAsync( file, "Layer0\n{\n\tshader \"shaders/complex.shader\"\n}\n" );
			await EditorThread.SwitchToMainThread();
			// The file watcher raises content.changed on its own; drive the handler too so the check
			// does not depend on watcher timing.
			var byWatcher = await WaitFor( () => Toasts.Recent.Skip( before ).Any( t => t.Title.Contains( "working on this" ) ), 6 );
			if ( !byWatcher )
				AssetGuard.OnContentChanged( file );
			var warned = await WaitFor( () => Toasts.Recent.Skip( before ).Any( t => t.Title.Contains( "working on this" ) && t.Subtitle.Contains( "storm.vmat" ) ), 5 );
			Expect( warned, "no warning toast for changing a teammate's reserved file" );
			return byWatcher ? "warned via the file watcher" : "warned (handler driven directly; watcher did not fire in 6 s)";
		} );

		// 7. The editor's own actions (what the dock buttons call).
		TaskItem mine = null;
		await Step( "actions.task_lifecycle", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var created = await CollabSession.CreateTaskAsync( "Boat buoyancy rewrite", "Gate-created task", "normal" );
			Expect( created is not null, $"create failed: {CollabSession.StatusText}" );
			var claimed = await CollabSession.ClaimTaskAsync( created );
			Expect( claimed?.OwnerId == CollabSession.MyId, $"claim failed: {CollabSession.StatusText}" );
			var started = await CollabSession.SetTaskStatusAsync( claimed, "in_progress" );
			Expect( started?.Status == "in_progress", $"start failed: {CollabSession.StatusText}" );
			mine = started;
			var listed = await WaitFor( () => CollabSession.Tasks.Any( t => t.Id == created.Id && t.Status == "in_progress" ), 10 );
			Expect( listed, "task list did not refresh" );
			return $"#{created.Id} created → claimed → in progress";
		} );

		await Step( "actions.claim_conflict", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var theirs = CollabSession.Tasks.FirstOrDefault( t => t.OwnerId is not null && t.OwnerId != CollabSession.MyId );
			Expect( theirs is not null, "no teammate task to try" );
			var result = await CollabSession.ClaimTaskAsync( theirs );
			Expect( result is null, "claimed a task another developer owns" );
			Expect( CollabSession.StatusText.Contains( "already", StringComparison.OrdinalIgnoreCase ), $"conflict not explained: '{CollabSession.StatusText}'" );
			return $"refused: {CollabSession.StatusText}";
		} );

		await Step( "actions.reservations", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var ok = await CollabSession.ReserveAsync( new[] { "Code/BoatController.cs", "Assets/ships/" }, "gate: buoyancy" );
			Expect( ok is not null && ok.Reserved.Count == 2 && ok.Conflicts.Count == 0, $"reserve failed: {CollabSession.StatusText}" );
			var clash = await CollabSession.ReserveAsync( new[] { "Assets/weather/rain.vpcf" }, "gate: clash" );
			Expect( clash is not null && clash.Reserved.Count == 0 && clash.Conflicts.Count == 1, "reserving inside the teammate's folder was not refused" );
			var check = await CollabSession.CheckConflictAsync( new[] { "Assets/weather/storm.vmat" } );
			Expect( check is not null && !check.Clear, "conflict check says clear for a reserved file" );
			var seen = await WaitFor( () => CollabSession.Reservations.Count( r => r.DeveloperId == CollabSession.MyId && !IsAutoReserved( r ) ) == 2, 10 );
			Expect( seen, "own reservations not listed" );
			return "reserved 2, teammate folder refused, conflict check warns";
		} );

		await Step( "actions.messages", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var sent = await CollabSession.SendMessageAsync( "mate", "info", "Buoyancy rewrite started; BoatController.cs is reserved." );
			Expect( sent is not null, $"send failed: {CollabSession.StatusText}" );
			await CollabSession.RefreshAsync();
			var unread = CollabSession.Unread.Select( m => m.Id ).ToList();
			Expect( unread.Count > 0, "nothing unread to acknowledge" );
			await CollabSession.AcknowledgeAsync( unread );
			await CollabSession.RefreshAsync();
			Expect( !CollabSession.Unread.Any(), "messages still unread after acknowledging" );
			var inbox = await mate.ToolAsync<List<TeamMessage>>( "message_get_unread", new { project = projectId } );
			Expect( inbox.Any( m => m.Body.Contains( "Buoyancy rewrite started" ) ), "teammate did not receive the editor's message" );
			return "sent to teammate, acknowledged own inbox";
		} );

		await Step( "actions.test_result", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var run = await CollabSession.LogTestAsync( "Gate playtest", "passed", "scenes/minimal.scene", null, null );
			Expect( run?.Status == "passed", $"test log failed: {CollabSession.StatusText}" );
			return "playtest logged";
		} );

		await Step( "assets.sync", async () =>
		{
			await EditorThread.SwitchToMainThread();
			await AssetSync.SyncAsync();
			Expect( AssetSync.LastSync is not null, $"asset sync did not finish: {AssetSync.LastResult}" );
			var found = await mate.ToolAsync<List<JsonElement>>( "asset_search", new { project = projectId, limit = 50 } );
			Expect( found.Count > 0, "server has no assets after the sync" );
			return $"{AssetSync.LastResult}; server lists {found.Count}";
		} );

		// 7b. v1.1: branch suggestion, handoff, catch-up, file history, automation, key encryption.
		await Step( "tasks.branch_suggestion", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var before = Toasts.Recent.Count;
			var created = await CollabSession.CreateTaskAsync( "Sail trim UI", "Gate: branch suggestion", "normal" );
			Expect( created is not null, $"create failed: {CollabSession.StatusText}" );
			var claimed = await CollabSession.ClaimTaskAsync( created );
			Expect( claimed is not null, $"claim failed: {CollabSession.StatusText}" );
			Expect( claimed.SuggestedBranch?.StartsWith( $"task/{claimed.Id}" ) == true, $"suggested branch is '{claimed.SuggestedBranch}', expected task/{claimed.Id}-…" );
			var toasted = await WaitFor( () => Toasts.Recent.Skip( before ).Any( t => t.Title == $"Claimed #{claimed.Id}" && t.Subtitle.Contains( claimed.SuggestedBranch ) ), 5 );
			Expect( toasted, "no toast with the branch after claiming" );
			Expect( CollabSession.BranchCommand( claimed ) == $"git switch -c {claimed.SuggestedBranch}", $"branch command is '{CollabSession.BranchCommand( claimed )}'" );
			await CollabSession.ReleaseTaskAsync( claimed );
			return $"#{claimed.Id} → {claimed.SuggestedBranch}";
		} );

		long handedTask = 0;
		await Step( "tasks.handoff_received", async () =>
		{
			var task = await mate.ToolAsync<TaskItem>( "task_create", new { project = projectId, title = "Ocean foam shader", priority = "normal" } );
			await mate.ToolAsync<JsonElement>( "task_claim", new { taskId = task.Id } );
			await EditorThread.SwitchToMainThread();
			var before = Toasts.Recent.Count;
			await mate.ToolAsync<TaskItem>( "task_handoff", new { taskId = task.Id, summary = "Foam mask works; edge fade not started.", next = "Add the depth fade in Ocean.shader.", gotchas = "Needs the depth texture enabled on the camera.", to = CollabSession.MyId } );
			handedTask = task.Id;
			var toasted = await WaitFor( () => Toasts.Recent.Skip( before ).Any( t => t.Title.Contains( $"task #{task.Id}" ) && t.Title.Contains( "to you" ) ), 10 );
			Expect( toasted, "no 'handed task to you' toast" );
			var full = await CollabSession.GetTaskAsync( task.Id );
			Expect( full?.LastHandoff?.Summary?.Contains( "Foam mask works" ) == true, "handoff note not on the task" );
			Expect( full.Notes?.Any( n => n.Kind == "handoff" ) == true, "task_get has no handoff note" );
			var listed = await WaitFor( () => CollabSession.Tasks.Any( t => t.Id == task.Id && t.LastHandoff is not null ), 10 );
			Expect( listed, "the task list does not show the handoff" );
			return $"#{task.Id} handed over with a note";
		} );
		if ( handedTask > 0 )
			await Screenshot( "14_task_handoff", () => _dock.Main.ShowTask( handedTask ) );

		await Step( "catch_up.summary", async () =>
		{
			await EditorThread.SwitchToMainThread();
			var result = await CollabSession.LoadCatchUpAsync();
			Expect( result is not null, $"team_catch_up failed: {CollabSession.StatusText}" );
			Expect( (result.Counts?.Total ?? 0) > 0, "catch-up counted nothing although the teammate was busy" );
			Expect( !string.IsNullOrWhiteSpace( result.Summary ), "empty summary" );
			Expect( result.Summary.Contains( "Teammate" ) || result.Summary.Contains( "Storm" ) || result.Summary.Contains( "foam", StringComparison.OrdinalIgnoreCase ), "summary doesn't mention the teammate's work" );
			Expect( CollabSession.CatchUp is not null, "the Home card has nothing to show" );
			return $"{result.Counts.Total} items";
		} );
		await Screenshot( "15_catch_up", () => _dock.Main.Show( MainView.HomePage ) );

		const string historyPath = "Assets/weather/storm.vmat";
		await Step( "history.file", async () =>
		{
			var secret = Env( "COLLAB_GATE_WEBHOOK_SECRET" );
			Expect( !string.IsNullOrEmpty( secret ), "COLLAB_GATE_WEBHOOK_SECRET not set by the driver" );
			var sha = Convert.ToHexString( System.Security.Cryptography.RandomNumberGenerator.GetBytes( 20 ) ).ToLowerInvariant();
			var push = new
			{
				@ref = "refs/heads/main",
				before = new string( '0', 40 ),
				after = sha,
				created = false,
				deleted = false,
				forced = false,
				repository = new { full_name = "gatefixture/collabgate", default_branch = "main" },
				sender = new { login = "mategh" },
				pusher = new { name = "mategh" },
				commits = new[]
				{
					new
					{
						id = sha,
						message = "Darker storm material",
						timestamp = DateTimeOffset.UtcNow.ToString( "o" ),
						url = $"https://github.com/gatefixture/collabgate/commit/{sha}",
						author = new { name = "Teammate", username = "mategh" },
						added = Array.Empty<string>(),
						modified = new[] { historyPath },
						removed = Array.Empty<string>(),
					},
				},
			};
			var body = JsonSerializer.Serialize( push );
			var key = System.Text.Encoding.UTF8.GetBytes( secret );
			var signature = "sha256=" + Convert.ToHexString( System.Security.Cryptography.HMACSHA256.HashData( key, System.Text.Encoding.UTF8.GetBytes( body ) ) ).ToLowerInvariant();
			using var http = new System.Net.Http.HttpClient();
			using var request = new System.Net.Http.HttpRequestMessage( System.Net.Http.HttpMethod.Post, $"{probe.BaseUrl}/webhooks/github" );
			request.Headers.Add( "X-GitHub-Event", "push" );
			request.Headers.Add( "X-GitHub-Delivery", Guid.NewGuid().ToString() );
			request.Headers.Add( "X-Hub-Signature-256", signature );
			request.Content = new System.Net.Http.StringContent( body, System.Text.Encoding.UTF8, "application/json" );
			var response = await http.SendAsync( request );
			var answer = await response.Content.ReadAsStringAsync();
			Expect( response.IsSuccessStatusCode, $"webhook answered {(int)response.StatusCode}: {answer}" );
			Expect( answer.Contains( "processed" ), $"webhook not processed: {answer}" );

			var history = await CollabSession.FileHistoryAsync( historyPath );
			Expect( history?.Commits?.Any( c => c.Sha == sha ) == true, "file_history does not list the pushed commit" );
			Expect( history.Reservations.Any( r => r.Path == "Assets/weather/" ), "file_history misses the teammate's reservation" );
			await EditorThread.SwitchToMainThread();
			HistoryWindow.Open( historyPath );
			var loaded = await WaitFor( () => HistoryWindow.Current?.History is not null, 10 );
			Expect( loaded, $"history window did not load: {HistoryWindow.Current?.Error}" );
			return $"{history.Commits.Count} commit(s), {history.Reservations.Count} reservation(s), {history.Tasks.Count} task(s)";
		} );
		await Screenshot( "16_history", windowTitle: $"{HistoryWindow.TitlePrefix}: storm.vmat" );
		HistoryWindow.Current?.GetWindow()?.Close();

		await Step( "auto.reserve_open_scene", async () =>
		{
			await EditorThread.SwitchToMainThread();
			Expect( Settings.AutoReserveOpen, "auto-reserve is switched off" );
			var sceneAsset = AssetSystem.All.FirstOrDefault( a => a.Path?.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) == true && ProjectPaths.ToRepo( a.AbsolutePath ) is not null );
			Expect( sceneAsset is not null, "the scratch project has no scene" );
			var repo = ProjectPaths.ToRepo( sceneAsset.AbsolutePath );

			// Start from "closed" so both directions are exercised.
			var open = SceneEditorSession.All.FirstOrDefault( x => string.Equals( ProjectPaths.FromEditorPath( x.Scene?.Source?.ResourcePath ), repo, StringComparison.OrdinalIgnoreCase ) );
			if ( open is not null )
			{
				open.Destroy();
				var releasedFirst = await WaitFor( () => !EditorAutomation.AutoReserved.Contains( repo, StringComparer.OrdinalIgnoreCase ), 10 );
				Expect( releasedFirst, "closing the already-open scene did not release it" );
			}

			EditorScene.OpenScene( sceneAsset.LoadResource<SceneFile>() );
			var reserved = await WaitFor( () => EditorAutomation.AutoReserved.Contains( repo, StringComparer.OrdinalIgnoreCase )
				&& CollabSession.Reservations.Any( r => r.DeveloperId == CollabSession.MyId && string.Equals( r.Path, repo, StringComparison.OrdinalIgnoreCase ) ), 15 );
			Expect( reserved, $"opening {repo} did not reserve it" );
			var check = await mate.ToolAsync<ConflictCheck>( "file_check_conflict", new { project = projectId, paths = new[] { repo } } );
			Expect( !check.Clear, "the teammate is not warned about the open scene" );

			await EditorThread.SwitchToMainThread();
			var session = SceneEditorSession.All.FirstOrDefault( x => string.Equals( ProjectPaths.FromEditorPath( x.Scene?.Source?.ResourcePath ), repo, StringComparison.OrdinalIgnoreCase ) );
			Expect( session is not null, "no editor session for the opened scene" );
			session.Destroy();
			var released = await WaitFor( () => !EditorAutomation.AutoReserved.Contains( repo, StringComparer.OrdinalIgnoreCase ), 10 );
			Expect( released, "closing the scene did not release it" );
			var gone = await WaitFor( () => !CollabSession.Reservations.Any( r => r.DeveloperId == CollabSession.MyId && string.Equals( r.Path, repo, StringComparison.OrdinalIgnoreCase ) ), 10 );
			Expect( gone, "the server still lists the reservation after closing" );
			EditorScene.OpenScene( sceneAsset.LoadResource<SceneFile>() ); // leave a scene open for the playtest
			return $"{repo}: reserved on open, released on close";
		} );

		await Step( "auto.compile_result", async () =>
		{
			await EditorThread.SwitchToMainThread();
			Expect( Settings.ShareCompile, "compile sharing is switched off" );
			var code = Path.Combine( ProjectPaths.Root, "Code" );
			Directory.CreateDirectory( code );
			var file = Path.Combine( code, "CollabGateBroken.cs" );
			var before = EditorAutomation.LastCompilePost;
			try
			{
				await File.WriteAllTextAsync( file, "public class CollabGateBroken { public void M() { int x = ; } }\n" );
				var broken = await WaitFor( () => EditorAutomation.LastCompilePost is { Status: "error" } post && post != before, 90 );
				Expect( broken, "no 'error' compile result was posted for a broken file" );
				var errors = EditorAutomation.LastCompilePost.Errors;
				Expect( errors.Any( e => e.Contains( "CollabGateBroken.cs" ) ), $"errors don't name the file: {string.Join( " | ", errors )}" );
				var seen = await mate.ToolAsync<List<TestRun>>( "test_get_recent", new { project = projectId, limit = 5 } );
				Expect( seen.Any( t => t.Build == "editor-compile" && t.Status == "error" ), "the teammate can't see the broken compile" );
			}
			finally
			{
				File.Delete( file );
			}
			var fixedUp = await WaitFor( () => EditorAutomation.LastCompilePost is { Status: "passed" }, 90 );
			Expect( fixedUp, "no 'passed' compile result after removing the broken file" );
			return "broken → error posted with the file name; fixed → passed posted";
		} );

		await Step( "auto.playtest_result", async () =>
		{
			await EditorThread.SwitchToMainThread();
			Expect( Settings.SharePlaytests, "playtest sharing is switched off" );
			Expect( SceneEditorSession.Active is not null, "no scene open to play" );
			var before = EditorAutomation.LastPlaytestPost;
			EditorScene.Play( SceneEditorSession.Active );
			var playing = await WaitFor( () => Game.IsPlaying, 30 );
			Expect( playing, "play mode did not start" );
			await Task.Delay( 1500 );
			await EditorThread.SwitchToMainThread();
			Log.Error( "GatePlaytestError: the storm spawner threw" );
			await Task.Delay( 5000 );
			await EditorThread.SwitchToMainThread();
			EditorScene.Stop();
			var posted = await WaitFor( () => EditorAutomation.LastPlaytestPost is { } latest && latest != before, 20 );
			Expect( posted, "no playtest result posted after stopping" );
			var run = EditorAutomation.LastPlaytestPost;
			Expect( run.Build == "playtest", $"build is '{run.Build}'" );
			Expect( run.Status == "failed" && run.Errors.Any( e => e.Contains( "GatePlaytestError" ) ), $"the error logged while playing was not captured (status {run.Status}, errors: {string.Join( " | ", run.Errors )})" );
			return $"{run.Description}: {run.Status}, {run.Errors.Count} error(s)";
		} );

		await Step( "security.key_encrypted", async () =>
		{
			await Task.CompletedTask;
			const string sample = "sbc_000000000000_gateSampleSecretValue1234";
			var stored = SecureStore.Protect( sample );
			Expect( SecureStore.IsProtected( stored ), $"the key was not protected ({SecureStore.Method})" );
			Expect( !stored.Contains( sample ), "the stored value contains the plain key" );
			Expect( SecureStore.Unprotect( stored ) == sample, "decrypting did not give the key back" );
			Expect( SecureStore.Unprotect( sample ) == sample, "a plain (older) value does not pass through" );
			// The portable fallback used on macOS/Linux machines without a keyring.
			var file = SecureStore.ProtectWithKeyFile( sample );
			Expect( file is not null && file.StartsWith( "aes:" ) && !file.Contains( sample ), "the key-file fallback did not encrypt" );
			Expect( SecureStore.UnprotectWithKeyFile( file ) == sample, "the key-file fallback did not decrypt" );
			Expect( SecureStore.Unprotect( file ) == sample, "an aes: value does not decrypt through Unprotect" );
			return $"{SecureStore.Method}: stored as {stored[..Math.Min( 12, stored.Length )]}…; key-file fallback ok";
		} );

		// 8. Every page renders with real data.
		var pages = new (int Index, string Name)[]
		{
			(MainView.HomePage, "04_home"), (MainView.TasksPage, "05_tasks"), (MainView.FilesPage, "06_files"),
			(MainView.MessagesPage, "07_messages"), (MainView.ActivityPage, "08_activity"), (MainView.TestsPage, "09_tests"),
			(MainView.SettingsPage, "10_settings"),
		};
		await CollabSession.RefreshAsync();
		foreach ( var (index, name) in pages )
		{
			await Step( $"ui.page.{name[3..]}", async () =>
			{
				await EditorThread.SwitchToMainThread();
				Expect( _dock.IsValid() && _dock.Main.IsValid() && _dock.Main.Visible, "main view not visible" );
				_dock.Main.Show( index );
				Expect( _dock.Main.CurrentPage == index, "page did not switch" );
				await Frames( 400 );
				// A person clicking the gate window can change the tab meanwhile; that is not a bug.
				var interfered = _dock.Main.CurrentPage != index;
				if ( interfered )
					_dock.Main.Show( index );
				Expect( _dock.Main.CurrentPage == index, "page did not stay selected" );
				return interfered ? "rendered (a click changed the tab meanwhile; re-selected)" : "rendered";
			} );
			await Screenshot( name, () => _dock.Main.Show( index ) );
		}

		// Narrow dock: tabs collapse to icons and no page may be wider than the dock.
		_window.Window.Size = new Vector2( 340, 760 );
		await Frames( 600 );
		foreach ( var (index, name) in pages )
		{
			var page = name[3..];
			await Step( $"ui.narrow.{page}", async () =>
			{
				await EditorThread.SwitchToMainThread();
				_dock.Main.Show( index );
				await Frames( 500 );
				var overflow = _dock.Main.CanvasOverflow;
				Expect( overflow <= 1, $"page is {overflow:0} px wider than a 340 px dock (content gets clipped)" );
				return "fits at 340 px";
			} );
			await Screenshot( $"11_narrow_{page}", () => _dock.Main.Show( index ) );
		}
		_window.Window.Size = new Vector2( 520, 900 );
		await Frames( 400 );

		// 9. Server goes away and comes back.
		await Step( "resilience.server_down", async () =>
		{
			Expect( await Request( "server_stop", "", 20 ), "driver did not stop the server" );
			await EditorThread.SwitchToMainThread();
			_ = CollabSession.RefreshAsync(); // the next poll/heartbeat would do the same
			var offline = await WaitFor( () => CollabSession.State == ConnectionState.Offline, 30 );
			Expect( offline, $"state stayed {CollabSession.State} with the server down" );
			return $"offline: {CollabSession.LastError}";
		} );
		await Screenshot( "12_offline" );
		await Step( "resilience.reconnects", async () =>
		{
			Expect( await Request( "server_start", "", 60 ), "driver did not restart the server" );
			var online = await WaitFor( () => CollabSession.State == ConnectionState.Online && CollabSession.Live, 60 );
			Expect( online, $"did not reconnect: {CollabSession.State} {CollabSession.LastError}" );
			return $"back online as agent {CollabSession.Agent?.Id}";
		} );

		// 10. Finish the task (releases its reservations), sign out, wizard returns.
		await Step( "actions.complete_task", async () =>
		{
			await EditorThread.SwitchToMainThread();
			Expect( mine is not null, "no task to complete" );
			var fresh = CollabSession.Tasks.FirstOrDefault( t => t.Id == mine.Id ) ?? mine;
			var done = await CollabSession.CompleteTaskAsync( fresh, "Gate: buoyancy rewrite done" );
			Expect( done?.Status == "done", $"complete failed: {CollabSession.StatusText}" );
			await CollabSession.ReleaseAsync( new[] { "Code/BoatController.cs", "Assets/ships/" } );
			await CollabSession.RefreshAsync();
			Expect( !CollabSession.Reservations.Any( r => r.DeveloperId == CollabSession.MyId && !IsAutoReserved( r ) ), "own reservations still active" );
			return $"#{mine.Id} done, reservations released";
		} );

		await Step( "session.sign_out", async () =>
		{
			await EditorThread.SwitchToMainThread();
			CollabSession.SignOut();
			var signedOut = await WaitFor( () => CollabSession.State == ConnectionState.SignedOut, 5 );
			Expect( signedOut && !Settings.HasCredentials, "sign out left credentials behind" );
			return "signed out; wizard shown";
		} );
		await Screenshot( "13_signed_out" );
	}
}
