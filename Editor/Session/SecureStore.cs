using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Collaborator.EditorTools.Session;

/// <summary>
/// Keeps the saved access key out of plain text, on every desktop OS:
/// <list type="bullet">
/// <item>Windows: DPAPI for the current user (stored as <c>dpapi:&lt;base64&gt;</c>).</item>
/// <item>macOS: the login Keychain (stored as a <c>keychain:</c> reference).</item>
/// <item>Linux: the Secret Service keyring via <c>secret-tool</c> (a <c>secret-service:</c> reference).</item>
/// <item>Anywhere those are unavailable: AES-256-GCM with a random key in a per-user file readable
/// only by the owner (<c>aes:&lt;base64&gt;</c>).</item>
/// </list>
/// Copying the editor settings to another machine or account gives nothing usable. Values saved
/// by older versions (plain text) keep working and are re-saved protected.
/// </summary>
public static class SecureStore
{
	private const string DpapiPrefix = "dpapi:";
	private const string KeychainPrefix = "keychain:";
	private const string SecretServicePrefix = "secret-service:";
	private const string AesPrefix = "aes:";
	private const string Service = "sbox-collaborator";
	private const string Account = "access-key";
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes( "collaborator.editor.v1" );

	public static bool IsProtected( string stored ) =>
		stored is not null && (stored.StartsWith( DpapiPrefix, StringComparison.Ordinal ) || stored.StartsWith( KeychainPrefix, StringComparison.Ordinal )
			|| stored.StartsWith( SecretServicePrefix, StringComparison.Ordinal ) || stored.StartsWith( AesPrefix, StringComparison.Ordinal ));

	/// <summary>Which protection <see cref="Protect"/> uses on this machine (shown in Settings).</summary>
	public static string Method => _method ??= DetectMethod();
	private static string _method;

	private static string DetectMethod()
	{
		if ( OperatingSystem.IsWindows() )
			return "Windows DPAPI";
		if ( OperatingSystem.IsMacOS() && ToolExists( "/usr/bin/security" ) )
			return "macOS Keychain";
		if ( OperatingSystem.IsLinux() && FindOnPath( "secret-tool" ) is not null )
			return "Secret Service keyring";
		return "encrypted key file";
	}

	/// <summary>The value to store in the editor settings: an encrypted value or a keyring reference.</summary>
	public static string Protect( string plain )
	{
		if ( string.IsNullOrEmpty( plain ) )
		{
			Forget();
			return plain;
		}
		try
		{
			if ( OperatingSystem.IsWindows() && Dpapi( Encoding.UTF8.GetBytes( plain ), encrypt: true ) is { } cipher )
				return DpapiPrefix + Convert.ToBase64String( cipher );
			if ( OperatingSystem.IsMacOS() && KeychainStore( plain ) )
				return KeychainPrefix + Account;
			if ( OperatingSystem.IsLinux() && SecretToolStore( plain ) )
				return SecretServicePrefix + Account;
		}
		catch ( Exception )
		{
			// fall through to the key file
		}
		return ProtectWithKeyFile( plain ) ?? plain;
	}

	/// <summary>The plain value from what was stored (older plain values pass through unchanged).</summary>
	public static string Unprotect( string stored )
	{
		if ( string.IsNullOrEmpty( stored ) || !IsProtected( stored ) )
			return stored;
		try
		{
			if ( stored.StartsWith( DpapiPrefix, StringComparison.Ordinal ) )
				return OperatingSystem.IsWindows() && Dpapi( Convert.FromBase64String( stored[DpapiPrefix.Length..] ), encrypt: false ) is { } plain ? Encoding.UTF8.GetString( plain ) : null;
			if ( stored.StartsWith( KeychainPrefix, StringComparison.Ordinal ) )
				return OperatingSystem.IsMacOS() ? KeychainLookup() : null;
			if ( stored.StartsWith( SecretServicePrefix, StringComparison.Ordinal ) )
				return OperatingSystem.IsLinux() ? SecretToolLookup() : null;
			return UnprotectWithKeyFile( stored );
		}
		catch ( Exception )
		{
			return null; // another user's / machine's value: treat as signed out
		}
	}

	/// <summary>Removes the key from the OS keyring (sign-out).</summary>
	public static void Forget()
	{
		try
		{
			if ( OperatingSystem.IsMacOS() && ToolExists( "/usr/bin/security" ) )
				Run( "/usr/bin/security", new[] { "delete-generic-password", "-s", Service, "-a", Account }, null, out _ );
			else if ( OperatingSystem.IsLinux() && FindOnPath( "secret-tool" ) is { } tool )
				Run( tool, new[] { "clear", "service", Service, "account", Account }, null, out _ );
		}
		catch ( Exception )
		{
		}
	}

	// ------------------------------------------------------------------ AES key file (any OS)

	/// <summary>Encrypts with the per-user key file (also the fallback on Windows/macOS/Linux).</summary>
	internal static string ProtectWithKeyFile( string plain )
	{
		try
		{
			var key = UserKey( create: true );
			if ( key is null )
				return null;
			var nonce = RandomNumberGenerator.GetBytes( 12 );
			var data = Encoding.UTF8.GetBytes( plain );
			var cipher = new byte[data.Length];
			var tag = new byte[16];
			using ( var aes = new AesGcm( key, 16 ) )
				aes.Encrypt( nonce, data, cipher, tag, Entropy );
			return AesPrefix + Convert.ToBase64String( nonce.Concat( tag ).Concat( cipher ).ToArray() );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	internal static string UnprotectWithKeyFile( string stored )
	{
		var key = UserKey( create: false );
		if ( key is null || !stored.StartsWith( AesPrefix, StringComparison.Ordinal ) )
			return null;
		var blob = Convert.FromBase64String( stored[AesPrefix.Length..] );
		if ( blob.Length < 28 )
			return null;
		var plain = new byte[blob.Length - 28];
		using ( var aes = new AesGcm( key, 16 ) )
			aes.Decrypt( blob.AsSpan( 0, 12 ), blob.AsSpan( 28 ), blob.AsSpan( 12, 16 ), plain, Entropy );
		return Encoding.UTF8.GetString( plain );
	}

	private static string KeyFilePath
	{
		get
		{
			var root = Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create );
			if ( string.IsNullOrEmpty( root ) )
				root = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".config" );
			return Path.Combine( root, "sbox-collaborator", "key.bin" );
		}
	}

	/// <summary>The random 256-bit key, created once with owner-only permissions.</summary>
	private static byte[] UserKey( bool create )
	{
		var path = KeyFilePath;
		if ( File.Exists( path ) )
		{
			var existing = File.ReadAllBytes( path );
			return existing.Length == 32 ? existing : null;
		}
		if ( !create )
			return null;
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		var key = RandomNumberGenerator.GetBytes( 32 );
		File.WriteAllBytes( path, key );
		if ( !OperatingSystem.IsWindows() )
			File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite );
		return key;
	}

	// ------------------------------------------------------------------ macOS Keychain

	/// <summary>
	/// Stores through <c>security -i</c> so the key travels on stdin and never shows up in the
	/// process list (command-line arguments are visible to other users on macOS).
	/// </summary>
	private static bool KeychainStore( string plain )
	{
		if ( !ToolExists( "/usr/bin/security" ) || plain.Contains( '"' ) || plain.Contains( '\n' ) )
			return false;
		var command = $"add-generic-password -U -s {Service} -a {Account} -l \"s&box Collaborator access key\" -w \"{plain}\"\n";
		return Run( "/usr/bin/security", new[] { "-i" }, command, out _ ) == 0 && KeychainLookup() == plain;
	}

	private static string KeychainLookup()
	{
		if ( !ToolExists( "/usr/bin/security" ) )
			return null;
		return Run( "/usr/bin/security", new[] { "find-generic-password", "-s", Service, "-a", Account, "-w" }, null, out var output ) == 0 ? output.TrimEnd( '\r', '\n' ) : null;
	}

	// ------------------------------------------------------------------ Linux Secret Service

	private static bool SecretToolStore( string plain )
	{
		if ( FindOnPath( "secret-tool" ) is not { } tool )
			return false;
		// secret-tool reads the secret from stdin.
		return Run( tool, new[] { "store", "--label=s&box Collaborator access key", "service", Service, "account", Account }, plain, out _ ) == 0
			&& SecretToolLookup() == plain;
	}

	private static string SecretToolLookup()
	{
		if ( FindOnPath( "secret-tool" ) is not { } tool )
			return null;
		return Run( tool, new[] { "lookup", "service", Service, "account", Account }, null, out var output ) == 0 && output.Length > 0 ? output.TrimEnd( '\r', '\n' ) : null;
	}

	// ------------------------------------------------------------------ process helpers

	private static bool ToolExists( string path ) => File.Exists( path );

	private static string FindOnPath( string name )
	{
		foreach ( var dir in (Environment.GetEnvironmentVariable( "PATH" ) ?? "").Split( Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
		{
			var candidate = Path.Combine( dir, name );
			if ( File.Exists( candidate ) )
				return candidate;
		}
		return null;
	}

	/// <summary>Runs a tool with a short timeout; stdin is closed after <paramref name="input"/>.</summary>
	private static int Run( string file, string[] args, string input, out string output )
	{
		output = "";
		var info = new ProcessStartInfo( file )
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach ( var arg in args )
			info.ArgumentList.Add( arg );
		using var process = Process.Start( info );
		if ( process is null )
			return -1;
		if ( input is not null )
			process.StandardInput.Write( input );
		process.StandardInput.Close();
		var stdout = process.StandardOutput.ReadToEndAsync();
		if ( !process.WaitForExit( 5000 ) )
		{
			try
			{
				process.Kill();
			}
			catch ( Exception )
			{
			}
			return -1;
		}
		output = stdout.Result;
		return process.ExitCode;
	}

	// ------------------------------------------------------------------ Windows DPAPI

	[StructLayout( LayoutKind.Sequential )]
	private struct DataBlob
	{
		public int Size;
		public IntPtr Data;
	}

	private const int UiForbidden = 0x1;

	[DllImport( "crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
	private static extern bool CryptProtectData( ref DataBlob input, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output );

	[DllImport( "crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
	private static extern bool CryptUnprotectData( ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output );

	[DllImport( "kernel32.dll" )]
	private static extern IntPtr LocalFree( IntPtr memory );

	private static byte[] Dpapi( byte[] data, bool encrypt )
	{
		var input = Pin( data );
		var entropy = Pin( Entropy );
		var output = new DataBlob();
		try
		{
			var ok = encrypt
				? CryptProtectData( ref input, "Collaborator access key", ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output )
				: CryptUnprotectData( ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output );
			if ( !ok || output.Data == IntPtr.Zero )
				return null;
			var result = new byte[output.Size];
			Marshal.Copy( output.Data, result, 0, output.Size );
			return result;
		}
		finally
		{
			Marshal.FreeHGlobal( input.Data );
			Marshal.FreeHGlobal( entropy.Data );
			if ( output.Data != IntPtr.Zero )
				LocalFree( output.Data );
		}
	}

	private static DataBlob Pin( byte[] data )
	{
		var blob = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal( Math.Max( 1, data.Length ) ) };
		Marshal.Copy( data, 0, blob.Data, data.Length );
		return blob;
	}
}
