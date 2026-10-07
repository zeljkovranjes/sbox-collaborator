using System.Text.Json;

namespace Collaborator.EditorTools.Net;

/// <summary>Who touched a file or folder, when and why (file_history).</summary>
public sealed class FileHistory
{
	public string Path { get; set; }
	public List<FileHistoryCommit> Commits { get; set; } = new();
	public List<FileHistoryChange> Changes { get; set; } = new();
	public List<FileHistoryTask> Tasks { get; set; } = new();
	public List<Reservation> Reservations { get; set; } = new();
}
