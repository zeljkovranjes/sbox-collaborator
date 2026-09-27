namespace Collaborator.UI;

/// <summary>Account, linked project, asset sync and sign-out.</summary>
public sealed class SettingsPage : Page
{
	public SettingsPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Settings";

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var me = CollabSession.Me;

		var account = AddCard( host, layout, "account_circle", "Account" );
		var row = account.Layout.AddRow();
		row.Spacing = 10;
		row.Add( new Avatar( account, me?.Developer?.Id, me?.Developer?.Name ?? Settings.DeveloperName, CollabSession.State == ConnectionState.Online, 40 ) );
		var scopes = me?.Scopes is { Count: > 0 } s ? string.Join( ", ", s ) : "unknown";
		Rows.TwoLines( account, row, me?.Developer?.Name ?? Settings.DeveloperName ?? "Signed in", $"{(string.IsNullOrEmpty( me?.Developer?.GithubLogin ) ? "" : $"@{me.Developer.GithubLogin} · ")}{me?.Developer?.Role ?? "member"} · access: {scopes}", boldTitle: true );
		account.Layout.Add( Field( account, "Server", Settings.ServerUrl ) );
		account.Layout.Add( Field( account, "Key", me?.Key is null ? "" : $"{me.Key.Name} ({me.Key.Prefix}…)" ) );
		var buttons = account.Layout.AddRow();
		buttons.Spacing = 6;
		buttons.Add( UiStyle.Secondary( account, "Open dashboard", "open_in_new", () => Browser.Open( Settings.ServerUrl ) ) );
		buttons.AddStretchCell();
		buttons.Add( new UiButton( account, "Sign out", "logout", ConfirmSignOut, "Forget this editor's access key" ) { Tint = Theme.Red } );

		var project = AddCard( host, layout, "folder_special", "Project" );
		project.Layout.Add( UiStyle.Muted( new Label( "The server project this s&box project reports to (remembered per s&box project).", project ) { WordWrap = true }, small: true ) );
		foreach ( var p in CollabSession.Projects )
		{
			var target = p;
			var selected = CollabSession.Project?.Id == p.Id;
			var pr = new ClickRow( project, selected ? null : () => _ = CollabSession.OpenProjectAsync( target ) ) { Selected = selected };
			pr.Layout.Add( new IconLabel( pr, p.Kind == "library" ? "extension" : "sports_esports", selected ? Theme.Green : Theme.TextLight ) );
			Rows.TwoLines( pr, pr.Layout, p.Title, string.Join( " · ", new[] { p.Id, p.Kind, p.Milestone }.Where( x => !string.IsNullOrEmpty( x ) ) ) );
			project.Layout.Add( pr );
		}

		var assets = AddCard( host, layout, "category", "Asset sync" );
		assets.Layout.Add( UiStyle.Muted( new Label( "Uploads your asset list and what each asset references (model → materials → textures), so agents can ask what uses what. Only paths and references are sent, never file contents.", assets ) { WordWrap = true }, small: true ) );
		var status = AssetSync.Running ? "Syncing…" : AssetSync.LastSync is null ? "Not synced in this session." : $"Last sync {UiStyle.Ago( AssetSync.LastSync )}: {AssetSync.LastResult}";
		assets.Layout.Add( new Label( status, assets ) { WordWrap = true } );
		var syncRow = assets.Layout.AddRow();
		syncRow.Spacing = 6;
		var auto = syncRow.Add( new Checkbox( "Sync automatically", assets ) { Value = Settings.AutoSyncAssets } );
		auto.StateChanged = state => Settings.AutoSyncAssets = state == CheckState.On;
		syncRow.AddStretchCell();
		var sync = syncRow.Add( UiStyle.Secondary( assets, "Sync now", "sync", () => _ = AssetSync.SyncAsync() ) );
		sync.Enabled = CollabSession.CanWrite && !AssetSync.Running;
	}

	private static Widget Field( Widget parent, string caption, string value )
	{
		var w = new Widget( parent );
		w.Layout = Layout.Row();
		w.Layout.Spacing = 6;
		w.Layout.Add( UiStyle.Muted( new Label( caption, w ) { FixedWidth = UiStyle.LabelWidth - 20 } ) );
		w.Layout.Add( UiStyle.Mono( new Label( UiStyle.Breakable( value ?? "" ), w ) { WordWrap = true }, Theme.Text ), 1 );
		return w;
	}

	private static void ConfirmSignOut()
		=> Dialog.AskConfirm( CollabSession.SignOut, "Sign out of Collaborator? The editor forgets its access key; you can sign in again with GitHub." );
}
