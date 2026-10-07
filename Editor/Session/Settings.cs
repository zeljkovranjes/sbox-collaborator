namespace Collaborator.EditorTools.Session;

/// <summary>
/// Where the library keeps what it remembers. The server address and the personal access key are
/// per user (editor cookies, outside every project) so they never end up in a repository; the
/// chosen server project is remembered per s&amp;box project.
/// </summary>
public static class Settings
{
	private const string ServerKey = "collaborator.server";
	private const string TokenKey = "collaborator.token";
	private const string DeveloperKey = "collaborator.developer";
	private const string ProjectKey = "collaborator.project";
	private const string AutoSyncKey = "collaborator.assets.autosync";

	/// <summary>
	/// Editor-gate runs keep the per-user values in memory, so an automated run never reads or
	/// overwrites the developer's real server address and access key.
	/// </summary>
	private static readonly Dictionary<string, string> Memory = Dev.EditorGate.Armed ? new() : null;

	public static bool InMemory => Memory is not null;

	private static string GetUser( string key )
	{
		if ( Memory is not null )
			return Memory.TryGetValue( key, out var value ) ? value : null;
		return EditorCookie.GetString( key, null );
	}

	private static void SetUser( string key, string value )
	{
		if ( Memory is not null )
			Memory[key] = value ?? "";
		else
			EditorCookie.SetString( key, value ?? "" );
	}

	public static string ServerUrl
	{
		get => GetUser( ServerKey );
		set => SetUser( ServerKey, value );
	}

	/// <summary>The personal access key (<c>sbc_…</c>). The server key used to join is never stored.</summary>
	private static string _cachedStored;
	private static string _cachedPlain;

	public static string Token
	{
		get
		{
			var stored = GetUser( TokenKey );
			if ( Memory is not null || string.IsNullOrEmpty( stored ) )
				return stored;
			// Keys saved by older versions are plain text: encrypt them on first read.
			if ( !SecureStore.IsProtected( stored ) )
			{
				var protectedValue = SecureStore.Protect( stored );
				if ( protectedValue != stored )
					SetUser( TokenKey, protectedValue );
				return stored;
			}
			if ( stored != _cachedStored )
			{
				_cachedStored = stored;
				_cachedPlain = SecureStore.Unprotect( stored );
			}
			return _cachedPlain;
		}
		set => SetUser( TokenKey, Memory is not null ? value : SecureStore.Protect( value ) );
	}

	/// <summary>Display name of the signed-in developer, shown before the server answers.</summary>
	public static string DeveloperName
	{
		get => GetUser( DeveloperKey );
		set => SetUser( DeveloperKey, value );
	}

	/// <summary>The server project this s&amp;box project is linked to.</summary>
	public static string ProjectId
	{
		get => ProjectCookie.GetString( ProjectKey, null );
		set => ProjectCookie.SetString( ProjectKey, value ?? "" );
	}

	/// <summary>Upload the asset list automatically after connecting (at most every few minutes).</summary>
	public static bool AutoSyncAssets
	{
		get => ProjectCookie.Get( AutoSyncKey, true );
		set => ProjectCookie.Set( AutoSyncKey, value );
	}

	/// <summary>Reserve the scenes and prefabs open in the editor while they are open.</summary>
	public static bool AutoReserveOpen
	{
		get => ProjectCookie.Get( "collaborator.autoreserve", true );
		set => ProjectCookie.Set( "collaborator.autoreserve", value );
	}

	/// <summary>Post the editor's compile results (broken / fixed) to the team.</summary>
	public static bool ShareCompile
	{
		get => ProjectCookie.Get( "collaborator.share.compile", true );
		set => ProjectCookie.Set( "collaborator.share.compile", value );
	}

	/// <summary>Post play-mode sessions (with any errors logged while playing) to the team.</summary>
	public static bool SharePlaytests
	{
		get => ProjectCookie.Get( "collaborator.share.playtests", true );
		set => ProjectCookie.Set( "collaborator.share.playtests", value );
	}

	public static bool HasCredentials => !string.IsNullOrEmpty( ServerUrl ) && !string.IsNullOrEmpty( Token );

	public static void ClearCredentials()
	{
		Token = "";
		DeveloperName = "";
	}
}
