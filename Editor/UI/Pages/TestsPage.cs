using Collaborator.EditorTools.Net;
using Collaborator.EditorTools.Session;
using Collaborator.EditorTools.UI.Widgets;

namespace Collaborator.EditorTools.UI.Pages;

/// <summary>
/// Playtest results: log what you tested (scene, pass/fail, errors) so agents know whether the
/// current commit works. A failure warns the whole team.
/// </summary>
public sealed class TestsPage : Page
{
	private LineEdit _description;
	private LineEdit _scene;
	private TextEdit _errors;
	private SegmentedControl _result;

	public TestsPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Tests";

	protected override void BuildStatic( Layout layout )
	{
		var card = layout.Add( new Card( this ) );
		card.Header( "science", "Log a playtest" );
		_description = card.Layout.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "What did you test? e.g. Storm sailing, 2 players" } ) );

		var row = card.Layout.AddRow();
		row.Spacing = 6;
		_scene = row.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "Scene or map" } ), 1 );
		row.Add( UiStyle.Icon( card, "landscape", () => _scene.Text = AssetGuard.OpenScenePath ?? _scene.Text, "Use the open scene" ) );
		_result = row.Add( new SegmentedControl( card ) { FixedHeight = UiStyle.ControlHeight, FixedWidth = 176 } );
		_result.AddOption( "Passed", "check_circle" );
		_result.AddOption( "Failed", "error" );
		_result.SelectedIndex = 0;

		_errors = card.Layout.Add( UiStyle.Framed( new TextEdit( card ) { PlaceholderText = "Errors or observations, one per line (optional)" }, 0 ) );
		_errors.FixedHeight = 60;

		var actions = card.Layout.AddRow();
		actions.Add( UiStyle.Muted( new Label( $"Branch: {ProjectPaths.GitBranch() ?? "unknown"}", card ), small: true ) );
		actions.AddStretchCell();
		actions.Add( UiStyle.Primary( "Log result", "send", () => _ = LogResult(), "Record the result for the team", UiStyle.ControlHeight ) );
	}

	private async Task LogResult()
	{
		var description = _description.Text?.Trim();
		if ( string.IsNullOrEmpty( description ) )
		{
			CollabSession.SetStatus( "Describe what you tested.", Theme.Yellow );
			return;
		}
		var errors = (_errors.PlainText ?? "").Split( '\n' ).Select( l => l.Trim() ).Where( l => l.Length > 0 ).Take( 50 ).ToList();
		var status = _result.SelectedIndex == 0 ? "passed" : "failed";
		var run = await CollabSession.LogTestAsync( description, status, _scene.Text, null, errors );
		if ( run is null || !IsValid )
			return;
		_description.Text = "";
		_errors.PlainText = "";
		_result.SelectedIndex = 0;
	}

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var card = AddCard( host, layout, "history", "Recent results" );
		var tests = CollabSession.Tests;
		if ( tests.Count == 0 )
		{
			card.Layout.Add( new EmptyState( card, "science", "No test results yet", "Agents log build and test runs here too." ) );
			return;
		}
		foreach ( var t in tests.OrderByDescending( t => t.FinishedAt ?? t.StartedAt ) )
		{
			var color = t.Status switch { "passed" => Theme.Green, "running" => Theme.Blue, _ => Theme.Red };
			var row = new ClickRow( card ) { Bar = color };
			var who = CollabSession.Overview?.Team?.FirstOrDefault( e => e.Developer?.Id == t.DeveloperId )?.Developer?.Name ?? t.DeveloperId;
			var detail = string.Join( " · ", new[] { who, t.Scene, t.Branch, t.CommitSha?.Length > 7 ? t.CommitSha[..7] : t.CommitSha, UiStyle.Ago( t.FinishedAt ?? t.StartedAt ) }.Where( s => !string.IsNullOrEmpty( s ) ) );
			var column = Rows.TwoLines( row, row.Layout, t.Description, detail );
			foreach ( var e in t.Errors.Take( 3 ) )
				column.Add( UiStyle.Mono( new Label( e, row ) { WordWrap = true }, Theme.Red ) );
			row.Layout.Add( new Pill( row, t.Status, color, column: true ) );
			card.Layout.Add( row );
		}
	}
}
