using System.IO;

namespace Collaborator.UI;

/// <summary>
/// The Collaborator window: its own floating window with the editor's dark title bar, the
/// Collaborator logo, minimise / maximise / close, and the size and position it had last time.
/// Opened from View ▸ Collaborator (like the Weapon Importer), never docked into the layout.
/// </summary>
public static class CollaboratorWindow
{
	public const string Title = "Collaborator";

	private static Dialog _dialog;
	private static CollaboratorView _view;

	/// <summary>The open window's dialog (the editor gate resizes it).</summary>
	internal static Dialog Dialog => _dialog.IsValid() ? _dialog : null;

	/// <summary>The open window's content, if the window is open.</summary>
	public static CollaboratorView View => _view.IsValid() ? _view : null;

	[Event( "tools.editorwindow.createview" )]
	private static void RegisterViewMenu( Menu menu ) => EditorWindow.DockManager.RegisterDockType( new DockManager.DockInfo
	{
		Title = Title,
		Icon = "hub",
		CreateAction = () =>
		{
			Open();
			return null;
		},
	} );

	/// <summary>The View menu entry opens (or raises) the window instead of toggling a dock.</summary>
	[Event( "tools.editorwindow.postcreateview" )]
	private static void ConfigureViewMenu( Menu menu )
	{
		var option = menu.GetOption( Title );
		if ( option is null )
			return;
		option.Toggled = null;
		option.Checkable = false;
		option.Triggered = () => Open();
	}

	/// <summary>Opens the window, or brings the open one to the front. Returns its content.</summary>
	public static CollaboratorView Open()
	{
		if ( View is { } existing && _dialog.IsValid() )
		{
			_dialog.Window.Show();
			_dialog.Window.Raise();
			return existing;
		}

		_dialog = new Dialog( null );
		var window = _dialog.Window;
		window.Title = Title;
		if ( Logo is { } logo )
			window.SetWindowIcon( logo );
		else
			window.SetWindowIcon( "hub" );
		window.MinimumSize = new Vector2( 320, 420 );
		window.Size = new Vector2( 520, 860 );
		_dialog.Layout = Layout.Column();
		_view = _dialog.Layout.Add( new CollaboratorView( _dialog ), 1 );

		// Remember size and position between sessions (not during automated gate runs).
		if ( !Settings.InMemory )
		{
			window.StateCookie = "collaborator.window";
			window.RestoreFromStateCookie();
		}
		_dialog.Show();
		return _view;
	}

	/// <summary>Saves the window geometry (called when the view goes away).</summary>
	internal static void SaveState()
	{
		try
		{
			if ( _dialog.IsValid() && !Settings.InMemory )
				_dialog.Window.SaveToStateCookie();
		}
		catch ( Exception )
		{
		}
	}

	// ------------------------------------------------------------------ logo

	private static Pixmap _logo;
	private static bool _logoTried;

	/// <summary>The Collaborator logo as a window icon (embedded, so it never depends on file layout).</summary>
	public static Pixmap Logo
	{
		get
		{
			if ( _logoTried )
				return _logo;
			_logoTried = true;
			try
			{
				var path = Path.Combine( Path.GetTempPath(), "collaborator-logo-v1.png" );
				if ( !File.Exists( path ) )
					File.WriteAllBytes( path, Convert.FromBase64String( LogoPng ) );
				_logo = Pixmap.FromFile( path );
			}
			catch ( Exception )
			{
				_logo = null;
			}
			return _logo;
		}
	}

	private const string LogoPng = "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";
}
