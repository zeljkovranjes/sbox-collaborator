using Collaborator.Net;

namespace Collaborator.UI;

/// <summary>
/// Messages between people and agents: unread first (acknowledge them), then recent ones, and a
/// short compose form. Messages are for intent (handoffs, blockers, questions), not chatter.
/// </summary>
public sealed class MessagesPage : Page
{
	private ComboBox _to;
	private ComboBox _type;
	private TextEdit _body;
	private string _recipient;
	private string _messageType = "info";
	private string _recipientsKey;

	public MessagesPage( Widget parent, MainView main ) : base( parent, main )
	{
	}

	public override string Title => "Messages";

	protected override void BuildStatic( Layout layout )
	{
		var card = layout.Add( new Card( this ) );
		card.Header( "edit", "Send a message" );
		var row = card.Layout.AddRow();
		row.Spacing = 6;
		_to = row.Add( UiStyle.Framed( new ComboBox( card ) { ToolTip = "Recipient" } ), 1 );
		_type = row.Add( UiStyle.Framed( new ComboBox( card ) { FixedWidth = 116, ToolTip = "Kind of message" } ) );
		foreach ( var type in new[] { "info", "question", "request", "warning", "blocker", "handoff" } )
		{
			var value = type;
			var (icon, _) = Toasts.MessageStyle( type );
			_type.AddItem( Toasts.TypeTitle( type ), icon, () => _messageType = value, selected: type == "info" );
		}
		_body = card.Layout.Add( UiStyle.Framed( new TextEdit( card ) { PlaceholderText = "Keep it short: what, where, and what you need." }, 0 ) );
		_body.FixedHeight = 64;
		var send = card.Layout.AddRow();
		send.AddStretchCell();
		send.Add( UiStyle.Primary( "Send", "send", () => _ = Send(), "Send the message", UiStyle.ControlHeight ) );
		FillRecipients();
	}

	/// <summary>Everyone, then each teammate (their agents see messages addressed to them).</summary>
	private void FillRecipients()
	{
		var team = CollabSession.Overview?.Team?.Select( t => t.Developer ).Where( d => d is not null && d.Id != CollabSession.MyId ).ToList() ?? new();
		var key = string.Join( ",", team.Select( d => d.Id ) );
		if ( key == _recipientsKey )
			return;
		_recipientsKey = key;
		_to.Clear();
		_to.AddItem( "Everyone", "campaign", () => _recipient = null, selected: _recipient is null );
		foreach ( var d in team )
		{
			var id = d.Id;
			_to.AddItem( d.Name, d.Online ? "person" : "person_outline", () => _recipient = id, selected: _recipient == id );
		}
	}

	private async Task Send()
	{
		var body = _body.PlainText?.Trim();
		if ( string.IsNullOrEmpty( body ) )
		{
			CollabSession.SetStatus( "Write a message first.", Theme.Yellow );
			return;
		}
		if ( body.Length > 1000 )
		{
			CollabSession.SetStatus( "Messages are limited to 1000 characters.", Theme.Yellow );
			return;
		}
		var sent = await CollabSession.SendMessageAsync( _recipient, _messageType, body );
		if ( sent is not null && IsValid )
			_body.PlainText = "";
	}

	protected override void BuildDynamic( Widget host, Layout layout )
	{
		FillRecipients();
		var unread = CollabSession.Unread.OrderByDescending( m => m.CreatedAt ).ToList();

		var unreadCard = AddCard( host, layout, "mark_email_unread", "Unread", unread.Any( m => m.Type is "blocker" or "warning" ) ? Theme.Yellow : (Color?)null );
		if ( unread.Count == 0 )
			unreadCard.Layout.Add( UiStyle.Muted( new Label( "You're all caught up.", unreadCard ) ) );
		else
		{
			foreach ( var m in unread )
				unreadCard.Layout.Add( MessageRow( unreadCard, m, ack: true ) );
			if ( unread.Count > 1 )
			{
				var row = unreadCard.Layout.AddRow();
				row.AddStretchCell();
				row.Add( UiStyle.Secondary( unreadCard, "Acknowledge all", "done_all", () => _ = CollabSession.AcknowledgeAsync( unread.Select( m => m.Id ) ) ) );
			}
		}

		var recent = CollabSession.Messages.Except( unread ).OrderByDescending( m => m.CreatedAt ).Take( 40 ).ToList();
		var card = AddCard( host, layout, "forum", "Recent" );
		if ( recent.Count == 0 )
		{
			card.Layout.Add( UiStyle.Muted( new Label( "No messages yet.", card ) ) );
			return;
		}
		foreach ( var m in recent )
			card.Layout.Add( MessageRow( card, m, ack: false ) );
	}

	private static Widget MessageRow( Widget parent, TeamMessage m, bool ack )
	{
		var (icon, color) = Toasts.MessageStyle( m.Type );
		var row = new ClickRow( parent ) { Bar = ack ? color : (Color?)null };
		row.Layout.Add( new IconLabel( row, icon, color ) );
		var from = m.FromDeveloperId == CollabSession.MyId ? "You" : m.FromName ?? m.FromDeveloperId;
		var to = m.Broadcast ? "everyone" : m.ToDeveloperId == CollabSession.MyId ? "you" : m.ToDeveloperId ?? m.ToAgentId;
		var column = row.Layout.AddColumn( 1 );
		column.Spacing = 2;
		column.Add( UiStyle.Muted( new Label( $"{from} → {to} · {Toasts.TypeTitle( m.Type )}{(m.TaskId is { } taskId ? $" · #{taskId}" : "")} · {UiStyle.Ago( m.CreatedAt )}", row ) { WordWrap = true }, small: true ) );
		if ( !string.IsNullOrEmpty( m.Subject ) )
			column.Add( UiStyle.Bold( new Label( m.Subject, row ) { WordWrap = true } ) );
		column.Add( new Label( m.Body ?? "", row ) { WordWrap = true } );
		if ( m.Paths is { Count: > 0 } )
			column.Add( UiStyle.Mono( new Label( UiStyle.Breakable( string.Join( ", ", m.Paths ) ), row ) { WordWrap = true } ) );
		if ( ack )
		{
			var id = m.Id;
			row.Layout.Add( UiStyle.Icon( row, "done", () => _ = CollabSession.AcknowledgeAsync( new[] { id } ), "Acknowledge", 24 ) );
		}
		return row;
	}
}
