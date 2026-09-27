namespace Collaborator.UI;

/// <summary>
/// One page of the dock. Forms (anything you type into) are built once in <see cref="BuildStatic"/>
/// so a refresh never eats a half-typed message; lists are rebuilt in <see cref="BuildDynamic"/>
/// when the shared data changes (or every 30 s, to keep "5m ago" honest). Rebuilds are deferred to
/// the next frame so a widget is never deleted inside its own click handler.
/// </summary>
public abstract class Page : Widget
{
	protected readonly MainView Main;
	private Widget _dynamic;
	private bool _built;
	private int _seen = -1;
	private bool _dirty = true;
	private RealTimeSince _sinceBuild;

	protected Page( Widget parent, MainView main ) : base( parent )
	{
		Main = main;
		Layout = Layout.Column();
		Layout.Spacing = 10;
	}

	public abstract string Title { get; }

	protected virtual void BuildStatic( Layout layout )
	{
	}

	protected abstract void BuildDynamic( Widget host, Layout layout );

	/// <summary>Rebuild the lists on the next frame (selection changed, filter changed…).</summary>
	public void MarkDirty() => _dirty = true;

	/// <summary>Called every frame by the dock while this page is visible.</summary>
	public void Flush()
	{
		if ( !_built )
		{
			_built = true;
			BuildStatic( Layout );
			_dynamic = Layout.Add( new Widget( this ) );
			_dynamic.Layout = Layout.Column();
			_dynamic.Layout.Spacing = 10;
			Layout.AddStretchCell();
		}
		if ( !_dirty && _seen == CollabSession.DataRevision && _sinceBuild < 30 )
			return;
		_dirty = false;
		_seen = CollabSession.DataRevision;
		_sinceBuild = 0;
		_dynamic.Layout.Clear( true );
		try
		{
			BuildDynamic( _dynamic, _dynamic.Layout );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[collaborator] {Title} page failed to build: {e}" );
			_dynamic.Layout.Add( new EmptyState( _dynamic, "error", "Something went wrong drawing this page", e.Message ) );
		}
	}

	/// <summary>A card with a header, added to <paramref name="layout"/>.</summary>
	protected static Card AddCard( Widget parent, Layout layout, string icon, string title, Color? accent = null, string tooltip = null )
	{
		var card = layout.Add( new Card( parent ) { Accent = accent } );
		card.Header( icon, title, tooltip );
		return card;
	}

	protected static Label AddText( Widget parent, Layout layout, string text, bool muted = false, bool small = false, bool wrap = true )
	{
		var label = layout.Add( new Label( text ?? "", parent ) { WordWrap = wrap } );
		if ( muted )
			UiStyle.Muted( label, small );
		return label;
	}
}
