using Collaborator.EditorTools.Net;
using Collaborator.EditorTools.Session;

namespace Collaborator.EditorTools.UI;

/// <summary>Short notices in the editor's toast column (teammate messages, reservation warnings, broken builds).</summary>
public static class Toasts
{
	/// <summary>The most recent toasts, newest last (the editor gate asserts on these).</summary>
	public static IReadOnlyList<(string Title, string Subtitle, DateTimeOffset At)> Recent => RecentList;

	private static readonly List<(string Title, string Subtitle, DateTimeOffset At)> RecentList = new();

	public static void Show( string title, string subtitle, string icon, Color color, float seconds = 8 )
	{
		EditorThread.Post( () =>
		{
			RecentList.Add( (title ?? "", subtitle ?? "", DateTimeOffset.UtcNow) );
			if ( RecentList.Count > 50 )
				RecentList.RemoveAt( 0 );
			try
			{
				var toast = new CollabToast { Title = title, Subtitle = subtitle, Icon = icon, BorderColor = color };
				ToastManager.Remove( toast, seconds );
			}
			catch ( Exception e )
			{
				Log.Info( $"[collaborator] {title}: {subtitle} ({e.Message})" );
			}
		} );
	}

	public static void Message( TeamMessage m )
	{
		var (icon, color) = MessageStyle( m.Type );
		var who = string.IsNullOrEmpty( m.FromName ) ? m.FromDeveloperId : m.FromName;
		var title = m.Type == "handoff"
			? $"{who} handed {(m.TaskId is { } id ? $"task #{id}" : "work")} {(m.Broadcast ? "back to the team" : "to you")}"
			: $"{who}: {(string.IsNullOrEmpty( m.Subject ) ? TypeTitle( m.Type ) : m.Subject)}";
		Show( title, m.Body, icon, color, m.Type is "blocker" or "warning" or "handoff" ? 16 : 10 );
	}

	public static (string Icon, Color Color) MessageStyle( string type ) => type switch
	{
		"blocker" => ("block", Theme.Red),
		"warning" => ("warning", Theme.Yellow),
		"question" => ("help", Theme.Blue),
		"request" => ("assignment", Theme.Blue),
		"handoff" => ("swap_horiz", Theme.Green),
		_ => ("chat", Theme.TextLight),
	};

	public static string TypeTitle( string type ) => type switch
	{
		"blocker" => "Blocker",
		"warning" => "Warning",
		"question" => "Question",
		"request" => "Request",
		"handoff" => "Handoff",
		_ => "Info",
	};

	/// <summary>A static toast: no pulsing border, no timer, sized for two lines of text.</summary>
	private sealed class CollabToast : ToastWidget
	{
		public CollabToast()
		{
			DrawTimer = false;
			IsRunning = false;
		}

		protected override Vector2 SizeHint() => new( 380, 96 );
	}
}
