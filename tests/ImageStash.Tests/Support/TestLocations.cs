using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>Default locations the check names.</summary>
public sealed record TestLocations(string DefaultStashFolder, string SettingsFolder) : ILocations;
