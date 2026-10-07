using Collaborator.EditorTools.Session;
using Collaborator.EditorTools.UI.Widgets;
using IconLabel = Collaborator.EditorTools.UI.Widgets.IconLabel;

namespace Collaborator.EditorTools.UI;

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
