using Collaborator.EditorTools.Session;
using Collaborator.EditorTools.UI.Widgets;

namespace Collaborator.EditorTools.UI;

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
