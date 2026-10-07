using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Conflict
{
	public string Path { get; set; }
	public string Relation { get; set; }
	public Reservation Reservation { get; set; }
	public string Message { get; set; }
}
