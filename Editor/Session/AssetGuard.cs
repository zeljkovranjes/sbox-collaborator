using System.IO;
using Collaborator.EditorTools.Net;
using Collaborator.EditorTools.UI;

namespace Collaborator.EditorTools.Session;

/// <summary>
/// Reservations inside the editor: the asset browser's context menu (reserve, release, who's
/// editing) and warnings when you open or change a file a teammate has reserved. Reservations are
/// advisory, so this warns and never blocks.
/// </summary>
public static class AssetGuard
{
	/// <summary>Repository path of the scene open in the editor (sent with heartbeats).</summary>
	public static string OpenScenePath { get; private set; }

	private static string _lastSceneResource;
	private static readonly Dictionary<string, RealTimeSince> Warned = new( StringComparer.OrdinalIgnoreCase );
	private const float WarnAgainAfter = 300;

	/// <summary>Called every frame while online: notices when a different scene is opened.</summary>
	public static void Tick()
	{
		string resource = null;
		try
		{
			resource = SceneEditorSession.Active?.Scene?.Source?.ResourcePath;
		}
		catch ( Exception )
		{
			// Scene sessions come and go while the editor switches; try again next frame.
		}
		if ( resource == _lastSceneResource )
			return;
		_lastSceneResource = resource;
		OpenScenePath = ProjectPaths.FromEditorPath( resource );
		if ( OpenScenePath is not null )
			WarnIfReserved( OpenScenePath, "You opened" );
	}

	/// <summary>A file changed on disk (saved in the editor, or by an external tool).</summary>
	[Event( "content.changed" )]
	internal static void OnContentChanged( string file )
	{
		if ( CollabSession.State != ConnectionState.Online )
			return;
		var path = ProjectPaths.FromEditorPath( file );
		if ( path is not null )
			WarnIfReserved( path, "You changed" );
	}

	/// <summary>A teammate just reserved something: warn when it covers the scene open here.</summary>
	public static void OnTeammateReserved( Reservation reservation )
	{
		if ( OpenScenePath is null || !ProjectPaths.Covers( reservation.Path, reservation.IsDirectory, OpenScenePath ) )
			return;
		Toasts.Show( $"{reservation.DeveloperName} reserved your open scene",
			$"{reservation.Path}{Describe( reservation )}. Save and coordinate before making more changes.", "lock", Theme.Yellow, 14 );
	}

	private static void WarnIfReserved( string path, string verb )
	{
		var reservation = CollabSession.ReservationsCovering( path ).FirstOrDefault();
		if ( reservation is null )
			return;
		if ( Warned.TryGetValue( path, out var since ) && since < WarnAgainAfter )
			return;
		Warned[path] = 0;
		Toasts.Show( $"{reservation.DeveloperName} is working on this",
			$"{verb} {Path.GetFileName( path )}, reserved by {reservation.DeveloperName}{Describe( reservation )}. Coordinate before changing it.",
			"lock", Theme.Yellow, 14 );
	}

	private static string Describe( Reservation r )
	{
		var who = string.IsNullOrEmpty( r.AgentLabel ) ? "" : $" ({r.AgentLabel})";
		var what = r.TaskId is { } id ? $" for #{id} {r.TaskTitle}" : string.IsNullOrEmpty( r.Reason ) ? "" : $": {r.Reason}";
		return who + what;
	}

	// ------------------------------------------------------------------ context menus

	[Event( "asset.contextmenu", Priority = 70 )]
	private static void OnAssetContextMenu( AssetContextMenu e )
	{
		if ( CollabSession.State != ConnectionState.Online || e.SelectedList is not { Count: > 0 } )
			return;
		var paths = e.SelectedList.Select( x => ProjectPaths.ToRepo( x.AbsolutePath ) ).Where( p => p is not null ).Distinct().ToList();
		if ( paths.Count == 0 )
			return;
		AddOptions( e.Menu, paths, false );
	}

	[Event( "folder.contextmenu", Priority = 70 )]
	private static void OnFolderContextMenu( FolderContextMenu e )
	{
		if ( CollabSession.State != ConnectionState.Online || e.Target is null || !e.Target.Exists )
			return;
		var path = ProjectPaths.ToRepo( e.Target.FullName );
		if ( string.IsNullOrEmpty( path ) || path == "." )
			return;
		AddOptions( e.Menu, new List<string> { path.TrimEnd( '/' ) + "/" }, true );
	}

	private static void AddOptions( Menu parent, List<string> paths, bool folder )
	{
		var menu = parent.AddMenu( "Collaborator", "groups" );
		var others = paths.SelectMany( p => CollabSession.ReservationsCovering( p ) ).DistinctBy( r => r.Id ).ToList();
		foreach ( var r in others.Take( 3 ) )
			menu.AddOption( $"Reserved by {r.DeveloperName}: {r.Path}", "lock" ).Enabled = false;

		var what = folder ? "folder" : paths.Count == 1 ? "file" : $"{paths.Count} files";
		var reserve = menu.AddOption( $"Reserve {what} for editing", "edit_note", () => _ = ReserveAsync( paths ) );
		reserve.Enabled = CollabSession.CanWrite;

		var mine = CollabSession.Reservations.Where( r => r.DeveloperId == CollabSession.MyId && paths.Any( p => string.Equals( r.Path, p, StringComparison.OrdinalIgnoreCase ) ) ).ToList();
		if ( mine.Count > 0 )
			menu.AddOption( "Release my reservation", "lock_open", () => _ = CollabSession.ReleaseAsync( mine.Select( r => r.Path ) ) );

		menu.AddOption( "Who's editing this?", "person_search", () => _ = WhoAsync( paths ) );
		if ( paths.Count == 1 )
		{
			var path = paths[0];
			menu.AddOption( "History…", "history", () => UI.HistoryWindow.Open( path ) );
		}
	}

	private static async Task ReserveAsync( List<string> paths )
	{
		var result = await CollabSession.ReserveAsync( paths, "Editing in the s&box editor" );
		if ( result is null )
			return;
		if ( result.Conflicts is { Count: > 0 } conflicts )
		{
			var first = conflicts[0];
			Toasts.Show( "Already reserved",
				$"{first.Reservation?.DeveloperName ?? "A teammate"} has {first.Reservation?.Path}{(first.Reservation is null ? "" : Describe( first.Reservation ))}. Nothing was reserved for the conflicting paths.",
				"lock", Theme.Yellow, 12 );
			CollabSession.SetStatus( result.Message ?? "Some paths are reserved by a teammate.", Theme.Yellow );
		}
		else
		{
			Toasts.Show( "Reserved", string.Join( ", ", result.Reserved.Select( r => r.Path ).Take( 4 ) ), "edit_note", Theme.Green, 5 );
		}
	}

	private static async Task WhoAsync( List<string> paths )
	{
		var check = await CollabSession.CheckConflictAsync( paths );
		if ( check is null )
			return;
		if ( check.Clear || check.Conflicts.Count == 0 )
		{
			// Own reservations are not conflicts; mention them so "nobody" isn't misleading.
			var mine = paths.SelectMany( p => CollabSession.ReservationsCovering( p, othersOnly: false ) ).Where( r => r.DeveloperId == CollabSession.MyId ).ToList();
			Toasts.Show( mine.Count > 0 ? "Reserved by you" : "Nobody is editing this", mine.Count > 0 ? string.Join( ", ", mine.Select( r => r.Path ) ) : "No active reservations cover it.", "check_circle", Theme.Green, 6 );
			return;
		}
		var lines = check.Conflicts.Take( 3 ).Select( c => $"{c.Reservation?.DeveloperName}: {c.Reservation?.Path}{(c.Reservation is null ? "" : Describe( c.Reservation ))}" );
		Toasts.Show( "Reserved by a teammate", string.Join( "\n", lines ), "lock", Theme.Yellow, 12 );
	}
}
