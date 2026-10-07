using Collaborator.EditorTools.Net;

namespace Collaborator.EditorTools.Session;

/// <summary>
/// Uploads the project's asset list with each asset's references (the asset system knows them:
/// model → materials → textures) so agents can ask the server what uses what. Runs after
/// connecting (at most every ten minutes), when assets change, and on demand.
/// </summary>
public static class AssetSync
{
	public static bool Running { get; private set; }
	public static DateTimeOffset? LastSync { get; private set; }
	public static string LastResult { get; private set; }

	private static RealTimeSince _sinceSync = 10000;
	private static RealTimeUntil _debounce;
	private static bool _pending;
	private const float AutoInterval = 600;

	public static void OnConnected()
	{
		if ( Settings.AutoSyncAssets && _sinceSync > AutoInterval )
			_ = SyncAsync();
	}

	/// <summary>Asset changes schedule a quiet re-sync a little later (edits come in bursts).</summary>
	[Event( "content.changed" )]
	private static void OnContentChanged( string file )
	{
		if ( !Settings.AutoSyncAssets || CollabSession.State != ConnectionState.Online )
			return;
		_pending = true;
		_debounce = 30;
	}

	public static void Tick()
	{
		if ( _pending && _debounce <= 0 && !Running && _sinceSync > 60 )
		{
			_pending = false;
			_ = SyncAsync( quiet: true );
		}
	}

	public static async Task SyncAsync( bool quiet = false )
	{
		var client = CollabSession.Client;
		var project = CollabSession.Project;
		if ( Running || client is null || project is null || !CollabSession.CanWrite )
			return;
		Running = true;
		_sinceSync = 0;
		if ( !quiet )
			CollabSession.SetStatus( "Collecting assets…" );

		try
		{
			// The asset system is main-thread only: collect here, upload in the background.
			var entries = Collect();
			if ( !quiet )
				CollabSession.SetStatus( $"Uploading {entries.Count} assets…" );

			// One request: fullScan retires assets the upload did not mention, so the list must be whole.
			var result = await client.PostAsync<BulkAssetResult>( "/api/assets/bulk", new
			{
				project = project.Id,
				fullScan = true,
				assets = entries,
			} );

			await EditorThread.SwitchToMainThread();
			LastSync = DateTimeOffset.UtcNow;
			LastResult = $"{result?.Upserted ?? 0} assets, {result?.Links ?? 0} links" + (result?.Removed > 0 ? $", {result.Removed} removed" : "");
			if ( !quiet )
				CollabSession.SetStatus( $"Assets synced: {LastResult}.", Theme.Green );
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			LastResult = e.Message;
			if ( !quiet )
				CollabSession.SetStatus( $"Asset sync failed: {e.Message}", Theme.Red );
		}
		catch ( Exception e )
		{
			await EditorThread.SwitchToMainThread();
			LastResult = e.Message;
			Log.Warning( $"[collaborator] asset sync failed: {e}" );
		}
		finally
		{
			Running = false;
			CollabSession.NotifyChanged();
		}
	}

	private sealed class AssetEntryDto
	{
		public string Path { get; set; }
		public List<string> Dependencies { get; set; }
		public Dictionary<string, object> Metadata { get; set; }
	}

	/// <summary>Every local project asset with its direct references, as repository paths.</summary>
	private static List<AssetEntryDto> Collect()
	{
		var list = new List<AssetEntryDto>();
		foreach ( var asset in AssetSystem.All )
		{
			try
			{
				if ( asset is null || asset.IsDeleted || asset.IsCloud || asset.IsProcedural || asset.IsTransient || asset.Package is not null )
					continue;
				var path = ProjectPaths.ToRepo( asset.AbsolutePath );
				if ( path is null )
					continue; // engine/core assets and other mounts

				var dependencies = asset.GetReferences( false )
					.Select( r => ProjectPaths.ToRepo( r?.AbsolutePath ) ?? r?.Path )
					.Where( p => !string.IsNullOrEmpty( p ) && p != path )
					.Distinct( StringComparer.OrdinalIgnoreCase )
					.ToList();

				// Sources the compiler reads (fbx for a vmdl, tga for a vtex) are dependencies too.
				foreach ( var input in asset.GetInputDependencies() ?? Enumerable.Empty<string>() )
				{
					var repo = ProjectPaths.FromEditorPath( input );
					if ( repo is not null && repo != path && !dependencies.Contains( repo, StringComparer.OrdinalIgnoreCase ) )
						dependencies.Add( repo );
				}

				list.Add( new AssetEntryDto
				{
					Path = path,
					Dependencies = dependencies,
					Metadata = new Dictionary<string, object>
					{
						["assetType"] = asset.AssetType?.FriendlyName,
						["compiled"] = asset.IsCompiled,
						["compileFailed"] = asset.IsCompileFailed,
					},
				} );
			}
			catch ( Exception )
			{
				// One broken asset shouldn't stop the scan.
			}
		}
		return list;
	}
}
