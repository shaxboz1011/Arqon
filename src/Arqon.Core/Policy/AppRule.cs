namespace Arqon.Core.Policy;

public enum AppKind { Normal, Game, Downloader, Background }

public enum LinkChoice { Auto, Only, Both }

/// <summary>How one application may use the links. <c>LinkId</c> is only meaningful with <see cref="LinkChoice.Only"/>.</summary>
public sealed record AppRule(string ExeName, AppKind Kind, LinkChoice Choice, string? LinkId, long? LimitBps);
