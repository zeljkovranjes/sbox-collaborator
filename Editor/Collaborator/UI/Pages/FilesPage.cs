using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>
/// Reservations: reserve a file or folder before a big edit (asset picker or typed path), see
/// who holds what, release yours. Reservations are advisory: they warn, they never lock.
/// </summary>
public sealed class FilesPage : Page
{
	private LineEdit _path;
	private LineEdit _reason;
	private Label _result;

	public FilesPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Files";

	protected override void BuildStatic( Layout layout )
	{
		var card = layout.Add( new Card( this ) );
		card.Header( "edit_note", "Reserve for editing", "Tell the team you're changing these files, so their agents stay clear" );
		card.Layout.Add( UiStyle.Muted( new Label( "Reserve a file or a whole folder (end it with /) before a big change. Teammates and their agents are warned; nothing is locked.", card ) { WordWrap = true }, small: true ) );

		var pathRow = card.Layout.AddRow();
		pathRow.Spacing = 6;
		_path = pathRow.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "Code/BoatController.cs or Assets/Ships/" } ), 1 );
		pathRow.Add( UiStyle.Icon( card, "folder_open", PickAsset, "Pick an asset" ) );
		pathRow.Add( UiStyle.Icon( card, "landscape", UseOpenScene, "Use the open scene" ) );

		_reason = card.Layout.Add( UiStyle.Framed( new LineEdit( card ) { PlaceholderText = "Why (e.g. rewriting buoyancy for #42)" } ) );

		var actions = card.Layout.AddRow();
		actions.Spacing = 6;
		_result = actions.Add( UiStyle.Muted( new Label( "", card ) { WordWrap = true }, small: true ), 1 );
		actions.Add( UiStyle.Secondary( card, "Check", "person_search", () => _ = Check(), "Who has this reserved?" ) );
		actions.Add( UiStyle.Primary( "Reserve", "lock", () => _ = Reserve(), "Reserve for editing", UiStyle.ControlHeight ) );
		_path.ReturnPressed += () => _ = Reserve();
	}

	private List<string> Paths() => (_path.Text ?? "").Split( new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries )
		.Select( p => p.Trim().Replace( '\\', '/' ).TrimStart( '/' ) ).Where( p => p.Length > 0 ).ToList();

	private void PickAsset()
	{
		var picker = AssetPicker.Create( this, null, new AssetPicker.PickerOptions { EnableCloud = false, EnableMounts = false } );
		picker.Title = "Reserve an asset";
		picker.OnAssetPicked = assets =>
		{
			var paths = assets?.Select( ProjectPaths.ToRepo ).Where( p => p is not null ).ToList();
			if ( paths is { Count: > 0 } )
				_path.Text = string.Join( ", ", paths );
		};
		picker.Show();
	}

	private void UseOpenScene()
	{
		if ( AssetGuard.OpenScenePath is { } scene )
			_path.Text = scene;
		else
			CollabSession.SetStatus( "No project scene is open.", Theme.Yellow );
	}

	private async Task Reserve()
	{
		var paths = Paths();
		if ( paths.Count == 0 )
		{
			SetResult( "Enter a path to reserve.", Theme.Yellow );
			return;
		}
		var reason = string.IsNullOrWhiteSpace( _reason.Text ) ? "Editing in the s&box editor" : _reason.Text.Trim();
		var result = await CollabSession.ReserveAsync( paths, reason );
		if ( result is null || !IsValid )
			return;
		if ( result.Conflicts is { Count: > 0 } )
		{
			var c = result.Conflicts[0];
			SetResult( $"{c.Reservation?.DeveloperName ?? "A teammate"} already has {c.Reservation?.Path}. {c.Message}", Theme.Yellow );
			if ( result.Reserved.Count == 0 )
			{
				Dialog.AskConfirm( () => _ = Force( paths, reason ), $"{c.Reservation?.DeveloperName ?? "A teammate"} has {c.Reservation?.Path} reserved. Reserve anyway? They will be told." );
				return;
			}
		}
		else
		{
			SetResult( $"Reserved {string.Join( ", ", result.Reserved.Select( r => r.Path ) )}.", Theme.Green );
			_path.Text = "";
		}
	}

	private async Task Force( List<string> paths, string reason )
	{
		var result = await CollabSession.ReserveAsync( paths, reason, force: true );
		if ( result is not null && IsValid )
			SetResult( $"Reserved {string.Join( ", ", result.Reserved.Select( r => r.Path ) )} (overlapping a teammate).", Theme.Yellow );
	}

	private async Task Check()
	{
		var paths = Paths();
		if ( paths.Count == 0 )
			return;
		var check = await CollabSession.CheckConflictAsync( paths );
		if ( check is null || !IsValid )
			return;
		SetResult( check.Clear || check.Conflicts.Count == 0 ? "Clear: nobody else has these reserved." : check.Message ?? $"{check.Conflicts.Count} conflict(s).", check.Clear ? Theme.Green : Theme.Yellow );
	}

	private void SetResult( string text, Color color )
	{
		_result.Text = text;
		UiStyle.Colored( _result, color );
	}

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var all = CollabSession.Reservations;
		var mine = all.Where( r => r.DeveloperId == CollabSession.MyId ).ToList();
		var others = all.Where( r => r.DeveloperId != CollabSession.MyId ).ToList();

		var mineCard = AddCard( host, layout, "person", "Yours" );
		if ( mine.Count == 0 )
			mineCard.Layout.Add( UiStyle.Muted( new Label( "You have nothing reserved.", mineCard ) ) );
		else
		{
			foreach ( var r in mine )
			{
				var path = r.Path;
				mineCard.Layout.Add( Rows.Reservation( mineCard, r, () => _ = CollabSession.ReleaseAsync( new[] { path } ) ) );
			}
			var row = mineCard.Layout.AddRow();
			row.AddStretchCell();
			row.Add( new UiButton( mineCard, "Release all", "lock_open", () => _ = CollabSession.ReleaseAsync( mine.Select( r => r.Path ) ) ) { Tint = Theme.TextLight } );
		}

		var othersCard = AddCard( host, layout, "groups", "Teammates" );
		if ( others.Count == 0 )
		{
			othersCard.Layout.Add( UiStyle.Muted( new Label( "Nobody else has anything reserved.", othersCard ) ) );
			return;
		}
		foreach ( var group in others.GroupBy( r => r.DeveloperName ?? r.DeveloperId ) )
		{
			othersCard.Layout.Add( new SectionHeader( othersCard, group.Key, group.Count() ) );
			foreach ( var r in group )
				othersCard.Layout.Add( Rows.Reservation( othersCard, r ) );
		}
	}
}
