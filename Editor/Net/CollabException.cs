using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Collaborator.EditorTools.Net;

/// <summary>A failed call: the server's error code and message, or a transport failure.</summary>
public sealed class CollabException : Exception
{
	/// <summary>Server error code (<c>conflict</c>, <c>unauthorized</c>…), or <c>network</c> / <c>timeout</c>.</summary>
	public string Code { get; }

	/// <summary>HTTP status, 0 when the server could not be reached.</summary>
	public int Status { get; }

	public JsonElement Details { get; }

	public CollabException( string code, string message, int status, JsonElement details = default ) : base( message )
	{
		Code = code;
		Status = status;
		Details = details;
	}

	public bool IsNetwork => Status == 0;
	public bool IsAuth => Status == 401 || Code is "unauthorized";
}
