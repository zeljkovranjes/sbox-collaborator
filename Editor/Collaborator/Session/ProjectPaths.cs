using System.IO;

namespace Collaborator;

/// <summary>
/// Converts between the editor's paths and the repository-relative paths the server uses
/// (<c>Assets/Ships/Ship.vmdl</c>, <c>Code/BoatController.cs</c>): forward slashes, relative to
/// the s&amp;box project's root folder.
/// </summary>
public static class ProjectPaths
{
	public static string Root
	{
		get
		{
			try
			{
				return Project.Current?.GetRootPath();
			}
			catch ( Exception )
			{
				return null;
			}
		}
	}

	/// <summary>Repository-relative path of an absolute file, or null when it is outside the project.</summary>
	public static string ToRepo( string absolute )
	{
		var root = Root;
		if ( string.IsNullOrEmpty( absolute ) || string.IsNullOrEmpty( root ) )
			return null;
		try
		{
			var full = Path.GetFullPath( absolute );
			var rel = Path.GetRelativePath( Path.GetFullPath( root ), full );
			if ( rel.StartsWith( ".." ) || Path.IsPathRooted( rel ) )
				return null;
			return rel.Replace( '\\', '/' );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>Repository path of an asset (its source file on disk).</summary>
	public static string ToRepo( Asset asset ) => asset is null ? null : ToRepo( asset.AbsolutePath );

	/// <summary>
	/// Repository path for a file named by an editor event: absolute, an asset path
	/// (<c>scenes/main.scene</c>) or a mount-relative path with a leading slash.
	/// </summary>
	public static string FromEditorPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;
		if ( Path.IsPathRooted( path ) && File.Exists( path ) )
			return ToRepo( path );
		var trimmed = path.Replace( '\\', '/' ).TrimStart( '/' );
		try
		{
			var asset = AssetSystem.FindByPath( trimmed );
			if ( asset is not null )
				return ToRepo( asset.AbsolutePath );
			var assets = Project.Current?.GetAssetsPath();
			if ( !string.IsNullOrEmpty( assets ) )
			{
				var candidate = Path.Combine( assets, trimmed );
				if ( File.Exists( candidate ) )
					return ToRepo( candidate );
			}
			var root = Root;
			if ( !string.IsNullOrEmpty( root ) && File.Exists( Path.Combine( root, trimmed ) ) )
				return ToRepo( Path.Combine( root, trimmed ) );
		}
		catch ( Exception )
		{
		}
		return null;
	}

	/// <summary>True when <paramref name="path"/> is covered by a reservation of <paramref name="reserved"/> (a file, or a folder ending in '/').</summary>
	public static bool Covers( string reserved, bool isDirectory, string path )
	{
		if ( string.IsNullOrEmpty( reserved ) || string.IsNullOrEmpty( path ) )
			return false;
		var a = reserved.Replace( '\\', '/' ).TrimStart( '/' );
		var b = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( isDirectory || a.EndsWith( '/' ) )
		{
			var dir = a.EndsWith( '/' ) ? a : a + "/";
			return b.StartsWith( dir, StringComparison.OrdinalIgnoreCase ) || string.Equals( b + "/", dir, StringComparison.OrdinalIgnoreCase );
		}
		return string.Equals( a, b, StringComparison.OrdinalIgnoreCase );
	}

	private static string _branch;
	private static string _branchRoot;
	private static RealTimeSince _sinceBranch = 1000;

	/// <summary>
	/// The checked-out git branch of the project (reads .git/HEAD, walking up to the repository
	/// root). Cached for a few seconds: the dock asks on every redraw.
	/// </summary>
	public static string GitBranch()
	{
		var root = Root;
		if ( _sinceBranch < 5 && root == _branchRoot )
			return _branch;
		_branch = ReadBranch();
		_branchRoot = root;
		_sinceBranch = 0;
		return _branch;
	}

	private static string _sha;
	private static RealTimeSince _sinceSha = 1000;

	/// <summary>The commit checked out (full sha), or null outside a git repository. Cached briefly.</summary>
	public static string GitHeadSha()
	{
		if ( _sinceSha < 5 )
			return _sha;
		_sinceSha = 0;
		_sha = null;
		try
		{
			var gitDir = FindGitDir();
			if ( gitDir is null )
				return null;
			var head = File.ReadAllText( Path.Combine( gitDir, "HEAD" ) ).Trim();
			if ( !head.StartsWith( "ref: " ) )
				return _sha = head.Length >= 40 ? head[..40] : null;
			var reference = head[5..].Trim();
			var loose = Path.Combine( gitDir, reference.Replace( '/', Path.DirectorySeparatorChar ) );
			if ( File.Exists( loose ) )
				return _sha = File.ReadAllText( loose ).Trim();
			// Packed refs: "<sha> refs/heads/main" lines (worktrees share the common dir's file).
			foreach ( var dir in new[] { gitDir, CommonDir( gitDir ) } )
			{
				var packed = Path.Combine( dir, "packed-refs" );
				if ( !File.Exists( packed ) )
					continue;
				foreach ( var line in File.ReadLines( packed ) )
					if ( line.EndsWith( " " + reference ) && line.Length > 40 )
						return _sha = line[..40];
			}
		}
		catch ( Exception )
		{
		}
		return null;
	}

	private static string CommonDir( string gitDir )
	{
		var common = Path.Combine( gitDir, "commondir" );
		if ( !File.Exists( common ) )
			return gitDir;
		var rel = File.ReadAllText( common ).Trim();
		return Path.IsPathRooted( rel ) ? rel : Path.GetFullPath( Path.Combine( gitDir, rel ) );
	}

	/// <summary>The project's .git directory (walking up; worktrees and submodules follow the "gitdir:" pointer).</summary>
	private static string FindGitDir()
	{
		var dir = Root;
		for ( var i = 0; i < 6 && !string.IsNullOrEmpty( dir ); i++ )
		{
			var git = Path.Combine( dir, ".git" );
			if ( Directory.Exists( git ) )
				return git;
			if ( File.Exists( git ) )
			{
				var pointer = File.ReadAllText( git ).Trim();
				if ( pointer.StartsWith( "gitdir:" ) )
				{
					var gitDir = pointer[7..].Trim();
					return Path.IsPathRooted( gitDir ) ? gitDir : Path.GetFullPath( Path.Combine( dir, gitDir ) );
				}
			}
			dir = Path.GetDirectoryName( dir );
		}
		return null;
	}

	private static string ReadBranch()
	{
		try
		{
			var gitDir = FindGitDir();
			var head = gitDir is null ? null : Path.Combine( gitDir, "HEAD" );
			if ( head is null || !File.Exists( head ) )
				return null;
			var text = File.ReadAllText( head ).Trim();
			const string prefix = "ref: refs/heads/";
			return text.StartsWith( prefix ) ? text[prefix.Length..] : text.Length >= 7 ? text[..7] : null;
		}
		catch ( Exception )
		{
			return null;
		}
	}
}
