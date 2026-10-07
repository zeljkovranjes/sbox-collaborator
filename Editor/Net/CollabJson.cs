using System.Text.Json;
using System.Text.Json.Serialization;

namespace Collaborator.EditorTools.Net;

/// <summary>
/// JSON settings shared by every request: camelCase names (the server's convention), nulls left
/// out of requests, and tolerant reads (ids may arrive as numbers or strings).
/// </summary>
public static class CollabJson
{
	public static readonly JsonSerializerOptions Options = Create();

	private static JsonSerializerOptions Create()
	{
		var options = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			NumberHandling = JsonNumberHandling.AllowReadingFromString,
		};
		options.Converters.Add( new FlexibleStringConverter() );
		return options;
	}

	public static string Serialize( object value ) => JsonSerializer.Serialize( value, Options );

	public static T Deserialize<T>( JsonElement element ) => element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
		? default
		: element.Deserialize<T>( Options );

	/// <summary>
	/// Reads any scalar into a string property: the contract leaves some ids untyped, so a number
	/// id and a string id both land in the same field.
	/// </summary>
	private sealed class FlexibleStringConverter : JsonConverter<string>
	{
		public override string Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			switch ( reader.TokenType )
			{
				case JsonTokenType.String:
					return reader.GetString();
				case JsonTokenType.Number:
					return reader.TryGetInt64( out var l ) ? l.ToString() : reader.GetDouble().ToString( System.Globalization.CultureInfo.InvariantCulture );
				case JsonTokenType.True:
					return "true";
				case JsonTokenType.False:
					return "false";
				case JsonTokenType.Null:
					return null;
				default:
					// Objects/arrays where a string was expected: keep the raw JSON rather than failing the whole read.
					using ( var doc = JsonDocument.ParseValue( ref reader ) )
						return doc.RootElement.GetRawText();
			}
		}

		public override void Write( Utf8JsonWriter writer, string value, JsonSerializerOptions options ) => writer.WriteStringValue( value );
	}
}
