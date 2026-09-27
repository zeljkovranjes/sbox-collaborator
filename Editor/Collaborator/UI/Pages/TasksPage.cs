using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>
/// The shared task board: filter chips, the list (click a task to open it in place with its
/// actions) and a form for new tasks.
/// </summary>
public sealed class TasksPage : Page
{
	private enum Filter { Open, Mine, Available, Done }

	private Filter _filter = Filter.Open;
	private readonly List<(Filter Filter, Chip Chip)> _chips = new();
	private Card _newCard;
	private LineEdit _title;
	private TextEdit _description;
	private ComboBox _priority;
	private string _newPriority = "normal";

	public long? SelectedTaskId { get; set; }

	public TasksPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Tasks";

	protected override void BuildStatic( Layout layout )
	{
		var bar = layout.AddRow();
		bar.Spacing = 6;
		foreach ( var (filter, label, icon) in new[] { (Filter.Open, "Open", "inbox"), (Filter.Mine, "Mine", "person"), (Filter.Available, "Available", "radio_button_unchecked"), (Filter.Done, "Done", "task_alt") } )
		{
			var f = filter;
			var chip = bar.Add( new Chip( this, label, icon ) { Active = f == _filter, Clicked = () => SetFilter( f ) } );
			_chips.Add( (f, chip) );
		}
		bar.AddStretchCell();
		_newButton = bar.Add( UiStyle.Secondary( this, "New task", "add", ToggleNew, "Add a task to the shared board" ) );
		FitFilterBar();

		_newCard = layout.Add( new Card( this ) { Visible = false } );
		_newCard.Header( "add_task", "New task" );
		_title = _newCard.Layout.Add( UiStyle.Framed( new LineEdit( _newCard ) { PlaceholderText = "What needs doing? e.g. Rewrite boat buoyancy" } ) );
		_description = _newCard.Layout.Add( UiStyle.Framed( new TextEdit( _newCard ) { PlaceholderText = "Details, acceptance criteria, related systems (optional)" }, 0 ) );
		_description.FixedHeight = 72;
		var row = _newCard.Layout.AddRow();
		row.Spacing = 6;
		row.Add( UiStyle.Muted( new Label( "Priority", _newCard ) { FixedWidth = 52, Alignment = TextFlag.LeftCenter } ) );
		_priority = row.Add( UiStyle.Framed( new ComboBox( _newCard ) { FixedWidth = 120 } ) );
		foreach ( var p in new[] { "low", "normal", "high", "urgent" } )
		{
			var value = p;
			_priority.AddItem( char.ToUpperInvariant( p[0] ) + p[1..], null, () => _newPriority = value, selected: p == "normal" );
		}
		row.AddStretchCell();
		row.Add( UiStyle.Secondary( _newCard, "Cancel", null, ToggleNew ) );
		row.Add( UiStyle.Primary( "Create", "add", Create, "Create the task as available", UiStyle.ControlHeight ) );
		_title.ReturnPressed += Create;
	}

	private UiButton _newButton;

	protected override void OnResize()
	{
		base.OnResize();
		FitFilterBar();
	}

	/// <summary>Narrow docks: chips drop their labels and the button its text, so nothing is clipped.</summary>
	private void FitFilterBar()
	{
		if ( _chips.Count == 0 || !_newButton.IsValid() )
			return;
		var full = _chips.Sum( c => c.Chip.FullWidth ) + 6 * _chips.Count + 6.2f * "New task".Length + 60;
		var compact = Width > 0 && full > Width;
		foreach ( var (_, chip) in _chips )
			chip.Compact = compact;
		var text = compact ? "" : "New task";
		if ( _newButton.Text != text )
			_newButton.Text = text;
	}

	private void ToggleNew()
	{
		_newCard.Visible = !_newCard.Visible;
		if ( _newCard.Visible )
			_title.Focus();
	}

	private async void Create()
	{
		var title = _title.Text?.Trim();
		if ( string.IsNullOrEmpty( title ) )
		{
			CollabSession.SetStatus( "Give the task a title.", Theme.Yellow );
			return;
		}
		var task = await CollabSession.CreateTaskAsync( title, _description.PlainText, _newPriority );
		if ( task is null || !IsValid )
			return;
		_title.Text = "";
		_description.PlainText = "";
		_newCard.Visible = false;
		SelectedTaskId = task.Id;
		MarkDirty();
	}

	private void SetFilter( Filter filter )
	{
		_filter = filter;
		foreach ( var (f, chip) in _chips )
			chip.Active = f == filter;
		MarkDirty();
	}

	private IEnumerable<TaskItem> Filtered()
	{
		var me = CollabSession.MyId;
		var tasks = CollabSession.Tasks;
		return _filter switch
		{
			Filter.Mine => tasks.Where( t => t.OwnerId == me && t.IsOpen ),
			Filter.Available => tasks.Where( t => t.Status is "available" or "backlog" ),
			Filter.Done => tasks.Where( t => t.Status == "done" ).OrderByDescending( t => t.CompletedAt ?? t.UpdatedAt ),
			_ => tasks.Where( t => t.IsOpen ),
		};
	}

	private static int StatusOrder( string s ) => s switch
	{
		"blocked" => 0,
		"in_progress" => 1,
		"claimed" => 2,
		"review" => 3,
		"available" => 4,
		"backlog" => 5,
		_ => 6,
	};

	private static int PriorityOrder( string p ) => p switch { "urgent" => 0, "high" => 1, "normal" => 2, _ => 3 };

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		var tasks = Filtered().ToList();
		if ( _filter != Filter.Done )
			tasks = tasks.OrderBy( t => StatusOrder( t.Status ) ).ThenBy( t => PriorityOrder( t.Priority ) ).ThenByDescending( t => t.UpdatedAt ).ToList();

		var card = layout.Add( new Card( host ) );
		card.Layout.Spacing = 4;
		if ( tasks.Count == 0 )
		{
			card.Layout.Add( new EmptyState( card, "task_alt", _filter switch
			{
				Filter.Mine => "You have no open tasks",
				Filter.Available => "Nothing is waiting to be picked up",
				Filter.Done => "No finished tasks yet",
				_ => "The board is empty",
			}, _filter == Filter.Done ? null : "Create a task, or ask your agent to." ) );
			return;
		}

		string group = null;
		foreach ( var task in tasks.Take( 150 ) )
		{
			var heading = _filter == Filter.Done ? "Done" : CollabSession.TaskStatusText( task.Status );
			if ( heading != group )
			{
				group = heading;
				card.Layout.Add( new SectionHeader( card, heading, tasks.Count( t => (_filter == Filter.Done ? "Done" : CollabSession.TaskStatusText( t.Status )) == heading ) ) );
			}
			var t = task;
			var selected = SelectedTaskId == t.Id;
			card.Layout.Add( Rows.Task( card, t, selected, () =>
			{
				SelectedTaskId = selected ? null : t.Id;
				MarkDirty();
			} ) );
			if ( selected )
				card.Layout.Add( Detail( card, t ) );
		}
	}

	/// <summary>The opened task: its text, files and the actions that fit its state.</summary>
	private Widget Detail( Widget parent, TaskItem t )
	{
		var box = new Widget( parent );
		box.Layout = Layout.Column();
		box.Layout.Margin = new Sandbox.UI.Margin( 14, 2, 6, 8 );
		box.Layout.Spacing = 6;

		if ( !string.IsNullOrWhiteSpace( t.Description ) )
			box.Layout.Add( new Label( t.Description, box ) { WordWrap = true } );
		if ( t.Status == "blocked" && !string.IsNullOrEmpty( t.BlockedReason ) )
			box.Layout.Add( UiStyle.Colored( new Label( $"Blocked: {t.BlockedReason}", box ) { WordWrap = true }, Theme.Red ) );
		if ( t.RelatedFiles is { Count: > 0 } )
			box.Layout.Add( UiStyle.Mono( new Label( UiStyle.Breakable( string.Join( "\n", t.RelatedFiles.Take( 8 ) ) ), box ) { WordWrap = true } ) );
		if ( t.DependsOn is { Count: > 0 } )
			box.Layout.Add( UiStyle.Muted( new Label( $"Depends on {string.Join( ", ", t.DependsOn.Select( d => $"#{d}" ) )}", box ), small: true ) );
		if ( !string.IsNullOrEmpty( t.CompletionSummary ) )
			box.Layout.Add( UiStyle.Colored( new Label( t.CompletionSummary, box ) { WordWrap = true }, Theme.Green ) );

		if ( !CollabSession.CanWrite )
			return box;

		var actions = box.Layout.AddRow();
		actions.Spacing = 6;
		var mine = t.OwnerId == CollabSession.MyId;
		switch ( t.Status )
		{
			case "available":
			case "backlog":
				actions.Add( UiStyle.Primary( "Claim", "assignment_ind", () => _ = CollabSession.ClaimTaskAsync( t ), "Take this task; teammates see it's yours", UiStyle.ControlHeight ) );
				break;
			case "done":
				break;
			default:
				if ( mine )
				{
					if ( t.Status is "claimed" or "blocked" or "review" )
						actions.Add( UiStyle.Primary( t.Status == "blocked" ? "Unblock" : "Start", "play_arrow", () => _ = CollabSession.SetTaskStatusAsync( t, "in_progress" ), "Mark in progress", UiStyle.ControlHeight ) );
					actions.Add( UiStyle.Secondary( box, "Complete…", "task_alt", () => AskComplete( t ) ) );
					if ( t.Status != "blocked" )
						actions.Add( UiStyle.Secondary( box, "Blocked…", "block", () => AskBlock( t ) ) );
					if ( t.Status == "in_progress" )
						actions.Add( UiStyle.Secondary( box, "Review", "rate_review", () => _ = CollabSession.SetTaskStatusAsync( t, "review" ) ) );
					actions.Add( new UiButton( box, "Release", "undo", () => _ = CollabSession.ReleaseTaskAsync( t ), "Put the task back on the board" ) { Tint = Theme.TextLight } );
				}
				else
				{
					actions.Add( UiStyle.Muted( new Label( $"{t.OwnerName ?? t.OwnerId} owns this task.", box ) ) );
					actions.Add( new UiButton( box, "Take over…", "swap_horiz", () => AskTakeOver( t ), "Claim it anyway; the owner gets a handoff message" ) { Tint = Theme.Yellow } );
				}
				break;
		}
		actions.AddStretchCell();
		return box;
	}

	private static void AskComplete( TaskItem t )
		=> Dialog.AskString( summary => _ = CollabSession.CompleteTaskAsync( t, summary ), $"What changed in #{t.Id}? One or two sentences for the team.", okay: "Complete", title: "Complete task" );

	private static void AskBlock( TaskItem t )
		=> Dialog.AskString( reason => _ = CollabSession.BlockTaskAsync( t, reason ), $"What is #{t.Id} waiting on?", okay: "Mark blocked", title: "Blocked" );

	private static void AskTakeOver( TaskItem t )
		=> Dialog.AskConfirm( () => _ = CollabSession.ClaimTaskAsync( t, force: true ),
			$"#{t.Id} belongs to {t.OwnerName ?? t.OwnerId}. Take it over? They get a handoff message." );
}
