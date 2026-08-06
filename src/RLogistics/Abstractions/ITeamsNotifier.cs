namespace RLogistics.Abstractions;

public interface ITeamsNotifier
{
    Task NotifyAsync(TeamsMessage message, CancellationToken ct = default);
}

public sealed record TeamsMessage(
    string Title,
    string Body,
    int? RequestId = null,
    string? DeepLink = null);
