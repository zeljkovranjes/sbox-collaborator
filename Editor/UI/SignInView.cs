using Collaborator.EditorTools.Net;
using Collaborator.EditorTools.Session;
using Collaborator.EditorTools.UI.Widgets;

namespace Collaborator.EditorTools.UI;

/// <summary>
/// First-run sign-in, three steps: the server address (checked with <c>/api/server-info</c>), the
/// server key the admin gave you (skippable when you already have an account), then GitHub login
/// in the browser (device flow). The editor ends up with its own personal access key; the server
/// key is never stored. Pasting an access key is kept as a small advanced fallback.
/// </summary>
public sealed class SignInView : Widget
{
	private enum Step { Server, Key, GitHub }

	private Step _step;
	private string _serverUrl;
	private ServerInfo _serverInfo;
	private string _serverKey;
	private bool _busy;
	private CancellationTokenSource _polling;
	private DeviceStart _device;

	private readonly StepIndicator _steps;
	private readonly Widget _stepHost;
	private readonly Card _advanced;
	private readonly LineEdit _tokenInput;
	private readonly Label _advancedError;
	private readonly LinkLabel _advancedLink;

	// Step widgets (rebuilt when the step changes).
	private LineEdit _addressInput;
	private LineEdit _keyInput;
	private Label _error;
	private Button _continue;

	public SignInView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 16;

		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		var canvas = new Widget( scroll );
		canvas.Layout = Layout.Column();
		scroll.Canvas = canvas;

		canvas.Layout.AddStretchCell();
		var row = canvas.Layout.AddRow();
		row.AddStretchCell();
		var column = row.AddColumn();
		column.Spacing = 14;
		row.AddStretchCell();
		canvas.Layout.AddStretchCell();

		var inner = new Widget( canvas ) { MaximumWidth = 380, MinimumWidth = 250 };
		inner.Layout = Layout.Column();
		inner.Layout.Spacing = 18;
		column.Add( inner );

		_steps = inner.Layout.Add( new StepIndicator( inner, "Server", "Server key", "GitHub" ) );
		_stepHost = inner.Layout.Add( new Widget( inner ) );
		_stepHost.Layout = Layout.Column();

		var linkRow = inner.Layout.AddRow();
		linkRow.AddStretchCell();
		_advancedLink = linkRow.Add( new LinkLabel( inner, "Use an access key instead", ToggleAdvanced ) );
		linkRow.AddStretchCell();

		_advanced = inner.Layout.Add( new Card( inner ) { Visible = false } );
		_advanced.Header( "key", "Access key", "A personal key (sbc_…) created in the dashboard", Theme.TextLight );
		_advanced.Layout.Add( UiStyle.Muted( new Label( "Paste a personal access key from the dashboard's Keys page. It uses the server address from step 1.", _advanced ) { WordWrap = true }, small: true ) );
		var tokenRow = _advanced.Layout.AddRow();
		tokenRow.Spacing = 6;
		_tokenInput = tokenRow.Add( UiStyle.Framed( new LineEdit( _advanced ) { PlaceholderText = "sbc_…" } ), 1 );
		tokenRow.Add( UiStyle.Secondary( _advanced, "Connect", "login", () => _ = ConnectWithToken() ) );
		_advancedError = _advanced.Layout.Add( UiStyle.Colored( new Label( "", _advanced ) { WordWrap = true, Visible = false }, Theme.Red ) );

		_serverUrl = Settings.ServerUrl;
		BuildStep();
	}

	// ------------------------------------------------------------------ steps

	private void GoTo( Step step )
	{
		CancelPolling();
		_step = step;
		_busy = false;
		BuildStep();
	}

	private void BuildStep()
	{
		_steps.Current = (int)_step;
		_stepHost.Layout.Clear( true );
		var card = _stepHost.Layout.Add( new StepPanel( _stepHost ) );
		switch ( _step )
		{
			case Step.Server:
				BuildServer( card );
				break;
			case Step.Key:
				BuildKey( card );
				break;
			case Step.GitHub:
				BuildGitHub( card );
				break;
		}
	}

	private void BuildServer( StepPanel card )
	{
		card.Header( "dns", "Which server?" );
		card.Layout.Add( UiStyle.Muted( new Label( "The address of your team's Collaborator server: a domain or an IP address. Ask whoever hosts it.", card ) { WordWrap = true, Alignment = TextFlag.Center } ) );
		_addressInput = card.Layout.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "mcp.example.com  or  192.168.1.20:8080", Text = Settings.ServerUrl ?? "" }, 30 ) );
		_addressInput.ReturnPressed += () => _ = CheckServer();
		_error = card.Layout.Add( ErrorLabel( card ) );
		var actions = card.Layout.AddRow();
		actions.AddStretchCell();
		_continue = actions.Add( UiStyle.Primary( "Continue", "arrow_forward", () => _ = CheckServer() ) );
		_continue.MinimumWidth = 120;
		_addressInput.Focus();
	}

	private void BuildKey( StepPanel card )
	{
		card.Header( "vpn_key", "Server key" );
		card.Layout.Add( ServerRow( card ) );
		card.Layout.Add( UiStyle.Muted( new Label( "Paste the server key the admin gave you (it starts with sbj_). It's only used to create your account and is not saved.", card ) { WordWrap = true, Alignment = TextFlag.Center } ) );
		_keyInput = card.Layout.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "sbj_…" }, 30 ) );
		_keyInput.ReturnPressed += () => _ = CheckKey();
		_error = card.Layout.Add( ErrorLabel( card ) );
		var actions = card.Layout.AddRow();
		actions.Spacing = 8;
		actions.Add( new LinkLabel( card, "Back", () => GoTo( Step.Server ) ) );
		actions.AddSpacingCell( 6 );
		actions.Add( new LinkLabel( card, "I already have an account", () =>
		{
			_serverKey = null;
			GoTo( Step.GitHub );
		}, Theme.Green ) );
		actions.AddStretchCell();
		_continue = actions.Add( UiStyle.Primary( "Continue", "arrow_forward", () => _ = CheckKey() ) );
		_continue.MinimumWidth = 120;
		_keyInput.Focus();
	}

	private void BuildGitHub( StepPanel card )
	{
		card.Header( "login", "Login with GitHub" );
		card.Layout.Add( ServerRow( card ) );

		if ( _serverInfo is { GithubLogin: false } )
		{
			card.Layout.Add( UiStyle.Colored( new Label( "This server has GitHub login turned off. Use a personal access key from the dashboard instead (below).", card ) { WordWrap = true }, Theme.Yellow ) );
			_advanced.Visible = true;
			var back = card.Layout.AddRow();
			back.Add( new LinkLabel( card, "Back", () => GoTo( Step.Key ) ) );
			back.AddStretchCell();
			return;
		}

		if ( _device is null )
		{
			card.Layout.Add( UiStyle.Muted( new Label( _serverKey is null
				? "Sign in with the GitHub account you already use on this server. Your browser opens; approve there and the editor gets its own access key."
				: "Sign in with GitHub to create your account. Your browser opens; approve there and the editor gets its own access key. Nothing to copy.", card ) { WordWrap = true, Alignment = TextFlag.Center } ) );
			_error = card.Layout.Add( ErrorLabel( card ) );
			var actions = card.Layout.AddRow();
			actions.Spacing = 8;
			actions.Add( new LinkLabel( card, "Back", () => GoTo( Step.Key ) ) );
			actions.AddStretchCell();
			_continue = actions.Add( UiStyle.Primary( "Login with GitHub", "open_in_new", () => _ = StartDevice() ) );
			_continue.MinimumWidth = 170;
			return;
		}

		// Waiting for the browser: the code to compare, a spinner, and a way out.
		card.Layout.Add( UiStyle.Muted( new Label( "Approve the sign-in in your browser. Check that it shows this code:", card ) { WordWrap = true, Alignment = TextFlag.Center } ) );
		card.Layout.Add( new CodeBox( card, _device.UserCode ) );
		var waiting = card.Layout.Add( new ProcessingIndicator( card, boxed: false ) { FixedHeight = 110 } );
		waiting.Set( "Waiting for approval…", "This page continues by itself once you approve." );
		_error = card.Layout.Add( ErrorLabel( card ) );
		var buttons = card.Layout.AddRow();
		buttons.Spacing = 8;
		buttons.Add( new LinkLabel( card, "Cancel", () =>
		{
			_device = null;
			GoTo( Step.GitHub );
		} ) );
		buttons.AddStretchCell();
		buttons.Add( UiStyle.Secondary( card, "Open browser again", "open_in_new", () => Browser.Open( _device?.VerificationUriComplete ?? _device?.VerificationUri ) ) );
	}

	/// <summary>The verified server: a green light, its name and address, and a way back to change it.</summary>
	private Widget ServerRow( Widget parent )
	{
		var row = new Widget( parent );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 8;
		row.Layout.Add( new StatusDot( row ) { Color = Theme.Green } );
		var name = string.IsNullOrEmpty( _serverInfo?.Name ) ? "Collaborator server" : _serverInfo.Name;
		Rows.TwoLines( row, row.Layout, name, _serverUrl, boldTitle: true );
		row.Layout.Add( new LinkLabel( row, "Change", () => GoTo( Step.Server ) ) );
		return row;
	}

	private static Label ErrorLabel( Widget parent ) => UiStyle.Colored( new Label( "", parent ) { WordWrap = true, Visible = false }, Theme.Red );

	private void ShowError( string text )
	{
		if ( !_error.IsValid() )
			return;
		_error.Text = text ?? "";
		_error.Visible = !string.IsNullOrEmpty( text );
	}

	private void SetBusy( bool busy, string text = null )
	{
		_busy = busy;
		if ( !_continue.IsValid() )
			return;
		_continue.Enabled = !busy;
		if ( text is not null )
			_continue.Text = text;
	}

	// ------------------------------------------------------------------ step 1: server

	private async Task CheckServer()
	{
		if ( _busy )
			return;
		var typed = _addressInput.Text?.Trim() ?? "";
		var url = CollabClient.NormalizeAddress( typed );
		if ( url is null )
		{
			ShowError( typed.Length == 0 ? "Enter the server's domain or IP address." : "That doesn't look like an address. Plain http:// only works for IP addresses and localhost." );
			return;
		}
		ShowError( null );
		SetBusy( true, "Checking…" );

		var (info, error, finalUrl) = await ProbeAsync( url );
		// An IP typed without a scheme is usually plain HTTP on a LAN: try that before giving up.
		if ( info is null && !typed.Contains( "://" ) && Uri.TryCreate( url, UriKind.Absolute, out var uri ) && CollabClient.IsLocalOrIp( uri.Host ) )
		{
			var http = "http://" + url["https://".Length..];
			var (info2, _, final2) = await ProbeAsync( http );
			if ( info2 is not null )
				(info, error, finalUrl) = (info2, null, final2);
		}

		await EditorThread.SwitchToMainThread();
		if ( !IsValid )
			return;
		SetBusy( false, "Continue" );
		if ( info is null )
		{
			ShowError( error );
			return;
		}
		_serverUrl = finalUrl;
		_serverInfo = info;
		Settings.ServerUrl = finalUrl;
		GoTo( Step.Key );
	}

	private static async Task<(ServerInfo Info, string Error, string Url)> ProbeAsync( string url )
	{
		try
		{
			var info = await new CollabClient( url ).GetAsync<ServerInfo>( "/api/server-info" );
			return info is null ? (null, "The server answered, but it doesn't look like a Collaborator server.", url) : (info, null, url);
		}
		catch ( CollabException e )
		{
			var message = e.Status == 404 ? "The server answered, but it doesn't look like a Collaborator server (no /api/server-info)." : e.Message;
			return (null, message, url);
		}
		catch ( Exception e )
		{
			return (null, e.Message, url);
		}
	}

	// ------------------------------------------------------------------ step 2: server key

	private async Task CheckKey()
	{
		if ( _busy )
			return;
		var key = _keyInput.Text?.Trim() ?? "";
		if ( key.Length == 0 )
		{
			ShowError( "Paste the server key, or choose “I already have an account”." );
			return;
		}
		ShowError( null );
		SetBusy( true, "Checking…" );
		string error = null;
		ServerKeyCheck check = null;
		try
		{
			check = await new CollabClient( _serverUrl ).PostAsync<ServerKeyCheck>( "/api/auth/server-key/check", new { serverKey = key } );
		}
		catch ( CollabException e )
		{
			error = e.Code == "invalid_server_key" || e.Status == 401 ? "That server key isn't valid (wrong, expired, used up or revoked). Ask the admin for a new one." : e.Message;
		}
		await EditorThread.SwitchToMainThread();
		if ( !IsValid )
			return;
		SetBusy( false, "Continue" );
		if ( error is not null || check is not { Valid: true } )
		{
			ShowError( error ?? "That server key isn't valid." );
			return;
		}
		_serverKey = key;
		if ( !string.IsNullOrEmpty( check.ServerName ) && _serverInfo is not null )
			_serverInfo.Name = check.ServerName;
		GoTo( Step.GitHub );
	}

	// ------------------------------------------------------------------ step 3: GitHub (device flow)

	private async Task StartDevice()
	{
		if ( _busy )
			return;
		ShowError( null );
		SetBusy( true, "Opening…" );
		DeviceStart device = null;
		string error = null;
		var backToKey = false;
		try
		{
			device = await new CollabClient( _serverUrl ).PostAsync<DeviceStart>( "/api/auth/device", new
			{
				clientName = $"s&box editor on {Environment.MachineName}",
				clientType = CollabSession.ClientType,
				serverKey = _serverKey,
			} );
		}
		catch ( CollabException e )
		{
			backToKey = e.Code == "invalid_server_key";
			error = backToKey ? "The server key was rejected. Check it and try again." : e.Message;
		}
		await EditorThread.SwitchToMainThread();
		if ( !IsValid )
			return;
		SetBusy( false, "Login with GitHub" );
		if ( device is null )
		{
			if ( backToKey )
			{
				GoTo( Step.Key );
				ShowError( error );
			}
			else
				ShowError( error ?? "The server didn't start a sign-in." );
			return;
		}

		_device = device;
		BuildStep();
		Browser.Open( device.VerificationUriComplete ?? device.VerificationUri );
		_polling = new CancellationTokenSource();
		_ = PollAsync( device, _polling.Token );
	}

	private async Task PollAsync( DeviceStart device, CancellationToken cancel )
	{
		var client = new CollabClient( _serverUrl );
		var interval = Math.Max( 2, device.Interval );
		var deadline = DateTime.UtcNow.AddSeconds( Math.Max( 60, device.ExpiresIn ) );
		while ( !cancel.IsCancellationRequested && DateTime.UtcNow < deadline )
		{
			try
			{
				await Task.Delay( TimeSpan.FromSeconds( interval ), cancel );
				var token = await client.PostAsync<DeviceToken>( "/api/auth/device/token", new { deviceCode = device.DeviceCode }, cancel );
				if ( token is null || string.IsNullOrEmpty( token.Token ) )
					continue;

				await EditorThread.SwitchToMainThread();
				if ( cancel.IsCancellationRequested )
					return;
				// The server key has done its job: only the address and the personal key are kept.
				_serverKey = null;
				_device = null;
				await CollabSession.SignInAsync( _serverUrl, token.Token, token.Developer );
				return;
			}
			catch ( OperationCanceledException )
			{
				return;
			}
			catch ( CollabException e )
			{
				switch ( e.Code )
				{
					case "authorization_pending":
						continue;
					case "slow_down":
						interval += 5;
						continue;
					case "access_denied":
						await Fail( "Sign-in was denied in the browser." );
						return;
					case "expired_token":
						await Fail( "The sign-in code expired. Try again." );
						return;
					default:
						if ( e.IsNetwork )
							continue; // flaky connection: keep waiting until the code expires
						await Fail( e.Message );
						return;
				}
			}
		}
		if ( !cancel.IsCancellationRequested )
			await Fail( "The sign-in code expired. Try again." );

		async Task Fail( string message )
		{
			await EditorThread.SwitchToMainThread();
			if ( !IsValid || cancel.IsCancellationRequested )
				return;
			_device = null;
			BuildStep();
			ShowError( message );
		}
	}

	private void CancelPolling()
	{
		_polling?.Cancel();
		_polling = null;
	}

	// ------------------------------------------------------------------ fallback: access key

	private void ToggleAdvanced()
	{
		_advanced.Visible = !_advanced.Visible;
		if ( _advanced.Visible )
			_tokenInput.Focus();
	}

	private async Task ConnectWithToken()
	{
		var token = _tokenInput.Text?.Trim() ?? "";
		var url = _step == Step.Server && _addressInput.IsValid() ? CollabClient.NormalizeAddress( _addressInput.Text ) : _serverUrl;
		void Error( string text )
		{
			_advancedError.Text = text;
			_advancedError.Visible = !string.IsNullOrEmpty( text );
		}
		if ( url is null )
		{
			Error( "Enter the server address in step 1 first." );
			return;
		}
		if ( !token.StartsWith( "sbc_" ) )
		{
			Error( "Access keys start with sbc_. (Server keys, sbj_…, go in step 2.)" );
			return;
		}
		Error( null );
		Me me = null;
		string failure = null;
		try
		{
			me = await new CollabClient( url, token ).GetAsync<Me>( "/api/me" );
		}
		catch ( CollabException e )
		{
			failure = e.IsAuth ? "That access key was rejected (wrong or revoked)." : e.Message;
		}
		await EditorThread.SwitchToMainThread();
		if ( !IsValid )
			return;
		if ( me is null )
		{
			Error( failure ?? "The server didn't accept the key." );
			return;
		}
		_tokenInput.Text = "";
		await CollabSession.SignInAsync( url, token, me.Developer );
	}

	public override void OnDestroyed()
	{
		CancelPolling();
		base.OnDestroyed();
	}

	// ------------------------------------------------------------------ pieces

	/// <summary>
	/// One step's content, no box: a large centred icon, a title and whatever the step adds below.
	/// </summary>
	private sealed class StepPanel : Widget
	{
		public StepPanel( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Spacing = 10;
		}

		public void Header( string icon, string title )
		{
			Layout.Add( new StepTitle( this, icon, title ) );
		}

		private sealed class StepTitle : Widget
		{
			private readonly string _icon;
			private readonly string _title;

			public StepTitle( Widget parent, string icon, string title ) : base( parent )
			{
				_icon = icon;
				_title = title;
				FixedHeight = 74;
			}

			protected override void OnPaint()
			{
				Paint.Antialiasing = true;
				var r = new Rect( (Width - 44) * .5f, 0, 44, 44 );
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green.WithAlpha( .12f ) );
				Paint.DrawRect( r, 22 );
				Paint.SetPen( Theme.Green );
				Paint.DrawIcon( r, _icon, 22 );
				Paint.SetDefaultFont( 12, 700 );
				Paint.SetPen( Theme.Text );
				Paint.DrawText( new Rect( 0, 50, Width, 22 ), _title, TextFlag.Center );
			}
		}
	}

	/// <summary>The device code, big and monospaced, in a framed box.</summary>
	private sealed class CodeBox : Widget
	{
		private readonly string _code;

		public CodeBox( Widget parent, string code ) : base( parent )
		{
			_code = code ?? "";
			FixedHeight = 52;
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			var box = new Rect( (Width - 220) * .5f, 2, 220, Height - 4 );
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.WindowBackground );
			Paint.DrawRect( box, 6 );
			Paint.SetFont( "Consolas", 18, 700 );
			Paint.SetPen( Theme.Green );
			Paint.DrawText( box, _code, TextFlag.Center );
		}
	}
}
