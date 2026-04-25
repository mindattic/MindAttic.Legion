namespace MindAttic.Legion;

/// <summary>
/// One message in a multi-turn conversation. <see cref="Role"/> is "user" or
/// "assistant"; "system" is supported but most providers prefer a separate
/// system prompt parameter — Legion will route a system role to the right place.
/// </summary>
public sealed record ChatTurn(string Role, string Content);
