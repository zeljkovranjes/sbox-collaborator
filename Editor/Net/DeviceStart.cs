using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class DeviceStart
{
	public string DeviceCode { get; set; }
	public string UserCode { get; set; }
	public string VerificationUri { get; set; }
	public string VerificationUriComplete { get; set; }
	public int Interval { get; set; } = 5;
	public int ExpiresIn { get; set; } = 600;
}
