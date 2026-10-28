namespace Placebook.Models;

/// <summary>Eintrag aus <c>data/categories.json</c>.</summary>
public sealed record Category(
    string Id,
    string Name,
    string Icon,
    string Color,
    string File,
    int Count,
    DateOnly UpdatedAt);
