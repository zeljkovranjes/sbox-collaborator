using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>
/// "Hand off…": stop working on a task and leave the next person (or agent) a note – where you
/// got to, what's next, what to watch out for – optionally handing it straight to a teammate.
/// </summary>
public sealed class HandoffDialog : Widget
{
	private readonly TaskItem _task;
	private readonly Dialog _dialog;
	private readonly TextEdit _summary;
	private readonly TextEdit _next;
	private readonly LineEdit _gotchas;
	private readonly Label _error;
	private string _to;

	private HandoffDialog( Dialog dialog, TaskItem task ) : base( dialog )
	{
		_dialog = dialog;
		_task = task;
		Layout = Layout.Column();
		Layout.Margin = 14;
		Layout.Spacing = 8;

		Layout.Add( UiStyle.Bold( new Label( $"#{task.Id} {task.Title}", this ) { WordWrap = true } ) );
		Layout.Add( UiStyle.Muted( new Label( "The note shows on the task and in everyone's next sync.", this ) { WordWrap = true }, small: true ) );

		Layout.Add( new SectionHeader( this, "Where I got to" ) );
		_summary = Layout.Add( UiStyle.Framed( new TextEdit( this ) { PlaceholderText = "Buoyancy integration works; foam shader not started." }, 0 ) );
		_summary.FixedHeight = 64;

		Layout.Add( new SectionHeader( this, "Next steps" ) );
		_next = Layout.Add( UiStyle.Framed( new TextEdit( this ) { PlaceholderText = "Optional: what the next person should do first." }, 0 ) );
		_next.FixedHeight = 52;

		Layout.Add( new SectionHeader( this, "Watch out for" ) );
		_gotchas = Layout.Add( UiStyle.Framed( new LineEdit( this ) { PlaceholderText = "Optional: traps, half-done changes, flaky tests" } ) );

		var toRow = Layout.AddRow();
		toRow.Spacing = 8;
		toRow.Add( UiStyle.Muted( new Label( "Hand to", this ) { FixedWidth = 60, Alignment = TextFlag.LeftCenter } ) );
		var to = toRow.Add( UiStyle.Framed( new ComboBox( this ) ), 1 );
		to.AddItem( "Nobody – back on the board", "inbox", () => _to = null, selected: true );
		foreach ( var dev in CollabSession.Overview?.Team?.Select( t => t.Developer ).Where( d => d is not null && d.Id != CollabSession.MyId ) ?? Enumerable.Empty<Developer>() )
		{
			var id = dev.Id;
			to.AddItem( dev.Name, dev.Online ? "person" : "person_outline", () => _to = id );
		}

		_error = Layout.Add( UiStyle.Colored( new Label( "", this ) { WordWrap = true, Visible = false }, Theme.Red ) );
		Layout.AddStretchCell();
		var buttons = Layout.AddRow();
		buttons.Spacing = 6;
		buttons.AddStretchCell();
		buttons.Add( UiStyle.Secondary( this, "Cancel", null, () => _dialog.Close() ) );
		buttons.Add( UiStyle.Primary( "Hand off", "swap_horiz", () => _ = Submit(), "Release the task with this note", UiStyle.ControlHeight ) );
	}

	public static void Open( TaskItem task )
	{
		var dialog = new Dialog( null );
		dialog.Window.Title = $"Hand off #{task.Id}";
		if ( CollaboratorWindow.Logo is { } logo )
			dialog.Window.SetWindowIcon( logo );
		dialog.Window.Size = new Vector2( 460, 520 );
		dialog.Window.MinimumSize = new Vector2( 360, 420 );
		dialog.Layout = Layout.Column();
		dialog.Layout.Add( new HandoffDialog( dialog, task ), 1 );
		dialog.Show();
	}

	private async Task Submit()
	{
		var summary = _summary.PlainText?.Trim();
		if ( string.IsNullOrEmpty( summary ) )
		{
			_error.Text = "Say where you got to – that's the point of a handoff.";
			_error.Visible = true;
			return;
		}
		var result = await CollabSession.HandOffAsync( _task, summary, _next.PlainText?.Trim(), _gotchas.Text?.Trim(), _to );
		if ( !IsValid )
			return;
		if ( result is null )
		{
			_error.Text = CollabSession.StatusText;
			_error.Visible = true;
			return;
		}
		_dialog.Close();
	}
}
