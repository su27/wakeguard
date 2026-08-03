using System.Text.Json.Serialization;

namespace WakeGuard.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    MaxDepth = 16,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ServiceRequest))]
[JsonSerializable(typeof(ServiceResponse))]
internal sealed partial class WakeGuardJsonContext : JsonSerializerContext;
