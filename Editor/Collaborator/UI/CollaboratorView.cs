namespace Collaborator.UI;

/// <summary>
/// Everything inside the Collaborator window (<see cref="CollaboratorWindow"/>). Shows the sign-in
/// wizard until the editor has an access key, a project picker when this s&amp;box project isn't
/// linked yet, a loader while the first connection is made, then the team view.
/// </summary>
public sealed class CollaboratorView : Widget
{
	private SignInView _signIn;
	private readonly ProjectPickerView _picker;
	private readonly ProcessingIndicator _connecting;
	private readonly MainView _main;
	private ConnectionState? _shown;
	private bool _hasOverview;

	/// <summary>The signed-in view (the editor gate switches its pages).</summary>
	internal MainView Main => _main;

	public CollaboratorView( Widget parent ) : base( parent )
	{
		Name = "Collaborator";
		MinimumSize = new Vector2( 300, 320 );
		FocusMode = FocusMode.Click;
		Layout = Layout.Column();

		_picker = Layout.Add( new ProjectPickerView( this ) { Visible = false }, 1 );
		_connecting = Layout.Add( new ProcessingIndicator( this ) { Visible = false }, 1 );
		_main = Layout.Add( new MainView( this ) { Visible = false }, 1 );
		Sync();
	}

	public override void OnDestroyed()
	{
		CollaboratorWindow.SaveState();
		base.OnDestroyed();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		var hasOverview = CollabSession.Overview is not null;
		if ( _shown == CollabSession.State && _hasOverview == hasOverview )
			return;
		Sync();
	}

	private void Sync()
	{
		var state = CollabSession.AboutToConnect ? ConnectionState.Connecting : CollabSession.State;
		_shown = CollabSession.State;
		_hasOverview = CollabSession.Overview is not null;

		// The wizard is rebuilt fresh each time it appears, so a new sign-in starts at step 1.
		var signedOut = state == ConnectionState.SignedOut;
		if ( signedOut && !_signIn.IsValid() )
			_signIn = Layout.Add( new SignInView( this ), 1 );
		else if ( !signedOut && _signIn.IsValid() )
		{
			_signIn.Destroy();
			_signIn = null;
		}

		var firstConnect = (state == ConnectionState.Connecting || state == ConnectionState.Offline) && CollabSession.Project is null;
		_picker.Visible = state == ConnectionState.PickProject;
		_connecting.Visible = firstConnect;
		if ( firstConnect )
		{
			_connecting.Busy = true;
			_connecting.Set( state == ConnectionState.Offline ? "Can't reach the server" : "Connecting…",
				state == ConnectionState.Offline ? $"{CollabSession.LastError} Retrying automatically." : Settings.ServerUrl );
		}
		_main.Visible = !signedOut && !firstConnect && state != ConnectionState.PickProject;
		if ( _picker.Visible )
			_picker.Rebuild();
	}
}

/// <summary>Links this s&amp;box project to one of the server's projects (remembered per project).</summary>
public sealed class ProjectPickerView : Widget
{
	private readonly Widget _list;

	public ProjectPickerView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 12;
		Layout.AddStretchCell();
		var row = Layout.AddRow();
		row.AddStretchCell();
		var card = row.Add( new Card( this ) { MaximumWidth = 460, MinimumWidth = 260 } );
		row.AddStretchCell();
		Layout.AddStretchCell();

		card.Header( "folder_special", "Choose a project" );
		string ident = null;
		try
		{
			ident = Sandbox.Project.Current?.Config?.Ident;
		}
		catch ( Exception )
		{
		}
		card.Layout.Add( UiStyle.Muted( new Label( $"Which server project is {(string.IsNullOrEmpty( ident ) ? "this s&box project" : $"“{ident}”")}? This is remembered for this project.", card ) { WordWrap = true } ) );
		_list = card.Layout.Add( new Widget( card ) );
		_list.Layout = Layout.Column();
		_list.Layout.Spacing = 4;
		var actions = card.Layout.AddRow();
		actions.Add( new LinkLabel( card, "Sign out", CollabSession.SignOut ) );
		actions.AddStretchCell();
		actions.Add( UiStyle.Secondary( card, "Open dashboard", "open_in_new", () => Browser.Open( Settings.ServerUrl ) ) );
	}

	public void Rebuild()
	{
		_list.Layout.Clear( true );
		if ( CollabSession.Projects.Count == 0 )
		{
			_list.Layout.Add( new EmptyState( _list, "create_new_folder", "No projects on the server yet", "An admin can create one in the dashboard." ) );
			return;
		}
		foreach ( var p in CollabSession.Projects )
		{
			var target = p;
			var row = new ClickRow( _list, () => _ = CollabSession.OpenProjectAsync( target ), p.Summary );
			row.Layout.Add( new IconLabel( row, p.Kind == "library" ? "extension" : "sports_esports", Theme.Green ) );
			Rows.TwoLines( row, row.Layout, p.Title, string.Join( " · ", new[] { p.Id, p.PackageIdent, p.Milestone }.Where( x => !string.IsNullOrEmpty( x ) ) ), boldTitle: true );
			row.Layout.Add( new IconLabel( row, "chevron_right", Theme.TextLight ) );
			_list.Layout.Add( row );
		}
	}
}
