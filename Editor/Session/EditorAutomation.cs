using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Collaborator.EditorTools.Net;
using Microsoft.CodeAnalysis;

namespace Collaborator.EditorTools.Session;

/// <summary>
/// What the editor shares on its own while connected: reservations for the scenes and prefabs
/// that are open, the result of every code compile that changes the state (broken ↔ fixed), and
/// each play-mode session with the errors logged while playing. Each has a per-project switch in
/// Settings; everything is quiet (no status-line noise) and failures never interrupt the user.
/// </summary>
public static class EditorAutomation
{
	public const string OpenReason = "Open in the s&box editor";
	private const int ReserveMinutes = 120;
	private const float RenewAfter = 30 * 60;
	private const float RetryRefusedAfter = 5 * 60;

	// ------------------------------------------------------------------ state (read by the gate and Settings page)

	/// <summary>Paths this editor reserved because they are open (repo paths).</summary>
	public static IReadOnlyCollection<string> AutoReserved => Held.Keys;

	/// <summary>The last compile result this editor posted, if any.</summary>
	public static TestRun LastCompilePost { get; private set; }

	/// <summary>The last playtest result this editor posted, if any.</summary>
	public static TestRun LastPlaytestPost { get; private set; }

	private static readonly Dictionary<string, RealTimeSince> Held = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<string, RealTimeSince> Refused = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> Pending = new( StringComparer.OrdinalIgnoreCase );
	private static RealTimeSince _sinceScan = 10;

	private static bool? _compileOk;
	private static bool _compileWasBuilding;
	private static bool _evaluateCompile;
	private static bool _posting;

	private static bool _playing;
	private static Stopwatch _playWatch;
	private static string _playScene;
	private static readonly List<string> PlayErrors = new();
	private static Action<LogEvent> _logHandler;

	// ------------------------------------------------------------------ lifecycle (called by CollabSession)

	public static void OnConnected()
	{
		Held.Clear();
		Refused.Clear();
		Pending.Clear();
		_compileOk = null;
		_evaluateCompile = true; // a project that is already broken when you connect is worth saying
		_sinceScan = 10;
	}

	/// <summary>Best-effort release of what this editor auto-reserved (sign-out, project switch).</summary>
	public static void OnDisconnecting( CollabClient client, string projectId )
	{
		var paths = Held.Keys.ToArray();
		Held.Clear();
		Refused.Clear();
		Pending.Clear();
		StopLogCapture();
		if ( client is null || projectId is null || paths.Length == 0 )
			return;
		_ = client.ToolAsync<JsonElement>( "file_release", new { project = projectId, paths } ).ContinueWith( _ => { } );
	}

	/// <summary>Every frame while online.</summary>
	public static void Tick()
	{
		if ( CollabSession.State != ConnectionState.Online || CollabSession.Client is null || CollabSession.Project is null || !CollabSession.CanWrite )
			return;
		try
		{
			TickReservations();
			TickCompile();
			TickPlaytest();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[collaborator] automation: {e.Message}" );
		}
	}

	// ------------------------------------------------------------------ open scenes / prefabs

	/// <summary>Repo paths of every scene and prefab open in the editor.</summary>
	public static List<string> OpenDocuments()
	{
		var list = new List<string>();
		foreach ( var session in SceneEditorSession.All.ToArray() )
		{
			string resource = null;
			try
			{
				resource = session?.Scene?.Source?.ResourcePath;
			}
			catch ( Exception )
			{
			}
			var repo = ProjectPaths.FromEditorPath( resource );
			if ( repo is not null && !list.Contains( repo, StringComparer.OrdinalIgnoreCase ) )
				list.Add( repo );
		}
		return list;
	}

	private static void TickReservations()
	{
		if ( _sinceScan < 1 )
			return;
		_sinceScan = 0;

		var open = Settings.AutoReserveOpen ? OpenDocuments() : new List<string>();

		// Closed (or the switch was turned off): release.
		var closed = Held.Keys.Where( p => !open.Contains( p, StringComparer.OrdinalIgnoreCase ) ).ToArray();
		if ( closed.Length > 0 )
		{
			foreach ( var p in closed )
				Held.Remove( p );
			_ = Quiet( "file_release", new { project = CollabSession.Project.Id, paths = closed } );
		}

		foreach ( var path in open )
		{
			if ( Pending.Contains( path ) )
				continue;
			if ( Held.TryGetValue( path, out var since ) && since < RenewAfter )
				continue;
			if ( Refused.TryGetValue( path, out var refused ) && refused < RetryRefusedAfter )
				continue;
			_ = ReserveAsync( path );
		}
	}

	private static async Task ReserveAsync( string path )
	{
		var client = CollabSession.Client;
		var project = CollabSession.Project?.Id;
		if ( client is null || project is null )
			return;
		Pending.Add( path );
		try
		{
			var result = await client.ToolAsync<ReserveResult>( "file_reserve", new { project, paths = new[] { path }, reason = OpenReason, ttlMinutes = ReserveMinutes, branch = ProjectPaths.GitBranch() } );
			await EditorThread.SwitchToMainThread();
			if ( result?.Reserved?.Any( r => string.Equals( r.Path, path, StringComparison.OrdinalIgnoreCase ) ) == true )
			{
				Held[path] = 0;
				Refused.Remove( path );
				CollabSession.RequestRefresh( 0.5f );
			}
			else
			{
				// A teammate holds it: AssetGuard already warned about opening it. Never force.
				Refused[path] = 0;
				Held.Remove( path );
			}
		}
		catch ( CollabException )
		{
			await EditorThread.SwitchToMainThread();
			Refused[path] = 0;
		}
		finally
		{
			Pending.Remove( path );
		}
	}

	// ------------------------------------------------------------------ compile results

	private static void TickCompile()
	{
		if ( !Settings.ShareCompile || _posting )
			return;
		var group = Sandbox.Project.CompileGroup;
		if ( group is null )
			return;
		var building = group.IsBuilding;
		var finished = _compileWasBuilding && !building;
		_compileWasBuilding = building;
		if ( building || (!finished && !_evaluateCompile) )
			return;
		_evaluateCompile = false;

		var compilers = group.Compilers?.ToList() ?? new();
		if ( compilers.Count == 0 )
			return;
		var errors = new List<string>();
		var ok = true;
		foreach ( var compiler in compilers )
		{
			if ( compiler.Output is { Successful: false } )
				ok = false;
			foreach ( var d in compiler.Diagnostics ?? Array.Empty<Diagnostic>() )
			{
				if ( d.Severity != DiagnosticSeverity.Error )
					continue;
				ok = false;
				if ( errors.Count >= 10 )
					continue;
				var span = d.Location.GetLineSpan();
				var file = string.IsNullOrEmpty( span.Path ) ? compiler.Name : Path.GetFileName( span.Path );
				errors.Add( $"{file}({span.StartLinePosition.Line + 1}): {d.Id} {d.GetMessage()}" );
			}
		}

		// Say it when it changes (broken ↔ fixed), and the first failure after connecting.
		var changed = _compileOk is { } previous ? previous != ok : !ok;
		_compileOk = ok;
		if ( changed )
			_ = PostCompileAsync( ok, errors );
	}

	private static async Task PostCompileAsync( bool ok, List<string> errors )
	{
		_posting = true;
		try
		{
			var run = await CollabSession.Client.ToolAsync<TestRun>( "test_result", new
			{
				project = CollabSession.Project.Id,
				description = "Editor compile",
				status = ok ? "passed" : "error",
				build = "editor-compile",
				branch = ProjectPaths.GitBranch(),
				// A failure is about the working copy, not a pushed commit: no sha, so the server
				// doesn't announce "commit X is broken" to everyone for a local typo.
				commitSha = ok ? ProjectPaths.GitHeadSha() : null,
				errors = ok ? null : errors,
			} );
			await EditorThread.SwitchToMainThread();
			LastCompilePost = run;
			CollabSession.RequestRefresh( 0.5f );
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			Log.Info( $"[collaborator] compile result not shared: {e.Message}" );
		}
		finally
		{
			_posting = false;
		}
	}

	// ------------------------------------------------------------------ playtests

	private static void TickPlaytest()
	{
		var playing = Game.IsPlaying;
		if ( playing == _playing )
			return;
		_playing = playing;
		if ( playing )
		{
			_playWatch = Stopwatch.StartNew();
			_playScene = ProjectPaths.FromEditorPath( SceneEditorSession.Active?.Scene?.Source?.ResourcePath ) ?? SceneEditorSession.Active?.Scene?.Name;
			lock ( PlayErrors )
				PlayErrors.Clear();
			if ( Settings.SharePlaytests )
				StartLogCapture();
			return;
		}

		StopLogCapture();
		var seconds = _playWatch?.Elapsed.TotalSeconds ?? 0;
		_playWatch = null;
		if ( !Settings.SharePlaytests || seconds < 5 )
			return;
		List<string> errors;
		lock ( PlayErrors )
			errors = PlayErrors.Take( 10 ).ToList();
		_ = PostPlaytestAsync( _playScene, seconds, errors );
	}

	private static async Task PostPlaytestAsync( string scene, double seconds, List<string> errors )
	{
		try
		{
			var duration = seconds < 90 ? $"{seconds:0} s" : $"{seconds / 60:0} min";
			var run = await CollabSession.Client.ToolAsync<TestRun>( "test_result", new
			{
				project = CollabSession.Project.Id,
				description = $"Playtest: {scene ?? "scene"} ({duration})",
				status = errors.Count > 0 ? "failed" : "passed",
				build = "playtest",
				scene,
				branch = ProjectPaths.GitBranch(),
				errors = errors.Count > 0 ? errors : null,
			} );
			await EditorThread.SwitchToMainThread();
			LastPlaytestPost = run;
			CollabSession.RequestRefresh( 0.5f );
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			Log.Info( $"[collaborator] playtest result not shared: {e.Message}" );
		}
	}

	/// <summary>
	/// Collects error-level log messages while playing. The engine's log event is not public, so it
	/// is attached by reflection – only for the length of a play session.
	/// </summary>
	private static void StartLogCapture()
	{
		StopLogCapture();
		try
		{
			var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging" );
			var onMessage = logging?.GetEvent( "OnMessage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
			var add = onMessage?.GetAddMethod( true );
			if ( add is null )
				return;
			_logHandler = OnLog;
			add.Invoke( null, new object[] { _logHandler } );
		}
		catch ( Exception )
		{
			_logHandler = null; // no error capture this session; the playtest is still reported
		}
	}

	private static void StopLogCapture()
	{
		if ( _logHandler is null )
			return;
		try
		{
			var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging" );
			var remove = logging?.GetEvent( "OnMessage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetRemoveMethod( true );
			remove?.Invoke( null, new object[] { _logHandler } );
		}
		catch ( Exception )
		{
		}
		_logHandler = null;
	}

	private static void OnLog( LogEvent e )
	{
		if ( e.Level != LogLevel.Error )
			return;
		var text = e.Message ?? e.Exception?.Message;
		if ( string.IsNullOrWhiteSpace( text ) || text.Contains( "[collaborator]" ) || text.Contains( "[collab-gate]" ) )
			return;
		lock ( PlayErrors )
		{
			if ( PlayErrors.Count < 50 )
				PlayErrors.Add( text.Length > 300 ? text[..300] : text );
		}
	}

	private static async Task Quiet( string tool, object args )
	{
		try
		{
			await CollabSession.Client.ToolAsync<JsonElement>( tool, args );
			await EditorThread.SwitchToMainThread();
			CollabSession.RequestRefresh( 0.5f );
		}
		catch ( CollabException )
		{
		}
	}
}
