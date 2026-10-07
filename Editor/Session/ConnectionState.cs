using System.Text.Json;
using Collaborator.EditorTools.Net;

namespace Collaborator.EditorTools.Session;

public enum ConnectionState
{
	/// <summary>No access key yet: the sign-in wizard shows.</summary>
	SignedOut,
	Connecting,
	/// <summary>Signed in, but this s&amp;box project isn't linked to a server project yet.</summary>
	PickProject,
	Online,
	/// <summary>Signed in, server unreachable: retrying with backoff.</summary>
	Offline,
}
