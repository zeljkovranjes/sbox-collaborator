using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class ReserveResult
{
	public List<Reservation> Reserved { get; set; } = new();
	public List<Conflict> Conflicts { get; set; } = new();
	public string Message { get; set; }
}
