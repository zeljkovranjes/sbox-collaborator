using Collaborator.EditorTools.Net;
using Collaborator.EditorTools.Session;
using Collaborator.EditorTools.UI.Widgets;
using IconLabel = Collaborator.EditorTools.UI.Widgets.IconLabel;

namespace Collaborator.EditorTools.UI;

/// <summary>
/// "History…" for a file or folder: who touched it and when (commits), what they said about it
/// (change announcements, breaking ones highlighted), open tasks and active reservations.
/// A floating window like the main one.
/// </summary>
public sealed class HistoryWindow : Widget
{
	public const string TitlePrefix = "Collaborator — History";

	private static HistoryWindow _open;

	/// <summary>The open history window, if any (the editor gate inspects it).</summary>
	internal static HistoryWindow Current => _open.IsValid() ? _open : null;

	private readonly string _path;
	private readonly Widget _content;

	/// <summary>The loaded history (null while loading or on failure).</summary>
	internal FileHistory History { get; private set; }

	internal string Error { get; private set; }

	private HistoryWindow( Widget parent, string path ) : base( parent )
	{
		_path = path;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 12, 10, 12, 10 );
		Layout.Spacing = 8;

		var header = Layout.AddRow();
		header.Spacing = 8;
		header.Add( new IconLabel( this, path.EndsWith( '/' ) ? "folder" : "description", Theme.Green ) );
		header.Add( UiStyle.Mono( new Label( UiStyle.Breakable( path ), this ) { WordWrap = true }, Theme.Text ), 1 );
		header.Add( UiStyle.Icon( this, "refresh", () => _ = LoadAsync(), "Reload", 24 ) );

		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		_content = new Widget( scroll );
		_content.Layout = Layout.Column();
		_content.Layout.Spacing = 10;
		_content.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 10, 0 );
		scroll.Canvas = _content;
		_content.Layout.Add( new EmptyState( _content, "history", "Loading history…" ) );
		_ = LoadAsync();
	}

	public static void Open( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return;
		if ( Current is { } existing )
			existing.GetWindow()?.Close();
		var dialog = new Dialog( null );
		dialog.Window.Title = $"{TitlePrefix}: {System.IO.Path.GetFileName( path.TrimEnd( '/' ) )}";
		if ( CollaboratorWindow.Logo is { } logo )
			dialog.Window.SetWindowIcon( logo );
		dialog.Window.Size = new Vector2( 520, 620 );
		dialog.Window.MinimumSize = new Vector2( 340, 360 );
		dialog.Layout = Layout.Column();
		_open = dialog.Layout.Add( new HistoryWindow( dialog, path ), 1 );
		dialog.Show();
	}

	private async Task LoadAsync()
	{
		try
		{
			History = await CollabSession.FileHistoryAsync( _path );
			Error = History is null ? "Not connected." : null;
		}
		catch ( CollabException e )
		{
			await EditorThread.SwitchToMainThread();
			History = null;
			Error = e.Message;
		}
		if ( IsValid )
			Build();
	}

	private void Build()
	{
		_content.Layout.Clear( true );
		if ( History is null )
		{
			_content.Layout.Add( new EmptyState( _content, "cloud_off", "Couldn't load the history", Error ) );
			return;
		}
		var h = History;

		if ( h.Reservations is { Count: > 0 } )
		{
			var card = Card( "lock", "Reserved now", Theme.Yellow );
			foreach ( var r in h.Reservations )
				card.Layout.Add( Rows.Reservation( card, r ) );
		}

		if ( h.Tasks is { Count: > 0 } )
		{
			var card = Card( "task_alt", "Open tasks" );
			foreach ( var t in h.Tasks )
			{
				var row = new ClickRow( card );
				row.Layout.Add( UiStyle.Mono( new Label( $"#{t.Id}", row ) { FixedWidth = 38 } ) );
				Rows.TwoLines( row, row.Layout, t.Title, t.OwnerName ?? "nobody" );
				row.Layout.Add( new Pill( row, CollabSession.TaskStatusText( t.Status ), UiStyle.TaskColor( t.Status ), column: true ) );
				card.Layout.Add( row );
			}
		}

		var changes = Card( "published_with_changes", "Change announcements" );
		if ( h.Changes is not { Count: > 0 } )
			changes.Layout.Add( UiStyle.Muted( new Label( "No agent has announced a change to this yet.", changes ) { WordWrap = true }, small: true ) );
		foreach ( var c in h.Changes ?? new() )
		{
			var row = new ClickRow( changes ) { Bar = c.Breaking ? Theme.Yellow : (Color?)null };
			row.Layout.Add( new IconLabel( row, c.Breaking ? "warning" : "published_with_changes", c.Breaking ? Theme.Yellow : Theme.Green ) );
			Rows.TwoLines( row, row.Layout, c.Summary, $"{c.DeveloperName}{(c.TaskId is { } id ? $" · #{id}" : "")} · {UiStyle.Ago( c.CompletedAt )}{(c.Breaking ? " · breaking" : "")}" );
			changes.Layout.Add( row );
		}

		var commits = Card( "commit", "Commits" );
		if ( h.Commits is not { Count: > 0 } )
			commits.Layout.Add( UiStyle.Muted( new Label( "No pushes touching this yet (commits arrive through the GitHub webhook).", commits ) { WordWrap = true }, small: true ) );
		foreach ( var c in h.Commits ?? new() )
		{
			var url = c.Url;
			var row = new ClickRow( commits, string.IsNullOrEmpty( url ) ? null : () => Browser.Open( url ), c.Message );
			row.Layout.Add( UiStyle.Mono( new Label( c.Short ?? "", row ) { FixedWidth = 58 }, Theme.Green ) );
			var what = c.Change switch { "added" => "added", "removed" => "removed", _ => "changed" };
			Rows.TwoLines( row, row.Layout, c.Headline, $"{c.Author} {what} it · {UiStyle.Ago( c.At )}{(string.IsNullOrEmpty( c.Branch ) ? "" : $" · {c.Branch}")}{(c.TaskId is { } id ? $" · #{id}" : "")}" );
			commits.Layout.Add( row );
		}
		_content.Layout.AddStretchCell();
	}

	private Card Card( string icon, string title, Color? accent = null )
	{
		var card = _content.Layout.Add( new Card( _content ) { Accent = accent } );
		card.Header( icon, title );
		return card;
	}
}
