namespace MindAttic.Legion;

/// <summary>
/// One message in a multi-turn conversation. <see cref="Role"/> is "user" or
/// "assistant"; "system" is supported but most providers prefer a separate
/// system prompt parameter — Legion will route a system role to the right place.
/// </summary>
/// <param name="Role">Speaker — "user", "assistant", or "system".</param>
/// <param name="Content">Message text.</param>
public sealed record ChatTurn(string Role, string Content);
