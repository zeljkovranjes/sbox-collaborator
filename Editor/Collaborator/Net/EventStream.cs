using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Collaborator.Net;

/// <summary>
/// Reads the server's event stream (<c>GET /api/events</c>, Server-Sent Events) on a background
/// task and reconnects with backoff. Events are raised on the reading thread; the owner
/// marshals them to the main thread.
/// </summary>
public sealed class EventStream
{
	private readonly CollabClient _client;
	private readonly string _projectId;
	private CancellationTokenSource _cancel;

	/// <summary>True while a stream is open and delivering.</summary>
	public bool Connected { get; private set; }

	public event Action<ServerEvent> Received;

	/// <summary>Raised when <see cref="Connected"/> changes.</summary>
	public event Action<bool> ConnectionChanged;

	/// <summary>Raised when the server rejects the key (the stream then stops for good).</summary>
	public event Action Unauthorized;

	public EventStream( CollabClient client, string projectId )
	{
		_client = client;
		_projectId = projectId;
	}

	public void Start()
	{
		Stop();
		_cancel = new CancellationTokenSource();
		var token = _cancel.Token;
		_ = Task.Run( () => RunAsync( token ) );
	}

	public void Stop()
	{
		try
		{
			_cancel?.Cancel();
		}
		catch ( ObjectDisposedException )
		{
		}
		_cancel = null;
		SetConnected( false );
	}

	private async Task RunAsync( CancellationToken cancel )
	{
		var delay = 1.0;
		while ( !cancel.IsCancellationRequested )
		{
			try
			{
				await ReadOnceAsync( cancel ).ConfigureAwait( false );
				delay = 1.0;
			}
			catch ( OperationCanceledException ) when ( cancel.IsCancellationRequested )
			{
				break;
			}
			catch ( CollabException e ) when ( e.IsAuth )
			{
				SetConnected( false );
				Unauthorized?.Invoke();
				return;
			}
			catch ( Exception )
			{
				// Server down, proxy timeout, network blip: back off and try again.
			}

			SetConnected( false );
			try
			{
				await Task.Delay( TimeSpan.FromSeconds( delay + Random.Shared.NextDouble() ), cancel ).ConfigureAwait( false );
			}
			catch ( OperationCanceledException )
			{
				break;
			}
			delay = Math.Min( delay * 2, 30 );
		}
		SetConnected( false );
	}

	private async Task ReadOnceAsync( CancellationToken cancel )
	{
		using var request = new HttpRequestMessage( HttpMethod.Get, $"{_client.BaseUrl}/api/events?project={Uri.EscapeDataString( _projectId )}" );
		_client.Authorize( request );
		request.Headers.TryAddWithoutValidation( "Accept", "text/event-stream" );

		using var response = await CollabClient.StreamHttp.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancel ).ConfigureAwait( false );
		if ( (int)response.StatusCode is 401 or 403 )
			throw new CollabException( "unauthorized", "The access key was rejected.", (int)response.StatusCode );
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync( cancel ).ConfigureAwait( false );
		using var reader = new StreamReader( stream, Encoding.UTF8 );
		SetConnected( true );

		// The server pings every 25 s; silence for much longer means a dead connection.
		using var watchdog = CancellationTokenSource.CreateLinkedTokenSource( cancel );
		string eventType = null;
		var data = new StringBuilder();

		while ( !cancel.IsCancellationRequested )
		{
			watchdog.CancelAfter( TimeSpan.FromSeconds( 75 ) );
			var line = await reader.ReadLineAsync( watchdog.Token ).ConfigureAwait( false );
			if ( line is null )
				return; // server closed the stream

			if ( line.Length == 0 )
			{
				if ( data.Length > 0 )
					Dispatch( eventType, data.ToString() );
				eventType = null;
				data.Clear();
				continue;
			}
			if ( line[0] == ':' )
				continue; // ping comment
			var colon = line.IndexOf( ':' );
			var field = colon < 0 ? line : line[..colon];
			var value = colon < 0 ? "" : line[(colon + 1)..].TrimStart( ' ' );
			if ( field == "event" )
				eventType = value;
			else if ( field == "data" )
			{
				if ( data.Length > 0 )
					data.Append( '\n' );
				data.Append( value );
			}
		}
	}

	private void Dispatch( string type, string payload )
	{
		ServerEvent e;
		try
		{
			using var doc = JsonDocument.Parse( payload );
			e = CollabJson.Deserialize<ServerEvent>( doc.RootElement );
			if ( e is null )
				return;
			e.Data = doc.RootElement.TryGetProperty( "data", out var d ) ? d.Clone() : default;
		}
		catch ( JsonException )
		{
			return;
		}
		e.Type ??= type;
		try
		{
			Received?.Invoke( e );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[collaborator] event handler failed: {ex.Message}" );
		}
	}

	private void SetConnected( bool connected )
	{
		if ( Connected == connected )
			return;
		Connected = connected;
		ConnectionChanged?.Invoke( connected );
	}
}
