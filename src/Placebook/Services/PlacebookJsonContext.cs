using System.Text.Json;
using System.Text.Json.Serialization;
using Placebook.Models;
using Placebook.Services.Auth;

namespace Placebook.Services;

/// <summary>
/// Quellgenerierte JSON-Serialisierung. Reflection-basiertes System.Text.Json ist im WebAssembly-Interpreter
/// sehr langsam (≈ 12 s für ~9.000 Orte), der generierte Code braucht nur einen Bruchteil davon.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<Category>))]
[JsonSerializable(typeof(PlacesFile))]
[JsonSerializable(typeof(VisitStore))]
[JsonSerializable(typeof(List<VisitChange>))]
[JsonSerializable(typeof(List<VisitPushRow>))]
[JsonSerializable(typeof(List<VisitPullRow>))]
[JsonSerializable(typeof(AuthSession))]
[JsonSerializable(typeof(AuthTokenResponse))]
[JsonSerializable(typeof(AuthUserResponse))]
[JsonSerializable(typeof(AuthErrorResponse))]
[JsonSerializable(typeof(AuthCredentials))]
[JsonSerializable(typeof(AuthRefreshRequest))]
[JsonSerializable(typeof(AuthEmailRequest))]
[JsonSerializable(typeof(AuthPasswordRequest))]
public sealed partial class PlacebookJsonContext : JsonSerializerContext;
