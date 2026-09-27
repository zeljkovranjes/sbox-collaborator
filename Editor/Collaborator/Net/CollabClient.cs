using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Collaborator.Net;

/// <summary>A failed call: the server's error code and message, or a transport failure.</summary>
public sealed class CollabException : Exception
{
	/// <summary>Server error code (<c>conflict</c>, <c>unauthorized</c>…), or <c>network</c> / <c>timeout</c>.</summary>
	public string Code { get; }

	/// <summary>HTTP status, 0 when the server could not be reached.</summary>
	public int Status { get; }

	public JsonElement Details { get; }

	public CollabException( string code, string message, int status, JsonElement details = default ) : base( message )
	{
		Code = code;
		Status = status;
		Details = details;
	}

	public bool IsNetwork => Status == 0;
	public bool IsAuth => Status == 401 || Code is "unauthorized";
}

/// <summary>
/// Talks to one Collaborator server over HTTP (docs/http-api.md). Every call runs off the main
/// thread and returns the envelope's <c>result</c>; failures throw <see cref="CollabException"/>
/// with the server's error code. Safe to use from any thread.
/// </summary>
public sealed class CollabClient
{
	private static readonly HttpClient Http = new( new SocketsHttpHandler
	{
		PooledConnectionLifetime = TimeSpan.FromMinutes( 5 ),
		AutomaticDecompression = DecompressionMethods.All,
	} )
	{ Timeout = TimeSpan.FromSeconds( 20 ) };

	/// <summary>Long-lived requests (the event stream) must not hit the normal timeout.</summary>
	internal static readonly HttpClient StreamHttp = new( new SocketsHttpHandler
	{
		PooledConnectionLifetime = TimeSpan.FromMinutes( 30 ),
	} )
	{ Timeout = Timeout.InfiniteTimeSpan };

	public string BaseUrl { get; }
	public string Token { get; set; }

	/// <summary>The editor's agent session, sent as <c>X-Collab-Agent</c> so calls count as heartbeats.</summary>
	public string AgentId { get; set; }

	/// <summary>Notices the server attached to the last responses (unread blockers, breaking changes).</summary>
	public event Action<List<string>> Notices;

	public CollabClient( string baseUrl, string token = null )
	{
		BaseUrl = baseUrl.TrimEnd( '/' );
		Token = token;
	}

	/// <summary>
	/// Turns what a person typed (<c>mcp.example.com</c>, <c>192.168.1.5:8080</c>, a full URL) into
	/// a base URL. HTTPS unless the address is an IP or localhost, where plain HTTP is allowed
	/// when the person wrote it.
	/// </summary>
	public static string NormalizeAddress( string input )
	{
		var text = (input ?? "").Trim().TrimEnd( '/' );
		if ( text.Length == 0 )
			return null;
		if ( !text.Contains( "://" ) )
			text = "https://" + text;
		if ( !Uri.TryCreate( text, UriKind.Absolute, out var uri ) || (uri.Scheme != "https" && uri.Scheme != "http") )
			return null;
		if ( uri.Scheme == "http" && !IsLocalOrIp( uri.Host ) )
			return null;
		var path = uri.AbsolutePath.TrimEnd( '/' );
		return $"{uri.Scheme}://{uri.Authority}{path}";
	}

	public static bool IsLocalOrIp( string host ) => host == "localhost" || IPAddress.TryParse( host.Trim( '[', ']' ), out _ );

	// ------------------------------------------------------------------ requests

	public Task<T> GetAsync<T>( string path, CancellationToken cancel = default )
		=> SendAsync<T>( HttpMethod.Get, path, null, cancel );

	public Task<T> PostAsync<T>( string path, object body, CancellationToken cancel = default )
		=> SendAsync<T>( HttpMethod.Post, path, body ?? new { }, cancel );

	/// <summary>Calls a tool over HTTP: <c>POST /api/tools/&lt;name&gt;</c> with the tool's arguments.</summary>
	public Task<T> ToolAsync<T>( string tool, object args = null, CancellationToken cancel = default )
		=> SendAsync<T>( HttpMethod.Post, $"/api/tools/{tool}", args ?? new { }, cancel );

	private async Task<T> SendAsync<T>( HttpMethod method, string path, object body, CancellationToken cancel )
	{
		using var request = new HttpRequestMessage( method, BaseUrl + path );
		Authorize( request );
		if ( body is not null )
			request.Content = new StringContent( CollabJson.Serialize( body ), Encoding.UTF8, "application/json" );

		HttpResponseMessage response;
		try
		{
			response = await Http.SendAsync( request, cancel ).ConfigureAwait( false );
		}
		catch ( OperationCanceledException ) when ( !cancel.IsCancellationRequested )
		{
			throw new CollabException( "timeout", "The server did not answer in time.", 0 );
		}
		catch ( HttpRequestException e )
		{
			throw new CollabException( "network", $"Can't reach the server ({e.GetBaseException().Message}).", 0 );
		}

		using ( response )
		{
			var text = await response.Content.ReadAsStringAsync( cancel ).ConfigureAwait( false );
			return Unwrap<T>( response.StatusCode, text );
		}
	}

	internal void Authorize( HttpRequestMessage request )
	{
		if ( !string.IsNullOrEmpty( Token ) )
			request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", Token );
		if ( !string.IsNullOrEmpty( AgentId ) )
			request.Headers.TryAddWithoutValidation( "X-Collab-Agent", AgentId );
		request.Headers.TryAddWithoutValidation( "X-Collab-Client", "sbox-editor" );
	}

	/// <summary>Reads the <c>{ ok, result, error, notices }</c> envelope.</summary>
	private T Unwrap<T>( HttpStatusCode status, string text )
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse( string.IsNullOrWhiteSpace( text ) ? "{}" : text );
		}
		catch ( JsonException )
		{
			// A proxy error page, or not a Collaborator server at all.
			var code = (int)status;
			throw new CollabException( code >= 500 ? "internal" : "bad_response",
				code is >= 200 and < 300 ? "The server's answer was not understood. Is this a Collaborator server?" : $"The server answered HTTP {code}.", code );
		}

		using ( doc )
		{
			var root = doc.RootElement;
			var ok = root.ValueKind == JsonValueKind.Object && root.TryGetProperty( "ok", out var okProp ) && okProp.ValueKind == JsonValueKind.True;

			if ( root.ValueKind == JsonValueKind.Object && root.TryGetProperty( "notices", out var notices ) && notices.ValueKind == JsonValueKind.Array && notices.GetArrayLength() > 0 )
			{
				var list = notices.EnumerateArray().Where( n => n.ValueKind == JsonValueKind.String ).Select( n => n.GetString() ).ToList();
				if ( list.Count > 0 )
					Notices?.Invoke( list );
			}

			if ( ok && (int)status < 400 )
			{
				if ( !root.TryGetProperty( "result", out var result ) )
					return default;
				try
				{
					return CollabJson.Deserialize<T>( result );
				}
				catch ( JsonException e )
				{
					throw new CollabException( "bad_response", $"Unexpected answer from the server: {e.Message}", (int)status );
				}
			}

			var errorCode = "error";
			var message = $"The server answered HTTP {(int)status}.";
			JsonElement details = default;
			if ( root.ValueKind == JsonValueKind.Object && root.TryGetProperty( "error", out var error ) && error.ValueKind == JsonValueKind.Object )
			{
				if ( error.TryGetProperty( "code", out var c ) && c.ValueKind == JsonValueKind.String )
					errorCode = c.GetString();
				if ( error.TryGetProperty( "message", out var m ) && m.ValueKind == JsonValueKind.String )
					message = m.GetString();
				if ( error.TryGetProperty( "details", out var d ) )
					details = d.Clone();
			}
			throw new CollabException( errorCode, message, (int)status, details );
		}
	}
}
